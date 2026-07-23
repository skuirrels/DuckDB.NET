package org.duckdb.benchmarks;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.SQLException;
import java.util.concurrent.TimeUnit;
import org.duckdb.DuckDBConnection;
import org.openjdk.jmh.annotations.Benchmark;
import org.openjdk.jmh.annotations.BenchmarkMode;
import org.openjdk.jmh.annotations.Fork;
import org.openjdk.jmh.annotations.Level;
import org.openjdk.jmh.annotations.Measurement;
import org.openjdk.jmh.annotations.Mode;
import org.openjdk.jmh.annotations.OperationsPerInvocation;
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
public class BulkIngestionBenchmark {
    private DuckDBConnection connection;
    private PreparedStatement preparedStatement;
    private RealisticBenchmarkSupport.IngestRow[] rows;

    @Setup(Level.Trial)
    public void setup() throws SQLException {
        connection = (DuckDBConnection) RealisticBenchmarkSupport.openConnection();
        RealisticBenchmarkSupport.initializeIngest(connection);
        preparedStatement = connection.prepareStatement(RealisticBenchmarkSupport.INGEST_STATEMENT);
        rows = RealisticBenchmarkSupport.createIngestRows();
    }

    @TearDown(Level.Trial)
    public void cleanup() throws SQLException {
        preparedStatement.close();
        connection.close();
    }

    @Benchmark
    @OperationsPerInvocation(RealisticBenchmarkSupport.INGEST_ROW_COUNT)
    public int insertPreparedInTransaction() throws SQLException {
        connection.setAutoCommit(false);
        try {
            for (var row : rows) {
                RealisticBenchmarkSupport.bindIngest(preparedStatement, row);
                preparedStatement.executeUpdate();
            }
            connection.rollback();
            return rows.length;
        } finally {
            connection.setAutoCommit(true);
        }
    }

    @Benchmark
    @OperationsPerInvocation(RealisticBenchmarkSupport.INGEST_ROW_COUNT)
    public int insertWithAppenderInTransaction() throws SQLException {
        connection.setAutoCommit(false);
        try {
            try (var appender = connection.createAppender("benchmark_ingest")) {
                for (var row : rows) {
                    appender.beginRow()
                            .append(row.id())
                            .append(row.eventTime())
                            .append(row.amount())
                            .append(row.category())
                            .append(row.isActive())
                            .endRow();
                }
            }
            connection.rollback();
            return rows.length;
        } finally {
            connection.setAutoCommit(true);
        }
    }
}
