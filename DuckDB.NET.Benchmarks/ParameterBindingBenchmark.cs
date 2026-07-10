using BenchmarkDotNet.Attributes;
using DuckDB.NET.Data;

namespace DuckDB.NET.Benchmarks;

// Finding #2: PreparedStatement.BindParameters runs a LINQ OfType<>().Any(...) on every
// command execution. This benchmark executes a small parameterized scalar query in a loop so
// the per-execution binding overhead dominates rather than result-set materialization.
[MemoryDiagnoser]
public class ParameterBindingBenchmark
{
    private DuckDBConnection connection = null!;
    private DuckDBCommand command = null!;

    [Params(5_000)]
    public int Executions { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        command = connection.CreateCommand();
        command.CommandText = "SELECT $a + $b + $c";
        command.Parameters.Add(new DuckDBParameter("a", 0));
        command.Parameters.Add(new DuckDBParameter("b", 0));
        command.Parameters.Add(new DuckDBParameter("c", 0));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        command.Dispose();
        connection.Dispose();
    }

    [Benchmark]
    public long ExecuteParameterized()
    {
        long sum = 0;

        for (var i = 0; i < Executions; i++)
        {
            command.Parameters[0].Value = i;
            command.Parameters[1].Value = i + 1;
            command.Parameters[2].Value = i + 2;

            sum += (long)Convert.ToInt64(command.ExecuteScalar());
        }

        return sum;
    }
}
