package org.duckdb.benchmarks;

import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;

final class BenchmarkSupport {
    static final String QUERY = "SELECT ?::BIGINT + ?::BIGINT + ?::BIGINT";

    private static final String EXPECTED_ENGINE_VERSION = "v1.5.4";

    private BenchmarkSupport() {
    }

    static Connection openVerifiedConnection() throws SQLException {
        var connection = DriverManager.getConnection("jdbc:duckdb:");

        try (Statement statement = connection.createStatement();
             ResultSet result = statement.executeQuery("SELECT version()")) {
            if (!result.next()) {
                connection.close();
                throw new SQLException("DuckDB did not return an engine version");
            }

            var actualVersion = result.getString(1);
            if (!EXPECTED_ENGINE_VERSION.equals(actualVersion)) {
                connection.close();
                throw new SQLException(
                        "The driver comparison requires DuckDB " + EXPECTED_ENGINE_VERSION
                                + ", but loaded " + actualVersion);
            }
        }

        return connection;
    }

    static void bindParameters(PreparedStatement statement, long first) throws SQLException {
        statement.setLong(1, first);
        statement.setLong(2, 2L);
        statement.setLong(3, 3L);
    }

    static long executeScalar(PreparedStatement statement) throws SQLException {
        try (ResultSet result = statement.executeQuery()) {
            if (!result.next()) {
                throw new SQLException("DuckDB did not return a scalar result");
            }

            return result.getLong(1);
        }
    }
}
