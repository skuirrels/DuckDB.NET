# DuckDB .NET, EF Core, Go, and Java benchmark comparison

## Technical summary

- **Tuned local DuckDB.NET 1.5.4 wins reusable prepared insertion** at
  19.931 us/row, 2.64x faster than both released DuckDB.NET 1.5.3 and the
  ADO.NET driver bundled by `DuckDB.EFCoreProvider` 1.9.0.
- **The EF provider's real public `DbContext.BulkInsert` path measures
  261.3 ns/row.** That is 4.8% slower than accessing its bundled Appender
  directly.
- **Java wins high-throughput Appender ingestion** at 168.3 ns/row, with Go
  close behind at 172.7 ns/row.
- **The three .NET lanes are effectively tied on mixed-type result
  materialization** and materially faster than Go and Java in this run.

## Key findings: side-by-side results

Results are from run `20260716T211118Z` on an Apple M4 Pro. Lower latency is
better; higher throughput is better. Values are harness-reported means, with
the arithmetic mean of ten reported repetitions used for Go.

| Real-work benchmark | DuckDB.NET 1.5.3 | EFCoreProvider 1.9.0 | Tuned .NET 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Parameterized analytical query, prepared | **2.417 ms** | 2.422 ms* | 2.428 ms | 2.431 ms | 2.526 ms | **DuckDB.NET 1.5.3** |
| Materialize 100,000 mixed-type rows | **9.347 ms** | 9.497 ms* | 9.470 ms | 52.774 ms | 26.292 ms | **DuckDB.NET 1.5.3** |
| Insert with reusable prepared statement | 52.573 us/row | 52.604 us/row* | **19.931 us/row** | 25.508 us/row | 28.906 us/row | **Tuned .NET 1.5.4** |
| Idiomatic high-throughput insert | 245.3 ns/row Appender | 261.3 ns/row `BulkInsert` | 242.4 ns/row Appender | **168.3 ns/row Appender** | 172.7 ns/row Appender | **Java JDBC 1.5.4** |
| TPC-H Q1, SF 0.1 | 13.1018 ms | **13.0752 ms*** | 13.1425 ms | 13.0767 ms | 13.3362 ms | **EF provider dependency path, effectively tied with Java** |
| TPC-H Q6, SF 0.1 | **0.8619 ms** | 0.8660 ms* | 0.8778 ms | 0.8755 ms | 0.8850 ms | **DuckDB.NET 1.5.3** |
| TPC-H Q12, SF 0.1 | 7.2306 ms | 7.2937 ms* | 7.2821 ms | **7.1969 ms** | 7.3073 ms | **Java JDBC 1.5.4** |
| TPC-H Q14, SF 0.1 | 1.4774 ms | 1.4779 ms* | **1.4642 ms** | 1.5230 ms | 1.5207 ms | **Tuned .NET 1.5.4** |

## Scope, packages, and metric definitions

The suite compares five package-level lanes:

- released `DuckDB.NET.Data.Full` 1.5.3;
- `DuckDB.EFCoreProvider` 1.9.0;
- the locally performance-tuned DuckDB.NET 1.5.4 source in this checkout;
- DuckDB's core-team-maintained `org.duckdb:duckdb_jdbc` 1.5.4.0 driver;
- `github.com/duckdb/duckdb-go/v2` v2.10504.0, using DuckDB 1.5.4.

`DuckDB.EFCoreProvider` 1.9.0 has a transitive dependency on
`DuckDB.NET.Data.Full` 1.5.3. The EF provider column therefore contains two
clearly separated kinds of measurements:

- **Public EF provider path:** `DbContext.BulkInsert`, which is the provider's
  appender-backed bulk-ingestion API.
- **Bundled ADO.NET dependency path:** the identical SQL, prepared-command,
  materialization, and TPC-H workloads executed through the provider package's
  bundled DuckDB.NET 1.5.3 dependency. These cells are marked with `*`.

