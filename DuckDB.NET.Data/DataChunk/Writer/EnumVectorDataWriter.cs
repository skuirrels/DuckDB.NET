namespace DuckDB.NET.Data.DataChunk.Writer;

internal sealed unsafe class EnumVectorDataWriter(IntPtr vector, void* vectorData, DuckDBLogicalType logicalType, DuckDBType columnType) : VectorDataWriterBase(vector, vectorData, columnType)
{
    private readonly DuckDBType enumType = NativeMethods.LogicalType.DuckDBEnumInternalType(logicalType);

    private readonly uint enumDictionarySize = NativeMethods.LogicalType.DuckDBEnumDictionarySize(logicalType);

    private readonly Dictionary<string, uint> enumValues = new(StringComparer.OrdinalIgnoreCase);

    internal override bool AppendString(string value, ulong rowIndex)
    {
        EnsureEnumValuesInitialized();
        if (enumValues.TryGetValue(value, out var enumValue))
        {
            return AppendEnumValue(enumValue, rowIndex);
        }

        throw new InvalidOperationException($"Failed to write Enum column because the value \"{value}\" is not valid.");
    }

    internal override bool AppendEnum<TEnum>(TEnum value, ulong rowIndex)
    {
        if (typeof(TEnum).IsDefined(typeof(FlagsAttribute), false))
        {
            throw new InvalidOperationException("Failed to write Enum column because [Flags] enums are not supported.");
        }

        var enumName = Enum.GetName(value);
        if (enumName is not null)
        {
            EnsureEnumValuesInitialized();
            if (enumValues.TryGetValue(enumName, out var enumValue))
            {
                return AppendEnumValue(enumValue, rowIndex);
            }
        }

        var enumOrdinal = ConvertEnumValueToUInt64(value);
        if (enumOrdinal < enumDictionarySize)
        {
            return AppendEnumValue(enumOrdinal, rowIndex);
        }

        throw new InvalidOperationException($"Failed to write Enum column because the value is outside the range (0-{enumDictionarySize - 1}).");
    }

    private bool AppendEnumValue(ulong enumValue, ulong rowIndex)
    {
        // The following casts to byte and ushort are safe because we ensure in the constructor that the enumDictionarySize is not too high.
        return enumType switch
        {
            DuckDBType.UnsignedTinyInt => AppendValueInternal((byte)enumValue, rowIndex),
            DuckDBType.UnsignedSmallInt => AppendValueInternal((ushort)enumValue, rowIndex),
            DuckDBType.UnsignedInteger => AppendValueInternal((uint)enumValue, rowIndex),
            _ => throw new InvalidOperationException("Failed to write Enum column because the internal enum type must be utinyint, usmallint, or uinteger."),
        };
    }

    private void EnsureEnumValuesInitialized()
    {
        if (enumValues.Count != 0)
        {
            return;
        }

        for (uint index = 0; index < enumDictionarySize; index++)
        {
            var enumValueName = NativeMethods.LogicalType.DuckDBEnumDictionaryValue(logicalType, index);
            enumValues.Add(enumValueName, index);
        }
    }

    private static ulong ConvertEnumValueToUInt64<TEnum>(TEnum value) where TEnum : Enum
    {
        return value.GetTypeCode() switch
        {
            TypeCode.SByte => (ulong)Convert.ToSByte(value),
            TypeCode.Byte => Convert.ToByte(value),
            TypeCode.Int16 => (ulong)Convert.ToInt16(value),
            TypeCode.UInt16 => Convert.ToUInt16(value),
            TypeCode.Int32 => (ulong)Convert.ToInt32(value),
            TypeCode.UInt32 => Convert.ToUInt32(value),
            TypeCode.Int64 => (ulong)Convert.ToInt64(value),
            TypeCode.UInt64 => Convert.ToUInt64(value),
            _ => throw new InvalidOperationException($"Failed to convert the enum value {value} to ulong."),
        };
    }

}
