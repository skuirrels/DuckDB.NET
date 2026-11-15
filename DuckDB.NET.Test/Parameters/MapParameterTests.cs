using System.Collections;

namespace DuckDB.NET.Test.Parameters;

public class MapParameterTests(DuckDBDatabaseFixture db) : DuckDBTestBase(db)
{
    private static readonly Dictionary<string, int> Map = new() { ["a"] = 1, ["b"] = 5, ["c"] = 7 };

    [Fact]
    public void InsertAndCompareDictionaryParameter()
    {
        Command.CommandText = "CREATE TABLE map_parameters (id INTEGER, m MAP(VARCHAR, INTEGER));";
        Command.ExecuteNonQuery();

        Command.CommandText = "INSERT INTO map_parameters VALUES (1, $1), (2, $2);";
        Command.Parameters.Add(new DuckDBParameter(Map));
        Command.Parameters.Add(new DuckDBParameter(new Dictionary<string, int> { ["x"] = 10 }));
        Command.ExecuteNonQuery().Should().Be(2);

        Command.Parameters.Clear();
        Command.CommandText = "SELECT m FROM map_parameters WHERE id = 1;";
        Command.ExecuteScalar().Should().BeEquivalentTo(Map);

        Command.CommandText = "SELECT id FROM map_parameters WHERE m = $1;";
        Command.Parameters.Add(new DuckDBParameter(Map));
        Command.ExecuteScalar().Should().Be(1);
    }

    [Fact]
    public void TypedDictionaryParameter()
    {
        Command.CommandText = "CREATE TABLE map_typed_parameters (m MAP(INTEGER, VARCHAR));";
        Command.ExecuteNonQuery();

        var map = new Dictionary<int, string> { [1] = "one", [2] = null, [3] = "three" };

        Command.CommandText = "INSERT INTO map_typed_parameters VALUES ($1);";
        Command.Parameters.Add(new DuckDBParameter<Dictionary<int, string>>(map));
        Command.ExecuteNonQuery();

        Command.Parameters.Clear();
        Command.CommandText = "SELECT m FROM map_typed_parameters;";
        using var reader = Command.ExecuteReader();
        reader.Read();
        reader.GetFieldValue<Dictionary<int, string>>(0).Should().BeEquivalentTo(map);
    }

    [Fact]
    public void MapFunctionsOnBoundMap()
    {
        Command.CommandText = "SELECT element_at($1, 'b'), map_extract($1, 'c'), $1['a'], cardinality($1);";
        Command.Parameters.Add(new DuckDBParameter(Map));

        using var reader = Command.ExecuteReader();
        reader.Read();

        UnwrapSingle(reader.GetValue(0)).Should().Be(5);
        UnwrapSingle(reader.GetValue(1)).Should().Be(7);
        UnwrapSingle(reader.GetValue(2)).Should().Be(1);
        reader.GetValue(3).Should().Be(3UL);
    }

    [Fact]
    public void InferredMapType()
    {
        Command.CommandText = "SELECT $1;";
        Command.Parameters.Add(new DuckDBParameter(new Dictionary<Guid, double> { [Guid.Empty] = 1.5 }));

        using var reader = Command.ExecuteReader();
        reader.Read();

        reader.GetFieldType(0).Should().Be(typeof(Dictionary<Guid, double>));
        reader.GetFieldValue<Dictionary<Guid, double>>(0).Should().Equal(new Dictionary<Guid, double> { [Guid.Empty] = 1.5 });
    }

    [Fact]
    public void InferredMapTypeWithListValues()
    {
        var map = new Dictionary<string, List<int>> { ["a"] = [1, 2], ["b"] = [] };

        Command.CommandText = "SELECT $1;";
        Command.Parameters.Add(new DuckDBParameter(map));

        using var reader = Command.ExecuteReader();
        reader.Read();

        reader.GetFieldValue<Dictionary<string, List<int>>>(0).Should().BeEquivalentTo(map);
    }

