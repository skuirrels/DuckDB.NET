namespace DuckDB.NET.Test;

// From DuckDB 2.0 an open streaming result holds its connection: every other statement on it is refused with a
// "Resource In Use" error until the stream is read to its end or destroyed.
public class StreamingResultTests(DuckDBDatabaseFixture db) : DuckDBTestBase(db)
{
    [Fact]
    public void NextResultRunsTheNextStatementAfterAPartlyReadStream()
    {
        Command.UseStreamingMode = true;
        Command.CommandText = "FROM range(1000000); SELECT 2 AS two; FROM range(1000000); SELECT 4 AS four;";

        using var reader = Command.ExecuteReader();

        reader.Read().Should().BeTrue();
        reader.GetInt64(0).Should().Be(0);

        reader.NextResult().Should().BeTrue();
        reader.Read().Should().BeTrue();
        reader.GetName(0).Should().Be("two");
        reader.GetInt32(0).Should().Be(2);

        reader.NextResult().Should().BeTrue();
        reader.Read().Should().BeTrue();

        reader.NextResult().Should().BeTrue();
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(4);

        reader.NextResult().Should().BeFalse();
    }

    [Fact]
    public void AStreamReleasedByNextResultHasNoColumnsAndNoRows()
    {
        Command.UseStreamingMode = true;
        Command.CommandText = "FROM range(1000000)";

        using var reader = Command.ExecuteReader();
        reader.Read().Should().BeTrue();

        reader.NextResult().Should().BeFalse();

        reader.FieldCount.Should().Be(0);
        reader.HasRows.Should().BeFalse();
        reader.Read().Should().BeFalse();

        Connection.ExecuteScalar<int>("SELECT 42").Should().Be(42);
    }

    [Fact]
    public void AStreamReadToItsEndStaysReadableAfterTheLastNextResult()
    {
        Command.UseStreamingMode = true;
        Command.CommandText = "SELECT 1 AS one";

        using var reader = Command.ExecuteReader();
        while (reader.Read())
        {
        }

        reader.NextResult().Should().BeFalse();

        reader.FieldCount.Should().Be(1);
        reader.GetName(0).Should().Be("one");
    }

    [Fact]
    public void AMaterializedResultStaysReadableAfterTheLastNextResult()
    {
        Command.CommandText = "SELECT 1 AS one";

        using var reader = Command.ExecuteReader();

        reader.NextResult().Should().BeFalse();

        reader.FieldCount.Should().Be(1);
        reader.GetName(0).Should().Be("one");
    }

    [Fact]
    public void AnOpenStreamRefusesAnotherCommandOnItsConnection()
    {
        Command.UseStreamingMode = true;
        Command.CommandText = "FROM range(1000000)";

        using var reader = Command.ExecuteReader();
        reader.Read().Should().BeTrue();

        using var other = Connection.CreateCommand();
        other.CommandText = "SELECT 42";

        other.Invoking(command => command.ExecuteScalar()).Should().Throw<DuckDBException>()
             .Where(exception => exception.Message.Contains("open result"));

        // The refused statement leaves the stream as it was.
        reader.Read().Should().BeTrue();
        reader.GetInt64(0).Should().Be(1);
    }

    [Fact]
    public void ClosingAPartlyReadStreamReleasesTheConnection()
    {
        Command.UseStreamingMode = true;
        Command.CommandText = "FROM range(1000000)";

        using (var reader = Command.ExecuteReader())
        {
            reader.Read().Should().BeTrue();
        }

        Connection.ExecuteScalar<int>("SELECT 42").Should().Be(42);
    }

    [Fact]
    public void AStreamReadToItsEndReleasesTheConnection()
    {
        Command.UseStreamingMode = true;
        Command.CommandText = "FROM range(10000)";

        using var reader = Command.ExecuteReader();
        while (reader.Read())
        {
        }

        Connection.ExecuteScalar<int>("SELECT 42").Should().Be(42);
    }

    [Fact]
    public void AnAppenderCannotFlushWhileAStreamIsOpen()
    {
        Connection.Execute("CREATE TABLE streaming_appender_target (i INTEGER)");

        Command.UseStreamingMode = true;
        Command.CommandText = "FROM range(1000000)";

        using var reader = Command.ExecuteReader();
        reader.Read().Should().BeTrue();

        var appender = Connection.CreateAppender("streaming_appender_target");
        appender.CreateRow().AppendValue((int?)1).EndRow();

        appender.Invoking(a => a.Close()).Should().Throw<DuckDBException>()
                .Where(exception => exception.ErrorType == DuckDBErrorType.ResourceInUse);
    }

    [Fact]
    public void StreamingExecuteNonQueryKeepsTheWrites()
    {
        Connection.Execute("CREATE TABLE streaming_insert_target (i INTEGER)");

        Command.UseStreamingMode = true;
        Command.CommandText = "INSERT INTO streaming_insert_target SELECT * FROM range(100000)";

        Command.ExecuteNonQuery().Should().Be(100000);
        Connection.ExecuteScalar<long>("SELECT count(*) FROM streaming_insert_target").Should().Be(100000);
    }
}
