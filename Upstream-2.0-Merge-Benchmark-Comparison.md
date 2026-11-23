# The fork after merging upstream 2.0 performance work

This follows `Upstream-2.0-Benchmark-Comparison.md`, which found upstream's `2.0.0-preview.1` about twice as fast as our
2.0.0 in the driver's own code. Branch `perf/merge-upstream-develop` merges upstream `develop` (`4125f1e`, 280 commits)
into `fork/main` (`c66d9d8`) and keeps the fork's own features. This report measures the merged branch against
upstream's package.

## Technical summary

- **The merged branch matches upstream.** Across the 148 micro-benchmarks it takes 0.993× upstream's time on geometric
  mean. None is more than 5% slower; 8 are more than 5% faster. Before the merge ours took 1.98×.
- **Realistic workloads are level.** Every workload is within 0.98× to 1.03× of the mean of two upstream runs, while those
  two runs differ by up to 9% on TPC-H Q12 and Q14.
- **The fork's fixes cost nothing measurable.** Checked integer conversions in the appender, MAP writes, the release of
  a partly read stream and the scoped row writer all sit on paths that now measure level with upstream.
- **Two changes made after the merge were needed to get there:**
  - The numeric vector writer is now generic over its storage type, so the fork's width check folds to a constant.
  - The MAP case of the mapped appender moved out of line, because as a switch arm it made nullable properties 10% to 16%
    slower.
- **Not ported:** the fork's native scalar bind fast path (`TryBindScalarValue`). Prepared commands are already level, and
  the fork's 2.0.0 with that path was no faster on them in the first report.

## Environment and method

- Same machine and settings as the first report: Apple M4 Pro, macOS 27.0.1, .NET SDK 10.0.403, .NET 10 (Arm64),
  BenchmarkDotNet 0.15.8. Run 2026-11-18.
- **Upstream:** `DuckDB.NET.Data.Full 2.0.0-preview.1` from nuget.org, built from `4df9b32`. Upstream `develop` at
  `4125f1e` differs from it only in trim annotations on two files.
- **Merged:** the working tree of `perf/merge-upstream-develop`, Release build, as a project reference.
- **Native library:** both use the same `libduckdb.dylib`, DuckDB v2.0.0 (SHA-256 `e74bb071…`).
- **Micro suite:** upstream's `DuckDB.NET.Benchmarks` classes, now in the fork as `--micro`. There are two jobs, each case
  in its own process, with the package as the baseline and the merged source as `Local`.
  - A full run, then re-runs of the classes that changed or showed a gap. The tables use the latest run of each class.
  - The mapped and HUGEINT appender classes use three launches per case.
- **Realistic suite:** the fork's driver-comparison workloads, in process, run as upstream, merged, upstream again. Both
  lanes compile the same sources with `DUCKDB_NET_BASELINE_1_5_3`.
- **A/A check:** upstream's own `develop` source, run as `Local` against its own package, measured 0.99× to 1.02× on the
  mapped appender class. Building from source therefore adds no bias.

## Realistic workloads

Means; lower is better. `BulkIngestion` figures are per inserted row. The ratio is merged against the mean of the two
upstream runs.

| Workload | Upstream run 1 | Upstream run 2 | Merged | Merged vs upstream |
| --- | ---: | ---: | ---: | ---: |
| AnalyticalQuery.ExecutePrepared | 1.541 ms | 1.506 ms | 1.531 ms | 1.00× |
| AnalyticalQuery.ExecuteUnprepared | 1.773 ms | 1.764 ms | 1.768 ms | 1.00× |
| BulkIngestion.InsertPreparedInTransaction | 40.64 µs | 40.91 µs | 41.59 µs | 1.02× |
| BulkIngestion.InsertWithAppenderInTransaction | 70.24 ns | 69.60 ns | 71.21 ns | 1.02× |
| PreparedCommand.ExecutePreparedBoxed | 30.65 µs | 30.56 µs | 30.89 µs | 1.01× |
| PreparedCommand.ExecuteUnprepared | 115.3 µs | 115.9 µs | 117.0 µs | 1.01× |
| PreparedCommandSetup.CreateAndPrepareCommand | 85.00 µs | 83.35 µs | 84.11 µs | 1.00× |
| PreparedCommandSetup.CreateUnpreparedCommand | 93.6 ns | 88.5 ns | 90.4 ns | 0.99× |
| ResultMaterialization.ReadOneHundredThousandMixedRows | 5.082 ms | 5.097 ms | 5.149 ms | 1.01× |
| Tpch.Query01 | 10.05 ms | 10.02 ms | 10.01 ms | 1.00× |
| Tpch.Query06 | 1.139 ms | 1.116 ms | 1.145 ms | 1.02× |
| Tpch.Query12 | 4.047 ms | 3.694 ms | 3.793 ms | 0.98× |
| Tpch.Query14 | 2.024 ms | 1.844 ms | 1.994 ms | 1.03× |

