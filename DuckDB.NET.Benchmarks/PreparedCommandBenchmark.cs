using BenchmarkDotNet.Attributes;
using DuckDB.NET.Data;

namespace DuckDB.NET.Benchmarks;

[MemoryDiagnoser]
public class PreparedCommandBenchmark
{
    private DuckDBConnection connection = null!;
    private DuckDBCommand unpreparedCommand = null!;
    private DuckDBCommand boxedPreparedCommand = null!;
    private DuckDBCommand preparedCommand = null!;
    private DuckDBParameter unpreparedParameter = null!;
    private DuckDBParameter boxedPreparedParameter = null!;
    private DuckDBParameter<int> preparedParameter = null!;
    private int nextValue;

    [GlobalSetup]
    public void Setup()
    {
        connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        unpreparedCommand = CreateCommand(out unpreparedParameter);
        boxedPreparedCommand = CreateCommand(out boxedPreparedParameter);
        boxedPreparedCommand.Prepare();
        preparedCommand = CreateTypedCommand(out preparedParameter);
        preparedCommand.Prepare();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        preparedCommand.Dispose();
        boxedPreparedCommand.Dispose();
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
    public int ExecutePreparedBoxed()
    {
        boxedPreparedParameter.Value = nextValue++;
        return (int)boxedPreparedCommand.ExecuteScalar()!;
    }

    [Benchmark]
    public int ExecutePrepared()
    {
        preparedParameter.TypedValue = nextValue++;
        return (int)preparedCommand.ExecuteScalar()!;
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

    private DuckDBCommand CreateTypedCommand(out DuckDBParameter<int> changingParameter)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT $first::INTEGER + $second::INTEGER + $third::INTEGER";
        changingParameter = new DuckDBParameter<int>("first", 1);
        command.Parameters.Add(changingParameter);
        command.Parameters.Add(new DuckDBParameter<int>("second", 2));
        command.Parameters.Add(new DuckDBParameter<int>("third", 3));
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