The report does not claim that the `*` cells measure EF LINQ translation or EF
change tracking. They show the performance an application receives from the
ADO.NET driver bundled by `DuckDB.EFCoreProvider` 1.9.0.

## Parameterized analytical query

This query filters and aggregates a deterministic 2,000,000-row orders table,
groups by year and region, orders the result, and consumes every returned
value.

| Measured operation | DuckDB.NET 1.5.3 | EFCoreProvider 1.9.0 | Tuned .NET 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Unprepared | **2.377 ms** | 2.421 ms* | 2.501 ms | 2.487 ms | 2.565 ms | **DuckDB.NET 1.5.3** |
| Prepared and reused | **2.417 ms** | 2.422 ms* | 2.428 ms | 2.431 ms | 2.526 ms | **DuckDB.NET 1.5.3** |
| Prepared/unprepared ratio | 1.017x | 1.000x* | 0.971x | 0.977x | 0.985x | **No meaningful winner; scan and aggregation dominate** |

All prepared results are within 4.5% of one another. Preparation materially
helps the tuned local, Java, and Go scalar paths, but it is not the dominant
cost once the query scans and aggregates two million rows.

## Result materialization

All providers fully read the same 100,000 ordered rows containing `BIGINT`,
`DATE`, `TIMESTAMP`, `DOUBLE`, nullable `VARCHAR`, and `BOOLEAN`, while building
a checksum so values cannot be skipped.

| Measured operation | DuckDB.NET 1.5.3 | EFCoreProvider 1.9.0 | Tuned .NET 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Mean latency | **9.347 ms** | 9.497 ms* | 9.470 ms | 52.774 ms | 26.292 ms | **DuckDB.NET 1.5.3** |
| Materialization throughput | **10.70 million rows/s** | 10.53 million rows/s* | 10.56 million rows/s | 1.89 million rows/s | 3.80 million rows/s | **DuckDB.NET 1.5.3** |

The three .NET observations are within 1.6%, so they should be considered
effectively tied. The meaningful result is that this typed ADO.NET
materialization path is about 2.8x faster than Go and 5.6x faster than Java in
this run.

## Bulk ingestion

Each invocation writes the same 10,000 precomputed mixed-type rows inside an
explicit transaction and rolls that transaction back. Input creation and
provider metadata warmup happen outside the measurement.

### Equivalent ingestion paths

| Measured operation | DuckDB.NET 1.5.3 | EFCoreProvider 1.9.0 | Tuned .NET 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Prepared insert latency | 52.573 us/row | 52.604 us/row* | **19.931 us/row** | 25.508 us/row | 28.906 us/row | **Tuned .NET 1.5.4** |
| Prepared insert throughput | 19,021 rows/s | 19,010 rows/s* | **50,173 rows/s** | 39,204 rows/s | 34,595 rows/s | **Tuned .NET 1.5.4** |
| Direct Appender latency | 245.3 ns/row | 249.4 ns/row* | 242.4 ns/row | **168.3 ns/row** | 172.7 ns/row | **Java JDBC 1.5.4** |
| Direct Appender throughput | 4.08 million rows/s | 4.01 million rows/s* | 4.13 million rows/s | **5.94 million rows/s** | 5.79 million rows/s | **Java JDBC 1.5.4** |
| Idiomatic bulk API latency | 245.3 ns/row Appender | 261.3 ns/row `BulkInsert` | 242.4 ns/row Appender | **168.3 ns/row Appender** | 172.7 ns/row Appender | **Java JDBC 1.5.4** |
| Idiomatic bulk API throughput | 4.08 million rows/s | 3.83 million rows/s `BulkInsert` | 4.13 million rows/s | **5.94 million rows/s** | 5.79 million rows/s | **Java JDBC 1.5.4** |

The tuned local prepared insert is 2.64x faster than both the released
DuckDB.NET package and the ADO.NET dependency bundled with the EF provider. It
is 1.28x faster than Java and 1.45x faster than Go.

