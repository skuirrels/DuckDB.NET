# DuckDB driver benchmark comparison: consolidated fork 1.5.4

## Current status

**Updated 2026-07-21:** `feature/appender-scoped-writer` is included in the
local `release/fork-1.5.4` branch at commit
`2b04e5e970b1610537f362e1cff435e0f86ee712`. The complete five-lane comparison
was rerun as `20260721T172000Z-appender-scoped-consolidated-rerun`.

The accepted run completed 13 released DuckDB.NET methods, 14 EFCoreProvider
methods, 18 consolidated-fork methods, 12 Java methods, and 12 Go methods (ten
Go repetitions each). The consolidation lane includes both the compatible
`CreateRow().EndRow()` path and the new allocation-free `AppendRowScoped` path.
Stock .NET `develop` is not included.

| Real-work benchmark | DuckDB.NET 1.5.3 | EFCoreProvider 1.13.0 | Consolidated fork 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Updated winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Parameterized analytics, prepared | 2.618 ms | 2.609 ms* | **2.498 ms** | 2.522 ms | 2.640 ms | **Consolidated fork** |
| Materialize 100,000 mixed rows | 9.931 ms | **9.928 ms*** | 10.180 ms | 54.588 ms | 28.105 ms | **EFCoreProvider** |
| Reusable prepared insert | 54.762 us/row† | 53.697 us/row*† | **19.517 us/row** | 26.111 us/row | 29.275 us/row | **Consolidated fork** |
| Idiomatic high-throughput insert | 259.7 ns/row | 276.0 ns/row `BulkInsert`* | **79.48 ns/row `AppendRowScoped`** | 174.25 ns/row | 183.46 ns/row | **Consolidated fork** |
| TPC-H Q1, SF 0.1 | 13.9593 ms | 13.7994 ms* | **13.7020 ms** | 14.0699 ms | 14.2022 ms | **Consolidated fork** |
| TPC-H Q6, SF 0.1 | 0.9579 ms | 0.9563 ms* | **0.9122 ms** | 0.9761 ms | 0.9914 ms | **Consolidated fork** |
| TPC-H Q12, SF 0.1 | 7.8996 ms | **7.6711 ms*** | 7.7706 ms | 7.8124 ms | 7.8385 ms | **EFCoreProvider** |
| TPC-H Q14, SF 0.1 | 1.5953 ms | 1.5309 ms* | **1.5235 ms** | 1.6955 ms | 1.5905 ms | **Consolidated fork** |

`*` Except for its public `BulkInsert`, the EF provider lane executes the same
low-level benchmark code through its transitive `DuckDB.NET.Data.Full` 1.5.3
dependency. These cells do not measure EF LINQ translation or change tracking.

`†` `DuckDBCommand.Prepare()` is a no-op in the released and EF dependency .NET
lanes. It does not create a native prepared statement in those lanes.

The raw winner changed materially: the consolidated fork now wins six of eight
real-work rows. EFCoreProvider has the lowest point estimate for materialization
and Q12, but those should be understood as DuckDB.NET 1.5.3/native-engine
results rather than provider-specific wins. The 0.03% materialization gap
between the released and EF lanes is effectively a tie.

## Appender optimization result

| Fork appender path | Latency | Throughput | Managed allocation |
| --- | ---: | ---: | ---: |
| Compatible `CreateRow().EndRow()` | 86.47 ns/row | 11.56 M rows/s | 64 B/row |
| New `AppendRowScoped` | **79.48 ns/row** | **12.58 M rows/s** | **0 B/row** |
| Java JDBC Appender | 174.25 ns/row | 5.74 M rows/s | Not comparable |
| Go Appender | 183.46 ns/row | 5.45 M rows/s | Not comparable |

The scoped writer is 8.1% faster than the compatible optimized path and removes
the remaining managed row allocation. Against the other drivers, it has 54.4%
lower latency than Java and 56.7% lower latency than Go. Put another way, this
run measured 2.19x Java's and 2.31x Go's row throughput.

The compatible path also benefits substantially from the branch: its 86.47
ns/row is 65% below the consolidated fork's previous 247.7 ns/row point
estimate. This cross-run delta is large, but the same-run scoped-versus-compatible
comparison is the stronger evidence for the incremental scoped API benefit.

## Prepared scalar microbenchmark

