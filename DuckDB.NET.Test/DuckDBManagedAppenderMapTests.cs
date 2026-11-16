using System.Collections;
using System.Collections.ObjectModel;

namespace DuckDB.NET.Test;

public class DuckDBManagedAppenderMapTests(DuckDBDatabaseFixture db) : DuckDBTestBase(db)
{
    [Fact]
    public void MapVarcharInteger()
    {
        MapValuesInternal("VARCHAR", "INTEGER", (_, index) => $"key{index}", faker => faker.Random.Int());
    }

    [Fact]
    public void MapIntegerVarchar()
    {
        MapValuesInternal("INTEGER", "VARCHAR", (_, index) => index * 3 - 7, faker => faker.Lorem.Word());
    }

    [Fact]
    public void MapVarcharDouble()
    {
        MapValuesInternal("VARCHAR", "DOUBLE", (faker, index) => $"{faker.Random.AlphaNumeric(5)}{index}", faker => faker.Random.Double(-1000, 1000));
    }

    [Fact]
    public void MapUuidTimestamp()
    {
        MapValuesInternal("UUID", "TIMESTAMP", (_, _) => Guid.NewGuid(), faker =>
        {
            // TIMESTAMP stores microseconds, so drop the sub-microsecond ticks.
            var value = faker.Date.Between(new DateTime(1990, 1, 1), new DateTime(2090, 1, 1));
            return new DateTime(value.Ticks - value.Ticks % 10);
        });
    }

    [Fact]
    public void MapVarcharIntegerList()
    {
        MapValuesInternal("VARCHAR", "INTEGER[]", (_, index) => $"key{index}",
                          faker => Enumerable.Range(0, faker.Random.Int(0, 6)).Select(_ => faker.Random.Int()).ToList());
    }

    [Fact]
    public void MapNullValues()
    {
        MapValuesInternal("VARCHAR", "INTEGER", (_, index) => $"key{index}", faker => faker.Random.Int().OrNull(faker));
    }

    [Fact]
    public void MapNullStringValues()
    {
        MapValuesInternal("INTEGER", "VARCHAR", (_, index) => index, faker => faker.Lorem.Word().OrNull(faker));
    }

    [Fact]
    public void EmptyAndNullMaps()
    {
        Command.CommandText = "CREATE TABLE map_empty_null (id INTEGER, m MAP(VARCHAR, INTEGER));";
        Command.ExecuteNonQuery();

        using (var appender = Connection.CreateAppender("map_empty_null"))
        {
            appender.CreateRow().AppendValue(0).AppendValue(new Dictionary<string, int>()).EndRow();
            appender.CreateRow().AppendValue(1).AppendValue((Dictionary<string, int>)null).EndRow();
            appender.CreateRow().AppendValue(2).AppendNullValue().EndRow();
            appender.CreateRow().AppendValue(3).AppendValue(new Dictionary<string, int> { ["a"] = 1 }).EndRow();
            appender.CreateRow().AppendValue(4).AppendValue((IDictionary)null).EndRow();
        }

        Command.CommandText = "SELECT m FROM map_empty_null ORDER BY id";
        using var reader = Command.ExecuteReader();

        reader.Read();
        reader.GetFieldValue<Dictionary<string, int>>(0).Should().BeEmpty();
        reader.Read();
        reader.IsDBNull(0).Should().BeTrue();
        reader.Read();
        reader.IsDBNull(0).Should().BeTrue();
        reader.Read();
        reader.GetFieldValue<Dictionary<string, int>>(0).Should().Equal(new Dictionary<string, int> { ["a"] = 1 });
        reader.Read();
        reader.IsDBNull(0).Should().BeTrue();
    }

