package org.duckdb.benchmarks;

import java.sql.Connection;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.concurrent.TimeUnit;
import org.openjdk.jmh.annotations.Benchmark;
import org.openjdk.jmh.annotations.BenchmarkMode;
import org.openjdk.jmh.annotations.Fork;
import org.openjdk.jmh.annotations.Level;
import org.openjdk.jmh.annotations.Measurement;
import org.openjdk.jmh.annotations.Mode;
import org.openjdk.jmh.annotations.OutputTimeUnit;
import org.openjdk.jmh.annotations.Scope;
import org.openjdk.jmh.annotations.Setup;
import org.openjdk.jmh.annotations.State;
import org.openjdk.jmh.annotations.TearDown;
import org.openjdk.jmh.annotations.Warmup;

@State(Scope.Thread)
@BenchmarkMode(Mode.AverageTime)
@OutputTimeUnit(TimeUnit.NANOSECONDS)
@Fork(1)
@Warmup(iterations = 5, time = 500, timeUnit = TimeUnit.MILLISECONDS)
@Measurement(iterations = 10, time = 500, timeUnit = TimeUnit.MILLISECONDS)
public class TpchBenchmark {
    private Connection connection;
    private Statement statement;

    @Setup(Level.Trial)
    public void setup() throws SQLException {
        connection = RealisticBenchmarkSupport.openConnection();
        RealisticBenchmarkSupport.initializeTpch(connection);
        statement = connection.createStatement();
    }

    @TearDown(Level.Trial)
    public void cleanup() throws SQLException {
        statement.close();
        connection.close();
    }

    @Benchmark
    public long query01() throws SQLException {
        return RealisticBenchmarkSupport.consumeTpch(statement, 1);
    }

    @Benchmark
    public long query06() throws SQLException {
        return RealisticBenchmarkSupport.consumeTpch(statement, 6);
    }

    @Benchmark
    public long query12() throws SQLException {
        return RealisticBenchmarkSupport.consumeTpch(statement, 12);
    }

    @Benchmark
    public long query14() throws SQLException {
        return RealisticBenchmarkSupport.consumeTpch(statement, 14);
    }
}
