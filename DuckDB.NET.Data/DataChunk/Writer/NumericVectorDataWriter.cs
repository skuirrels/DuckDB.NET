using System.Runtime.CompilerServices;

namespace DuckDB.NET.Data.DataChunk.Writer;

// The vector stores sizeof(column type) bytes per row, so only a value of exactly that type is copied as it is; for
// each overload that is one comparison. Any other integer is converted to the column's type and throws
// OverflowException when out of range: copying it as it is would write the wrong number of bytes, corrupting the
// next values of a narrower column or running past the vector. float widens to DOUBLE; double to REAL and floating
// point to an integer column need an explicit conversion by the caller.
//
// TStorage is the column's storage type (NoStorage for a column that is not a number), so the comparison is a
// constant for each instantiation and the matching-type path is the plain copy, as fast as writing without the check.
internal sealed unsafe class NumericVectorDataWriter<TStorage>(IntPtr vector, void* vectorData, DuckDBType columnType) : VectorDataWriterBase(vector, vectorData, columnType)
    where TStorage : unmanaged
{
    internal override bool AppendNumeric(sbyte value, ulong rowIndex) => typeof(TStorage) == typeof(sbyte) ? AppendValueInternal(value, rowIndex) : AppendInteger(value, rowIndex);

    internal override bool AppendNumeric(short value, ulong rowIndex) => typeof(TStorage) == typeof(short) ? AppendValueInternal(value, rowIndex) : AppendInteger(value, rowIndex);

    internal override bool AppendNumeric(int value, ulong rowIndex) => typeof(TStorage) == typeof(int) ? AppendValueInternal(value, rowIndex) : AppendInteger(value, rowIndex);

    internal override bool AppendNumeric(long value, ulong rowIndex) => typeof(TStorage) == typeof(long) ? AppendValueInternal(value, rowIndex) : AppendInteger(value, rowIndex);

    internal override bool AppendNumeric(byte value, ulong rowIndex) => typeof(TStorage) == typeof(byte) ? AppendValueInternal(value, rowIndex) : AppendInteger(value, rowIndex);

    internal override bool AppendNumeric(ushort value, ulong rowIndex) => typeof(TStorage) == typeof(ushort) ? AppendValueInternal(value, rowIndex) : AppendInteger(value, rowIndex);

    internal override bool AppendNumeric(uint value, ulong rowIndex) => typeof(TStorage) == typeof(uint) ? AppendValueInternal(value, rowIndex) : AppendInteger(value, rowIndex);

    internal override bool AppendNumeric(ulong value, ulong rowIndex) => typeof(TStorage) == typeof(ulong) ? AppendValueInternal(value, rowIndex) : AppendInteger(value, rowIndex);

    internal override bool AppendNumeric(float value, ulong rowIndex) =>
        typeof(TStorage) == typeof(float) ? AppendValueInternal(value, rowIndex)
        : typeof(TStorage) == typeof(double) ? AppendValueInternal((double)value, rowIndex) // exact
        : throw Mismatch<float>();

    internal override bool AppendNumeric(double value, ulong rowIndex) => typeof(TStorage) == typeof(double) ? AppendValueInternal(value, rowIndex) : throw Mismatch<double>();

    // BigInteger converts like the other integers, with a range check for every column: OverflowException for the
    // narrower columns, ArgumentOutOfRangeException (from DuckDBHugeInt and DuckDBUHugeInt) for HUGEINT and UHUGEINT.
    // A column that is not a number, such as VARINT, refuses it.
    internal override bool AppendBigInteger(BigInteger value, ulong rowIndex)
    {
        switch (ColumnType)
        {
            // The constructors throw ArgumentOutOfRangeException for a value outside the column's range.
            case DuckDBType.HugeInt:
                return AppendValueInternal(new DuckDBHugeInt(value), rowIndex);
            case DuckDBType.UnsignedHugeInt:
                return AppendValueInternal(new DuckDBUHugeInt(value), rowIndex);
            case DuckDBType.Float:
                return AppendValueInternal((float)value, rowIndex);
            case DuckDBType.Double:
                return AppendValueInternal((double)value, rowIndex);
        }

        if (!IsIntegerColumn(ColumnType))
        {
            return base.AppendBigInteger(value, rowIndex);
        }

        if (value < (BigInteger)Int128.MinValue || value > (BigInteger)Int128.MaxValue)
        {
            throw OutOfRange(value);
        }

        return AppendIntegral((Int128)value, rowIndex);
    }

    // An Int128 has the bytes of a HUGEINT and a UInt128 those of a UHUGEINT, on a little-endian machine, so they are
    // stored as they are, in either of the two columns, like the other integers: an Int128 of -1 in a UHUGEINT column is
    // 2^128 - 1, as a long of -1 in a UBIGINT column is 2^64 - 1. Any other column refuses them: the 16 bytes would
    // overwrite the next value of a narrower column, or run past the vector.
    internal override bool AppendNumeric(Int128 value, ulong rowIndex) => typeof(TStorage) == typeof(Int128) || typeof(TStorage) == typeof(UInt128)
        ? AppendValueInternal(value, rowIndex)
        : base.AppendNumeric(value, rowIndex);

    internal override bool AppendNumeric(UInt128 value, ulong rowIndex) => typeof(TStorage) == typeof(Int128) || typeof(TStorage) == typeof(UInt128)
        ? AppendValueInternal(value, rowIndex)
        : base.AppendNumeric(value, rowIndex);

    // Kept out of line so the matching-type path above stays small enough to inline.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool AppendInteger(Int128 value, ulong rowIndex) => ColumnType switch
    {
        DuckDBType.Float => AppendValueInternal((float)value, rowIndex),
        DuckDBType.Double => AppendValueInternal((double)value, rowIndex),
        _ => AppendIntegral(value, rowIndex)
    };

    private bool AppendIntegral(Int128 value, ulong rowIndex) => ColumnType switch
    {
        DuckDBType.TinyInt => AppendValueInternal(Narrow<sbyte>(value), rowIndex),
        DuckDBType.SmallInt => AppendValueInternal(Narrow<short>(value), rowIndex),
        DuckDBType.Integer => AppendValueInternal(Narrow<int>(value), rowIndex),
        DuckDBType.BigInt => AppendValueInternal(Narrow<long>(value), rowIndex),
        DuckDBType.UnsignedTinyInt => AppendValueInternal(Narrow<byte>(value), rowIndex),
        DuckDBType.UnsignedSmallInt => AppendValueInternal(Narrow<ushort>(value), rowIndex),
        DuckDBType.UnsignedInteger => AppendValueInternal(Narrow<uint>(value), rowIndex),
        DuckDBType.UnsignedBigInt => AppendValueInternal(Narrow<ulong>(value), rowIndex),
        DuckDBType.HugeInt => AppendValueInternal(value, rowIndex),
        DuckDBType.UnsignedHugeInt when value >= 0 => AppendValueInternal((UInt128)value, rowIndex),
        DuckDBType.UnsignedHugeInt => throw OutOfRange(value),
        _ => throw new InvalidOperationException($"Cannot write an integer to {ColumnType} column")
    };

    private static bool IsIntegerColumn(DuckDBType type) => type is DuckDBType.TinyInt or DuckDBType.SmallInt or DuckDBType.Integer
        or DuckDBType.BigInt or DuckDBType.UnsignedTinyInt or DuckDBType.UnsignedSmallInt or DuckDBType.UnsignedInteger
        or DuckDBType.UnsignedBigInt;

    private TTarget Narrow<TTarget>(Int128 value) where TTarget : unmanaged, IBinaryInteger<TTarget>, IMinMaxValue<TTarget>
    {
        if (value < Int128.CreateTruncating(TTarget.MinValue) || value > Int128.CreateTruncating(TTarget.MaxValue))
        {
            throw OutOfRange(value);
        }

        return TTarget.CreateTruncating(value);
    }

    private OverflowException OutOfRange<TValue>(TValue value) =>
        new($"Value {value} is out of range for {ColumnType} column");

    private InvalidOperationException Mismatch<T>() =>
        new($"Cannot write {typeof(T).Name} to {ColumnType} column without an explicit conversion");
}

// The storage type of a column that is not a number, such as VARINT: no integer is copied as it is.
internal readonly struct NoStorage;
