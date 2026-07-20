package org.duckdb.benchmarks;

import java.sql.Connection;
import java.sql.Date;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.sql.Timestamp;
import java.time.LocalDate;
import java.time.LocalDateTime;

final class RealisticBenchmarkSupport {
    static final int ANALYTICS_ROW_COUNT = 2_000_000;
    static final int MATERIALIZATION_ROW_COUNT = 100_000;
    static final int INGEST_ROW_COUNT = 10_000;
    static final double TPCH_SCALE_FACTOR = 0.1;

    static final LocalDate ANALYTICS_FROM_DATE = LocalDate.of(2020, 1, 1);
    static final LocalDate ANALYTICS_TO_DATE = LocalDate.of(2024, 1, 1);

    static final String ANALYTICS_QUERY = """
            SELECT
                year(order_date) AS order_year,
                region,
                count(*) AS order_count,
                sum(amount) AS revenue
            FROM benchmark_orders
            WHERE customer_id = ?
              AND order_date >= ?
              AND order_date < ?
            GROUP BY order_year, region
            ORDER BY order_year, region
            """;

    static final String MATERIALIZATION_QUERY = """
            SELECT id, event_date, event_time, amount, customer_name, is_active
            FROM benchmark_materialization
            ORDER BY id
            """;

    static final String INGEST_STATEMENT = """
            INSERT INTO benchmark_ingest
            VALUES (?, ?, ?, ?, ?)
            """;

    private RealisticBenchmarkSupport() {
    }

    static Connection openConnection() throws SQLException {
        var connection = BenchmarkSupport.openVerifiedConnection();
        executeNonQuery(connection, "SET threads = 1");
        return connection;
    }

    static void initializeAnalytics(Connection connection) throws SQLException {
        executeNonQuery(connection, """
                CREATE TABLE benchmark_orders AS
                SELECT
                    i::BIGINT AS order_id,
                    (i %% 10000)::INTEGER AS customer_id,
                    DATE '2020-01-01' + ((i %% 1461)::INTEGER) AS order_date,
                    CASE i %% 4
                        WHEN 0 THEN 'north'
                        WHEN 1 THEN 'south'
                        WHEN 2 THEN 'east'
                        ELSE 'west'
                    END::VARCHAR AS region,
                    ((i %% 100000)::DOUBLE / 100.0) AS amount
                FROM range(%d) AS source(i)
                """.formatted(ANALYTICS_ROW_COUNT));
    }

    static void initializeMaterialization(Connection connection) throws SQLException {
        executeNonQuery(connection, """
                CREATE TABLE benchmark_materialization AS
                SELECT
                    i::BIGINT AS id,
                    DATE '2020-01-01' + ((i %% 1461)::INTEGER) AS event_date,
                    TIMESTAMP '2020-01-01 00:00:00' + ((i %% 31536000) * INTERVAL 1 SECOND) AS event_time,
                    ((i %% 100000)::DOUBLE / 100.0) AS amount,
                    CASE
                        WHEN i %% 10 = 0 THEN NULL
                        ELSE ('customer-' || (i %% 10000)::VARCHAR)
                    END::VARCHAR AS customer_name,
                    (i %% 2 = 0) AS is_active
                FROM range(%d) AS source(i)
                """.formatted(MATERIALIZATION_ROW_COUNT));
    }

    static void initializeIngest(Connection connection) throws SQLException {
        executeNonQuery(connection, """
                CREATE TABLE benchmark_ingest(
                    id BIGINT,
                    event_time TIMESTAMP,
                    amount DOUBLE,
                    category VARCHAR,
                    is_active BOOLEAN
                )
                """);
    }

    static void initializeTpch(Connection connection) throws SQLException {
        executeNonQuery(connection, "INSTALL tpch");
        executeNonQuery(connection, "LOAD tpch");
        executeNonQuery(connection, "CALL dbgen(sf = " + TPCH_SCALE_FACTOR + ")");
    }

    static void bindAnalytics(PreparedStatement statement, int customerId) throws SQLException {
        statement.setInt(1, customerId);
        statement.setDate(2, Date.valueOf(ANALYTICS_FROM_DATE));
        statement.setDate(3, Date.valueOf(ANALYTICS_TO_DATE));
    }

    static long consumeAnalytics(PreparedStatement statement) throws SQLException {
        try (ResultSet result = statement.executeQuery()) {
            var rowCount = 0;
            long checksum = 17;

            while (result.next()) {
                checksum = checksum * 31 + result.getInt(1);
                checksum = checksum * 31 + result.getString(2).length();
                checksum = checksum * 31 + result.getLong(3);
                checksum = checksum * 31 + Double.doubleToLongBits(result.getDouble(4));
                rowCount++;
            }

            if (rowCount == 0) {
                throw new SQLException("The analytical workload returned no rows");
            }

            return checksum;
        }
    }

    static long consumeMaterialization(Statement statement) throws SQLException {
        try (ResultSet result = statement.executeQuery(MATERIALIZATION_QUERY)) {
            var rowCount = 0;
            long checksum = 17;

            while (result.next()) {
                var id = result.getLong(1);
                var eventDate = result.getDate(2).toLocalDate();
                var eventTime = result.getTimestamp(3).toLocalDateTime();
                var amount = result.getDouble(4);
                var customer = result.getString(5);
                var isActive = result.getBoolean(6);

                checksum += id + eventDate.toEpochDay() + eventTime.getNano();
                checksum += Double.doubleToLongBits(amount);
                checksum += (customer == null ? 0 : customer.length()) + (isActive ? 1 : 0);
                rowCount++;
            }

            if (rowCount != MATERIALIZATION_ROW_COUNT) {
                throw new SQLException(
                        "Expected " + MATERIALIZATION_ROW_COUNT + " materialized rows, but read " + rowCount);
            }

            return checksum;
        }
    }

    static long consumeTpch(Statement statement, int queryNumber) throws SQLException {
        try (ResultSet result = statement.executeQuery("PRAGMA tpch(" + queryNumber + ")")) {
            var rowCount = 0;
            long checksum = 17;
            var columnCount = result.getMetaData().getColumnCount();

            while (result.next()) {
                for (var column = 1; column <= columnCount; column++) {
                    var value = result.getObject(column);
                    checksum = checksum * 31 + (value == null ? 0 : value.hashCode());
                }
                rowCount++;
            }

            if (rowCount == 0) {
                throw new SQLException("The TPC-H workload returned no rows");
            }

            return checksum;
        }
    }

    static IngestRow[] createIngestRows() {
        var rows = new IngestRow[INGEST_ROW_COUNT];
        var start = LocalDateTime.of(2024, 1, 1, 0, 0);
        var categories = new String[] {"north", "south", "east", "west"};

        for (var index = 0; index < rows.length; index++) {
            rows[index] = new IngestRow(
                    index,
                    start.plusSeconds(index),
                    index % 100000 / 100.0,
                    categories[index % categories.length],
                    index % 2 == 0);
        }

        return rows;
    }

    static void bindIngest(PreparedStatement statement, IngestRow row) throws SQLException {
        statement.setLong(1, row.id());
        statement.setTimestamp(2, Timestamp.valueOf(row.eventTime()));
        statement.setDouble(3, row.amount());
        statement.setString(4, row.category());
        statement.setBoolean(5, row.isActive());
    }

    static void executeNonQuery(Connection connection, String sql) throws SQLException {
        try (Statement statement = connection.createStatement()) {
            statement.execute(sql);
        }
    }

    record IngestRow(long id, LocalDateTime eventTime, double amount, String category, boolean isActive) {
    }
}
