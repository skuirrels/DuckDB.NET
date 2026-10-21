namespace DuckDB.NET.Data.DataChunk.Writer;

internal sealed unsafe class DateTimeVectorDataWriter(IntPtr vector, void* vectorData, DuckDBType columnType) : VectorDataWriterBase(vector, vectorData, columnType)
{
    internal override bool AppendDateTime(DateTime value, ulong rowIndex)
    {
        if (ColumnType == DuckDBType.Date)
        {
            return AppendValueInternal(((DuckDBDateOnly)value.Date).ToDuckDBDate(), rowIndex);
        }

        var timestamp = value.ToTimestampStruct(ColumnType);

        return AppendValueInternal(timestamp, rowIndex);
    }

    internal override bool AppendDateTimeOffset(DateTimeOffset value, ulong rowIndex)
    {
        if (ColumnType == DuckDBType.TimeTz)
        {
            var timeTz = value.ToTimeTzStruct();

            return AppendValueInternal(timeTz, rowIndex);
        }

        if (ColumnType == DuckDBType.TimestampTz)
        {
            var timestamp = value.ToTimestampStruct();

            return AppendValueInternal(timestamp, rowIndex);
        }

        if (ColumnType == DuckDBType.TimestampTzNs)
        {
            return AppendValueInternal(value.ToTimestampNsStruct(), rowIndex);
        }

        return base.AppendDateTimeOffset(value, rowIndex);
    }

    internal override bool AppendDateOnly(DateOnly value, ulong rowIndex) => AppendValueInternal(((DuckDBDateOnly)value).ToDuckDBDate(), rowIndex);

    internal override bool AppendTimeOnly(TimeOnly value, ulong rowIndex)
    {
        if (ColumnType == DuckDBType.TimeNs)
        {
            return AppendValueInternal(value.ToDuckDBTimeNs(), rowIndex);
        }

        return AppendValueInternal(NativeMethods.DateTimeHelpers.DuckDBToTime(value), rowIndex);
    }

    internal override bool AppendDateOnly(DuckDBDateOnly value, ulong rowIndex) => AppendValueInternal(value.ToDuckDBDate(), rowIndex);

    // A TimeOnly given to IDuckDBAppenderRow becomes a DuckDBTimeOnly, so a TIME_NS column is written here too.
    internal override bool AppendTimeOnly(DuckDBTimeOnly value, ulong rowIndex)
    {
        var time = NativeMethods.DateTimeHelpers.DuckDBToTime(value);

        if (ColumnType == DuckDBType.TimeNs)
        {
            return AppendValueInternal(new DuckDBTimeNs { Nanos = time.Micros * 1000 }, rowIndex);
        }

        return AppendValueInternal(time, rowIndex);
    }
}
