using DuckDB.NET.Data.Extensions;

namespace DuckDB.NET.Test;

public class DateTimeConversionTests(DuckDBDatabaseFixture db) : DuckDBTestBase(db)
{
    public static IEnumerable<object[]> TimestampConversionCases()
    {
        var epoch = DateTime.UnixEpoch;
        var values = new[]
        {
            DateTime.MinValue,
            new DateTime(1600, 2, 29, 23, 59, 59, DateTimeKind.Unspecified).AddTicks(9_999_999),
            epoch.AddTicks(-11),
            epoch.AddTicks(-10),
            epoch.AddTicks(-9),
            epoch.AddTicks(-1),
            epoch,
            epoch.AddTicks(1),
            epoch.AddTicks(9),
            epoch.AddTicks(10),
            epoch.AddTicks(11),
            new DateTime(2000, 2, 29, 12, 34, 56, DateTimeKind.Utc).AddTicks(7_654_321),
            new DateTime(2262, 4, 11, 23, 47, 16, DateTimeKind.Unspecified).AddTicks(8_000_000),
            DateTime.MaxValue,
        };

        var types = new[]
        {
            DuckDBType.Timestamp,
            DuckDBType.TimestampS,
            DuckDBType.TimestampMs,
            DuckDBType.TimestampNs,
            DuckDBType.TimestampTz,
        };

        foreach (var value in values)
        {
            foreach (var type in types)
            {
                yield return new object[] { value, type };
            }
        }
    }

    public static IEnumerable<object[]> TimestampOffsetConversionCases()
    {
        yield return new object[] { new DateTimeOffset(1970, 1, 1, 0, 30, 0, TimeSpan.FromHours(1)) };
        yield return new object[] { new DateTimeOffset(1969, 12, 31, 23, 30, 0, TimeSpan.FromHours(-1)) };
        yield return new object[] { new DateTimeOffset(2000, 2, 29, 23, 45, 12, 345, TimeSpan.FromHours(5.5)).AddTicks(6_789) };
        yield return new object[] { DateTimeOffset.MinValue };
        yield return new object[] { DateTimeOffset.MaxValue };
    }

    [Theory]
    [MemberData(nameof(TimestampConversionCases))]
    public void ManagedTimestampConversionMatchesPreviousNativeConversion(DateTime value, DuckDBType type)
    {
        var expected = ConvertUsingNativeTimestampHelper(value, type);
        var actual = value.ToTimestampStruct(type);

        actual.Micros.Should().Be(expected.Micros);
    }

    [Theory]
    [MemberData(nameof(TimestampOffsetConversionCases))]
    public void ManagedTimestampOffsetConversionMatchesPreviousNativeConversion(DateTimeOffset value)
    {
        var expected = DuckDBTimestamp.FromDateTime(value.UtcDateTime).ToDuckDBTimestampStruct();
        var actual = value.ToTimestampStruct();

        actual.Micros.Should().Be(expected.Micros);
    }

    [Fact]
    public void TimestampEdgeCasesRoundTripThroughScopedAppenderAndPreparedStatement()
    {
        var rows = new[]
        {
            new TimestampEdgeRow(
                1,
                new DateTime(DateTime.UnixEpoch.Ticks - 11, DateTimeKind.Unspecified),
                new DateTimeOffset(1970, 1, 1, 0, 30, 0, TimeSpan.FromHours(1))),
            new TimestampEdgeRow(
                2,
                new DateTime(DateTime.UnixEpoch.Ticks - 1, DateTimeKind.Unspecified),
                new DateTimeOffset(1969, 12, 31, 23, 30, 0, TimeSpan.FromHours(-1))),
            new TimestampEdgeRow(
                3,
                new DateTime(DateTime.UnixEpoch.Ticks + 11, DateTimeKind.Unspecified),
                new DateTimeOffset(2000, 2, 29, 23, 45, 12, 345, TimeSpan.FromHours(5.5)).AddTicks(6_789)),
        };

        CreateTimestampTable("managedTimestampScopedAppender");
        using (var appender = Connection.CreateAppender("managedTimestampScopedAppender"))
        {
            foreach (var row in rows)
            {
                appender.AppendRowScoped(row,
                    static (ref DuckDBAppenderRowWriter writer, TimestampEdgeRow value) =>
                    {
                        writer.AppendValue((int?)value.Id);
                        writer.AppendValue((DateTime?)value.Timestamp);
                        writer.AppendValue((DateTime?)value.Timestamp);
                        writer.AppendValue((DateTime?)value.Timestamp);
                        writer.AppendValue((DateTime?)value.Timestamp);
                        writer.AppendValue((DateTimeOffset?)value.TimestampOffset);
                    });
            }
        }

        CreateTimestampTable("managedTimestampPreparedStatement");
        InsertPreparedTimestampRows("managedTimestampPreparedStatement", rows);

        AssertTimestampRows("managedTimestampScopedAppender", rows);
        AssertTimestampRows("managedTimestampPreparedStatement", rows);
    }

