using System.Globalization;

namespace DuckDB.NET.Test;

public class DuckDBManagedAppenderNumericConversionTests(DuckDBDatabaseFixture db) : DuckDBTestBase(db)
{
    public static TheoryData<string> IntegralColumns =>
    [
        "TINYINT", "SMALLINT", "INTEGER", "BIGINT", "HUGEINT",
        "UTINYINT", "USMALLINT", "UINTEGER", "UBIGINT", "UHUGEINT",
        "FLOAT", "DOUBLE"
    ];

    [Fact]
    public void IntegerLiteralsDoNotCorruptIntegerColumn()
    {
        Command.CommandText = "CREATE TABLE literal_ids (id INTEGER, name VARCHAR);";
        Command.ExecuteNonQuery();

        using (var appender = Connection.CreateAppender("literal_ids"))
        {
            // Literals bind to the narrowest matching overload rather than int?.
            appender.CreateRow().AppendValue(0).AppendValue("x").EndRow();
            appender.CreateRow().AppendValue(1).AppendValue("y").EndRow();
            appender.CreateRow().AppendValue(3).AppendValue("z").EndRow();
            appender.CreateRow().AppendValue(70000).AppendValue("w").EndRow();
        }

        Connection.Query<(int, string)>("SELECT id, name FROM literal_ids ORDER BY id")
                  .Should().Equal((0, "x"), (1, "y"), (3, "z"), (70000, "w"));
    }

    [Fact]
    public void NarrowerValuesWidenAcrossChunks()
    {
        Command.CommandText = "CREATE TABLE widened (a INTEGER, b BIGINT, c UBIGINT, d DOUBLE);";
        Command.ExecuteNonQuery();

        const int rowCount = 5_000;

        using (var appender = Connection.CreateAppender("widened"))
        {
            for (var index = 0; index < rowCount; index++)
            {
                appender.CreateRow()
                        .AppendValue((short)(index - 2_500))
                        .AppendValue(index)
                        .AppendValue((byte)(index % 256))
                        .AppendValue((float)index / 4)
                        .EndRow();
            }
        }

        Connection.Query<(int, long, ulong, double)>("SELECT a, b, c, d FROM widened ORDER BY b")
                  .Should().Equal(Enumerable.Range(0, rowCount)
                                            .Select(index => (index - 2_500, (long)index, (ulong)(index % 256), index / 4d)));
    }

