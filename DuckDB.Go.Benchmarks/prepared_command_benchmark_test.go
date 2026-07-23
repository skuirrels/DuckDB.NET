package benchmarks

import (
	"database/sql"
	"fmt"
	"testing"

	_ "github.com/duckdb/duckdb-go/v2"
)

const (
	preparedCommandQuery  = "SELECT $first::BIGINT + $second::BIGINT + $third::BIGINT"
	expectedEngineVersion = "v1.5.4"
)

var resultSink int64

func BenchmarkPreparedCommandExecuteUnprepared(b *testing.B) {
	db := openVerifiedDatabase(b)
	arguments := preparedCommandArguments()
	var result int64

	b.ReportAllocs()
	b.ResetTimer()

	for i := 0; i < b.N; i++ {
		arguments[0] = sql.Named("first", int64(i))
		if err := db.QueryRow(preparedCommandQuery, arguments...).Scan(&result); err != nil {
			b.Fatal(err)
		}
	}

	resultSink = result
}

func BenchmarkPreparedCommandExecutePrepared(b *testing.B) {
	db := openVerifiedDatabase(b)
	statement, err := db.Prepare(preparedCommandQuery)
	if err != nil {
		b.Fatal(err)
	}
	b.Cleanup(func() {
		if err := statement.Close(); err != nil {
			b.Errorf("close prepared statement: %v", err)
		}
	})

	arguments := preparedCommandArguments()
	var result int64

	b.ReportAllocs()
	b.ResetTimer()

	for i := 0; i < b.N; i++ {
		arguments[0] = sql.Named("first", int64(i))
		if err := statement.QueryRow(arguments...).Scan(&result); err != nil {
			b.Fatal(err)
		}
	}

	resultSink = result
}

func BenchmarkPreparedCommandCreateAndPrepare(b *testing.B) {
	db := openVerifiedDatabase(b)

	b.ReportAllocs()
	b.ResetTimer()

	for i := 0; i < b.N; i++ {
		statement, err := db.Prepare(preparedCommandQuery)
		if err != nil {
			b.Fatal(err)
		}
		if err := statement.Close(); err != nil {
			b.Fatal(err)
		}
	}
}

func openVerifiedDatabase(tb testing.TB) *sql.DB {
	tb.Helper()

	db, err := sql.Open("duckdb", "")
	if err != nil {
		tb.Fatal(err)
	}
	db.SetMaxOpenConns(1)
	db.SetMaxIdleConns(1)
	tb.Cleanup(func() {
		if err := db.Close(); err != nil {
			tb.Errorf("close database: %v", err)
		}
	})

	var actualVersion string
	if err := db.QueryRow("SELECT version()").Scan(&actualVersion); err != nil {
		tb.Fatal(err)
	}
	if actualVersion != expectedEngineVersion {
		tb.Fatal(fmt.Errorf(
			"the driver comparison requires DuckDB %s, but loaded %s",
			expectedEngineVersion,
			actualVersion,
		))
	}
	if _, err := db.Exec("SET threads = 1"); err != nil {
		tb.Fatal(err)
	}

	return db
}

func preparedCommandArguments() []any {
	return []any{
		sql.Named("first", int64(1)),
		sql.Named("second", int64(2)),
		sql.Named("third", int64(3)),
	}
}
