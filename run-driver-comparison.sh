#!/usr/bin/env bash

set -euo pipefail

PROJECT_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
ARTIFACTS_ROOT="${DRIVER_BENCHMARK_OUTPUT:-${PROJECT_ROOT}/BenchmarkDotNet.Artifacts/DriverComparison/${RUN_ID}}"
GO_RUN_COUNT="${GO_COUNT:-10}"
GO_RUN_BENCHTIME="${GO_BENCHTIME:-500ms}"
GO_MODULE_CACHE="${DRIVER_BENCHMARK_GOMODCACHE:-${GOMODCACHE:-}}"
GO_BUILD_CACHE="${DRIVER_BENCHMARK_GOCACHE:-${GOCACHE:-}}"
JAVA_RUN_WARMUP_ITERATIONS="${JAVA_WARMUP_ITERATIONS:-5}"
JAVA_RUN_MEASUREMENT_ITERATIONS="${JAVA_MEASUREMENT_ITERATIONS:-10}"
JAVA_RUN_ITERATION_TIME="${JAVA_ITERATION_TIME:-500ms}"
SKIP_RELEASE_BUILD="${SKIP_DOTNET_BUILD:-false}"
SKIP_JAVA_RELEASE_BUILD="${SKIP_JAVA_BUILD:-false}"

# Keep runner overrides away from MSBuild; environment variables are also
# imported as MSBuild properties and can interfere with the .NET project build.
unset DRIVER_BENCHMARK_OUTPUT GO_COUNT GO_BENCHTIME SKIP_DOTNET_BUILD SKIP_JAVA_BUILD
unset DRIVER_BENCHMARK_GOMODCACHE DRIVER_BENCHMARK_GOCACHE
unset JAVA_WARMUP_ITERATIONS JAVA_MEASUREMENT_ITERATIONS JAVA_ITERATION_TIME
unset GOMODCACHE GOCACHE

mkdir -p "${ARTIFACTS_ROOT}/dotnet-1.5.3"
mkdir -p "${ARTIFACTS_ROOT}/dotnet-efcore-1.13.0"
mkdir -p "${ARTIFACTS_ROOT}/dotnet-fork-1.5.4"
mkdir -p "${ARTIFACTS_ROOT}/java"

{
  uname -a
  dotnet --version
  go version
  java -version 2>&1
  mvn -version
  echo "DuckDB.NET package baseline: 1.5.3"
  echo "DuckDB.EFCoreProvider package: 1.13.0 (depends on DuckDB.NET.Data.Full 1.5.3)"
  echo "DuckDB.NET source lane: consolidated fork 1.5.4"
  echo "duckdb-go: v2.10504.0 (DuckDB 1.5.4)"
  echo "DuckDB JDBC: org.duckdb:duckdb_jdbc:1.5.4.0 (DuckDB 1.5.4)"
  echo "DuckDB threads per connection: 1"
  echo "Analytical rows: 2000000"
  echo "Materialization rows: 100000"
  echo "Ingest rows per transaction: 10000"
  echo "TPC-H scale factor: 0.1; queries: 1, 6, 12, 14"
} > "${ARTIFACTS_ROOT}/environment.txt"

if [[ "${SKIP_RELEASE_BUILD}" != "1" && "${SKIP_RELEASE_BUILD}" != "true" ]]; then
  dotnet build \
    "${PROJECT_ROOT}/DuckDB.NET.Benchmarks/Benchmarks.csproj" \
    --configuration Release \
    --disable-build-servers \
    --nologo

  dotnet build \
    "${PROJECT_ROOT}/DuckDB.NET.1_5_3.Benchmarks/DuckDB.NET.1_5_3.Benchmarks.csproj" \
    --configuration Release \
    --disable-build-servers \
    --nologo

  dotnet build \
    "${PROJECT_ROOT}/DuckDB.EFCoreProvider.1_13_0.Benchmarks/DuckDB.EFCoreProvider.1_13_0.Benchmarks.csproj" \
    --configuration Release \
    --disable-build-servers \
    --nologo
fi

if [[ "${SKIP_JAVA_RELEASE_BUILD}" != "1" && "${SKIP_JAVA_RELEASE_BUILD}" != "true" ]]; then
  (
    cd "${PROJECT_ROOT}/DuckDB.Java.Benchmarks"
    mvn --batch-mode --no-transfer-progress package
  )
fi

dotnet run \
  --no-build \
  --configuration Release \
  --project "${PROJECT_ROOT}/DuckDB.NET.1_5_3.Benchmarks/DuckDB.NET.1_5_3.Benchmarks.csproj" \
  -- \
  --filter '*' \
  --artifacts "${ARTIFACTS_ROOT}/dotnet-1.5.3"

dotnet run \
  --no-build \
  --configuration Release \
  --project "${PROJECT_ROOT}/DuckDB.EFCoreProvider.1_13_0.Benchmarks/DuckDB.EFCoreProvider.1_13_0.Benchmarks.csproj" \
  -- \
  --filter '*' \
  --artifacts "${ARTIFACTS_ROOT}/dotnet-efcore-1.13.0"

dotnet run \
  --no-build \
  --configuration Release \
  --project "${PROJECT_ROOT}/DuckDB.NET.Benchmarks/Benchmarks.csproj" \
  -- \
  --filter '*' \
  --artifacts "${ARTIFACTS_ROOT}/dotnet-fork-1.5.4"

(
  cd "${PROJECT_ROOT}/DuckDB.Go.Benchmarks"
  if [[ -n "${GO_MODULE_CACHE}" ]]; then
    export GOMODCACHE="${GO_MODULE_CACHE}"
  fi
  if [[ -n "${GO_BUILD_CACHE}" ]]; then
    export GOCACHE="${GO_BUILD_CACHE}"
  fi
  go test \
    -run '^$' \
    -bench '^Benchmark(PreparedCommand|AnalyticalQuery|ResultMaterialization|BulkIngestion|Tpch)' \
    -benchmem \
    -count "${GO_RUN_COUNT}" \
    -benchtime "${GO_RUN_BENCHTIME}" \
    .
) | tee "${ARTIFACTS_ROOT}/go.txt"

(
  cd "${PROJECT_ROOT}/DuckDB.Java.Benchmarks"
  java --enable-native-access=ALL-UNNAMED \
    -jar target/benchmarks-all.jar \
    'org.duckdb.benchmarks.*' \
    -f 1 \
    -wi "${JAVA_RUN_WARMUP_ITERATIONS}" \
    -i "${JAVA_RUN_MEASUREMENT_ITERATIONS}" \
    -w "${JAVA_RUN_ITERATION_TIME}" \
    -r "${JAVA_RUN_ITERATION_TIME}" \
    -tu ns \
    -foe true \
    -prof gc \
    -rf json \
    -rff "${ARTIFACTS_ROOT}/java/results.json"
) | tee "${ARTIFACTS_ROOT}/java.txt"

echo "Comparison artifacts: ${ARTIFACTS_ROOT}"
