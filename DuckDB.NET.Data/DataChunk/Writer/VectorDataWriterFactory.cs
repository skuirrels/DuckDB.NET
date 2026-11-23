namespace DuckDB.NET.Data.DataChunk.Writer;

internal static class VectorDataWriterFactory
{
    public static unsafe VectorDataWriterBase CreateWriter(IntPtr vector, DuckDBLogicalType logicalType)
    {
        var dataPointer = NativeMethods.Vectors.DuckDBVectorGetData(vector);
        var columnType = NativeMethods.LogicalType.DuckDBGetTypeId(logicalType);

        return columnType switch
        {
            DuckDBType.Uuid => new GuidVectorDataWriter(vector, dataPointer, columnType),
            DuckDBType.Date => new DateTimeVectorDataWriter(vector, dataPointer, columnType),
            DuckDBType.Time => new TimeVectorDataWriter<DuckDBTime>(vector, dataPointer, columnType),
            DuckDBType.TimeNs => new TimeVectorDataWriter<DuckDBTimeNs>(vector, dataPointer, columnType),
            DuckDBType.TimeTz => new DateTimeVectorDataWriter(vector, dataPointer, columnType),
            DuckDBType.Interval => new IntervalVectorDataWriter(vector, dataPointer, columnType),
            DuckDBType.Timestamp => new DateTimeVectorDataWriter(vector, dataPointer, columnType),
                
            DuckDBType.Boolean => new BooleanVectorDataWriter(vector, dataPointer, columnType),

            DuckDBType.Map => new MapVectorDataWriter(vector, dataPointer, columnType, logicalType),
            DuckDBType.List => new ListVectorDataWriter(vector, dataPointer, columnType, logicalType),
            DuckDBType.Array => new ArrayVectorDataWriter(vector, dataPointer, columnType, logicalType),
            DuckDBType.Blob => new StringVectorDataWriter(vector, dataPointer, columnType),
            DuckDBType.Varchar => new StringVectorDataWriter(vector, dataPointer, columnType),
            DuckDBType.Bit => throw new NotImplementedException($"Writing {columnType} to data chunk is not yet supported"),
            DuckDBType.Enum => new EnumVectorDataWriter(vector, dataPointer, logicalType, columnType),
            DuckDBType.Struct => throw new NotImplementedException($"Writing {columnType} to data chunk is not yet supported"),
            DuckDBType.Decimal => new DecimalVectorDataWriter(vector, dataPointer, logicalType, columnType),
            DuckDBType.TimestampS => new DateTimeVectorDataWriter(vector, dataPointer, columnType),
            DuckDBType.TimestampMs => new DateTimeVectorDataWriter(vector, dataPointer, columnType),
            DuckDBType.TimestampNs => new DateTimeVectorDataWriter(vector, dataPointer, columnType),
            DuckDBType.TimestampTz => new DateTimeVectorDataWriter(vector, dataPointer, columnType),
            DuckDBType.TimestampTzNs => new DateTimeVectorDataWriter(vector, dataPointer, columnType),
            DuckDBType.TinyInt => new NumericVectorDataWriter<sbyte>(vector, dataPointer, columnType),
            DuckDBType.SmallInt => new NumericVectorDataWriter<short>(vector, dataPointer, columnType),
            DuckDBType.Integer => new NumericVectorDataWriter<int>(vector, dataPointer, columnType),
            DuckDBType.BigInt => new NumericVectorDataWriter<long>(vector, dataPointer, columnType),
            DuckDBType.UnsignedTinyInt => new NumericVectorDataWriter<byte>(vector, dataPointer, columnType),
            DuckDBType.UnsignedSmallInt => new NumericVectorDataWriter<ushort>(vector, dataPointer, columnType),
            DuckDBType.UnsignedInteger => new NumericVectorDataWriter<uint>(vector, dataPointer, columnType),
            DuckDBType.UnsignedBigInt => new NumericVectorDataWriter<ulong>(vector, dataPointer, columnType),
            DuckDBType.HugeInt => new NumericVectorDataWriter<Int128>(vector, dataPointer, columnType),
            DuckDBType.UnsignedHugeInt => new NumericVectorDataWriter<UInt128>(vector, dataPointer, columnType),
            DuckDBType.Float => new NumericVectorDataWriter<float>(vector, dataPointer, columnType),
            DuckDBType.Double => new NumericVectorDataWriter<double>(vector, dataPointer, columnType),
            _ => new NumericVectorDataWriter<NoStorage>(vector, dataPointer, columnType)
        };
    }
}
