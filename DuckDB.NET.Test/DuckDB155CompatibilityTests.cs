namespace DuckDB.NET.Test;

public class DuckDB155CompatibilityTests(DuckDBDatabaseFixture db) : DuckDBTestBase(db)
{
    [Fact]
    public void MergeIntoReturnsManagedStatementType()
    {
        Command.CommandText =
            """
            create table statement_type_target(id integer primary key, value varchar);
            create table statement_type_source(id integer, value varchar);
            insert into statement_type_target values (1, 'old');
            insert into statement_type_source values (1, 'new'), (2, 'added');
            """;
        Command.ExecuteNonQuery();

        var state = NativeMethods.Query.DuckDBQuery(
            Connection.NativeConnection,
            """
            merge into statement_type_target
            using statement_type_source
            on statement_type_target.id = statement_type_source.id
            when matched then update set value = statement_type_source.value
            when not matched then
                insert (id, value) values (statement_type_source.id, statement_type_source.value)
            """,
            out var result);

        try
        {
            state.Should().Be(DuckDBState.Success);
            NativeMethods.Query.DuckDBResultStatementType(result)
                .Should().Be(DuckDBStatementType.MergeInto);
        }
        finally
        {
            NativeMethods.Query.DuckDBDestroyResult(ref result);
        }
    }

    [Fact]
    public void ShowIsReportedAsSelect()
    {
        var state = NativeMethods.Query.DuckDBQuery(
            Connection.NativeConnection,
            "show tables",
            out var result);

        try
        {
            state.Should().Be(DuckDBState.Success);
            NativeMethods.Query.DuckDBResultStatementType(result)
                .Should().Be(DuckDBStatementType.Select);
        }
        finally
        {
            NativeMethods.Query.DuckDBDestroyResult(ref result);
        }
    }
}