Allocations per operation are within 20 B of upstream's in every workload.

Compared with the first report:
- Materialising 100,000 mixed rows went from 10.2 ms to 5.1 ms.
- Appender ingestion went from 89.6 ns to 71.2 ns per row.

## Micro-benchmarks by area

Geometric mean of merged time over upstream's, per benchmark class; above 1 means merged is slower.

| Area | Cases | Merged vs upstream (geomean) | Best | Worst |
| --- | ---: | ---: | ---: | ---: |
| HugeIntAppender | 3 | 1.02× | 1.00× | 1.03× |
| Function | 10 | 1.01× | 0.99× | 1.04× |
| DecimalParameter | 4 | 1.00× | 0.98× | 1.03× |
| MappedAppender | 3 | 1.00× | 0.99× | 1.02× |
| WideTableReader | 9 | 1.00× | 0.94× | 1.04× |
| StringReader | 6 | 1.00× | 0.98× | 1.02× |
| CompositeReader | 16 | 1.00× | 0.97× | 1.03× |
| DateAppender | 10 | 1.00× | 0.96× | 1.04× |
| StringAppender | 5 | 1.00× | 0.98× | 1.01× |
| Command | 10 | 1.00× | 0.98× | 1.00× |
| TableFunctionRow | 8 | 0.99× | 0.97× | 1.02× |
| EnumAppender | 4 | 0.99× | 0.98× | 1.01× |
| RowMaterialization | 10 | 0.99× | 0.96× | 1.01× |
| DataReader | 8 | 0.99× | 0.96× | 1.02× |
| DateTimeReader | 16 | 0.99× | 0.87× | 1.02× |
| TypedReader | 9 | 0.98× | 0.86× | 1.02× |
| DecimalAppender | 4 | 0.98× | 0.96× | 1.02× |
| ListAppender | 4 | 0.98× | 0.94× | 1.02× |
| NumericAppender | 1 | 0.98× | 0.98× | 0.98× |
| RowCost | 4 | 0.98× | 0.93× | 1.03× |
| MixedNumericReader | 4 | 0.90× | 0.81× | 0.99× |

The largest wins, MixedNumericReader (0.81× on its best case) and two DateTimeReader and TypedReader cases (0.86× to
0.87×), come from upstream's readers. The difference from the package is measured, but I have not traced its cause.

## What the merge changed

- **Upstream's code is taken whole for the hot paths:**
  - the vector readers and writers
  - `GetFieldValue<T>`
  - parameter binding and the prepared statement layer
  - the mapped appender's per-property switch
  - the list writers

  That is where the 2× came from.
- **Fork features kept:**
  - `AppendRowScoped` and `DuckDBAppenderRowWriter`
  - `DuckDBParameter<T>`
  - MAP writes through the appender, parameters and the mapped appender
  - TIMESTAMP_TZ_NS and GEOMETRY
  - checked integer conversions in the appender
  - release of a partly read streaming result before the next statement
  - the fork's packaging and workflows
- **Checked conversions on upstream's writer.** Upstream copies an integer of any width as it is, so a `long` into an
  INTEGER column overwrites the next value. The fork converts it with a range check.
  - `NumericVectorDataWriter<TStorage>` keeps that check, and `typeof(TStorage) == typeof(int)` and the like are JIT
    constants.
  - A value of the column's own type is therefore a plain copy.
  - Before this change, the check cost 5% to 8% on integer appenders.
- **Mapped appender MAP support.** The dictionary case is a `NoInlining` helper reached from the default arm, not a
  pattern in the switch.
  - Measured, the pattern made `AppendNullableIntProperties` 10% to 16% slower (three runs).
  - Moving it out brought the case to 1.00×.
  - Turning the static flag into an instance field did not help.

## Behaviour changes for users of the fork's 2.0.0

These come from upstream and were adopted where upstream changed behaviour on purpose for 2.0. Tests that encoded the
fork's old behaviour were adapted or removed.

- UHUGEINT reads as `UInt128`, and `Int128` and `UInt128` are the default CLR types for HUGEINT and UHUGEINT.
- BLOB and GEOMETRY read as `byte[]`.
- TIME_NS is written to the tick, and its provider-specific type is `DuckDBTimeOnly`.
- **Appender failures:**
  - An incomplete row faults the appender.
  - A failed `AppendRow` discards the row, and completed rows are written on close.