    [Fact]
    public void LargeMapsGrowChildCapacity()
    {
        Command.CommandText = "CREATE TABLE map_large (id INTEGER, m MAP(INTEGER, VARCHAR), n MAP(VARCHAR, INTEGER[]));";
        Command.ExecuteNonQuery();

        // Sizes chosen so the child vector grows several times within a chunk, and across chunks.
        int[] sizes = [0, 5_000, 1, 20_000, 3, 2_047, 2_048, 2_049, 70_000];
        var rows = new List<(Dictionary<int, string> M, Dictionary<string, List<int>> N)>();

        for (var row = 0; row < sizes.Length * 300; row++)
        {
            var size = sizes[row % sizes.Length] / (row < sizes.Length ? 1 : 100);
            rows.Add((Enumerable.Range(0, size).ToDictionary(index => index, index => $"value{row}-{index}"),
                      Enumerable.Range(0, size).ToDictionary(index => $"k{index}", index => new List<int> { row, index })));
        }

        using (var appender = Connection.CreateAppender("map_large"))
        {
            for (var index = 0; index < rows.Count; index++)
            {
                appender.CreateRow().AppendValue(index).AppendValue(rows[index].M).AppendValue(rows[index].N).EndRow();
            }
        }

        Command.CommandText = "SELECT m, n FROM map_large ORDER BY id";
        using var reader = Command.ExecuteReader();

        foreach (var (m, n) in rows)
        {
            reader.Read().Should().BeTrue();
            reader.GetFieldValue<Dictionary<int, string>>(0).Should().Equal(m);
            reader.GetFieldValue<Dictionary<string, List<int>>>(1).Should().BeEquivalentTo(n);
        }

        reader.Read().Should().BeFalse();
    }

    [Fact]
    public void DictionaryInterfacesAndNonGenericDictionary()
    {
        Command.CommandText = "CREATE TABLE map_interfaces (id INTEGER, m MAP(VARCHAR, INTEGER));";
        Command.ExecuteNonQuery();

        var expected = new Dictionary<string, int> { ["one"] = 1, ["two"] = 2 };

        using (var appender = Connection.CreateAppender("map_interfaces"))
        {
            IDictionary<string, int> genericInterface = new Dictionary<string, int>(expected);
            IReadOnlyDictionary<string, int> readOnlyInterface = new ReadOnlyDictionary<string, int>(expected);
            var sorted = new SortedDictionary<string, int>(expected);
            var readOnlyOnly = new ReadOnlyOnlyDictionary<string, int>(expected);
            var hashtable = new Hashtable { ["one"] = 1, ["two"] = 2 };

            appender.CreateRow().AppendValue(0).AppendValue(genericInterface).EndRow();
            appender.CreateRow().AppendValue(1).AppendValue(readOnlyInterface).EndRow();
            appender.CreateRow().AppendValue(2).AppendValue(sorted).EndRow();
            appender.CreateRow().AppendValue(3).AppendValue(readOnlyOnly).EndRow();
            appender.CreateRow().AppendValue(4).AppendValue(hashtable).EndRow();
            appender.AppendRowScoped(expected, static (ref DuckDBAppenderRowWriter row, Dictionary<string, int> map) =>
            {
                row.AppendValue(5);
                row.AppendValue(map);
            });
            appender.AppendRowScoped(hashtable, static (ref DuckDBAppenderRowWriter row, Hashtable map) =>
            {
                row.AppendValue(6);
                row.AppendValue(map);
            });
        }

        Command.CommandText = "SELECT m FROM map_interfaces ORDER BY id";
        using var reader = Command.ExecuteReader();

        for (var index = 0; index <= 6; index++)
        {
            reader.Read().Should().BeTrue();
            reader.GetFieldValue<Dictionary<string, int>>(0).Should().BeEquivalentTo(expected);
        }
    }

    [Fact]
    public void NullKeyThrows()
    {
        Command.CommandText = "CREATE TABLE map_null_key (m MAP(VARCHAR, INTEGER));";
        Command.ExecuteNonQuery();

        var map = new ReadOnlyOnlyDictionary<string, int>([new("a", 1), new(null, 2)]);

        using var appender = Connection.CreateAppender("map_null_key");

        appender.Invoking(a => a.CreateRow().AppendValue(map))
                .Should().Throw<ArgumentException>()
                .WithMessage("*null key*");
    }

    [Fact]
    public void NonGenericNullKeyThrows()
    {
        Command.CommandText = "CREATE TABLE map_null_key_nongeneric (m MAP(VARCHAR, INTEGER));";
        Command.ExecuteNonQuery();

        var map = new NullKeyNonGenericDictionary();

        using var appender = Connection.CreateAppender("map_null_key_nongeneric");

        appender.Invoking(a => a.CreateRow().AppendValue(map))
                .Should().Throw<ArgumentException>()
                .WithMessage("*null key*");
    }

    [Fact]
    public void NonDictionaryToMapColumnThrows()
    {
        Command.CommandText = "CREATE TABLE map_wrong_value (m MAP(VARCHAR, INTEGER));";
        Command.ExecuteNonQuery();

        using var appender = Connection.CreateAppender("map_wrong_value");

        appender.Invoking(a => a.CreateRow().AppendValue(new List<int> { 1, 2 }))
                .Should().Throw<InvalidOperationException>()
                .WithMessage("*Map*");
    }

