using DuckDB.NET.Data.Mapping;
using System.Threading;

namespace DuckDB.NET.Data;

/// <summary>
/// A type-safe appender that uses AppenderMap to validate type mappings.
/// </summary>
/// <typeparam name="T">The type being appended</typeparam>
/// <typeparam name="TMap">The AppenderMap type defining the mappings</typeparam>
public class DuckDBMappedAppender<T, TMap> : IDisposable where TMap : DuckDBAppenderMap<T>, new()
{
    private static readonly Lazy<CompiledAppenderMap> CompiledMap =
        new(CreateCompiledMap, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly DuckDBAppender appender;
    private readonly Action<IDuckDBAppenderRow, T> writeRecord;

    internal DuckDBMappedAppender(DuckDBAppender appender)
    {
        this.appender = appender;
        var compiledMap = CompiledMap.Value;
        var mappings = compiledMap.Mappings;
        writeRecord = compiledMap.WriteRecord;

        // Validate mappings match the table structure
        if (mappings.Count == 0)
        {
            throw new InvalidOperationException($"AppenderMap {typeof(TMap).Name} has no property mappings defined");
        }

        var columnTypes = appender.LogicalTypes;
        if (mappings.Count != columnTypes.Count)
        {
            throw new InvalidOperationException($"AppenderMap {typeof(TMap).Name} has {mappings.Count} mappings but table has {columnTypes.Count} columns");
        }

        for (int index = 0; index < mappings.Count; index++)
        {
            var mapping = mappings[index];

            if (mapping.MappingType != PropertyMappingType.Property)
            {
                continue;
            }

            var columnType = NativeMethods.LogicalType.DuckDBGetTypeId(columnTypes[index]);

            // Only types that are not scalars need the reflection-based dictionary check.
            if (mapping.PropertyType.UnderlyingTypeOrSelf().GetDuckDBType() == DuckDBType.Invalid &&
                IsDictionaryType(mapping.PropertyType))
            {
                ValidateMapColumn(index, mapping.PropertyType, columnTypes[index], columnType);
                continue;
            }

            var expectedType = GetExpectedDuckDBType(mapping.PropertyType);

            if (expectedType != columnType)
            {
                throw new InvalidOperationException(
                    $"Type mismatch at column index {index}: Mapped type is {mapping.PropertyType.Name} (expected DuckDB type: {expectedType}) but actual column type is {columnType}");
            }
        }

    }

    /// <summary>
    /// Appends multiple records to the table.
    /// </summary>
    /// <param name="records">The records to append</param>
    public void AppendRecords(IEnumerable<T> records)
    {
        if (records == null)
        {
            throw new ArgumentNullException(nameof(records));
        }

        foreach (var record in records)
        {
            AppendRecord(record);
        }
    }

    private void AppendRecord(T record)
    {
        if (record == null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        appender.AppendRow(record, writeRecord);
    }

    private static DuckDBType GetExpectedDuckDBType(Type type)
    {
        var duckDBType = type.UnderlyingTypeOrSelf().GetDuckDBType();

        return duckDBType switch
        {
            DuckDBType.Invalid => throw new NotSupportedException($"Type {type.Name} is not supported for mapping"),
            _ => duckDBType
        };
    }

    private static bool IsDictionaryType(Type type) =>
        type.TryGetDictionaryTypes(out _, out _) || typeof(IDictionary).IsAssignableFrom(type);

    private static void ValidateMapColumn(int index, Type propertyType, DuckDBLogicalType columnLogicalType, DuckDBType columnType)
    {
        if (columnType != DuckDBType.Map)
        {
            throw new InvalidOperationException(
                $"Type mismatch at column index {index}: Mapped type is {propertyType.Name} (expected DuckDB type: {DuckDBType.Map}) but actual column type is {columnType}");
        }

        // A non-generic IDictionary carries no key or value type to check.
        if (!propertyType.TryGetDictionaryTypes(out var keyType, out var valueType))
        {
            return;
        }

        using var keyLogicalType = NativeMethods.LogicalType.DuckDBMapTypeKeyType(columnLogicalType);
        using var valueLogicalType = NativeMethods.LogicalType.DuckDBMapTypeValueType(columnLogicalType);

        var columnKeyType = NativeMethods.LogicalType.DuckDBGetTypeId(keyLogicalType);
        var columnValueType = NativeMethods.LogicalType.DuckDBGetTypeId(valueLogicalType);

        var expectedKeyType = GetExpectedDuckDBType(keyType);
        var expectedValueType = GetExpectedDuckDBType(valueType);

        if (expectedKeyType != columnKeyType || expectedValueType != columnValueType)
        {
            throw new InvalidOperationException(
                $"Type mismatch at column index {index}: Mapped type is {propertyType.Name} (expected DuckDB type: {DuckDBType.Map}({expectedKeyType}, {expectedValueType})) but actual column type is {DuckDBType.Map}({columnKeyType}, {columnValueType})");
        }
    }

    private static CompiledAppenderMap CreateCompiledMap()
    {
        var mappings = new TMap().PropertyMappings;
        return new CompiledAppenderMap(mappings, DuckDBAppenderMapCompiler.Compile(mappings));
    }

    private sealed record CompiledAppenderMap(
        IReadOnlyList<IPropertyMapping<T>> Mappings,
        Action<IDuckDBAppenderRow, T> WriteRecord);

    /// <summary>
    /// Closes the appender and flushes any remaining data.
    /// </summary>
    public void Close()
    {
        appender.Close();
    }

    /// <summary>
    /// Disposes the appender.
    /// </summary>
    public void Dispose()
    {
        appender.Dispose();
    }
}