- `GetFieldValue<Dictionary<string, object>>` on a MAP column throws. Read the map with its own key and value types.
- A `BigInteger` outside the range of a HUGEINT or UHUGEINT column throws `ArgumentOutOfRangeException`, not
  `OverflowException`. Narrower columns still throw `OverflowException`.
- `Int128` and `UInt128` are refused by columns other than HUGEINT and UHUGEINT.
- `DuckDBClientFactory.CreateCommandBuilder` and `CreateDataAdapter` return null, with `CanCreate…` false. Before, they
  threw `NotImplementedException`.
- The benchmark project's assembly is `Benchmarks`, and `InternalsVisibleTo` in Data follows it.

## Issues found

- **Flaky upstream test.** `ScalarFunctionTests.ScalarFunctionError_PreservesInnerExceptionInAFilter(place: "where")` failed
  in three of eight full test runs, on either target framework. The inner exception it expects was null. The test and the code
  under it are upstream's, unchanged by the merge.
- **1.5.3 lane.** The fork's `DUCKDB_NET_BASELINE_1_5_3` build of `DuckDB.NET.Benchmarks` fails on `DuckDBParameter<int>`
  in `PreparedCommandBenchmark.cs`. This is already the case on `fork/main`, not caused by the merge.

## Reproducing

Raw reports are in `BenchmarkDotNet.Artifacts/Merged-vs-Upstream-2.0/` (git ignores it):
- `micro`, `micro-rerun`, `micro-mapped` and `micro-mapped3` for the micro suite.
- `micro-combined.json` with every mean and allocation used here.
- `realistic-upstream-a`, `realistic-merged` and `realistic-upstream-b` for the realistic suite.
- `realistic-lanes` with the lane projects.

The micro suite runs from this branch:

```bash
dotnet run -c Release -f net10.0 --project DuckDB.NET.Benchmarks/Benchmarks.csproj -- --micro --filter '*'
```

`--baseline-version` and `--baseline-id` pick another package, and `--aa` runs the baseline against itself.

## Full micro-benchmark results