    [Fact]
    public void EmptyAndNullMapParameters()
    {
        Command.CommandText = "CREATE TABLE map_empty_parameters (id INTEGER, m MAP(VARCHAR, INTEGER));";
        Command.ExecuteNonQuery();

        Command.CommandText = "INSERT INTO map_empty_parameters VALUES (1, $1), (2, $2);";
        Command.Parameters.Add(new DuckDBParameter(new Dictionary<string, int>()));
        Command.Parameters.Add(new DuckDBParameter((object)null));
        Command.ExecuteNonQuery();

        Command.Parameters.Clear();
        Command.CommandText = "SELECT m FROM map_empty_parameters ORDER BY id;";
        using var reader = Command.ExecuteReader();

        reader.Read();
        reader.GetFieldValue<Dictionary<string, int>>(0).Should().BeEmpty();
        reader.Read();
        reader.IsDBNull(0).Should().BeTrue();
    }

    [Fact]
    public void NullValuesInMapParameter()
    {
        var map = new Dictionary<string, int?> { ["a"] = null, ["b"] = 2 };

        Command.CommandText = "CREATE TABLE map_null_value_parameters (m MAP(VARCHAR, INTEGER));";
        Command.ExecuteNonQuery();

        Command.CommandText = "INSERT INTO map_null_value_parameters VALUES ($1);";
        Command.Parameters.Add(new DuckDBParameter(map));
        Command.ExecuteNonQuery();

        Command.Parameters.Clear();
        Command.CommandText = "SELECT m FROM map_null_value_parameters;";
        using var reader = Command.ExecuteReader();
        reader.Read();
        reader.GetFieldValue<Dictionary<string, int?>>(0).Should().BeEquivalentTo(map);
    }

    [Fact]
    public void NonGenericDictionaryWithMapTarget()
    {
        Command.CommandText = "CREATE TABLE map_hashtable_parameters (m MAP(VARCHAR, INTEGER));";
        Command.ExecuteNonQuery();

        Command.CommandText = "INSERT INTO map_hashtable_parameters VALUES ($1);";
        Command.Parameters.Add(new DuckDBParameter(new Hashtable { ["a"] = 1, ["b"] = 5, ["c"] = 7 }));
        Command.ExecuteNonQuery();

        Command.Parameters.Clear();
        Command.CommandText = "SELECT m FROM map_hashtable_parameters;";
        Command.ExecuteScalar().Should().BeEquivalentTo(Map);
    }

    [Fact]
    public void NullKeyThrows()
    {
        Command.CommandText = "CREATE TABLE map_null_key_parameters (m MAP(VARCHAR, INTEGER));";
        Command.ExecuteNonQuery();

        Command.CommandText = "INSERT INTO map_null_key_parameters VALUES ($1);";
        Command.Parameters.Add(new DuckDBParameter(new NullKeyDictionary()));

        Command.Invoking(command => command.ExecuteNonQuery())
               .Should().Throw<ArgumentException>()
               .WithMessage("*null key*");
    }

    // map_extract and element_at return a single-element list on some DuckDB versions and the value on others.
    private static object UnwrapSingle(object value) => value is IList { Count: 1 } list ? list[0] : value;

    private sealed class NullKeyDictionary : IReadOnlyDictionary<string, int>
    {
        private readonly List<KeyValuePair<string, int>> pairs = [new("a", 1), new(null, 2)];

        public int Count => pairs.Count;
        public IEnumerable<string> Keys => pairs.Select(pair => pair.Key);
        public IEnumerable<int> Values => pairs.Select(pair => pair.Value);
        public int this[string key] => pairs.First(pair => pair.Key == key).Value;
        public bool ContainsKey(string key) => pairs.Any(pair => pair.Key == key);

        public bool TryGetValue(string key, out int value)
        {
            var index = pairs.FindIndex(pair => pair.Key == key);
            value = index >= 0 ? pairs[index].Value : 0;
            return index >= 0;
        }

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => pairs.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
