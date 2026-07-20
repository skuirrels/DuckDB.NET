using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace DuckDB.NET.Benchmarks;

/// <summary>
/// Measures DuckDB.EFCoreProvider's public appender-backed BulkInsert API over
/// the same 10,000-row transaction used by the driver ingestion benchmarks.
/// </summary>
[MemoryDiagnoser]
public class EfCoreProviderBulkIngestionBenchmark
{
    private DuckDBConnection connection = null!;
    private EfCoreProviderBenchmarkContext context = null!;
    private EfCoreIngestRow[] rows = null!;

    [GlobalSetup]
    public void Setup()
    {
        VerifyProviderVersion();

        connection = PreparedCommandWorkload.OpenVerifiedConnection();
        RealisticWorkload.InitializeIngest(connection);

        var options = new DbContextOptionsBuilder<EfCoreProviderBenchmarkContext>()
            .UseDuckDB(connection, contextOwnsConnection: false)
            .Options;

        context = new EfCoreProviderBenchmarkContext(options);
        rows = RealisticWorkload.CreateIngestRows()
            .Select(static row => new EfCoreIngestRow
            {
                Id = row.Id,
                EventTime = row.EventTime,
                Amount = row.Amount,
                Category = row.Category,
                IsActive = row.IsActive,
            })
            .ToArray();

        // Resolve and cache the provider's physical-column accessor plan before
        // measurement, matching the warmed steady-state used by every harness.
        context.BulkInsert(Array.Empty<EfCoreIngestRow>());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        context.Dispose();
        connection.Dispose();
    }

    [Benchmark(OperationsPerInvoke = RealisticWorkload.IngestRowCount)]
    public int BulkInsertInTransaction()
    {
        using var transaction = context.Database.BeginTransaction();
        var inserted = context.BulkInsert(rows);
        transaction.Rollback();
        return inserted;
    }

    private static void VerifyProviderVersion()
    {
        var version = typeof(DuckDBBulkExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (version is null || !version.StartsWith("1.9.0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The provider comparison requires DuckDB.EFCoreProvider 1.9.0, but loaded {version ?? "an unknown version"}.");
        }
    }
}

internal sealed class EfCoreProviderBenchmarkContext(
    DbContextOptions<EfCoreProviderBenchmarkContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<EfCoreIngestRow>();
        entity.ToTable("benchmark_ingest");
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(row => row.EventTime).HasColumnName("event_time");
        entity.Property(row => row.Amount).HasColumnName("amount");
        entity.Property(row => row.Category).HasColumnName("category");
        entity.Property(row => row.IsActive).HasColumnName("is_active");
    }
}

internal sealed class EfCoreIngestRow
{
    public long Id { get; init; }

    public DateTime EventTime { get; init; }

    public double Amount { get; init; }

    public string Category { get; init; } = string.Empty;

    public bool IsActive { get; init; }
}
