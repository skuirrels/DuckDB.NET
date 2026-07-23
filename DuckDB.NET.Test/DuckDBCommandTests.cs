namespace DuckDB.NET.Test;

public class DuckDBCommandTests(DuckDBDatabaseFixture db) : DuckDBTestBase(db)
{
    [Fact]
    public void SetCommandText()
    {
        var cmd = new DuckDBCommand("Select 1");
        cmd.CommandText.Should().Be("Select 1");
    }

    [Fact]
    public void SetCommandTextAndConnection()
    {
        var cmd = new DuckDBCommand("Select 1", Connection);

        cmd.CommandText.Should().Be("Select 1");
        cmd.Connection.Should().Be(Connection);
    }

    [Fact]
    public void PreparedCommandCanBeExecutedRepeatedlyWithNewParameterValues()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "SELECT $left::INTEGER + $right::INTEGER";
        command.Parameters.Add(new DuckDBParameter("left", 10));
        command.Parameters.Add(new DuckDBParameter("right", 1));

        command.Prepare();

        command.ExecuteScalar().Should().Be(11);

        command.Parameters["left"].Value = 20;
        command.Parameters["right"].Value = 2;
        command.ExecuteScalar().Should().Be(22);
    }

    [Fact]
    public void TypedPreparedParameterCanBeExecutedRepeatedly()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "SELECT $value::INTEGER";
        var parameter = new DuckDBParameter<int>("value", 10);
        command.Parameters.Add(parameter);
        command.Prepare();

        command.ExecuteScalar().Should().Be(10);

        parameter.TypedValue = 20;
        command.ExecuteScalar().Should().Be(20);

        parameter.Value = 30;
        command.ExecuteScalar().Should().Be(30);
    }

    [Fact]
    public void TypedParameterRejectsValuesOfAnotherType()
    {
        DuckDBParameter parameter = new DuckDBParameter<int>("value", 10);

        parameter.Invoking(value => value.Value = "wrong")
            .Should().Throw<InvalidCastException>();
    }

    [Fact]
    public void PreparedBindingPlanInvalidatesWhenParameterNameChanges()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "SELECT $value::INTEGER";
        var parameter = new DuckDBParameter<int>("value", 10);
        command.Parameters.Add(parameter);
        command.Prepare();

        command.ExecuteScalar().Should().Be(10);

        parameter.ParameterName = "unused";
        command.Invoking(value => value.ExecuteScalar()).Should().Throw<DuckDBException>();

        parameter.ParameterName = "value";
        parameter.TypedValue = 20;
        command.ExecuteScalar().Should().Be(20);
    }

    [Fact]
    public void PreparedCommandClearsBindingsBeforeReuse()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "SELECT $used::INTEGER";
        command.Parameters.Add(new DuckDBParameter("used", 10));
        command.Prepare();

        command.ExecuteScalar().Should().Be(10);

        command.Parameters.Clear();
        command.Parameters.Add(new DuckDBParameter("unused", 20));
        command.Invoking(value => value.ExecuteScalar()).Should().Throw<DuckDBException>();

        command.Parameters.Clear();
        command.Parameters.Add(new DuckDBParameter("used", 30));
        command.ExecuteScalar().Should().Be(30);
    }

    [Fact]
    public void PreparedExecuteNonQueryReturnsAffectedRowsAndCanBeReused()
    {
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        ExecuteNonQuery(connection, "CREATE TABLE prepared_values(value INTEGER)");

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO prepared_values VALUES ($value)";
        command.Parameters.Add(new DuckDBParameter("value", 10));
        command.Prepare();

        command.ExecuteNonQuery().Should().Be(1);

        command.Parameters["value"].Value = 20;
        command.ExecuteNonQuery().Should().Be(1);

        using var query = connection.CreateCommand();
        query.CommandText = "SELECT value FROM prepared_values ORDER BY value";
        using var reader = query.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(10);
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(20);
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public void PreparedExecuteNonQueryCanBeReusedAfterExecutionFailure()
    {
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        ExecuteNonQuery(connection, "CREATE TABLE unique_prepared_values(value INTEGER PRIMARY KEY)");

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO unique_prepared_values VALUES ($value)";
        command.Parameters.Add(new DuckDBParameter("value", 10));
        command.Prepare();

        command.ExecuteNonQuery().Should().Be(1);
        command.Invoking(value => value.ExecuteNonQuery()).Should().Throw<DuckDBException>();

        command.Parameters["value"].Value = 20;
        command.ExecuteNonQuery().Should().Be(1);
    }

    [Fact]
    public void PreparePreservesMultipleResultSetsAcrossExecutions()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "SELECT $value::INTEGER; SELECT $value::INTEGER + 1";
        command.Parameters.Add(new DuckDBParameter("value", 10));
        command.Prepare();

        AssertResults(command, 10, 11);

        command.Parameters["value"].Value = 20;
        AssertResults(command, 20, 21);

        static void AssertResults(DuckDBCommand preparedCommand, int first, int second)
        {
            using var reader = preparedCommand.ExecuteReader();

            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(first);
            reader.NextResult().Should().BeTrue();
            reader.Read().Should().BeTrue();
            reader.GetInt32(0).Should().Be(second);
            reader.NextResult().Should().BeFalse();
        }
    }

    [Fact]
    public void PreparePreservesDependentMultipleStatements()
    {
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS prepared_dependency(value INTEGER);
                              DELETE FROM prepared_dependency;
                              INSERT INTO prepared_dependency VALUES ($value);
                              SELECT value FROM prepared_dependency;
                              """;
        command.Parameters.Add(new DuckDBParameter("value", 42));
        command.Prepare();

        command.ExecuteScalar().Should().Be(42);

        command.Parameters["value"].Value = 84;
        command.ExecuteScalar().Should().Be(84);
    }

    [Fact]
    public void StaticPivotCanBePreparedAndReused()
    {
        using var connection = CreatePivotConnection();

        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT *
                              FROM Cities
                              PIVOT (
                                  SUM(Population)
                                  FOR Year IN (2022, 2023)
                                  GROUP BY Country, Name
                              );
                              """;
        command.Prepare();

        AssertPivotValue(command, "2022", 3_688_647L);

        ExecuteNonQuery(connection, "INSERT INTO Cities VALUES ('Georgia', 'Tbilisi', 2022, 100)");

        AssertPivotValue(command, "2022", 3_688_747L);
    }

    [Fact]
    public void DynamicPivotIsPreparedPerExecutionSoNewColumnsAreVisible()
    {
        using var connection = CreatePivotConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "PIVOT Cities ON Year USING SUM(Population);";
        command.Prepare();

        GetColumnNames(command).Should().Equal("Country", "Name", "2022", "2023");

        ExecuteNonQuery(connection, "INSERT INTO Cities VALUES ('Georgia', 'Tbilisi', 2024, 3800000)");

        GetColumnNames(command).Should().Equal("Country", "Name", "2022", "2023", "2024");
    }

    [Fact]
    public void ChangingCommandTextInvalidatesPreparedStatements()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "SELECT $value::INTEGER";
        command.Parameters.Add(new DuckDBParameter("value", 10));
        command.Prepare();

        command.ExecuteScalar().Should().Be(10);

        command.CommandText = "SELECT $value::INTEGER * 2";
        command.ExecuteScalar().Should().Be(20);

        command.Prepare();
        command.Parameters["value"].Value = 15;
        command.ExecuteScalar().Should().Be(30);
    }

    [Fact]
    public void InvalidatedPreparedStatementsRemainAliveForAnActiveReader()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "SELECT value FROM range(1, 4) AS values(value)";
        command.Prepare();

        using var reader = command.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetInt64(0).Should().Be(1);

        command.CommandText = "SELECT 4";
        command.ExecuteScalar().Should().Be(4);

        reader.Read().Should().BeTrue();
        reader.GetInt64(0).Should().Be(2);
        reader.Read().Should().BeTrue();
        reader.GetInt64(0).Should().Be(3);
        reader.Read().Should().BeFalse();
    }

    [Fact]
    public void ClosingConnectionInvalidatesPreparedStatementsBeforeDisconnect()
    {
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT $value::INTEGER";
        command.Parameters.Add(new DuckDBParameter("value", 10));
        command.Prepare();
        command.ExecuteScalar().Should().Be(10);

        connection.Close();
        connection.Open();

        command.Parameters["value"].Value = 20;
        command.ExecuteScalar().Should().Be(20);
    }

    private static DuckDBConnection CreatePivotConnection()
    {
        var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        ExecuteNonQuery(connection, """
                                    CREATE TABLE Cities(Country VARCHAR, Name VARCHAR, Year INT, Population INT);
                                    INSERT INTO Cities VALUES
                                        ('Georgia', 'Tbilisi', 2022, 3688647),
                                        ('Georgia', 'Tbilisi', 2023, 3736400);
                                    """);

        return connection;
    }

    private static void ExecuteNonQuery(DuckDBConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void AssertPivotValue(DuckDBCommand command, string columnName, long expected)
    {
        using var reader = command.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetInt64(reader.GetOrdinal(columnName)).Should().Be(expected);
        reader.NextResult().Should().BeFalse();
    }

    private static string[] GetColumnNames(DuckDBCommand command)
    {
        using var reader = command.ExecuteReader();
        return Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
    }
}