    [Fact]
    public void WrongKeyTypeThrows()
    {
        Command.CommandText = "CREATE TABLE map_wrong_key (m MAP(INTEGER, INTEGER));";
        Command.ExecuteNonQuery();

        using var appender = Connection.CreateAppender("map_wrong_key");

        appender.Invoking(a => a.CreateRow().AppendValue(new Dictionary<string, int> { ["a"] = 1 }))
                .Should().Throw<InvalidOperationException>();
    }

    private void MapValuesInternal<TKey, TValue>(string keyType, string valueType, Func<Faker, int, TKey> keyGenerator,
                                                 Func<Faker, TValue> valueGenerator, int rowCount = 5_000) where TKey : notnull
    {
        var table = $"map_{Guid.NewGuid():N}";
        Command.CommandText = $"CREATE TABLE {table} (id INTEGER, m MAP({keyType}, {valueType}));";
        Command.ExecuteNonQuery();

        var faker = new Faker();
        var rows = new List<Dictionary<TKey, TValue>>(rowCount);

        for (var row = 0; row < rowCount; row++)
        {
            if (row % 17 == 3)
            {
                rows.Add(null);
                continue;
            }

            var size = row % 23 == 5 ? 0 : faker.Random.Int(1, 12);
            var map = new Dictionary<TKey, TValue>(size);

            for (var index = 0; index < size; index++)
            {
                map[keyGenerator(faker, index)] = valueGenerator(faker);
            }

            rows.Add(map);
        }

        using (var appender = Connection.CreateAppender(table))
        {
            for (var row = 0; row < rows.Count; row++)
            {
                appender.CreateRow().AppendValue(row).AppendValue(rows[row]).EndRow();
            }
        }

        Command.CommandText = $"SELECT m FROM {table} ORDER BY id";
        using var reader = Command.ExecuteReader();

        foreach (var expected in rows)
        {
            reader.Read().Should().BeTrue();

            if (expected is null)
            {
                reader.IsDBNull(0).Should().BeTrue();
            }
            else
            {
                reader.GetFieldValue<Dictionary<TKey, TValue>>(0).Should().BeEquivalentTo(expected);
            }
        }

        reader.Read().Should().BeFalse();
    }

    /// <summary>
    /// Implements IReadOnlyDictionary only, so it is neither an ICollection nor an IDictionary, and
    /// can hold a null key.
    /// </summary>
    private sealed class ReadOnlyOnlyDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> pairs) : IReadOnlyDictionary<TKey, TValue>
    {
        private readonly List<KeyValuePair<TKey, TValue>> pairs = pairs.ToList();

        public int Count => pairs.Count;
        public IEnumerable<TKey> Keys => pairs.Select(pair => pair.Key);
        public IEnumerable<TValue> Values => pairs.Select(pair => pair.Value);
        public TValue this[TKey key] => pairs.First(pair => Equals(pair.Key, key)).Value;
        public bool ContainsKey(TKey key) => pairs.Any(pair => Equals(pair.Key, key));

        public bool TryGetValue(TKey key, out TValue value)
        {
            var index = pairs.FindIndex(pair => Equals(pair.Key, key));
            value = index >= 0 ? pairs[index].Value : default;
            return index >= 0;
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => pairs.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A non-generic dictionary whose enumeration yields a null key, which Hashtable would reject.
    /// </summary>
    private sealed class NullKeyNonGenericDictionary : IDictionary
    {
        public int Count => 2;
        public bool IsFixedSize => true;
        public bool IsReadOnly => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public ICollection Keys => throw new NotSupportedException();
        public ICollection Values => throw new NotSupportedException();

        public object this[object key]
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void Add(object key, object value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(object key) => throw new NotSupportedException();
        public void Remove(object key) => throw new NotSupportedException();
        public void CopyTo(Array array, int index) => throw new NotSupportedException();
        public IDictionaryEnumerator GetEnumerator() => new Enumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IDictionaryEnumerator
        {
            private int position = -1;
            public object Key => position == 0 ? "a" : null;
            public object Value => position + 1;
            public DictionaryEntry Entry => new(Key, Value);
            public object Current => Entry;
            public bool MoveNext() => ++position < 2;
            public void Reset() => position = -1;
        }
    }
}