This isolates command creation, parameter binding, execution, and one returned
value. It is not a full application workload.

| Operation | DuckDB.NET 1.5.3 | EFCoreProvider 1.13.0 | Consolidated fork 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Unprepared execution | 66.11 us | 64.18 us* | 62.04 us | 69.44 us | **55.03 us** | **Go** |
| Prepared execution | 65.80 us† | 67.11 us*† | 24.32 us | 24.27 us | **17.68 us** | **Go** |
| Create and prepare | 0.090 us† | 0.084 us*† | **33.03 us** | 36.50 us | 33.42 us | **Consolidated fork** |

The approximately 84-90 ns setup results in the released and EF dependency
lanes only allocate a command object and call the no-op `Prepare()`. They are
not comparable with the fork, Java, and Go native preparation results.

## Parameterized analytical query

The workload filters and aggregates a deterministic 2,000,000-row table,
groups and orders the result, and consumes every returned value.

| Operation | DuckDB.NET 1.5.3 | EFCoreProvider 1.13.0 | Consolidated fork 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Unprepared | 2.618 ms | 2.594 ms* | **2.553 ms** | 2.685 ms | 2.721 ms | **Consolidated fork** |
| Prepared/reused | 2.618 ms† | 2.609 ms*† | **2.498 ms** | 2.522 ms | 2.640 ms | **Consolidated fork** |

The prepared fork/Java difference is only 1.0%, so this ordering is directional
without alternating repeated runs.

## Result materialization

Each lane fully reads 100,000 ordered rows containing `BIGINT`, `DATE`,
`TIMESTAMP`, `DOUBLE`, nullable `VARCHAR`, and `BOOLEAN`, while calculating a
checksum.

| Metric | DuckDB.NET 1.5.3 | EFCoreProvider 1.13.0 | Consolidated fork 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Latency | 9.931 ms | **9.928 ms*** | 10.180 ms | 54.588 ms | 28.105 ms | **EFCoreProvider** |
| Throughput | 10.07 M rows/s | **10.07 M rows/s*** | 9.82 M rows/s | 1.83 M rows/s | 3.56 M rows/s | **EFCoreProvider** |

The .NET results are within 2.5%. The meaningful result is the clear .NET lead
over Go and Java in this typed materialization workload, not the tiny ordering
among the closely related .NET lanes. This workload contains no MAP or LIST
columns, so it does not exercise the fork's MAP/LIST materialization work.

## Bulk ingestion

Each invocation writes 10,000 precomputed mixed-type rows inside an explicit
transaction and rolls the transaction back.

| Metric | DuckDB.NET 1.5.3 | EFCoreProvider 1.13.0 | Consolidated fork 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Prepared insert latency | 54.762 us/row† | 53.697 us/row*† | **19.517 us/row** | 26.111 us/row | 29.275 us/row | **Consolidated fork** |
| Prepared insert throughput | 18,261 rows/s† | 18,623 rows/s*† | **51,238 rows/s** | 38,298 rows/s | 34,159 rows/s | **Consolidated fork** |
| Idiomatic Appender latency | 259.7 ns/row | 276.0 ns/row `BulkInsert`* | **79.48 ns/row scoped** | 174.25 ns/row | 183.46 ns/row | **Consolidated fork** |
| Idiomatic Appender throughput | 3.85 M rows/s | 3.62 M rows/s `BulkInsert`* | **12.58 M rows/s scoped** | 5.74 M rows/s | 5.45 M rows/s | **Consolidated fork** |
| Direct compatible Appender | 259.7 ns/row | 237.8 ns/row* | **86.47 ns/row** | 174.25 ns/row | 183.46 ns/row | **Consolidated fork** |

EFCoreProvider's public `BulkInsert` is 16.1% slower than directly calling the
Appender bundled in its DuckDB.NET 1.5.3 dependency (276.0 versus 237.8
ns/row). The public API result is the appropriate idiomatic provider figure.

## TPC-H analytical queries

The suite generates scale factor 0.1 using DuckDB's `tpch` extension, then
executes and fully consumes Q1, Q6, Q12, and Q14. Every connection uses one
DuckDB thread.

