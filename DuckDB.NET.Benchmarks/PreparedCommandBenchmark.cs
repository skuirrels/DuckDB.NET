using BenchmarkDotNet.Attributes;
using DuckDB.NET.Data;

namespace DuckDB.NET.Benchmarks;

[MemoryDiagnoser]
public class PreparedCommandBenchmark
{
    private DuckDBConnection connection = null!;
    private DuckDBCommand unpreparedCommand = null!;
    private DuckDBCommand preparedCommand = null!;
    private DuckDBParameter unpreparedParameter = null!;
    private DuckDBParameter preparedParameter = null!;
    private int nextValue;

    [GlobalSetup]
    public void Setup()
    {
        connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        unpreparedCommand = CreateCommand(out unpreparedParameter);
        preparedCommand = CreateCommand(out preparedParameter);
        preparedCommand.Prepare();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        preparedCommand.Dispose();
        unpreparedCommand.Dispose();
        connection.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int ExecuteUnprepared()
    {
        unpreparedParameter.Value = nextValue++;
        return (int)unpreparedCommand.ExecuteScalar()!;
    }

    [Benchmark]
    public int ExecutePrepared()
    {
        preparedParameter.Value = nextValue++;
        return (int)preparedCommand.ExecuteScalar()!;
    }

    // Preserves the former ExecuteScalar path as a direct same-process comparison.
    [Benchmark]
    public int ExecutePreparedViaReader()
    {
        preparedParameter.Value = nextValue++;
        using var reader = preparedCommand.ExecuteReader();
        return reader.Read() ? (int)reader.GetValue(0) : default;
    }

    private DuckDBCommand CreateCommand(out DuckDBParameter changingParameter)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT $first::INTEGER + $second::INTEGER + $third::INTEGER";
        changingParameter = new DuckDBParameter("first", 1);
        command.Parameters.Add(changingParameter);
        command.Parameters.Add(new DuckDBParameter("second", 2));
        command.Parameters.Add(new DuckDBParameter("third", 3));
        return command;
    }
}

[MemoryDiagnoser]
public class PreparedCommandSetupBenchmark
{
    private DuckDBConnection connection = null!;

    [GlobalSetup]
    public void Setup()
    {
        connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        connection.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void CreateUnpreparedCommand()
    {
        using var command = CreateCommand();
    }

    [Benchmark]
    public void CreateAndPrepareCommand()
    {
        using var command = CreateCommand();
        command.Prepare();
    }

    private DuckDBCommand CreateCommand()
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT $first::INTEGER + $second::INTEGER + $third::INTEGER";
        command.Parameters.Add(new DuckDBParameter("first", 1));
        command.Parameters.Add(new DuckDBParameter("second", 2));
        command.Parameters.Add(new DuckDBParameter("third", 3));
        return command;
    }
}
