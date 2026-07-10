using BenchmarkDotNet.Attributes;
using DuckDB.NET.Data;
using DuckDB.NET.Data.Mapping;

namespace DuckDB.NET.Benchmarks;

// Finding #3: PropertyMapping<T, TProperty>.AppendToRow dispatches value-type properties through a
// `value switch` over the generic TProperty, which boxes every value-type cell. This benchmark
// appends records with several value-type columns via a mapped appender.
[MemoryDiagnoser]
public class MappedAppenderBenchmark
{
    private DuckDBConnection connection = null!;
    private Person[] people = null!;

    [Params(1_000_000)]
    public int RowCount { get; set; }

    public class Person
    {
        public int Id { get; set; }
        public long Score { get; set; }
        public double Height { get; set; }
        public bool Active { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class PersonMap : DuckDBAppenderMap<Person>
    {
        public PersonMap()
        {
            Map(p => p.Id);
            Map(p => p.Score);
            Map(p => p.Height);
            Map(p => p.Active);
            Map(p => p.Name);
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();

        people = new Person[RowCount];
        for (var i = 0; i < RowCount; i++)
        {
            people[i] = new Person { Id = i, Score = i, Height = i * 0.5, Active = i % 2 == 0, Name = "row" };
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        connection.Dispose();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE IF EXISTS person; CREATE TABLE person (id INTEGER, score BIGINT, height DOUBLE, active BOOLEAN, name VARCHAR);";
        command.ExecuteNonQuery();
    }

    [Benchmark]
    public void AppendMappedRecords()
    {
        using var appender = connection.CreateAppender<Person, PersonMap>("person");
        appender.AppendRecords(people);
    }
}
