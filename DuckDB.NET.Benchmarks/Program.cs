using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using DuckDB.NET.Benchmarks;

// The repo's Directory.Build.props renames the output assembly to DuckDB.NET.Benchmarks
// while the project file stays Benchmarks.csproj, so BenchmarkDotNet's default toolchain
// can't locate the csproj. Run in-process to avoid the separate build/spawn step.
var config = DefaultConfig.Instance
    .AddJob(
        Job.Default
            .WithId("DriverComparison")
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithLaunchCount(1)
            .WithWarmupCount(5)
            .WithIterationCount(10)
            .WithInvocationCount(1)
            .WithUnrollFactor(1));

var benchmarkTypes = new List<Type>
{
#if !DUCKDB_NET_BASELINE_1_5_3
    typeof(AppenderBenchmark),
    typeof(ListAppenderBenchmark),
    typeof(MappedAppenderBenchmark),
#endif
    typeof(PreparedCommandBenchmark),
    typeof(PreparedCommandSetupBenchmark),
    typeof(AnalyticalQueryBenchmark),
    typeof(ResultMaterializationBenchmark),
    typeof(BulkIngestionBenchmark),
    typeof(TpchBenchmark),
};

#if DUCKDB_EFCORE_PROVIDER_1_13_0
benchmarkTypes.Add(typeof(EfCoreProviderBulkIngestionBenchmark));
#endif

BenchmarkSwitcher
    .FromTypes(benchmarkTypes.ToArray())
    .Run(args, config);