Java has the lowest Appender latency, with Go 2.6% behind it. The direct .NET
Appender results are close to one another, confirming that the prepared-command
tuning is not responsible for Appender performance.

### EF provider `BulkInsert` overhead

This isolates the extra public-provider layer by comparing two measurements
from the same EF provider benchmark project and the same bundled DuckDB.NET
1.5.3 dependency.

| Measurement | Direct bundled Appender | Public EF `DbContext.BulkInsert` | Difference | Winner |
| --- | ---: | ---: | ---: | --- |
| Latency per row | **249.4 ns** | 261.3 ns | `BulkInsert` is 11.9 ns, or 4.8%, slower | **Direct bundled Appender** |
| Throughput | **4.01 million rows/s** | 3.83 million rows/s | `BulkInsert` processes about 4.6% fewer rows/s | **Direct bundled Appender** |

The EF provider's public bulk path adds only 4.8% latency over the direct
Appender dependency path in this warmed 10,000-row scenario.

## TPC-H analytical queries

The suite uses DuckDB's official `tpch` extension to generate scale factor 0.1
during setup, then executes and fully consumes Q1, Q6, Q12, and Q14. Every
connection uses one DuckDB thread.

| Query | DuckDB.NET 1.5.3 | EFCoreProvider 1.9.0 | Tuned .NET 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Q1 | 13.1018 ms | **13.0752 ms*** | 13.1425 ms | 13.0767 ms | 13.3362 ms | **EF provider dependency path, effectively tied with Java** |
| Q6 | **0.8619 ms** | 0.8660 ms* | 0.8778 ms | 0.8755 ms | 0.8850 ms | **DuckDB.NET 1.5.3** |
| Q12 | 7.2306 ms | 7.2937 ms* | 7.2821 ms | **7.1969 ms** | 7.3073 ms | **Java JDBC 1.5.4** |
| Q14 | 1.4774 ms | 1.4779 ms* | **1.4642 ms** | 1.5230 ms | 1.5207 ms | **Tuned .NET 1.5.4** |

The winners change by query and the gaps are small. Q1's first two observations
differ by only 0.0015 ms. Treat these as essentially comparable engine-query
results rather than evidence that one wrapper is universally faster.

## Prepared scalar microbenchmark

The three-`BIGINT` scalar query isolates statement and parameter-binding
overhead. It is intentionally not presented as a complete application
workload.

| Measured operation | DuckDB.NET 1.5.3 | EFCoreProvider 1.9.0 | Tuned .NET 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Unprepared execution | 63.25 us | 63.03 us* | 64.74 us | 67.77 us | **54.04 us** | **Go 1.5.4** |
| Prepared execution | 64.05 us | 62.23 us* | 21.47 us | 23.43 us | **17.71 us** | **Go 1.5.4** |
| Speedup from preparing within provider | 0.99x | 1.01x* | 3.02x | 2.89x | **3.05x** | **Go 1.5.4** |
| Create and prepare | 0.080 us, no-op | 0.079 us, no-op* | **32.396 us** | 36.005 us | 32.482 us | **Tuned .NET 1.5.4 among real prepare paths** |

`DuckDBCommand.Prepare()` is a no-op in DuckDB.NET 1.5.3, including the copy
bundled with the EF provider. Their setup timings are therefore not comparable
with the three paths that perform real native preparation.

## What this run establishes

- **Tuned local DuckDB.NET 1.5.4 wins the reusable prepared-insert workload.**
  It is 2.64x faster than the released .NET 1.5.3 path and the ADO.NET driver
  bundled with `DuckDB.EFCoreProvider` 1.9.0.
- **`DuckDB.EFCoreProvider` has a small bulk-layer cost.** Its public
  `DbContext.BulkInsert` is 4.8% slower than using its bundled Appender
  directly: 261.3 versus 249.4 ns/row.
