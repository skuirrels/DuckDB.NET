using System.Data;
using DuckDB.NET.Data;

namespace DuckDB.NET.Benchmarks;

internal static class RealisticWorkload
{
    public const int AnalyticsRowCount = 2_000_000;
    public const int MaterializationRowCount = 100_000;
    public const int IngestRowCount = 10_000;
    public const double TpchScaleFactor = 0.1;

    public const string AnalyticsQuery = """
        SELECT
            year(order_date) AS order_year,
            region,
            count(*) AS order_count,
            sum(amount) AS revenue
        FROM benchmark_orders
        WHERE customer_id = $customerId
          AND order_date >= $fromDate
          AND order_date < $toDate
        GROUP BY order_year, region
        ORDER BY order_year, region
        """;

    public const string MaterializationQuery = """
        SELECT id, event_date, event_time, amount, customer_name, is_active
        FROM benchmark_materialization
        ORDER BY id
        """;

    public const string IngestStatement = """
        INSERT INTO benchmark_ingest
        VALUES ($id, $eventTime, $amount, $category, $isActive)
        """;

    public static readonly DateOnly AnalyticsFromDate = new(2020, 1, 1);
    public static readonly DateOnly AnalyticsToDate = new(2024, 1, 1);

    public static DuckDBConnection OpenConnection()
    {
        var connection = PreparedCommandWorkload.OpenVerifiedConnection();
        ExecuteNonQuery(connection, "SET threads = 1");
        return connection;
    }

    public static void InitializeAnalytics(DuckDBConnection connection)
    {
        ExecuteNonQuery(connection, $$"""
            CREATE TABLE benchmark_orders AS
            SELECT
                i::BIGINT AS order_id,
                (i % 10000)::INTEGER AS customer_id,
                DATE '2020-01-01' + ((i % 1461)::INTEGER) AS order_date,
                CASE i % 4
                    WHEN 0 THEN 'north'
                    WHEN 1 THEN 'south'
                    WHEN 2 THEN 'east'
                    ELSE 'west'
                END::VARCHAR AS region,
                ((i % 100000)::DOUBLE / 100.0) AS amount
            FROM range({{AnalyticsRowCount}}) AS source(i)
            """);
    }

    public static void InitializeMaterialization(DuckDBConnection connection)
    {
        ExecuteNonQuery(connection, $$"""
            CREATE TABLE benchmark_materialization AS
            SELECT
                i::BIGINT AS id,
                DATE '2020-01-01' + ((i % 1461)::INTEGER) AS event_date,
                TIMESTAMP '2020-01-01 00:00:00' + ((i % 31536000) * INTERVAL 1 SECOND) AS event_time,
                ((i % 100000)::DOUBLE / 100.0) AS amount,
                CASE
                    WHEN i % 10 = 0 THEN NULL
                    ELSE ('customer-' || (i % 10000)::VARCHAR)
                END::VARCHAR AS customer_name,
                (i % 2 = 0) AS is_active
            FROM range({{MaterializationRowCount}}) AS source(i)
            """);
    }

    public static void InitializeIngest(DuckDBConnection connection)
    {
        ExecuteNonQuery(connection, """
            CREATE TABLE benchmark_ingest(
                id BIGINT,
                event_time TIMESTAMP,
                amount DOUBLE,
                category VARCHAR,
                is_active BOOLEAN
            )
            """);
    }

    public static void InitializeTpch(DuckDBConnection connection)
    {
        ExecuteNonQuery(connection, "INSTALL tpch");
        ExecuteNonQuery(connection, "LOAD tpch");
        ExecuteNonQuery(connection, $"CALL dbgen(sf = {TpchScaleFactor.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
    }

    public static DuckDBCommand CreateAnalyticsCommand(DuckDBConnection connection, out DuckDBParameter customerParameter)
    {
        var command = connection.CreateCommand();
        command.CommandText = AnalyticsQuery;
        customerParameter = new DuckDBParameter("customerId", 0);
        command.Parameters.Add(customerParameter);
        command.Parameters.Add(new DuckDBParameter("fromDate", AnalyticsFromDate));
        command.Parameters.Add(new DuckDBParameter("toDate", AnalyticsToDate));
        return command;
    }

    public static long ConsumeAnalytics(DuckDBCommand command)
    {
        using var reader = command.ExecuteReader();
        var rowCount = 0;
        long checksum = 17;

        while (reader.Read())
        {
            checksum = unchecked(checksum * 31 + reader.GetInt32(0));
            checksum = unchecked(checksum * 31 + reader.GetString(1).Length);
            checksum = unchecked(checksum * 31 + reader.GetInt64(2));
            checksum = unchecked(checksum * 31 + BitConverter.DoubleToInt64Bits(reader.GetDouble(3)));
            rowCount++;
        }

        if (rowCount == 0)
        {
            throw new InvalidOperationException("The analytical workload returned no rows.");
        }

        return checksum;
    }

    public static long ConsumeMaterialization(DuckDBCommand command)
    {
        using var reader = command.ExecuteReader();
        var rowCount = 0;
        long checksum = 17;

        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var eventDate = reader.GetFieldValue<DateOnly>(1);
            var eventTime = reader.GetDateTime(2);
            var amount = reader.GetDouble(3);
            var customerLength = reader.IsDBNull(4) ? 0 : reader.GetString(4).Length;
            var isActive = reader.GetBoolean(5);

            checksum = unchecked(checksum + id + eventDate.DayNumber + eventTime.Ticks);
            checksum = unchecked(checksum + BitConverter.DoubleToInt64Bits(amount));
            checksum = unchecked(checksum + customerLength + (isActive ? 1 : 0));
            rowCount++;
        }

        if (rowCount != MaterializationRowCount)
        {
            throw new InvalidOperationException(
                $"Expected {MaterializationRowCount} materialized rows, but read {rowCount}.");
        }

        return checksum;
    }

    public static long ConsumeTpch(DuckDBCommand command)
    {
        using var reader = command.ExecuteReader();
        var rowCount = 0;
        long checksum = 17;

        while (reader.Read())
        {
            for (var column = 0; column < reader.FieldCount; column++)
            {
                var value = reader.GetValue(column);
                checksum = unchecked(checksum * 31 + (value is DBNull ? 0 : value.GetHashCode()));
            }

            rowCount++;
        }

        if (rowCount == 0)
        {
            throw new InvalidOperationException("The TPC-H workload returned no rows.");
        }

        return checksum;
    }

    public static IngestRow[] CreateIngestRows()
    {
        var rows = new IngestRow[IngestRowCount];
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var categories = new[] { "north", "south", "east", "west" };

        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = new IngestRow(
                index,
                start.AddSeconds(index),
                index % 100000 / 100.0,
                categories[index % categories.Length],
                index % 2 == 0);
        }

        return rows;
    }

    public static void ExecuteNonQuery(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

internal readonly record struct IngestRow(
    long Id,
    DateTime EventTime,
    double Amount,
    string Category,
    bool IsActive);