| Query | DuckDB.NET 1.5.3 | EFCoreProvider 1.13.0 | Consolidated fork 1.5.4 | Java JDBC 1.5.4 | Go 1.5.4 | Winner |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Q1 | 13.9593 ms | 13.7994 ms* | **13.7020 ms** | 14.0699 ms | 14.2022 ms | **Consolidated fork** |
| Q6 | 0.9579 ms | 0.9563 ms* | **0.9122 ms** | 0.9761 ms | 0.9914 ms | **Consolidated fork** |
| Q12 | 7.8996 ms | **7.6711 ms*** | 7.7706 ms | 7.8124 ms | 7.8385 ms | **EFCoreProvider** |
| Q14 | 1.5953 ms | 1.5309 ms* | **1.5235 ms** | 1.6955 ms | 1.5905 ms | **Consolidated fork** |

These are primarily native-engine workloads. Most gaps are small enough to
require repeated alternating runs before attributing them to a wrapper. Q12's
raw EF result should likewise not be interpreted as an EF provider advantage.

## Packages and environment

- macOS 26.5.1, Apple M4 Pro, arm64;
- .NET SDK 10.0.300;
- Java 26.0.1 and `org.duckdb:duckdb_jdbc:1.5.4.0`;
- Go 1.26.5 and `github.com/duckdb/duckdb-go/v2` v2.10504.0;
- released `DuckDB.NET.Data.Full` 1.5.3;
- `DuckDB.EFCoreProvider` 1.13.0, transitively using DuckDB.NET 1.5.3 and
  Entity Framework Core 10.0.10;
- consolidated DuckDB.NET fork 1.5.4 at release commit `2b04e5e`;
- one DuckDB thread per connection.

The released .NET and EF dependency lanes use native DuckDB 1.5.3. The
consolidated fork, Java, and Go use native DuckDB 1.5.4. This is therefore a
package-level comparison rather than a wrapper-only comparison against one
identical native library.

For reproducibility, the benchmark ran from harness commit `a73a0eb`. Its tree
was verified byte-identical to isolated tree `b8a9257`, constructed from
release commit `2b04e5e` plus the benchmark-only commits. Java JMH was rerun
outside the filesystem sandbox because JMH requires a local coordinator socket;
its benchmark configuration was unchanged.

## Methodology and limitations

- One in-memory database and connection are used per benchmark instance.
- Data generation, extension installation, and input construction occur outside
  measured methods.
- All returned rows and columns are consumed.
- All five lanes ran sequentially on the same machine under one accepted run
  configuration; Java alone required the local-socket permission noted above.
- Go values are arithmetic means of ten repetitions. BenchmarkDotNet and JMH
  values are their reported arithmetic means.
- Cross-runtime allocation counts are not compared because .NET, JVM, and Go
  allocations have different representations and costs.
- BenchmarkDotNet process-priority warnings and Java's `Unsafe` deprecation
  warnings did not invalidate the completed measurements.
- No failed benchmark remains in the accepted result set. The earlier partial
  `20260721T160513Z-appender-scoped-consolidated` run is not used.

## Raw reports

- [Environment](BenchmarkDotNet.Artifacts/DriverComparison/20260721T172000Z-appender-scoped-consolidated-rerun/environment.txt)
- [DuckDB.NET 1.5.3 reports](BenchmarkDotNet.Artifacts/DriverComparison/20260721T172000Z-appender-scoped-consolidated-rerun/dotnet-1.5.3/results/)
- [EFCoreProvider 1.13.0 reports](BenchmarkDotNet.Artifacts/DriverComparison/20260721T172000Z-appender-scoped-consolidated-rerun/dotnet-efcore-1.13.0/results/)
- [Consolidated fork reports](BenchmarkDotNet.Artifacts/DriverComparison/20260721T172000Z-appender-scoped-consolidated-rerun/dotnet-fork-1.5.4/results/)
- [Java JMH JSON](BenchmarkDotNet.Artifacts/DriverComparison/20260721T172000Z-appender-scoped-consolidated-rerun/java/results.json)
- [Go benchmark output](BenchmarkDotNet.Artifacts/DriverComparison/20260721T172000Z-appender-scoped-consolidated-rerun/go.txt)

## Recommended follow-up

Repeat the five lanes three to five times on an idle, fixed-power machine while
alternating their order, then report medians and variation. If the goal is to
isolate managed-wrapper performance, also run every lane against exactly the
same native DuckDB build.
