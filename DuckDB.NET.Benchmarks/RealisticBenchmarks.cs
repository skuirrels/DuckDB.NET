using BenchmarkDotNet.Attributes;
using DuckDB.NET.Data;

namespace DuckDB.NET.Benchmarks;

[MemoryDiagnoser]
public class AnalyticalQueryBenchmark
{
    private DuckDBConnection connection = null!;
    private DuckDBCommand unpreparedCommand = null!;
    private DuckDBCommand preparedCommand = null!;
    private DuckDBParameter unpreparedCustomer = null!;
    private DuckDBParameter preparedCustomer = null!;
    private int nextCustomer;

    [GlobalSetup]
    public void Setup()
    {
        connection = RealisticWorkload.OpenConnection();
        RealisticWorkload.InitializeAnalytics(connection);

        unpreparedCommand = RealisticWorkload.CreateAnalyticsCommand(connection, out unpreparedCustomer);
        preparedCommand = RealisticWorkload.CreateAnalyticsCommand(connection, out preparedCustomer);
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
    public long ExecuteUnprepared()
    {
        unpreparedCustomer.Value = nextCustomer++ % 100;
        return RealisticWorkload.ConsumeAnalytics(unpreparedCommand);
    }

    [Benchmark]
    public long ExecutePrepared()
    {
        preparedCustomer.Value = nextCustomer++ % 100;
        return RealisticWorkload.ConsumeAnalytics(preparedCommand);
    }
}

[MemoryDiagnoser]
public class ResultMaterializationBenchmark
{
    private DuckDBConnection connection = null!;
    private DuckDBCommand command = null!;

    [GlobalSetup]
    public void Setup()
    {
        connection = RealisticWorkload.OpenConnection();
        RealisticWorkload.InitializeMaterialization(connection);
        command = connection.CreateCommand();
        command.CommandText = RealisticWorkload.MaterializationQuery;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        command.Dispose();
        connection.Dispose();
    }

    [Benchmark]
    public long ReadOneHundredThousandMixedRows() => RealisticWorkload.ConsumeMaterialization(command);
}

[MemoryDiagnoser]
public class BulkIngestionBenchmark
{
    private DuckDBConnection connection = null!;
    private DuckDBCommand preparedInsert = null!;
    private DuckDBParameter idParameter = null!;
    private DuckDBParameter eventTimeParameter = null!;
    private DuckDBParameter amountParameter = null!;
    private DuckDBParameter categoryParameter = null!;
    private DuckDBParameter isActiveParameter = null!;
    private IngestRow[] rows = null!;

    [GlobalSetup]
    public void Setup()
    {
        connection = RealisticWorkload.OpenConnection();
        RealisticWorkload.InitializeIngest(connection);
        rows = RealisticWorkload.CreateIngestRows();

        preparedInsert = connection.CreateCommand();
        preparedInsert.CommandText = RealisticWorkload.IngestStatement;
        idParameter = new DuckDBParameter("id", 0L);
        eventTimeParameter = new DuckDBParameter("eventTime", rows[0].EventTime);
        amountParameter = new DuckDBParameter("amount", 0.0);
        categoryParameter = new DuckDBParameter("category", rows[0].Category);
        isActiveParameter = new DuckDBParameter("isActive", false);
        preparedInsert.Parameters.Add(idParameter);
        preparedInsert.Parameters.Add(eventTimeParameter);
        preparedInsert.Parameters.Add(amountParameter);
        preparedInsert.Parameters.Add(categoryParameter);
        preparedInsert.Parameters.Add(isActiveParameter);
        preparedInsert.Prepare();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        preparedInsert.Dispose();
        connection.Dispose();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = RealisticWorkload.IngestRowCount)]
    public int InsertPreparedInTransaction()
    {
        using var transaction = connection.BeginTransaction();
        preparedInsert.Transaction = transaction;

        foreach (var row in rows)
        {
            idParameter.Value = row.Id;
            eventTimeParameter.Value = row.EventTime;
            amountParameter.Value = row.Amount;
            categoryParameter.Value = row.Category;
            isActiveParameter.Value = row.IsActive;
            preparedInsert.ExecuteNonQuery();
        }

        transaction.Rollback();
        preparedInsert.Transaction = null;
        return rows.Length;
    }

    [Benchmark(OperationsPerInvoke = RealisticWorkload.IngestRowCount)]
    public int InsertWithAppenderInTransaction()
    {
        using var transaction = connection.BeginTransaction();

        using (var appender = connection.CreateAppender("benchmark_ingest"))
        {
            foreach (var row in rows)
            {
                appender.CreateRow()
                    .AppendValue(row.Id)
                    .AppendValue(row.EventTime)
                    .AppendValue(row.Amount)
                    .AppendValue(row.Category)
                    .AppendValue(row.IsActive)
                    .EndRow();
            }
        }

        transaction.Rollback();
        return rows.Length;
    }

#if !DUCKDB_NET_BASELINE_1_5_3
    [Benchmark(OperationsPerInvoke = RealisticWorkload.IngestRowCount)]
    public int InsertWithScopedAppenderInTransaction()
    {
        using var transaction = connection.BeginTransaction();

        using (var appender = connection.CreateAppender("benchmark_ingest"))
        {
            foreach (var row in rows)
            {
                appender.AppendRowScoped(row, static (ref DuckDBAppenderRowWriter writer, IngestRow value) =>
                {
                    writer.AppendValue(value.Id);
                    writer.AppendValue(value.EventTime);
                    writer.AppendValue(value.Amount);
                    writer.AppendValue(value.Category);
                    writer.AppendValue(value.IsActive);
                });
            }
        }

        transaction.Rollback();
        return rows.Length;
    }
#endif
}

[MemoryDiagnoser]
public class TpchBenchmark
{
    private DuckDBConnection connection = null!;

    [GlobalSetup]
    public void Setup()
    {
        connection = RealisticWorkload.OpenConnection();
        RealisticWorkload.InitializeTpch(connection);
    }

    [GlobalCleanup]
    public void Cleanup() => connection.Dispose();

    [Benchmark]
    public long Query01() => Execute(1);

    [Benchmark]
    public long Query06() => Execute(6);

    [Benchmark]
    public long Query12() => Execute(12);

    [Benchmark]
    public long Query14() => Execute(14);

    private long Execute(int queryNumber)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA tpch({queryNumber})";
        return RealisticWorkload.ConsumeTpch(command);
    }
}
