namespace DuckDB.NET.Test;

// TIMESTAMP_TZ_NS (new in DuckDB 2.0) and TIME_NS count nanoseconds. A .NET value counts ticks of 100 nanoseconds, so
// what is below a tick is dropped on a read and a write adds none.
public class NanosecondTemporalTypeTests(DuckDBDatabaseFixture db) : DuckDBTestBase(db)
{
    private static readonly DateTime Instant = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234567);

    [Fact]
    public void TimestampTzNsIsReadAsUtcDateTime()
    {
        Command.CommandText = "SELECT TIMESTAMPTZ_NS '2024-01-02 03:04:05.123456789+00'";

        using var reader = Command.ExecuteReader();
        reader.Read();

        reader.GetDataTypeName(0).Should().Be(nameof(DuckDBType.TimestampTzNs));
        reader.GetFieldType(0).Should().Be(typeof(DateTime));
        reader.GetProviderSpecificFieldType(0).Should().Be(typeof(DuckDBTimestamp));

        reader.GetDateTime(0).Should().Be(Instant);
        reader.GetValue(0).Should().Be(Instant);
        reader.GetFieldValue<DateTimeOffset>(0).Should().Be(new DateTimeOffset(Instant));
        reader.GetFieldValue<DuckDBTimestamp>(0).ToDateTime().Should().Be(Instant.AddTicks(-7));
    }

    [Fact]
    public void TimestampTzNsBeforeTheEpochKeepsItsDateAndTime()
    {
        Command.CommandText = "SELECT TIMESTAMPTZ_NS '1969-12-31 23:59:59.999999999+00'";

        Command.ExecuteScalar().Should().Be(new DateTime(1969, 12, 31, 23, 59, 59).AddTicks(9999999));
    }

    [Fact]
    public void TimestampTzNsInfinityIsReadAsDuckDBTimestamp()
    {
        Command.CommandText = "SELECT 'infinity'::TIMESTAMPTZ_NS";

        using var reader = Command.ExecuteReader();
        reader.Read();

        reader.GetFieldValue<DuckDBTimestamp>(0).IsPositiveInfinity.Should().BeTrue();
        reader.Invoking(r => r.GetDateTime(0)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TimestampTzNsParametersAreWrittenToTheTick()
    {
        Connection.Execute("CREATE TABLE timestamp_tz_ns_parameters (value TIMESTAMPTZ_NS)");

        Command.CommandText = "INSERT INTO timestamp_tz_ns_parameters VALUES (?), (?)";
        Command.Parameters.Add(new DuckDBParameter(Instant));
        Command.Parameters.Add(new DuckDBParameter(new DateTimeOffset(Instant).ToOffset(TimeSpan.FromHours(4))));
        Command.ExecuteNonQuery();

        Connection.Query<DateTime>("SELECT value FROM timestamp_tz_ns_parameters ORDER BY rowid")
                  .Should().Equal(Instant, Instant);

        Connection.ExecuteScalar<long>("SELECT count(*) FROM timestamp_tz_ns_parameters WHERE value = TIMESTAMPTZ_NS '2024-01-02 03:04:05.1234567+00'")
                  .Should().Be(2);
    }

    [Fact]
    public void TimestampTzNsIsAppendedToTheTick()
    {
        Connection.Execute("CREATE TABLE timestamp_tz_ns_appender (value TIMESTAMPTZ_NS)");

        using (var appender = Connection.CreateAppender("timestamp_tz_ns_appender"))
        {
            appender.CreateRow().AppendValue(Instant).EndRow();
            appender.CreateRow().AppendValue(new DateTimeOffset(Instant).ToOffset(TimeSpan.FromHours(-5))).EndRow();
        }

        Connection.ExecuteScalar<long>("SELECT count(*) FROM timestamp_tz_ns_appender WHERE value = TIMESTAMPTZ_NS '2024-01-02 03:04:05.1234567+00'")
                  .Should().Be(2);
    }

    [Fact]
    public void TimeNsIsReadAsTimeOnlyAndTimeSpan()
    {
        Command.CommandText = "SELECT TIME_NS '13:14:15.123456789'";

        using var reader = Command.ExecuteReader();
        reader.Read();

        var expected = new TimeSpan(13, 14, 15) + TimeSpan.FromTicks(1234567);

        reader.GetDataTypeName(0).Should().Be(nameof(DuckDBType.TimeNs));
        reader.GetFieldType(0).Should().Be(typeof(TimeOnly));
        reader.GetProviderSpecificFieldType(0).Should().Be(typeof(TimeSpan));

        reader.GetValue(0).Should().Be(TimeOnly.FromTimeSpan(expected));
        reader.GetFieldValue<TimeOnly>(0).Should().Be(TimeOnly.FromTimeSpan(expected));
        reader.GetFieldValue<TimeSpan>(0).Should().Be(expected);
        reader.GetProviderSpecificValue(0).Should().Be(expected);
        reader.GetDateTime(0).Should().Be(new DateTime(expected.Ticks));
    }

    [Fact]
    public void TimeNsParametersAreWrittenToTheTick()
    {
        Connection.Execute("CREATE TABLE time_ns_parameters (value TIME_NS)");

        Command.CommandText = "INSERT INTO time_ns_parameters VALUES (?)";
        Command.Parameters.Add(new DuckDBParameter(new TimeOnly(13, 14, 15).Add(TimeSpan.FromTicks(1234567))));
        Command.ExecuteNonQuery();

        Connection.ExecuteScalar<string>("SELECT value::VARCHAR FROM time_ns_parameters").Should().Be("13:14:15.1234567");
    }

    [Fact]
    public void TimeNsIsAppendedToTheMicrosecond()
    {
        Connection.Execute("CREATE TABLE time_ns_appender (value TIME_NS)");

        // IDuckDBAppenderRow takes a TimeOnly as a DuckDBTimeOnly, which holds microseconds.
        using (var appender = Connection.CreateAppender("time_ns_appender"))
        {
            appender.CreateRow().AppendValue(new TimeOnly(13, 14, 15).Add(TimeSpan.FromTicks(1234567))).EndRow();
            appender.CreateRow().AppendValue(new DuckDBTimeOnly(13, 14, 15, 123456)).EndRow();
        }

        Connection.Query<string>("SELECT value::VARCHAR FROM time_ns_appender ORDER BY rowid")
                  .Should().Equal("13:14:15.123456", "13:14:15.123456");
    }

    [Fact]
    public void GeometryIsReadAsItsWellKnownBinary()
    {
        Command.CommandText = "SELECT 'POINT(1 2)'::GEOMETRY AS g, ST_AsWKB('POINT(1 2)'::GEOMETRY) AS wkb";

        using var reader = Command.ExecuteReader();
        reader.Read();

        reader.GetDataTypeName(0).Should().Be(nameof(DuckDBType.Geometry));
        reader.GetFieldType(0).Should().Be(typeof(Stream));

        using var geometry = reader.GetStream(0);
        using var wkb = reader.GetStream(1);

        using var geometryBytes = new MemoryStream();
        using var wkbBytes = new MemoryStream();
        geometry.CopyTo(geometryBytes);
        wkb.CopyTo(wkbBytes);

        geometryBytes.ToArray().Should().Equal(wkbBytes.ToArray());
    }

    [Fact]
    public void TheNewColumnsOfTestAllTypesAreRead()
    {
        Command.CommandText = "SELECT timestamp_tz_ns, time_ns, geometry FROM test_all_types()";

        using var reader = Command.ExecuteReader();

        while (reader.Read())
        {
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                if (!reader.IsDBNull(ordinal))
                {
                    reader.GetProviderSpecificValue(ordinal).Should().NotBeNull();
                }
            }
        }
    }
}