- **Java wins Appender ingestion, closely followed by Go.** Java measured
  168.3 ns/row and Go measured 172.7 ns/row.
- **The .NET lanes lead mixed-type result materialization.** Their results are
  within 1.6% of each other and materially ahead of Go and Java in this run.
- **Scan-heavy analytics and TPC-H are broadly close.** Small winner changes
  should not be interpreted as universal wrapper advantages.

## Methodology and workload contract

| Workload | Data and measured boundary |
| --- | --- |
| Scalar execution | Reused command/statement; bind one changing `BIGINT`; execute and read scalar |
| Parameterized analytics | Filter, group, aggregate, and order 2,000,000 orders; fully read result |
| Materialization | Read all six typed columns from 100,000 ordered rows and checksum values |
| Prepared ingestion | Bind and execute 10,000 precomputed rows inside one transaction |
| Appender ingestion | Append the same 10,000 rows inside one transaction |
| EF bulk ingestion | Call `DbContext.BulkInsert` for the same 10,000 mapped rows inside one transaction |
| TPC-H | Generate SF 0.1 in setup; execute and fully consume Q1, Q6, Q12, and Q14 |

Common controls:

- one in-memory database and one connection per benchmark instance;
- `SET threads = 1` in every provider;
- deterministic data generation before measurement;
- setup, extension installation, TPC-H generation, and input construction are
  outside measured methods;
- all returned rows and columns are consumed;
- ten measured results per workload after warmup;
- benchmark setup verifies the expected package or native engine version.

JDBC has no unprepared parameterized `Statement` API. Its unprepared lane
creates, binds, executes, reads, and closes a `PreparedStatement` per operation,
which is the closest public-API equivalent.

The released .NET package and EF provider dependency use native DuckDB 1.5.3.
The tuned local .NET, Java, and Go lanes use native DuckDB 1.5.4. This is a real
package-to-package comparison, not a wrapper-only comparison against one
identical native library.

## Limitations and robustness

- The machine was not otherwise idle, and the provider lanes ran sequentially
  rather than in an alternating order.
- The package comparison intentionally includes two native engine versions:
  DuckDB 1.5.3 for released .NET and the EF provider dependency, and DuckDB
  1.5.4 for tuned local .NET, Java, and Go.
- Large effects such as the 2.64x prepared-insert improvement are useful
  signals. Single-digit percentage differences, including the EF `BulkInsert`
  overhead, should be confirmed with alternating runs on an idle,
  fixed-power machine.
- Cross-runtime allocation counts are not comparable because .NET, JVM, and Go
  allocations have different representations and costs.

## Recommended next steps

1. Repeat the complete five-lane run three to five times on an idle machine,
   alternating provider order, and report the median plus variation.
2. Add a separate EF-specific application benchmark for LINQ query translation
   and materialization. Keep it separate from the exact-SQL table because it
   measures a different abstraction boundary.
3. Add an EF `SaveChanges` ingestion benchmark if that is a production usage
   pattern. Compare it with `BulkInsert`, but do not present it as equivalent
   to a direct Appender.
4. Run a wrapper-only comparison against the same native DuckDB version if the
   goal changes from real package performance to isolating managed-driver
   overhead.

## Further questions

- Does the target EF application primarily use `BulkInsert`, `SaveChanges`, or
  raw SQL through the provider?
- Should the next report optimize for real package versions, as this one does,
  or pin every lane to one native DuckDB engine for wrapper-only attribution?

## Projects

- `DuckDB.NET.1_5_3.Benchmarks` benchmarks released
  `DuckDB.NET.Data.Full` 1.5.3.
- `DuckDB.EFCoreProvider.1_9_0.Benchmarks` benchmarks
  `DuckDB.EFCoreProvider` 1.9.0, including its public `BulkInsert` API.
- `DuckDB.NET.Benchmarks` benchmarks the locally tuned DuckDB.NET 1.5.4
  projects in this checkout.
