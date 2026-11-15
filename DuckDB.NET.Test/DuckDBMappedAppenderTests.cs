using DuckDB.NET.Data.Mapping;

namespace DuckDB.NET.Test;

public class DuckDBMappedAppenderTests(DuckDBDatabaseFixture db) : DuckDBTestBase(db)
{
    // Example entity
    public class Person
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public float Height { get; set; }
        public DateTime BirthDate { get; set; }
    }

    // AppenderMap for Person - matches the example from the comment
    public class PersonMap : DuckDBAppenderMap<Person>
    {
        public PersonMap()
        {
            Map(p => p.Id);
            Map(p => p.Name);
            Map(p => p.Height);
            Map(p => p.BirthDate);
        }
    }

    [Fact]
    public void MappedAppender_ValidatesTypeMatching()
    {
        // Create table with specific types
        Command.CommandText = "CREATE TABLE person(id INTEGER, name VARCHAR, height REAL, birth_date TIMESTAMP);";
        Command.ExecuteNonQuery();

        // Create records
        var people = new[]
        {
            new Person { Id = 1, Name = "Alice", Height = 1.65f, BirthDate = new DateTime(1990, 1, 15) },
            new Person { Id = 2, Name = "Bob", Height = 1.80f, BirthDate = new DateTime(1985, 5, 20) },
        };

        // Use mapped appender - types are validated at creation
        using (var appender = Connection.CreateAppender<Person, PersonMap>("person"))
        {
            appender.AppendRecords(people);
        }

        // Verify data
        Command.CommandText = "SELECT * FROM person ORDER BY id";
        using var reader = Command.ExecuteReader();
        
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.GetString(1).Should().Be("Alice");
        reader.GetFloat(2).Should().BeApproximately(1.65f, 0.01f);
        reader.GetDateTime(3).Should().Be(new DateTime(1990, 1, 15));

        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(2);
        reader.GetString(1).Should().Be("Bob");
        reader.GetFloat(2).Should().BeApproximately(1.80f, 0.01f);
        reader.GetDateTime(3).Should().Be(new DateTime(1985, 5, 20));
    }

    // Example entity with a binary column
    public class FileEntry
    {
        public int Id { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    public class FileEntryMap : DuckDBAppenderMap<FileEntry>
    {
        public FileEntryMap()
        {
            Map(f => f.Id);
            Map(f => f.Data);
        }
    }

    [Fact]
    public void MappedAppender_SupportsByteArray()
    {
        Command.CommandText = "CREATE TABLE file_entry(id INTEGER, data BLOB);";
        Command.ExecuteNonQuery();

        var entries = new[]
        {
            new FileEntry { Id = 1, Data = new byte[] { 1, 2, 3 } },
            new FileEntry { Id = 2, Data = new byte[] { 10, 20, 30, 40 } },
        };

        using (var appender = Connection.CreateAppender<FileEntry, FileEntryMap>("file_entry"))
        {
            appender.AppendRecords(entries);
        }

        Command.CommandText = "SELECT id, data FROM file_entry ORDER BY id";
        using var reader = Command.ExecuteReader();

        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        ReadBlob(reader, 1).Should().Equal(1, 2, 3);

        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(2);
        ReadBlob(reader, 1).Should().Equal(10, 20, 30, 40);

        static byte[] ReadBlob(System.Data.Common.DbDataReader reader, int ordinal)
        {
            using var stream = reader.GetStream(ordinal);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }

    // Example with type mismatch - should throw
    public class WrongTypeMap : DuckDBAppenderMap<Person>
    {
        public WrongTypeMap()
        {
            Map(p => p.Id);
            Map(p => p.Name);
            Map(p => p.BirthDate);  // DateTime mapped to column 2, but column 2 is REAL
            Map(p => p.Height);
        }
    }

    [Fact]
    public void MappedAppender_ThrowsOnTypeMismatch()
    {
        Command.CommandText = "CREATE TABLE person_mismatch(id INTEGER, name VARCHAR, height REAL, birth_date TIMESTAMP);";
        Command.ExecuteNonQuery();

        // Should throw when creating the appender due to type mismatch
        Connection.Invoking(conn =>
        {
            var appender = conn.CreateAppender<Person, WrongTypeMap>("person_mismatch");
        }).Should().Throw<InvalidOperationException>()
          .WithMessage("*Type mismatch*");
    }

    // Example with DefaultValue and NullValue
    public class PersonWithDefaults
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class PersonWithDefaultsMap : DuckDBAppenderMap<PersonWithDefaults>
    {
        public PersonWithDefaultsMap()
        {
            Map(p => p.Id);
            Map(p => p.Name);
            DefaultValue();  // Use default for column 2
            NullValue();     // Use null for column 3
        }
    }

    [Fact]
    public void MappedAppender_SupportsDefaultAndNull()
    {
        Command.CommandText = "CREATE TABLE person_defaults(id INTEGER, name VARCHAR, age INT DEFAULT 18, city VARCHAR);";
        Command.ExecuteNonQuery();

        var people = new[]
        {
            new PersonWithDefaults { Id = 1, Name = "Alice" },
            new PersonWithDefaults { Id = 2, Name = "Bob" },
        };

        using (var appender = Connection.CreateAppender<PersonWithDefaults, PersonWithDefaultsMap>("person_defaults"))
        {
            appender.AppendRecords(people);
        }

        Command.CommandText = "SELECT id, name, age, city FROM person_defaults";
        using var reader = Command.ExecuteReader();
        
        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.GetString(1).Should().Be("Alice");
        reader.GetInt32(2).Should().Be(18);
        reader.IsDBNull(3).Should().BeTrue();
    }

    public class NullableValue
    {
        public int Id { get; set; }
        public int? Value { get; set; }
    }

    public class NullableValueMap : DuckDBAppenderMap<NullableValue>
    {
        public NullableValueMap()
        {
            Map(value => value.Id);
            Map(value => value.Value);
        }
    }

    [Fact]
    public void MappedAppender_CompiledWriterSupportsNullableValues()
    {
        Command.CommandText = "CREATE TABLE nullable_values(id INTEGER, value INTEGER);";
        Command.ExecuteNonQuery();

        using (var appender = Connection.CreateAppender<NullableValue, NullableValueMap>("nullable_values"))
        {
            appender.AppendRecords(
            [
                new NullableValue { Id = 1, Value = 42 },
                new NullableValue { Id = 2, Value = null }
            ]);
        }

        Command.CommandText = "SELECT id, value FROM nullable_values ORDER BY id";
        using var reader = Command.ExecuteReader();

        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(1);
        reader.GetInt32(1).Should().Be(42);

        reader.Read().Should().BeTrue();
        reader.GetInt32(0).Should().Be(2);
        reader.IsDBNull(1).Should().BeTrue();
    }
    public class Tagged
    {
        public int Id { get; set; }
        public Dictionary<string, int> Counts { get; set; }
        public IReadOnlyDictionary<int, string> Labels { get; set; }
        public System.Collections.Hashtable Extra { get; set; }
    }

    public class TaggedMap : DuckDBAppenderMap<Tagged>
    {
        public TaggedMap()
        {
            Map(t => t.Id);
            Map(t => t.Counts);
            Map(t => t.Labels);
            Map(t => t.Extra);
        }
    }

    public class TaggedCountsOnlyMap : DuckDBAppenderMap<Tagged>
    {
        public TaggedCountsOnlyMap()
        {
            Map(t => t.Id);
            Map(t => t.Counts);
        }
    }

    [Fact]
    public void MappedAppender_WritesDictionariesToMapColumns()
    {
        Command.CommandText = "CREATE TABLE tagged(id INTEGER, counts MAP(VARCHAR, INTEGER), labels MAP(INTEGER, VARCHAR), extra MAP(VARCHAR, INTEGER));";
        Command.ExecuteNonQuery();

        var records = Enumerable.Range(0, 3_000).Select(index => new Tagged
        {
            Id = index,
            Counts = index % 10 == 0 ? null : new Dictionary<string, int> { ["a"] = index, ["b"] = -index },
            Labels = new Dictionary<int, string> { [index] = $"label{index}", [-1] = null },
            Extra = new System.Collections.Hashtable { ["x"] = index }
        }).ToList();

        using (var appender = Connection.CreateAppender<Tagged, TaggedMap>("tagged"))
        {
            appender.AppendRecords(records);
        }

        Command.CommandText = "SELECT counts, labels, extra FROM tagged ORDER BY id";
        using var reader = Command.ExecuteReader();

        foreach (var record in records)
        {
            reader.Read().Should().BeTrue();

            if (record.Counts is null)
            {
                reader.IsDBNull(0).Should().BeTrue();
            }
            else
            {
                reader.GetFieldValue<Dictionary<string, int>>(0).Should().Equal(record.Counts);
            }

            reader.GetFieldValue<Dictionary<int, string>>(1).Should().BeEquivalentTo(record.Labels);
            reader.GetFieldValue<Dictionary<string, int>>(2).Should().Equal(new Dictionary<string, int> { ["x"] = record.Id });
        }

        reader.Read().Should().BeFalse();
    }

    [Theory]
    [InlineData("MAP(INTEGER, INTEGER)")]
    [InlineData("MAP(VARCHAR, BIGINT)")]
    [InlineData("VARCHAR")]
    [InlineData("INTEGER[]")]
    public void MappedAppender_ThrowsOnMapTypeMismatch(string columnType)
    {
        Command.CommandText = $"CREATE OR REPLACE TABLE tagged_mismatch(id INTEGER, counts {columnType});";
        Command.ExecuteNonQuery();

        Connection.Invoking(conn =>
        {
            using var appender = conn.CreateAppender<Tagged, TaggedCountsOnlyMap>("tagged_mismatch");
        }).Should().Throw<InvalidOperationException>()
          .WithMessage("*Type mismatch at column index 1*");
    }

    public class Scored
    {
        public int Id { get; set; }
        public Dictionary<string, List<int>> Scores { get; set; }
    }

    public class ScoredMap : DuckDBAppenderMap<Scored>
    {
        public ScoredMap()
        {
            Map(s => s.Id);
            Map(s => s.Scores);
        }
    }

    [Fact]
    public void MappedAppender_WritesMapWithListValues()
    {
        Command.CommandText = "CREATE TABLE scored(id INTEGER, scores MAP(VARCHAR, INTEGER[]));";
        Command.ExecuteNonQuery();

        var record = new Scored { Id = 1, Scores = new Dictionary<string, List<int>> { ["a"] = [1, 2], ["b"] = [] } };

        using (var appender = Connection.CreateAppender<Scored, ScoredMap>("scored"))
        {
            appender.AppendRecords([record]);
        }

        Command.CommandText = "SELECT scores FROM scored";
        using var reader = Command.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetFieldValue<Dictionary<string, List<int>>>(0).Should().BeEquivalentTo(record.Scores);
    }

    [Theory]
    [InlineData("MAP(VARCHAR, VARCHAR[])")]
    [InlineData("MAP(VARCHAR, INTEGER)")]
    [InlineData("MAP(INTEGER, INTEGER[])")]
    public void MappedAppender_ThrowsOnNestedMapTypeMismatch(string columnType)
    {
        Command.CommandText = $"CREATE OR REPLACE TABLE scored_mismatch(id INTEGER, scores {columnType});";
        Command.ExecuteNonQuery();

        Connection.Invoking(conn =>
        {
            using var appender = conn.CreateAppender<Scored, ScoredMap>("scored_mismatch");
        }).Should().Throw<InvalidOperationException>()
          .WithMessage("*Type mismatch at column index 1*Map(Varchar, List(Integer))*");
    }
}
