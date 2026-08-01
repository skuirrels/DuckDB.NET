using BenchmarkDotNet.Attributes;
using DuckDB.NET.Data;

namespace DuckDB.NET.Benchmarks;

[MemoryDiagnoser]
public class ListMaterializationBenchmark
{
    private const int RowCount = 100_000;
    private const int ItemCount = 16;
    private const int OperationsPerInvocation = 10;

    private readonly List<DuckDBCommand> commands = [];
    private DuckDBConnection connection = null!;
    private DuckDBCommand int32Command = null!;
    private DuckDBCommand nullableInt32Command = null!;
    private DuckDBCommand int64Command = null!;
    private DuckDBCommand nullableInt64Command = null!;
    private DuckDBCommand floatCommand = null!;
    private DuckDBCommand nullableFloatCommand = null!;
    private DuckDBCommand doubleCommand = null!;
    private DuckDBCommand nullableDoubleCommand = null!;
    private DuckDBCommand decimalCommand = null!;
    private DuckDBCommand nullableDecimalCommand = null!;
    private DuckDBCommand stringCommand = null!;
    private DuckDBCommand timestampCommand = null!;
    private DuckDBCommand uuidCommand = null!;
    private DuckDBCommand nestedInt32Command = null!;
    private DuckDBCommand int32ArrayCommand = null!;
    private DuckDBCommand nullableInt32ArrayCommand = null!;

    [GlobalSetup]
    public void Setup()
    {
        connection = PreparedCommandWorkload.OpenVerifiedConnection();
        int32Command = CreateCommand("INTEGER");
        nullableInt32Command = CreateNullableCommand("INTEGER");
        int64Command = CreateCommand("BIGINT");
        nullableInt64Command = CreateNullableCommand("BIGINT");
        floatCommand = CreateCommand("REAL");
        nullableFloatCommand = CreateNullableCommand("REAL");
        doubleCommand = CreateCommand("DOUBLE");
        nullableDoubleCommand = CreateNullableCommand("DOUBLE");
        decimalCommand = CreateCommand("DECIMAL(18, 2)");
        nullableDecimalCommand = CreateNullableCommand("DECIMAL(18, 2)");
        stringCommand = CreateCommand("VARCHAR");
        timestampCommand = CreateCommand(
            $"list_transform(range(0, {ItemCount}), value -> TIMESTAMP '2026-01-01' + value * INTERVAL '1 second')",
            expressionIsComplete: true);
        uuidCommand = CreateCommand(
            $"list_transform(range(0, {ItemCount}), value -> '00112233-4455-6677-8899-aabbccddeeff'::UUID)",
            expressionIsComplete: true);
        nestedInt32Command = CreateCommand(
            "[[0, 1, 2, 3], [4, 5, 6, 7], [8, 9, 10, 11], [12, 13, 14, 15]]::INTEGER[][]",
            expressionIsComplete: true);
        int32ArrayCommand = CreateCommand(
            $"range(0, {ItemCount})::INTEGER[{ItemCount}]",
            expressionIsComplete: true);
        nullableInt32ArrayCommand = CreateNullableCommand("INTEGER", fixedArray: true);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var command in commands)
        {
            command.Dispose();
        }
        connection.Dispose();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadInt32Lists() => Consume<int>(int32Command);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadNullableInt32Lists() => Consume<int?>(nullableInt32Command);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadInt64Lists() => Consume<long>(int64Command);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadNullableInt64Lists() => Consume<long?>(nullableInt64Command);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadFloatLists() => Consume<float>(floatCommand);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadNullableFloatLists() => Consume<float?>(nullableFloatCommand);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadDoubleLists() => Consume<double>(doubleCommand);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadNullableDoubleLists() => Consume<double?>(nullableDoubleCommand);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadDecimalLists() => Consume<decimal>(decimalCommand);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadNullableDecimalLists() => Consume<decimal?>(nullableDecimalCommand);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadStringLists() => Consume<string>(stringCommand);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadTimestampLists() => Consume<DateTime>(timestampCommand);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadUuidLists() => Consume<Guid>(uuidCommand);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadNestedInt32Lists() => ConsumeNested(nestedInt32Command);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadInt32Arrays() => Consume<int>(int32ArrayCommand);

    [Benchmark(OperationsPerInvoke = OperationsPerInvocation)]
    public long ReadNullableInt32Arrays() => Consume<int?>(nullableInt32ArrayCommand);

    private DuckDBCommand CreateNullableCommand(string itemType, bool fixedArray = false)
    {
        var collectionType = fixedArray ? $"{itemType}[{ItemCount}]" : $"{itemType}[]";
        return CreateCommand(
            $"list_transform(range(0, {ItemCount}), value -> CASE WHEN value % 4 = 0 THEN NULL ELSE value END)::{collectionType}",
            expressionIsComplete: true);
    }

    private DuckDBCommand CreateCommand(string itemTypeOrExpression, bool expressionIsComplete = false)
    {
        var command = connection.CreateCommand();
        var expression = expressionIsComplete
            ? itemTypeOrExpression
            : $"range(0, {ItemCount})::{itemTypeOrExpression}[]";
        command.CommandText = $"SELECT {expression} FROM range({RowCount})";
        commands.Add(command);
        return command;
    }

    private static long Consume<T>(DuckDBCommand command)
    {
        long totalChecksum = 0;

        for (var operation = 0; operation < OperationsPerInvocation; operation++)
        {
            using var reader = command.ExecuteReader();
            long checksum = 0;
            var rowsRead = 0;

            while (reader.Read())
            {
                checksum += reader.GetFieldValue<List<T>>(0).Count;
                rowsRead++;
            }

            if (rowsRead != RowCount || checksum != RowCount * ItemCount)
            {
                throw new InvalidOperationException(
                    $"Expected {RowCount} rows and {RowCount * ItemCount} values, but read {rowsRead} rows and {checksum} values.");
            }

            totalChecksum += checksum;
        }

        return totalChecksum;
    }

    private static long ConsumeNested(DuckDBCommand command)
    {
        long totalChecksum = 0;

        for (var operation = 0; operation < OperationsPerInvocation; operation++)
        {
            using var reader = command.ExecuteReader();
            long checksum = 0;
            var rowsRead = 0;

            while (reader.Read())
            {
                var lists = reader.GetFieldValue<List<List<int>>>(0);
                checksum += lists.Count;
                foreach (var list in lists)
                {
                    checksum += list.Count;
                }
                rowsRead++;
            }

            const int ValuesPerRow = 20;
            if (rowsRead != RowCount || checksum != RowCount * ValuesPerRow)
            {
                throw new InvalidOperationException(
                    $"Expected {RowCount} rows and checksum {RowCount * ValuesPerRow}, but read {rowsRead} rows and checksum {checksum}.");
            }

            totalChecksum += checksum;
        }

        return totalChecksum;
    }
}
