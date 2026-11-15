using System.Runtime.CompilerServices;

namespace DuckDB.NET.Data.DataChunk.Writer;

internal sealed unsafe class NumericVectorDataWriter(IntPtr vector, void* vectorData, DuckDBType columnType) : VectorDataWriterBase(vector, vectorData, columnType)
{
    internal override bool AppendNumeric<T>(T value, ulong rowIndex)
    {
        // The vector stores sizeof(column type) bytes per row, so only a value of exactly that type can be
        // copied as is. For a given T the JIT reduces this check to a single comparison.
        if (IsStorageType<T>(ColumnType))
        {
            return AppendValueInternal(value, rowIndex);
        }

        return AppendConverted(value, rowIndex);
    }

    internal override bool AppendBigInteger(BigInteger value, ulong rowIndex)
    {
        switch (ColumnType)
        {
            case DuckDBType.HugeInt when value >= (BigInteger)Int128.MinValue && value <= (BigInteger)Int128.MaxValue:
                return AppendValueInternal(new DuckDBHugeInt(value), rowIndex);
            case DuckDBType.UnsignedHugeInt when value.Sign >= 0 && value <= (BigInteger)UInt128.MaxValue:
                return AppendValueInternal(new DuckDBUHugeInt(value), rowIndex);
            case DuckDBType.HugeInt or DuckDBType.UnsignedHugeInt:
                throw OutOfRange(value);
            case DuckDBType.Float:
                return AppendValueInternal((float)value, rowIndex);
            case DuckDBType.Double:
                return AppendValueInternal((double)value, rowIndex);
        }

        if (value < (BigInteger)Int128.MinValue || value > (BigInteger)Int128.MaxValue)
        {
            throw OutOfRange(value);
        }

        return AppendIntegral((Int128)value, rowIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsStorageType<T>(DuckDBType columnType)
    {
        if (typeof(T) == typeof(sbyte)) return columnType == DuckDBType.TinyInt;
        if (typeof(T) == typeof(short)) return columnType == DuckDBType.SmallInt;
        if (typeof(T) == typeof(int)) return columnType == DuckDBType.Integer;
        if (typeof(T) == typeof(long)) return columnType == DuckDBType.BigInt;
        if (typeof(T) == typeof(byte)) return columnType == DuckDBType.UnsignedTinyInt;
        if (typeof(T) == typeof(ushort)) return columnType == DuckDBType.UnsignedSmallInt;
        if (typeof(T) == typeof(uint)) return columnType == DuckDBType.UnsignedInteger;
        if (typeof(T) == typeof(ulong)) return columnType == DuckDBType.UnsignedBigInt;
        if (typeof(T) == typeof(float)) return columnType == DuckDBType.Float;
        if (typeof(T) == typeof(double)) return columnType == DuckDBType.Double;
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool AppendConverted<T>(T value, ulong rowIndex) where T : unmanaged
    {
        if (typeof(T) == typeof(float) || typeof(T) == typeof(double))
        {
            // Widening float to DOUBLE is exact. Narrowing to REAL or converting to an integer can
            // silently lose data, so those need an explicit conversion by the caller.
            if (typeof(T) == typeof(float) && ColumnType == DuckDBType.Double)
            {
                return AppendValueInternal((double)Unsafe.As<T, float>(ref value), rowIndex);
            }

            throw Mismatch<T>();
        }

        var integral = ToInt128(value);

        return ColumnType switch
        {
            DuckDBType.Float => AppendValueInternal((float)integral, rowIndex),
            DuckDBType.Double => AppendValueInternal((double)integral, rowIndex),
            _ => AppendIntegral(integral, rowIndex)
        };
    }

    private bool AppendIntegral(Int128 value, ulong rowIndex)
    {
        return ColumnType switch
        {
            DuckDBType.TinyInt => AppendValueInternal(Narrow<sbyte>(value), rowIndex),
            DuckDBType.SmallInt => AppendValueInternal(Narrow<short>(value), rowIndex),
            DuckDBType.Integer => AppendValueInternal(Narrow<int>(value), rowIndex),
            DuckDBType.BigInt => AppendValueInternal(Narrow<long>(value), rowIndex),
            DuckDBType.UnsignedTinyInt => AppendValueInternal(Narrow<byte>(value), rowIndex),
            DuckDBType.UnsignedSmallInt => AppendValueInternal(Narrow<ushort>(value), rowIndex),
            DuckDBType.UnsignedInteger => AppendValueInternal(Narrow<uint>(value), rowIndex),
            DuckDBType.UnsignedBigInt => AppendValueInternal(Narrow<ulong>(value), rowIndex),
            DuckDBType.HugeInt => AppendValueInternal(new DuckDBHugeInt((ulong)value, (long)(value >> 64)), rowIndex),
            DuckDBType.UnsignedHugeInt when value >= 0 => AppendValueInternal(new DuckDBUHugeInt((ulong)value, (ulong)(value >> 64)), rowIndex),
            DuckDBType.UnsignedHugeInt => throw OutOfRange(value),
            _ => throw new InvalidOperationException($"Cannot write an integer to {ColumnType} column")
        };
    }

    private TTarget Narrow<TTarget>(Int128 value) where TTarget : unmanaged, IBinaryInteger<TTarget>, IMinMaxValue<TTarget>
    {
        if (value < Int128.CreateTruncating(TTarget.MinValue) || value > Int128.CreateTruncating(TTarget.MaxValue))
        {
            throw OutOfRange(value);
        }

        return TTarget.CreateTruncating(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Int128 ToInt128<T>(T value) where T : unmanaged
    {
        if (typeof(T) == typeof(sbyte)) return Unsafe.As<T, sbyte>(ref value);
        if (typeof(T) == typeof(short)) return Unsafe.As<T, short>(ref value);
        if (typeof(T) == typeof(int)) return Unsafe.As<T, int>(ref value);
        if (typeof(T) == typeof(long)) return Unsafe.As<T, long>(ref value);
        if (typeof(T) == typeof(byte)) return Unsafe.As<T, byte>(ref value);
        if (typeof(T) == typeof(ushort)) return Unsafe.As<T, ushort>(ref value);
        if (typeof(T) == typeof(uint)) return Unsafe.As<T, uint>(ref value);
        if (typeof(T) == typeof(ulong)) return Unsafe.As<T, ulong>(ref value);

        throw new InvalidOperationException($"{typeof(T).Name} is not an integer type");
    }

    private OverflowException OutOfRange<TValue>(TValue value) =>
        new($"Value {value} is out of range for {ColumnType} column");

    private InvalidOperationException Mismatch<T>() =>
        new($"Cannot write {typeof(T).Name} to {ColumnType} column without an explicit conversion");
}