| Benchmark | Upstream | Merged | Ratio | Allocated (upstream → merged) |
| --- | ---: | ---: | ---: | --- |
| Command.InsertRow | 148.6 µs | 149.2 µs | 1.00× | 776 B → 776 B |
| Command.ReadPage | 331.6 µs | 325.2 µs | 0.98× | 5.95 KB → 5.95 KB |
| Command.ReusedCommand | 92.39 µs | 92.27 µs | 1.00× | 912 B → 912 B |
| Command.ReusedExpression | 41.12 µs | 40.99 µs | 1.00× | 920 B → 920 B |
| Command.ReusedInsert | 70.43 µs | 70.2 µs | 1.00× | 272 B → 272 B |
| Command.SelectByIntParameter | 180.6 µs | 178.8 µs | 0.99× | 1.30 KB → 1.30 KB |
| Command.SelectByNamedParameter | 180.5 µs | 180 µs | 1.00× | 1.30 KB → 1.30 KB |
| Command.SelectByStringParameter | 205 µs | 205.4 µs | 1.00× | 1.35 KB → 1.35 KB |
| Command.SelectConstant | 96.17 µs | 95.53 µs | 0.99× | 1.06 KB → 1.06 KB |
| Command.SelectExpression | 122.3 µs | 121.6 µs | 0.99× | 1.30 KB → 1.30 KB |
| CompositeReader.ReadArrayAsListOfInt(RowCount=200000) | 3.877 ms | 3.748 ms | 0.97× | 15.26 MB → 15.26 MB |
| CompositeReader.ReadArrayAsListOfLong(RowCount=200000) | 4.511 ms | 4.509 ms | 1.00× | 18.32 MB → 18.32 MB |
| CompositeReader.ReadListOfStrings(RowCount=200000) | 18.31 ms | 18.23 ms | 1.00× | 56.08 MB → 56.08 MB |
| CompositeReader.ReadListOfTimestamps(RowCount=200000) | 4.634 ms | 4.655 ms | 1.00× | 15.26 MB → 15.26 MB |
| CompositeReader.ReadListWithNullsAsListOfNullableInt(RowCount=200000) | 5.523 ms | 5.434 ms | 0.98× | 18.32 MB → 18.32 MB |
| CompositeReader.ReadListWithNullsAsListOfNullableLong(RowCount=200000) | 6.016 ms | 5.982 ms | 0.99× | 25.95 MB → 25.95 MB |
| CompositeReader.ReadMapAsDictionary(RowCount=200000) | 8.979 ms | 8.87 ms | 0.99× | 48.07 MB → 48.07 MB |
| CompositeReader.ReadMapOfSmallIntToDecimal(RowCount=200000) | 6.52 ms | 6.567 ms | 1.01× | 45.78 MB → 45.78 MB |
| CompositeReader.ReadMapOfTenAsDictionary(RowCount=200000) | 43.6 ms | 44.2 ms | 1.01× | 152.60 MB → 152.60 MB |
| CompositeReader.ReadMapWithWiderKey(RowCount=200000) | 9.057 ms | 9.13 ms | 1.01× | 48.07 MB → 48.07 MB |
| CompositeReader.ReadNestedList(RowCount=200000) | 10.9 ms | 10.94 ms | 1.00× | 38.15 MB → 38.15 MB |
| CompositeReader.ReadStructAsClass(RowCount=200000) | 6.315 ms | 6.426 ms | 1.02× | 14.50 MB → 14.50 MB |
| CompositeReader.ReadStructAsDictionary(RowCount=200000) | 13.1 ms | 12.79 ms | 0.98× | 57.23 MB → 57.23 MB |
| CompositeReader.ReadStructAsRecordStruct(RowCount=200000) | 6.363 ms | 6.473 ms | 1.02× | 14.50 MB → 14.50 MB |
| CompositeReader.ReadStructOfStringsAsClass(RowCount=200000) | 15.09 ms | 15.1 ms | 1.00× | 39.38 MB → 39.38 MB |
| CompositeReader.ReadVaryingListAsListOfInt(RowCount=200000) | 4.133 ms | 4.277 ms | 1.03× | 14.04 MB → 14.04 MB |
| DataReader.ReadBooleans(RowCount=1000000) | 10.9 ms | 10.45 ms | 0.96× | 18.08 KB → 18.08 KB |
| DataReader.ReadIntegers(RowCount=1000000) | 10.8 ms | 10.48 ms | 0.97× | 18.01 KB → 18.01 KB |
| DataReader.ReadIntegersAsLong(RowCount=1000000) | 12.73 ms | 12.68 ms | 1.00× | 18.01 KB → 18.01 KB |
| DataReader.ReadIntegersAsObject(RowCount=1000000) | 33.26 ms | 32.73 ms | 0.98× | 183.13 MB → 183.13 MB |
| DataReader.ReadIntegersCheckingNull(RowCount=1000000) | 19.11 ms | 19.27 ms | 1.01× | 18.09 KB → 18.09 KB |
| DataReader.ReadIntegersStreaming(RowCount=1000000) | 10.69 ms | 10.67 ms | 1.00× | 18.01 KB → 18.01 KB |
| DataReader.ReadNullableIntegers(RowCount=1000000) | 17.75 ms | 18.12 ms | 1.02× | 18.09 KB → 18.09 KB |
| DataReader.ReadRowsOnly(RowCount=1000000) | 5.614 ms | 5.489 ms | 0.98× | 18.01 KB → 18.01 KB |
| DateAppender.AppendDateOnlyToDate(RowCount=1000000) | 23.25 ms | 23.06 ms | 0.99× | 92.84 KB → 92.84 KB |
| DateAppender.AppendDateTimeOffsetToTimestampTz(RowCount=1000000) | 21.59 ms | 20.98 ms | 0.97× | 92.84 KB → 92.84 KB |
| DateAppender.AppendDateTimeToDate(RowCount=1000000) | 19.5 ms | 18.77 ms | 0.96× | 92.84 KB → 92.84 KB |
| DateAppender.AppendDateTimeToTime(RowCount=1000000) | 20.66 ms | 20.91 ms | 1.01× | 92.84 KB → 92.84 KB |
| DateAppender.AppendDateTimeToTimeNs(RowCount=1000000) | 19.58 ms | 19.6 ms | 1.00× | 92.84 KB → 92.84 KB |
| DateAppender.AppendDateTimeToTimestamp(RowCount=1000000) | 20.07 ms | 20.83 ms | 1.04× | 92.84 KB → 92.84 KB |
| DateAppender.AppendDuckDBTimeOnlyToTime(RowCount=1000000) | 25.56 ms | 24.99 ms | 0.98× | 92.84 KB → 92.84 KB |
| DateAppender.AppendIntegersControl(RowCount=1000000) | 19.51 ms | 19.94 ms | 1.02× | 92.84 KB → 92.84 KB |
| DateAppender.AppendTimeOnlyToTime(RowCount=1000000) | 20.37 ms | 19.97 ms | 0.98× | 92.84 KB → 92.84 KB |
| DateAppender.AppendTimeOnlyToTimeNs(RowCount=1000000) | 20.38 ms | 21.25 ms | 1.04× | 92.84 KB → 92.84 KB |
| DateTimeReader.ReadDatesAsDateOnly(RowCount=1000000) | 5.837 ms | 5.89 ms | 1.01× | 17.17 KB → 17.17 KB |
| DateTimeReader.ReadDatesAsDateTime(RowCount=1000000) | 6.597 ms | 6.606 ms | 1.00× | 17.17 KB → 17.17 KB |
| DateTimeReader.ReadIntegersControl(RowCount=1000000) | 5.438 ms | 5.429 ms | 1.00× | 17.08 KB → 17.08 KB |
| DateTimeReader.ReadTimeNsAsDateTime(RowCount=1000000) | 7.303 ms | 7.302 ms | 1.00× | 17.08 KB → 17.08 KB |
| DateTimeReader.ReadTimeNsAsDuckDBTimeOnly(RowCount=1000000) | 25.2 ms | 25.13 ms | 1.00× | 17.11 KB → 17.11 KB |
| DateTimeReader.ReadTimeNsAsTimeOnly(RowCount=1000000) | 7.006 ms | 6.946 ms | 0.99× | 17.08 KB → 17.08 KB |
| DateTimeReader.ReadTimeNsAsTimeSpan(RowCount=1000000) | 17.1 ms | 17.11 ms | 1.00× | 17.11 KB → 17.11 KB |
| DateTimeReader.ReadTimeTzAsDateTimeOffset(RowCount=1000000) | 12.87 ms | 12.93 ms | 1.00× | 17.19 KB → 17.19 KB |
| DateTimeReader.ReadTimesAsDateTime(RowCount=1000000) | 6.706 ms | 6.801 ms | 1.01× | 17.08 KB → 17.08 KB |
| DateTimeReader.ReadTimesAsDuckDBTimeOnly(RowCount=1000000) | 28.79 ms | 25.15 ms | 0.87× | 17.10 KB → 17.10 KB |
| DateTimeReader.ReadTimesAsTimeOnly(RowCount=1000000) | 6.76 ms | 6.847 ms | 1.01× | 17.08 KB → 17.08 KB |
| DateTimeReader.ReadTimesAsTimeSpan(RowCount=1000000) | 17.07 ms | 16.47 ms | 0.96× | 17.10 KB → 17.10 KB |
| DateTimeReader.ReadTimestampNsAsDateTime(RowCount=1000000) | 11 ms | 11.27 ms | 1.02× | 17.19 KB → 17.19 KB |
| DateTimeReader.ReadTimestampTzAsDateTimeOffset(RowCount=1000000) | 21.06 ms | 19.84 ms | 0.94× | 17.20 KB → 17.20 KB |
| DateTimeReader.ReadTimestampsAsDateTime(RowCount=1000000) | 9.92 ms | 9.848 ms | 0.99× | 17.19 KB → 17.19 KB |
| DateTimeReader.ReadTimestampsWithGetValue(RowCount=1000000) | 23.12 ms | 22.85 ms | 0.99× | 91.57 MB → 91.57 MB |
| DecimalAppender.AppendQuotients(RowCount=1000000) | 79.59 ms | 76.72 ms | 0.96× | 138.69 KB → 138.69 KB |
| DecimalAppender.AppendSameScale(RowCount=1000000) | 25.63 ms | 26.22 ms | 1.02× | 138.69 KB → 138.69 KB |
| DecimalAppender.AppendSmallerScale(RowCount=1000000) | 26.53 ms | 25.59 ms | 0.96× | 138.69 KB → 138.69 KB |
| DecimalAppender.AppendToWide(RowCount=1000000) | 33.74 ms | 33.29 ms | 0.99× | 138.69 KB → 138.69 KB |
| DecimalParameter.InsertDecimalLists(Executions=1000) | 61.04 ms | 61.28 ms | 1.00× | 16.80 MB → 16.80 MB |
| DecimalParameter.InsertDecimals(Executions=1000) | 221.4 ms | 216.8 ms | 0.98× | 665.35 KB → 665.35 KB |
| DecimalParameter.InsertLongs(Executions=1000) | 171 ms | 171.6 ms | 1.00× | 634.10 KB → 634.10 KB |
| DecimalParameter.SelectFromDecimalLists(Executions=1000) | 59.16 ms | 60.97 ms | 1.03× | 16.81 MB → 16.81 MB |
| EnumAppender.AppendCSharpEnums(RowCount=1000000) | 31.69 ms | 30.96 ms | 0.98× | 291.50 KB → 291.50 KB |
| EnumAppender.AppendFiveNames(RowCount=1000000) | 62.88 ms | 63.61 ms | 1.01× | 1.30 MB → 1.30 MB |
| EnumAppender.AppendLongs(RowCount=1000000) | 19.76 ms | 19.9 ms | 1.01× | 92.84 KB → 92.84 KB |
| EnumAppender.AppendTwoHundredNames(RowCount=1000000) | 97.31 ms | 95.34 ms | 0.98× | 56.53 MB → 56.53 MB |
| Function.DoubleFunction(RowCount=1000000) | 2.523 ms | 2.545 ms | 1.01× | 104.35 KB → 104.35 KB |
| Function.IntFunction(RowCount=1000000) | 2.689 ms | 2.716 ms | 1.01× | 104.37 KB → 104.37 KB |
| Function.IntToNullableFunction(RowCount=1000000) | 2.833 ms | 2.874 ms | 1.01× | 104.38 KB → 104.38 KB |
| Function.LowLevelIntFunction(RowCount=1000000) | 6.196 ms | 6.152 ms | 0.99× | 104.39 KB → 104.39 KB |
| Function.NullableIntFunction(RowCount=1000000) | 3.186 ms | 3.186 ms | 1.00× | 104.38 KB → 104.38 KB |
| Function.StringFunction(RowCount=1000000) | 14.01 ms | 14.05 ms | 1.00× | 38.26 MB → 38.26 MB |
| Function.SumInSql(RowCount=1000000) | 347.7 µs | 349.2 µs | 1.00× | 1.09 KB → 1.09 KB |
| Function.TableFunction(RowCount=1000000) | 9.318 ms | 9.507 ms | 1.02× | 110.36 KB → 114.87 KB |
| Function.TableFunctionOneColumn(RowCount=1000000) | 2.794 ms | 2.903 ms | 1.04× | 40.51 KB → 43.80 KB |
| Function.TwoIntFunction(RowCount=1000000) | 3.179 ms | 3.224 ms | 1.01× | 157.86 KB → 157.86 KB |
| HugeIntAppender.AppendLargeToHugeInt(RowCount=1000000) | 45.08 ms | 46.52 ms | 1.03× | 92.84 KB → 92.84 KB |
| HugeIntAppender.AppendLargeToUHugeInt(RowCount=1000000) | 48.5 ms | 48.63 ms | 1.00× | 92.84 KB → 92.84 KB |
| HugeIntAppender.AppendSmallToHugeInt(RowCount=1000000) | 39.61 ms | 40.52 ms | 1.02× | 92.84 KB → 92.84 KB |
| ListAppender.AppendIntLists(RowCount=1000000) | 54.77 ms | 51.32 ms | 0.94× | 77.40 KB → 77.40 KB |
| ListAppender.AppendNullableIntArrays(RowCount=1000000) | 72.33 ms | 71.85 ms | 0.99× | 77.40 KB → 77.40 KB |
| ListAppender.AppendNullableIntLists(RowCount=1000000) | 72.26 ms | 73.5 ms | 1.02× | 77.40 KB → 77.40 KB |
| ListAppender.AppendShortStringLists(RowCount=1000000) | 103.1 ms | 101.9 ms | 0.99× | 77.40 KB → 77.40 KB |
| MappedAppender.AppendIntProperties(RowCount=1000000) | 55.44 ms | 56.81 ms | 1.02× | 185.82 KB → 185.82 KB |
| MappedAppender.AppendMixedProperties(RowCount=1000000) | 63.67 ms | 63.08 ms | 0.99× | 116.85 KB → 116.88 KB |
| MappedAppender.AppendNullableIntProperties(RowCount=1000000) | 71.53 ms | 71.34 ms | 1.00× | 186.23 KB → 186.48 KB |
| MixedNumericReader.ReadAsColumnType(RowCount=1000000) | 11.25 ms | 11.14 ms | 0.99× | 18.01 KB → 18.01 KB |
| MixedNumericReader.ReadAsDouble(RowCount=1000000) | 21.5 ms | 17.42 ms | 0.81× | 18.03 KB → 18.03 KB |
| MixedNumericReader.ReadAsLong(RowCount=1000000) | 19.63 ms | 16.38 ms | 0.83× | 18.03 KB → 18.03 KB |
| MixedNumericReader.ReadAsObject(RowCount=1000000) | 45.67 ms | 45.29 ms | 0.99× | 183.13 MB → 183.13 MB |
| NumericAppender.AppendMixedNumerics(RowCount=1000000) | 49.67 ms | 48.62 ms | 0.98× | 184.75 KB → 184.75 KB |
| RowCost.AppendRow(RowCount=1000000,Columns=1) | 9.898 ms | 9.859 ms | 1.00× | 23.91 KB → 23.91 KB |
| RowCost.AppendRow(RowCount=1000000,Columns=8) | 36.67 ms | 34.22 ms | 0.93× | 184.75 KB → 184.75 KB |
| RowCost.CreateRow(RowCount=1000000,Columns=1) | 15.17 ms | 15.57 ms | 1.03× | 61.06 MB → 61.06 MB |
| RowCost.CreateRow(RowCount=1000000,Columns=8) | 43.48 ms | 41.38 ms | 0.95× | 61.22 MB → 61.22 MB |
| RowMaterialization.MixedDapper(RowCount=500000) | 163.5 ms | 159.1 ms | 0.97× | 183.49 MB → 183.49 MB |
| RowMaterialization.MixedDapperGetFieldValue(RowCount=500000) | 169.6 ms | 163.4 ms | 0.96× | 183.49 MB → 183.49 MB |
| RowMaterialization.MixedGetValue(RowCount=500000) | 148 ms | 146.7 ms | 0.99× | 183.49 MB → 183.49 MB |
| RowMaterialization.MixedRowsOnly(RowCount=500000) | 11 ms | 10.9 ms | 0.99× | 10.58 KB → 10.58 KB |
| RowMaterialization.MixedTyped(RowCount=500000) | 86.98 ms | 86.94 ms | 1.00× | 111.01 MB → 111.01 MB |
| RowMaterialization.StringsDapper(RowCount=500000) | 307.2 ms | 306.6 ms | 1.00× | 258.65 MB → 258.65 MB |
| RowMaterialization.StringsDapperGetFieldValue(RowCount=500000) | 322.4 ms | 324.8 ms | 1.01× | 258.65 MB → 258.65 MB |
| RowMaterialization.StringsGetValue(RowCount=500000) | 293.9 ms | 295.1 ms | 1.00× | 258.65 MB → 258.65 MB |
| RowMaterialization.StringsRowsOnly(RowCount=500000) | 33.73 ms | 33.33 ms | 0.99× | 10.62 KB → 10.62 KB |
| RowMaterialization.StringsTyped(RowCount=500000) | 295.9 ms | 296.8 ms | 1.00× | 258.65 MB → 258.65 MB |
| StringAppender.AppendLongAscii(RowCount=500000) | 181.5 ms | 179.1 ms | 0.99× | 47.09 KB → 47.09 KB |
| StringAppender.AppendMediumAscii(RowCount=500000) | 85.75 ms | 86.19 ms | 1.01× | 47.09 KB → 47.09 KB |
| StringAppender.AppendMediumGeorgian(RowCount=500000) | 110.8 ms | 110.5 ms | 1.00× | 47.09 KB → 47.09 KB |
| StringAppender.AppendShortAscii(RowCount=500000) | 57.82 ms | 58.48 ms | 1.01× | 47.09 KB → 47.09 KB |
| StringAppender.AppendShortGeorgian(RowCount=500000) | 50.73 ms | 49.65 ms | 0.98× | 47.09 KB → 47.09 KB |
| StringReader.LongAscii(RowCount=200000) | 23.58 ms | 23.66 ms | 1.00× | 199.90 MB → 199.90 MB |
| StringReader.LongUnicode(RowCount=200000) | 78.8 ms | 78.31 ms | 0.99× | 89.27 MB → 89.27 MB |
| StringReader.MediumAscii(RowCount=200000) | 5.579 ms | 5.653 ms | 1.01× | 19.84 MB → 19.84 MB |
| StringReader.Mixed(RowCount=200000) | 9.77 ms | 9.829 ms | 1.01× | 47.46 MB → 47.46 MB |
| StringReader.ShortAscii(RowCount=200000) | 3.043 ms | 3.09 ms | 1.02× | 6.87 MB → 6.87 MB |
| StringReader.ShortUnicode(RowCount=200000) | 7.867 ms | 7.677 ms | 0.98× | 10.69 MB → 10.69 MB |
| TableFunctionRow.ClassRows(RowCount=1000000,Columns=1) | 2.788 ms | 2.703 ms | 0.97× | 40.52 KB → 41.58 KB |
| TableFunctionRow.ClassRows(RowCount=1000000,Columns=8) | 16.36 ms | 16.21 ms | 0.99× | 204.50 KB → 212.74 KB |
| TableFunctionRow.EnumeratedRows(RowCount=1000000,Columns=1) | 4.001 ms | 4.063 ms | 1.02× | 40.66 KB → 41.71 KB |
| TableFunctionRow.EnumeratedRows(RowCount=1000000,Columns=8) | 18.02 ms | 18 ms | 1.00× | 204.64 KB → 212.87 KB |
| TableFunctionRow.SqlTable(RowCount=1000000,Columns=1) | 287.4 µs | 285 µs | 0.99× | 1.34 KB → 1.34 KB |
| TableFunctionRow.SqlTable(RowCount=1000000,Columns=8) | 1.805 ms | 1.771 ms | 0.98× | 3.67 KB → 3.67 KB |
| TableFunctionRow.StructRows(RowCount=1000000,Columns=1) | 2.958 ms | 2.972 ms | 1.00× | 40.59 KB → 41.65 KB |
| TableFunctionRow.StructRows(RowCount=1000000,Columns=8) | 16.26 ms | 16.31 ms | 1.00× | 204.57 KB → 212.81 KB |
| TypedReader.ReadDecimals(RowCount=1000000) | 20.85 ms | 20.83 ms | 1.00× | 17.23 KB → 17.23 KB |
| TypedReader.ReadGuids(RowCount=1000000) | 14.26 ms | 14.42 ms | 1.01× | 17.00 KB → 17.00 KB |
| TypedReader.ReadHugeIntsAsBigInteger(RowCount=1000000) | 21.41 ms | 21.09 ms | 0.98× | 17.01 KB → 17.01 KB |
| TypedReader.ReadHugeIntsAsLong(RowCount=1000000) | 12.29 ms | 10.56 ms | 0.86× | 17.00 KB → 17.00 KB |
| TypedReader.ReadHugeIntsAsObject(RowCount=1000000) | 26.81 ms | 27.31 ms | 1.02× | 122.09 MB → 122.09 MB |
| TypedReader.ReadIntegersAsNullableLong(RowCount=1000000) | 7.345 ms | 7.427 ms | 1.01× | 16.99 KB → 16.99 KB |
| TypedReader.ReadLargeHugeIntsAsBigInteger(RowCount=1000000) | 40.19 ms | 40.24 ms | 1.00× | 152.61 MB → 152.61 MB |
| TypedReader.ReadVarIntsAsBigInteger(RowCount=1000000) | 51.19 ms | 50.56 ms | 0.99× | 76.31 MB → 76.31 MB |
| TypedReader.ReadWideDecimals(RowCount=1000000) | 27.57 ms | 27.62 ms | 1.00× | 17.23 KB → 17.23 KB |
| WideTableReader.ReadAllColumns(RowCount=100000,ColumnCount=10) | 5.939 ms | 5.882 ms | 0.99× | 7.63 MB → 7.63 MB |
| WideTableReader.ReadAllColumns(RowCount=100000,ColumnCount=20) | 12.2 ms | 12.36 ms | 1.01× | 11.45 MB → 11.45 MB |
| WideTableReader.ReadAllColumns(RowCount=100000,ColumnCount=5) | 3.121 ms | 3.141 ms | 1.01× | 3.82 MB → 3.82 MB |
| WideTableReader.ReadAllColumnsByName(RowCount=100000,ColumnCount=10) | 10.26 ms | 10.67 ms | 1.04× | 7.63 MB → 7.63 MB |
| WideTableReader.ReadAllColumnsByName(RowCount=100000,ColumnCount=20) | 24.71 ms | 25.14 ms | 1.02× | 11.45 MB → 11.45 MB |
| WideTableReader.ReadAllColumnsByName(RowCount=100000,ColumnCount=5) | 5.392 ms | 5.079 ms | 0.94× | 3.82 MB → 3.82 MB |
| WideTableReader.ReadAllColumnsWithGetValues(RowCount=100000,ColumnCount=10) | 7.624 ms | 7.782 ms | 1.02× | 24.42 MB → 24.42 MB |
| WideTableReader.ReadAllColumnsWithGetValues(RowCount=100000,ColumnCount=20) | 14.74 ms | 14.48 ms | 0.98× | 45.79 MB → 45.79 MB |
| WideTableReader.ReadAllColumnsWithGetValues(RowCount=100000,ColumnCount=5) | 3.515 ms | 3.529 ms | 1.00× | 11.45 MB → 11.45 MB |
