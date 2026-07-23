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
