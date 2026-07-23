using BenchmarkDotNet.Attributes;
using DuckDB.NET.Data;
using System.Collections.ObjectModel;

namespace DuckDB.NET.Benchmarks;

[MemoryDiagnoser]
public class ListAppenderBenchmark
{
    private const int ItemCount = 32;

    private DuckDBConnection connection = null!;
    private int[] arrayValues = null!;
    private List<int> listValues = null!;
    private ReadOnlyCollection<int> readOnlyValues = null!;

    [Params(1_000_000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        arrayValues = Enumerable.Range(0, ItemCount).ToArray();
        listValues = arrayValues.ToList();
        readOnlyValues = Array.AsReadOnly(arrayValues);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        connection.Dispose();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TABLE IF EXISTS list_from_array;
            DROP TABLE IF EXISTS list_from_list;
            DROP TABLE IF EXISTS list_from_read_only_collection;
            DROP TABLE IF EXISTS array_from_array;
            CREATE TABLE list_from_array (values INTEGER[]);
            CREATE TABLE list_from_list (values INTEGER[]);
            CREATE TABLE list_from_read_only_collection (values INTEGER[]);
            CREATE TABLE array_from_array (values INTEGER[32]);
            """;
        command.ExecuteNonQuery();
    }

    [Benchmark(Baseline = true)]
    public void AppendListFromArray()
    {
        using var appender = connection.CreateAppender("list_from_array");

        for (var index = 0; index < RowCount; index++)
        {
            appender.AppendRow(arrayValues, static (row, values) => row.AppendValue(values));
        }
    }

    [Benchmark]
    public void AppendListFromList()
    {
        using var appender = connection.CreateAppender("list_from_list");

        for (var index = 0; index < RowCount; index++)
        {
            appender.AppendRow(listValues, static (row, values) => row.AppendValue(values));
        }
    }

    [Benchmark]
    public void AppendListFromReadOnlyCollection()
    {
        using var appender = connection.CreateAppender("list_from_read_only_collection");

        for (var index = 0; index < RowCount; index++)
        {
            appender.AppendRow(readOnlyValues, static (row, values) => row.AppendValue(values));
        }
    }

    [Benchmark]
    public void AppendArrayFromArray()
    {
        using var appender = connection.CreateAppender("array_from_array");

        for (var index = 0; index < RowCount; index++)
        {
            appender.AppendRow(arrayValues, static (row, values) => row.AppendValue(values));
        }
    }
}
