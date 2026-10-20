using DuckDB.NET.Test.Helpers;

namespace DuckDB.NET.Test;

public class TransactionTests(DuckDBDatabaseFixture db) : DuckDBTestBase(db)
{
    [Fact]
    public void SimpleTransactionTest()
    {
        Command.CommandText = "CREATE TABLE transactionUsers (id INTEGER, name TEXT);";
        Command.ExecuteNonQuery();

        object rowsInTable;
        using (var transaction = Connection.BeginTransaction(IsolationLevel.Snapshot))
        {
            transaction.IsolationLevel.Should().Be(IsolationLevel.Snapshot);
            transaction.Connection.Should().Be(Connection);

            Command.CommandText = "INSERT INTO transactionUsers VALUES (1, 'user1'), (2, 'user2')";
            Command.ExecuteNonQuery();

            Command.CommandText = "SELECT count(*) FROM transactionUsers";
            rowsInTable = Command.ExecuteScalar();
            rowsInTable.Should().Be(2);
            transaction.Commit();
        }

        Command.CommandText = "SELECT count(*) FROM transactionUsers";
        rowsInTable = Command.ExecuteScalar();
        rowsInTable.Should().Be(2);

        using (Connection.BeginTransaction())
        {
            Command.CommandText = "INSERT INTO transactionUsers VALUES (3, 'user3'), (4, 'user4')";
            Command.ExecuteNonQuery();

            Command.CommandText = "SELECT count(*) FROM transactionUsers";
            rowsInTable = Command.ExecuteScalar();
            rowsInTable.Should().Be(4);
        }

        Command.CommandText = "SELECT count(*) FROM transactionUsers";
        rowsInTable = Command.ExecuteScalar();
        rowsInTable.Should().Be(2);
    }

    [Fact]
    public void ParallelTransactionsTest()
    {
        using (Connection.BeginTransaction())
        {
            Connection
                .Invoking(con => con.BeginTransaction())
                .Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void CommitTransactionTwiceTest()
    {
        using (var transaction = Connection.BeginTransaction())
        {
            transaction.Commit();
            transaction.Invoking(tr => tr.Commit())
                .Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void RollbackAndCommitTransactionTest()
    {
        using (var transaction = Connection.BeginTransaction())
        {
            transaction.Rollback();
            transaction.Invoking(tr => tr.Commit())
                .Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void RollbackTransactionTwiceTest()
    {
        using (var transaction = Connection.BeginTransaction())
        {
            transaction.Rollback();
            transaction.Invoking(tr => tr.Rollback())
                .Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void CommitAndRollbackTransactionTest()
    {
        using (var transaction = Connection.BeginTransaction())
        {
            transaction.Commit();
            transaction.Invoking(tr => tr.Rollback())
                .Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void TransactionInvalidStateTest()
    {
        Connection.Close();
        Connection.Invoking(connection => connection.BeginTransaction()).Should().Throw<InvalidOperationException>();
        Connection.Open();

        Connection.Invoking(connection => connection.BeginTransaction(IsolationLevel.Serializable)).Should()
            .Throw<ArgumentException>();
    }

    [Fact]
    public void AbortedTransactionTest()
    {
        // Make the commit fail: another connection to the same database commits the same primary key first.
        // DuckDB 2.0 commits the DROP COLUMN case of https://github.com/duckdb/duckdb/issues/20570 this test used before.
        using var file = DisposableFile.GenerateInTemp("db");

        using var connection = new DuckDBConnection(file.ConnectionString);
        using var other = new DuckDBConnection(file.ConnectionString);
        connection.Open();
        other.Open();

        connection.Execute("CREATE TABLE aborted_test (id INTEGER PRIMARY KEY);");

        using var tx2 = connection.BeginTransaction();
        connection.Execute("INSERT INTO aborted_test VALUES (1);");

        other.Execute("INSERT INTO aborted_test VALUES (1);");

        using (new FluentAssertions.Execution.AssertionScope())
        {
            // Check that when the transaction commit fails and the transaction
            // enters an aborted state, the transaction and connection objects
            // remain in the expected state.
            tx2.Invoking(tx2 => tx2.Commit()).Should().Throw<DuckDBException>().Where(ex => ex.ErrorType == Native.DuckDBErrorType.Transaction);
            tx2.Invoking(tx2 => tx2.Commit()).Should().Throw<InvalidOperationException>();
            tx2.Invoking(tx2 => tx2.Rollback()).Should().Throw<InvalidOperationException>();
            tx2.Invoking(tx2 => tx2.Dispose()).Should().NotThrow();

            connection.Invoking(conn => conn.BeginTransaction().Dispose()).Should().NotThrow();
        }
    }
}
