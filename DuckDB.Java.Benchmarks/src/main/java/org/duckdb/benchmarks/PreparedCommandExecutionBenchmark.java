package org.duckdb.benchmarks;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.SQLException;
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
public class PreparedCommandExecutionBenchmark {
    private Connection connection;
    private PreparedStatement preparedStatement;
    private long nextValue;

    @Setup(Level.Trial)
    public void setup() throws SQLException {
        connection = BenchmarkSupport.openVerifiedConnection();
        preparedStatement = connection.prepareStatement(BenchmarkSupport.QUERY);
    }

    @TearDown(Level.Trial)
    public void cleanup() throws SQLException {
        preparedStatement.close();
        connection.close();
    }

    @Benchmark
    public long executeUnprepared() throws SQLException {
        try (PreparedStatement statement = connection.prepareStatement(BenchmarkSupport.QUERY)) {
            BenchmarkSupport.bindParameters(statement, nextValue++);
            return BenchmarkSupport.executeScalar(statement);
        }
    }

    @Benchmark
    public long executePrepared() throws SQLException {
        BenchmarkSupport.bindParameters(preparedStatement, nextValue++);
        return BenchmarkSupport.executeScalar(preparedStatement);
    }
}