    [Theory]
    [MemberData(nameof(IntegralColumns))]
    public void EveryIntegerTypeConvertsToEveryNumericColumn(string columnType)
    {
        Command.CommandText = $"CREATE OR REPLACE TABLE numeric_matrix (id INTEGER, v {columnType});";
        Command.ExecuteNonQuery();

        object[] values = [(sbyte)42, (short)42, 42, 42L, (byte)42, (ushort)42, 42U, 42UL, new BigInteger(42)];

        using (var appender = Connection.CreateAppender("numeric_matrix"))
        {
            for (var index = 0; index < values.Length; index++)
            {
                var row = appender.CreateRow().AppendValue(index);

                _ = values[index] switch
                {
                    sbyte v => row.AppendValue(v),
                    short v => row.AppendValue(v),
                    int v => row.AppendValue(v),
                    long v => row.AppendValue(v),
                    byte v => row.AppendValue(v),
                    ushort v => row.AppendValue(v),
                    uint v => row.AppendValue(v),
                    ulong v => row.AppendValue(v),
                    BigInteger v => row.AppendValue(v),
                    _ => throw new InvalidOperationException()
                };

                row.EndRow();
            }
        }

        Command.CommandText = "SELECT v FROM numeric_matrix ORDER BY id";
        using var reader = Command.ExecuteReader();

        for (var index = 0; index < values.Length; index++)
        {
            reader.Read().Should().BeTrue();
            Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture).Should().Be("42", $"{values[index].GetType().Name} into {columnType}");
        }
    }

    [Fact]
    public void NarrowingWithinRangeSucceeds()
    {
        Command.CommandText = "CREATE TABLE narrowed (a TINYINT, b INTEGER, c UINTEGER, d UHUGEINT);";
        Command.ExecuteNonQuery();

        using (var appender = Connection.CreateAppender("narrowed"))
        {
            appender.CreateRow().AppendValue(-128L).AppendValue((long)int.MinValue).AppendValue((ulong)uint.MaxValue).AppendValue(ulong.MaxValue).EndRow();
        }

        Command.CommandText = "SELECT * FROM narrowed";
        using var reader = Command.ExecuteReader();
        reader.Read();
        reader.GetFieldValue<sbyte>(0).Should().Be(sbyte.MinValue);
        reader.GetFieldValue<int>(1).Should().Be(int.MinValue);
        reader.GetFieldValue<uint>(2).Should().Be(uint.MaxValue);
        reader.GetFieldValue<BigInteger>(3).Should().Be(ulong.MaxValue);
    }

    [Theory]
    [InlineData("INTEGER", long.MaxValue)]
    [InlineData("INTEGER", (long)int.MinValue - 1)]
    [InlineData("TINYINT", 128L)]
    [InlineData("UINTEGER", -1L)]
    [InlineData("UBIGINT", -1L)]
    [InlineData("UHUGEINT", -1L)]
    public void OutOfRangeIntegerThrows(string columnType, long value)
    {
        AssertRejected<OverflowException>(columnType, row => row.AppendValue(value), "*out of range*");
    }

    public static TheoryData<string, string> OutOfRangeBigIntegers => new()
    {
        { "BIGINT", ((BigInteger)long.MaxValue + 1).ToString(CultureInfo.InvariantCulture) },
        { "HUGEINT", BigInteger.Pow(2, 127).ToString(CultureInfo.InvariantCulture) },
        { "HUGEINT", (-BigInteger.Pow(2, 127) - 1).ToString(CultureInfo.InvariantCulture) },
        { "UHUGEINT", BigInteger.Pow(2, 128).ToString(CultureInfo.InvariantCulture) },
        { "UHUGEINT", "-1" }
    };

    [Theory]
    [MemberData(nameof(OutOfRangeBigIntegers))]
    public void OutOfRangeBigIntegerThrows(string columnType, string value)
    {
        var bigInteger = BigInteger.Parse(value, CultureInfo.InvariantCulture);

        // HUGEINT and UHUGEINT keep upstream's ArgumentOutOfRangeException from DuckDBHugeInt and DuckDBUHugeInt.
        if (columnType is "HUGEINT" or "UHUGEINT")
        {
            AssertRejected<ArgumentOutOfRangeException>(columnType, row => row.AppendValue(bigInteger), "*value must be between*");
            return;
        }

        AssertRejected<OverflowException>(columnType, row => row.AppendValue(bigInteger), "*out of range*");
    }

    [Theory]
    [InlineData("FLOAT")]
    [InlineData("INTEGER")]
    [InlineData("BIGINT")]
    public void LossyFloatingPointConversionThrows(string columnType)
    {
        AssertRejected<InvalidOperationException>(columnType, row => row.AppendValue(1.5), "*Double*explicit conversion*");
    }

    [Fact]
    public void FloatToIntegerThrows()
    {
        AssertRejected<InvalidOperationException>("INTEGER", row => row.AppendValue(1.5f), "*Single*explicit conversion*");
    }

    [Fact]
    public void FloatWidensToDouble()
    {
        Command.CommandText = "CREATE TABLE float_to_double (v DOUBLE);";
        Command.ExecuteNonQuery();

        using (var appender = Connection.CreateAppender("float_to_double"))
        {
            appender.CreateRow().AppendValue(1.25f).EndRow();
            appender.CreateRow().AppendValue((float?)null).EndRow();
        }

        Connection.Query<double?>("SELECT v FROM float_to_double ORDER BY v NULLS LAST").Should().Equal(1.25, null);
    }

    [Fact]
    public void LargeUnsignedHugeIntBigIntegerRoundTrips()
    {
        Command.CommandText = "CREATE TABLE uhugeint_values (v UHUGEINT);";
        Command.ExecuteNonQuery();

        var value = BigInteger.Pow(2, 127) + 5;

        using (var appender = Connection.CreateAppender("uhugeint_values"))
        {
            appender.CreateRow().AppendValue(value).EndRow();
        }

        // UHUGEINT is read as UInt128 by default from 2.0.
        Command.CommandText = "SELECT v FROM uhugeint_values";
        Command.ExecuteScalar().Should().Be((UInt128)value);
    }

    private void AssertRejected<TException>(string columnType, Action<IDuckDBAppenderRow> append, string message) where TException : Exception
    {
        var table = $"rejected_{Guid.NewGuid():N}";
        Command.CommandText = $"CREATE TABLE {table} (v {columnType});";
        Command.ExecuteNonQuery();

        using (var appender = Connection.CreateAppender(table))
        {
            // AppendRow discards the failed row and closes the appender.
            appender.Invoking(a => a.AppendRow(append)).Should().Throw<TException>().WithMessage(message);
        }

        Command.CommandText = $"SELECT count(*) FROM {table}";
        Command.ExecuteScalar().Should().Be(0L);
    }
}
