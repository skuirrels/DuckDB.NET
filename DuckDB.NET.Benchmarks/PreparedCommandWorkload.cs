using DuckDB.NET.Data;

namespace DuckDB.NET.Benchmarks;

internal static class PreparedCommandWorkload
{
    public const string Query = "SELECT $first::BIGINT + $second::BIGINT + $third::BIGINT";

#if DUCKDB_NET_BASELINE_1_5_3
    private const string ExpectedEngineVersion = "v1.5.3";
#else
    private const string ExpectedEngineVersion = "v1.5.5";
#endif

    public static DuckDBConnection OpenVerifiedConnection()
    {
        var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT version()";
        var actualVersion = versionCommand.ExecuteScalar() as string;

        if (!string.Equals(actualVersion, ExpectedEngineVersion, StringComparison.Ordinal))
        {
            connection.Dispose();
            throw new InvalidOperationException(
                $"The driver comparison requires DuckDB {ExpectedEngineVersion}, but loaded {actualVersion ?? "an unknown version"}.");
        }

        return connection;
    }
}