    private void CreateTimestampTable(string tableName)
    {
        Command.CommandText = $$"""
                              CREATE TABLE {{tableName}}(
                                  id INTEGER,
                                  timestamp_value TIMESTAMP,
                                  timestamp_s_value TIMESTAMP_S,
                                  timestamp_ms_value TIMESTAMP_MS,
                                  timestamp_ns_value TIMESTAMP_NS,
                                  timestamp_tz_value TIMESTAMPTZ)
                              """;
        Command.ExecuteNonQuery();
    }

    private void InsertPreparedTimestampRows(string tableName, IReadOnlyList<TimestampEdgeRow> rows)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = $"INSERT INTO {tableName} VALUES (?, ?, ?, ?, ?, ?)";

        var id = new DuckDBParameter(rows[0].Id);
        var timestamp = new DuckDBParameter(rows[0].Timestamp);
        var timestampS = new DuckDBParameter(rows[0].Timestamp);
        var timestampMs = new DuckDBParameter(rows[0].Timestamp);
        var timestampNs = new DuckDBParameter(rows[0].Timestamp);
        var timestampTz = new DuckDBParameter(rows[0].TimestampOffset);
        command.Parameters.Add(id);
        command.Parameters.Add(timestamp);
        command.Parameters.Add(timestampS);
        command.Parameters.Add(timestampMs);
        command.Parameters.Add(timestampNs);
        command.Parameters.Add(timestampTz);
        command.Prepare();

        foreach (var row in rows)
        {
            id.Value = row.Id;
            timestamp.Value = row.Timestamp;
            timestampS.Value = row.Timestamp;
            timestampMs.Value = row.Timestamp;
            timestampNs.Value = row.Timestamp;
            timestampTz.Value = row.TimestampOffset;
            command.ExecuteNonQuery();
        }
    }

    private void AssertTimestampRows(string tableName, IReadOnlyList<TimestampEdgeRow> rows)
    {
        Command.CommandText = $"SELECT * FROM {tableName} ORDER BY id";
        using var reader = Command.ExecuteReader();

        foreach (var row in rows)
        {
            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(row.Id);
            reader.GetDateTime(1).Ticks.Should().Be(ExpectedDateTime(row.Timestamp, DuckDBType.Timestamp).Ticks);
            reader.GetDateTime(2).Ticks.Should().Be(ExpectedDateTime(row.Timestamp, DuckDBType.TimestampS).Ticks);
            reader.GetDateTime(3).Ticks.Should().Be(ExpectedDateTime(row.Timestamp, DuckDBType.TimestampMs).Ticks);
            reader.GetDateTime(4).Ticks.Should().Be(ExpectedDateTime(row.Timestamp, DuckDBType.TimestampNs).Ticks);
            reader.GetFieldValue<DateTimeOffset>(5).UtcDateTime.Ticks.Should()
                .Be(ExpectedDateTime(row.TimestampOffset.UtcDateTime, DuckDBType.TimestampTz).Ticks);
        }

        reader.Read().Should().BeFalse();
    }

    private static DateTime ExpectedDateTime(DateTime value, DuckDBType type)
    {
        var timestamp = ConvertUsingNativeTimestampHelper(value, type);
        var (duckDBTimestamp, additionalTicks) = timestamp.ToDuckDBTimestamp(type);
        return duckDBTimestamp.ToDateTime().AddTicks(additionalTicks);
    }

    private static DuckDBTimestampStruct ConvertUsingNativeTimestampHelper(DateTime value, DuckDBType type)
    {
        var timestamp = DuckDBTimestamp.FromDateTime(value).ToDuckDBTimestampStruct();

        unchecked
        {
            if (type == DuckDBType.TimestampNs)
            {
                timestamp.Micros *= 1000;
                timestamp.Micros += value.Nanosecond;
            }

            if (type == DuckDBType.TimestampMs)
            {
                timestamp.Micros /= 1000;
            }

            if (type == DuckDBType.TimestampS)
            {
                timestamp.Micros /= 1_000_000;
            }
        }

        return timestamp;
    }

    private readonly record struct TimestampEdgeRow(
        int Id,
        DateTime Timestamp,
        DateTimeOffset TimestampOffset);
}
