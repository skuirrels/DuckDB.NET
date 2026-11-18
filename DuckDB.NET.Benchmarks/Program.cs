using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using DuckDB.NET.Benchmarks;

// Two suites share this entry point.
//
// The default is the realistic driver-comparison suite: it runs in-process against the code it was built with, so
// the same sources can be compiled against other driver versions (see the DuckDB.NET.1_5_3 and EF Core lanes and
// run-driver-comparison.sh).
//
// --micro runs the micro suite: every benchmark twice, each in its own process, against a published package (the
// baseline) and against the code in this repository. --baseline-id and --baseline-version pick the package
// (default DuckDB.NET.Data.Full 2.0.0-preview.1). --aa runs the baseline against itself instead, which shows how much
// two identical builds differ on this machine: the noise every other run has to be read against.
var micro = TakeFlag(ref args, "--micro");

#if !DUCKDB_NET_BASELINE_1_5_3
if (micro)
{
    var aa = TakeFlag(ref args, "--aa");
    var baselineId = TakeOption(ref args, "--baseline-id") ?? "DuckDB.NET.Data.Full";
    var baselineVersion = TakeOption(ref args, "--baseline-version") ?? "2.0.0-preview.1";

    var baseline = Job.Default.WithMsBuildArguments("/p:UseNuGet=true", $"/p:DuckDBNuGetId={baselineId}", $"/p:DuckDBNuGetVersion={baselineVersion}");

    var microConfig = DefaultConfig.Instance
        .AddJob(baseline.WithId(baselineVersion).AsBaseline())
        .AddJob(aa ? baseline.WithId($"{baselineVersion}-again") : Job.Default.WithId("Local"))
        .HideColumns(Column.Arguments); // it only repeats what the Job column says

    BenchmarkSwitcher
        .FromTypes([typeof(CompositeReaderBenchmark), typeof(NumericAppenderBenchmark), typeof(HugeIntAppenderBenchmark), typeof(DateAppenderBenchmark), typeof(FunctionBenchmark), typeof(ListAppenderBenchmark), typeof(DecimalAppenderBenchmark), typeof(DecimalParameterBenchmark), typeof(StringAppenderBenchmark), typeof(RowCostBenchmark), typeof(TableFunctionRowBenchmark), typeof(EnumAppenderBenchmark), typeof(MappedAppenderBenchmark), typeof(DataReaderBenchmark), typeof(DateTimeReaderBenchmark), typeof(MixedNumericReaderBenchmark), typeof(StringReaderBenchmark), typeof(TypedReaderBenchmark), typeof(WideTableReaderBenchmark), typeof(CommandBenchmark), typeof(RowMaterializationBenchmark)])
        .Run(args, microConfig);
    return;
}
#endif

#if DUCKDB_NET_PACKAGE
throw new InvalidOperationException("A package build only runs the micro suite (--micro).");
#else
// Run in-process so the lanes that link these sources need no separate build/spawn step.
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
    typeof(ListSourceAppenderBenchmark),
    typeof(ListMaterializationBenchmark),
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
#endif

static bool TakeFlag(ref string[] args, string flag)
{
    var found = args.Contains(flag);
    args = args.Where(arg => arg != flag).ToArray();
    return found;
}

static string? TakeOption(ref string[] args, string option)
{
    var index = Array.IndexOf(args, option);
    if (index < 0 || index + 1 >= args.Length)
    {
        return null;
    }

    var value = args[index + 1];
    args = args.Where((_, i) => i != index && i != index + 1).ToArray();
    return value;
}