- `DuckDB.Java.Benchmarks` uses JMH against
  `org.duckdb:duckdb_jdbc:1.5.4.0`.
- `DuckDB.Go.Benchmarks` uses Go's `testing.B` against
  `github.com/duckdb/duckdb-go/v2` v2.10504.0.

## Raw reports for this run

- [Benchmark environment](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/environment.txt)
- [DuckDB.NET 1.5.3 analytical query](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-1.5.3/results/DuckDB.NET.Benchmarks.AnalyticalQueryBenchmark-report-github.md)
- [DuckDB.NET 1.5.3 materialization](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-1.5.3/results/DuckDB.NET.Benchmarks.ResultMaterializationBenchmark-report-github.md)
- [DuckDB.NET 1.5.3 ingestion](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-1.5.3/results/DuckDB.NET.Benchmarks.BulkIngestionBenchmark-report-github.md)
- [DuckDB.NET 1.5.3 TPC-H](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-1.5.3/results/DuckDB.NET.Benchmarks.TpchBenchmark-report-github.md)
- [EF provider dependency analytical query](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-efcore-1.9.0/results/DuckDB.NET.Benchmarks.AnalyticalQueryBenchmark-report-github.md)
- [EF provider dependency materialization](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-efcore-1.9.0/results/DuckDB.NET.Benchmarks.ResultMaterializationBenchmark-report-github.md)
- [EF provider dependency ingestion](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-efcore-1.9.0/results/DuckDB.NET.Benchmarks.BulkIngestionBenchmark-report-github.md)
- [EF provider public `BulkInsert`](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-efcore-1.9.0/results/DuckDB.NET.Benchmarks.EfCoreProviderBulkIngestionBenchmark-report-github.md)
- [EF provider dependency TPC-H](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-efcore-1.9.0/results/DuckDB.NET.Benchmarks.TpchBenchmark-report-github.md)
- [Tuned local DuckDB.NET 1.5.4 analytical query](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-local-1.5.4/results/DuckDB.NET.Benchmarks.AnalyticalQueryBenchmark-report-github.md)
- [Tuned local DuckDB.NET 1.5.4 materialization](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-local-1.5.4/results/DuckDB.NET.Benchmarks.ResultMaterializationBenchmark-report-github.md)
- [Tuned local DuckDB.NET 1.5.4 ingestion](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-local-1.5.4/results/DuckDB.NET.Benchmarks.BulkIngestionBenchmark-report-github.md)
- [Tuned local DuckDB.NET 1.5.4 TPC-H](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/dotnet-local-1.5.4/results/DuckDB.NET.Benchmarks.TpchBenchmark-report-github.md)
- [Java JMH text](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/java.txt)
- [Java JMH JSON](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/java/results.json)
- [Go benchmark output](BenchmarkDotNet.Artifacts/DriverComparison/20260716T211118Z/go.txt)

## Run

From the repository root:

```bash
./run-driver-comparison.sh
```

Results are written beneath
`BenchmarkDotNet.Artifacts/DriverComparison/<UTC timestamp>/`, separated into
`dotnet-1.5.3`, `dotnet-efcore-1.9.0`, `dotnet-local-1.5.4`, Java, and Go
outputs. Each .NET lane uses one launch with five warmup and ten measured
iterations. Java uses one JMH fork with five warmup and ten measured 500 ms
iterations plus the GC profiler. Go uses ten benchmark repetitions at 500 ms
per workload.

For repeated runs after successful Release builds:

```bash
SKIP_DOTNET_BUILD=1 SKIP_JAVA_BUILD=1 ./run-driver-comparison.sh
```

Run on an otherwise idle machine with fixed power and thermal settings, and
compare several alternating provider orders for publishable claims. Elapsed
time is the meaningful cross-language comparison. Allocation counts are useful
within each runtime but should not be compared as if .NET, JVM, and Go
allocations had equal cost.
