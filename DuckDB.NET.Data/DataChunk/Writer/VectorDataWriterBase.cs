using System.Runtime.CompilerServices;

namespace DuckDB.NET.Data.DataChunk.Writer;

internal unsafe class VectorDataWriterBase(IntPtr vector, void* vectorData, DuckDBType columnType) : IDuckDBDataWriter, IDisposable
{
    private ulong* validity;

    internal IntPtr Vector => vector;
    internal DuckDBType ColumnType => columnType;

    public void WriteNull(ulong rowIndex)
    {
        if (validity == default)
        {
            NativeMethods.Vectors.DuckDBVectorEnsureValidityWritable(Vector);
            validity = NativeMethods.Vectors.DuckDBVectorGetValidity(Vector);
        }

        NativeMethods.ValidityMask.DuckDBValiditySetRowValidity(validity, rowIndex, false);
    }

    private void WriteValueFallback<T>(T value, ulong rowIndex)
    {
        if (value == null)
        {
            WriteNull(rowIndex);
            return;
        }

        _ = value switch
        {
            bool val => AppendBool(val, rowIndex),

            sbyte val => AppendNumeric(val, rowIndex),
            short val => AppendNumeric(val, rowIndex),
            int val => AppendNumeric(val, rowIndex),
            long val => AppendNumeric(val, rowIndex),

            byte val => AppendNumeric(val, rowIndex),
            ushort val => AppendNumeric(val, rowIndex),
            uint val => AppendNumeric(val, rowIndex),
            ulong val => AppendNumeric(val, rowIndex),

            float val => AppendNumeric(val, rowIndex),
            double val => AppendNumeric(val, rowIndex),

            decimal val => AppendDecimal(val, rowIndex),
            BigInteger val => AppendBigInteger(val, rowIndex),

            Enum val => AppendEnum(val, rowIndex),

            string val => AppendString(val, rowIndex),
            Guid val => AppendGuid(val, rowIndex),
            DateTime val => AppendDateTime(val, rowIndex),
            TimeSpan val => AppendTimeSpan(val, rowIndex),
            DuckDBDateOnly val => AppendDateOnly(val, rowIndex),
            DuckDBTimeOnly val => AppendTimeOnly(val, rowIndex),
            DateOnly val => AppendDateOnly(val, rowIndex),
            TimeOnly val => AppendTimeOnly(val, rowIndex),
            DateTimeOffset val => AppendDateTimeOffset(val, rowIndex),
            ICollection val => AppendCollection(val, rowIndex),
            _ => ThrowException<T>()
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteValue<T>(T value, ulong rowIndex)
    {
        if (typeof(T) == typeof(bool))
        {
            _ = AppendBool(Unsafe.As<T, bool>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(bool?))
        {
            var typedValue = Unsafe.As<T, bool?>(ref value);
            if (typedValue.HasValue) _ = AppendBool(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(sbyte))
        {
            _ = AppendNumeric(Unsafe.As<T, sbyte>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(sbyte?))
        {
            var typedValue = Unsafe.As<T, sbyte?>(ref value);
            if (typedValue.HasValue) _ = AppendNumeric(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(short))
        {
            _ = AppendNumeric(Unsafe.As<T, short>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(short?))
        {
            var typedValue = Unsafe.As<T, short?>(ref value);
            if (typedValue.HasValue) _ = AppendNumeric(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(int))
        {
            _ = AppendNumeric(Unsafe.As<T, int>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(int?))
        {
            var typedValue = Unsafe.As<T, int?>(ref value);
            if (typedValue.HasValue) _ = AppendNumeric(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(long))
        {
            _ = AppendNumeric(Unsafe.As<T, long>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(long?))
        {
            var typedValue = Unsafe.As<T, long?>(ref value);
            if (typedValue.HasValue) _ = AppendNumeric(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(byte))
        {
            _ = AppendNumeric(Unsafe.As<T, byte>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(byte?))
        {
            var typedValue = Unsafe.As<T, byte?>(ref value);
            if (typedValue.HasValue) _ = AppendNumeric(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(ushort))
        {
            _ = AppendNumeric(Unsafe.As<T, ushort>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(ushort?))
        {
            var typedValue = Unsafe.As<T, ushort?>(ref value);
            if (typedValue.HasValue) _ = AppendNumeric(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(uint))
        {
            _ = AppendNumeric(Unsafe.As<T, uint>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(uint?))
        {
            var typedValue = Unsafe.As<T, uint?>(ref value);
            if (typedValue.HasValue) _ = AppendNumeric(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(ulong))
        {
            _ = AppendNumeric(Unsafe.As<T, ulong>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(ulong?))
        {
            var typedValue = Unsafe.As<T, ulong?>(ref value);
            if (typedValue.HasValue) _ = AppendNumeric(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(float))
        {
            _ = AppendNumeric(Unsafe.As<T, float>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(float?))
        {
            var typedValue = Unsafe.As<T, float?>(ref value);
            if (typedValue.HasValue) _ = AppendNumeric(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(double))
        {
            _ = AppendNumeric(Unsafe.As<T, double>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(double?))
        {
            var typedValue = Unsafe.As<T, double?>(ref value);
            if (typedValue.HasValue) _ = AppendNumeric(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(decimal))
        {
            _ = AppendDecimal(Unsafe.As<T, decimal>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(decimal?))
        {
            var typedValue = Unsafe.As<T, decimal?>(ref value);
            if (typedValue.HasValue) _ = AppendDecimal(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(BigInteger))
        {
            _ = AppendBigInteger(Unsafe.As<T, BigInteger>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(BigInteger?))
        {
            var typedValue = Unsafe.As<T, BigInteger?>(ref value);
            if (typedValue.HasValue) _ = AppendBigInteger(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(string))
        {
            var typedValue = Unsafe.As<T, string?>(ref value);
            if (typedValue is not null) _ = AppendString(typedValue, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(Guid))
        {
            _ = AppendGuid(Unsafe.As<T, Guid>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(Guid?))
        {
            var typedValue = Unsafe.As<T, Guid?>(ref value);
            if (typedValue.HasValue) _ = AppendGuid(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(DateTime))
        {
            _ = AppendDateTime(Unsafe.As<T, DateTime>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(DateTime?))
        {
            var typedValue = Unsafe.As<T, DateTime?>(ref value);
            if (typedValue.HasValue) _ = AppendDateTime(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(TimeSpan))
        {
            _ = AppendTimeSpan(Unsafe.As<T, TimeSpan>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(TimeSpan?))
        {
            var typedValue = Unsafe.As<T, TimeSpan?>(ref value);
            if (typedValue.HasValue) _ = AppendTimeSpan(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(DuckDBDateOnly))
        {
            _ = AppendDateOnly(Unsafe.As<T, DuckDBDateOnly>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(DuckDBDateOnly?))
        {
            var typedValue = Unsafe.As<T, DuckDBDateOnly?>(ref value);
            if (typedValue.HasValue) _ = AppendDateOnly(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(DuckDBTimeOnly))
        {
            _ = AppendTimeOnly(Unsafe.As<T, DuckDBTimeOnly>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(DuckDBTimeOnly?))
        {
            var typedValue = Unsafe.As<T, DuckDBTimeOnly?>(ref value);
            if (typedValue.HasValue) _ = AppendTimeOnly(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(DateOnly))
        {
            _ = AppendDateOnly(Unsafe.As<T, DateOnly>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(DateOnly?))
        {
            var typedValue = Unsafe.As<T, DateOnly?>(ref value);
            if (typedValue.HasValue) _ = AppendDateOnly(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(TimeOnly))
        {
            _ = AppendTimeOnly(Unsafe.As<T, TimeOnly>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(TimeOnly?))
        {
            var typedValue = Unsafe.As<T, TimeOnly?>(ref value);
            if (typedValue.HasValue) _ = AppendTimeOnly(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        if (typeof(T) == typeof(DateTimeOffset))
        {
            _ = AppendDateTimeOffset(Unsafe.As<T, DateTimeOffset>(ref value), rowIndex);
            return;
        }

        if (typeof(T) == typeof(DateTimeOffset?))
        {
            var typedValue = Unsafe.As<T, DateTimeOffset?>(ref value);
            if (typedValue.HasValue) _ = AppendDateTimeOffset(typedValue.Value, rowIndex); else WriteNull(rowIndex);
            return;
        }

        WriteValueFallback(value, rowIndex);
    }

    internal virtual bool AppendBool(bool value, ulong rowIndex) => ThrowException<bool>();

    internal virtual bool AppendDecimal(decimal value, ulong rowIndex) => ThrowException<decimal>();

    internal virtual bool AppendTimeSpan(TimeSpan value, ulong rowIndex) => ThrowException<TimeSpan>();

    internal virtual bool AppendGuid(Guid value, ulong rowIndex) => ThrowException<Guid>();

    internal virtual bool AppendBlob(byte* value, int length, ulong rowIndex) => ThrowException<byte[]>();

    internal virtual bool AppendString(string value, ulong rowIndex) => ThrowException<string>();

    internal virtual bool AppendDateTime(DateTime value, ulong rowIndex) => ThrowException<DateTime>();

    internal virtual bool AppendDateOnly(DateOnly value, ulong rowIndex) => ThrowException<DateOnly>();

    internal virtual bool AppendTimeOnly(TimeOnly value, ulong rowIndex) => ThrowException<TimeOnly>();

    internal virtual bool AppendDateOnly(DuckDBDateOnly value, ulong rowIndex) => ThrowException<DuckDBDateOnly>();

    internal virtual bool AppendTimeOnly(DuckDBTimeOnly value, ulong rowIndex) => ThrowException<DuckDBTimeOnly>();

    internal virtual bool AppendDateTimeOffset(DateTimeOffset value, ulong rowIndex) => ThrowException<DateTimeOffset>();

    internal virtual bool AppendNumeric<T>(T value, ulong rowIndex) where T : unmanaged => ThrowException<T>();

    internal virtual bool AppendBigInteger(BigInteger value, ulong rowIndex) => ThrowException<BigInteger>();

    internal virtual bool AppendEnum<TEnum>(TEnum value, ulong rowIndex) where TEnum : Enum => ThrowException<TEnum>();

    internal virtual bool AppendCollection(ICollection value, ulong rowIndex) => ThrowException<ICollection>();

    private bool ThrowException<T>()
    {
        throw new InvalidOperationException($"Cannot write {typeof(T).Name} to {columnType} column");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool AppendValueInternal<T>(T value, ulong rowIndex) where T : unmanaged
    {
        ((T*)vectorData)[rowIndex] = value;
        return true;
    }

    internal void InitializeWriter()
    {
        validity = default;
        vectorData = NativeMethods.Vectors.DuckDBVectorGetData(Vector);
    }

    public virtual void Dispose()
    {

    }
}
