package benchmarks

import (
	"context"
	"database/sql"
	"database/sql/driver"
	"fmt"
	"math"
	"testing"
	"time"

	duckdb "github.com/duckdb/duckdb-go/v2"
)

const (
	analyticsRowCount       = 2_000_000
	materializationRowCount = 100_000
	ingestRowCount          = 10_000
	tpchScaleFactor         = 0.1

	analyticsQuery = `
		SELECT
			year(order_date) AS order_year,
			region,
			count(*) AS order_count,
			sum(amount) AS revenue
		FROM benchmark_orders
		WHERE customer_id = $customerId
		  AND order_date >= $fromDate
		  AND order_date < $toDate
		GROUP BY order_year, region
		ORDER BY order_year, region`

	materializationQuery = `
		SELECT id, event_date, event_time, amount, customer_name, is_active
		FROM benchmark_materialization
		ORDER BY id`

	ingestStatement = `
		INSERT INTO benchmark_ingest
		VALUES ($id, $eventTime, $amount, $category, $isActive)`
)

var realisticResultSink int64

type ingestRow struct {
	id        int64
	eventTime time.Time
	amount    float64
	category  string
	isActive  bool
}

func BenchmarkAnalyticalQueryExecuteUnprepared(b *testing.B) {
	db := openVerifiedDatabase(b)
	initializeAnalytics(b, db)

	b.ReportAllocs()
	b.ResetTimer()

	var checksum int64
	for i := 0; i < b.N; i++ {
		rows, err := db.Query(
			analyticsQuery,
			analyticsArguments(i%100)...,
		)
		if err != nil {
			b.Fatal(err)
		}
		checksum = consumeAnalytics(b, rows)
	}

	realisticResultSink = checksum
}

func BenchmarkAnalyticalQueryExecutePrepared(b *testing.B) {
	db := openVerifiedDatabase(b)
	initializeAnalytics(b, db)
	statement, err := db.Prepare(analyticsQuery)
	if err != nil {
		b.Fatal(err)
	}
	b.Cleanup(func() {
		if err := statement.Close(); err != nil {
			b.Errorf("close analytical statement: %v", err)
		}
	})

	b.ReportAllocs()
	b.ResetTimer()

	var checksum int64
	for i := 0; i < b.N; i++ {
		rows, err := statement.Query(analyticsArguments(i % 100)...)
		if err != nil {
			b.Fatal(err)
		}
		checksum = consumeAnalytics(b, rows)
	}

	realisticResultSink = checksum
}

func BenchmarkResultMaterializationReadOneHundredThousandMixedRows(b *testing.B) {
	db := openVerifiedDatabase(b)
	initializeMaterialization(b, db)

	b.ReportAllocs()
	b.ResetTimer()

	var checksum int64
	for i := 0; i < b.N; i++ {
		rows, err := db.Query(materializationQuery)
		if err != nil {
			b.Fatal(err)
		}
		checksum = consumeMaterialization(b, rows)
	}

	realisticResultSink = checksum
}

func BenchmarkBulkIngestionInsertPreparedInTransaction(b *testing.B) {
	db := openVerifiedDatabase(b)
	initializeIngest(b, db)
	rows := createIngestRows()
	statement, err := db.Prepare(ingestStatement)
	if err != nil {
		b.Fatal(err)
	}
	b.Cleanup(func() {
		if err := statement.Close(); err != nil {
			b.Errorf("close ingest statement: %v", err)
		}
	})

	b.ReportAllocs()
	b.ResetTimer()
	started := time.Now()

	for i := 0; i < b.N; i++ {
		transaction, err := db.Begin()
		if err != nil {
			b.Fatal(err)
		}
		transactionStatement := transaction.Stmt(statement)

		for _, row := range rows {
			if _, err := transactionStatement.Exec(
				sql.Named("id", row.id),
				sql.Named("eventTime", row.eventTime),
				sql.Named("amount", row.amount),
				sql.Named("category", row.category),
				sql.Named("isActive", row.isActive),
			); err != nil {
				b.Fatal(err)
			}
		}

		if err := transactionStatement.Close(); err != nil {
			b.Fatal(err)
		}
		if err := transaction.Rollback(); err != nil {
			b.Fatal(err)
		}
	}

	reportRowsPerSecond(b, started, b.N*len(rows))
}

func BenchmarkBulkIngestionInsertWithAppenderInTransaction(b *testing.B) {
	db := openVerifiedDatabase(b)
	initializeIngest(b, db)
	rows := createIngestRows()
	connection, err := db.Conn(context.Background())
	if err != nil {
		b.Fatal(err)
	}
	b.Cleanup(func() {
		if err := connection.Close(); err != nil {
			b.Errorf("close appender connection: %v", err)
		}
	})

	b.ReportAllocs()
	b.ResetTimer()
	started := time.Now()

	for i := 0; i < b.N; i++ {
		transaction, err := connection.BeginTx(context.Background(), nil)
		if err != nil {
			b.Fatal(err)
		}

		err = connection.Raw(func(rawConnection any) error {
			driverConnection, ok := rawConnection.(driver.Conn)
			if !ok {
				return fmt.Errorf("DuckDB connection does not implement driver.Conn")
			}

			appender, err := duckdb.NewAppenderFromConn(driverConnection, "", "benchmark_ingest")
			if err != nil {
				return err
			}

			for _, row := range rows {
				if err := appender.AppendRow(
					row.id,
					row.eventTime,
					row.amount,
					row.category,
					row.isActive,
				); err != nil {
					_ = appender.Close()
					return err
				}
			}

			return appender.Close()
		})
		if err != nil {
			_ = transaction.Rollback()
			b.Fatal(err)
		}
		if err := transaction.Rollback(); err != nil {
			b.Fatal(err)
		}
	}

	reportRowsPerSecond(b, started, b.N*len(rows))
}

func BenchmarkTpchQuery01(b *testing.B) {
	benchmarkTpchQuery(b, 1)
}

func BenchmarkTpchQuery06(b *testing.B) {
	benchmarkTpchQuery(b, 6)
}

func BenchmarkTpchQuery12(b *testing.B) {
	benchmarkTpchQuery(b, 12)
}

func BenchmarkTpchQuery14(b *testing.B) {
	benchmarkTpchQuery(b, 14)
}

func initializeAnalytics(tb testing.TB, db *sql.DB) {
	tb.Helper()
	mustExec(tb, db, fmt.Sprintf(`
		CREATE TABLE benchmark_orders AS
		SELECT
			i::BIGINT AS order_id,
			(i %% 10000)::INTEGER AS customer_id,
			DATE '2020-01-01' + ((i %% 1461)::INTEGER) AS order_date,
			CASE i %% 4
				WHEN 0 THEN 'north'
				WHEN 1 THEN 'south'
				WHEN 2 THEN 'east'
				ELSE 'west'
			END::VARCHAR AS region,
			((i %% 100000)::DOUBLE / 100.0) AS amount
		FROM range(%d) AS source(i)`, analyticsRowCount))
}

func initializeMaterialization(tb testing.TB, db *sql.DB) {
	tb.Helper()
	mustExec(tb, db, fmt.Sprintf(`
		CREATE TABLE benchmark_materialization AS
		SELECT
			i::BIGINT AS id,
			DATE '2020-01-01' + ((i %% 1461)::INTEGER) AS event_date,
			TIMESTAMP '2020-01-01 00:00:00' + ((i %% 31536000) * INTERVAL 1 SECOND) AS event_time,
			((i %% 100000)::DOUBLE / 100.0) AS amount,
			CASE
				WHEN i %% 10 = 0 THEN NULL
				ELSE ('customer-' || (i %% 10000)::VARCHAR)
			END::VARCHAR AS customer_name,
			(i %% 2 = 0) AS is_active
		FROM range(%d) AS source(i)`, materializationRowCount))
}

func initializeIngest(tb testing.TB, db *sql.DB) {
	tb.Helper()
	mustExec(tb, db, `
		CREATE TABLE benchmark_ingest(
			id BIGINT,
			event_time TIMESTAMP,
			amount DOUBLE,
			category VARCHAR,
			is_active BOOLEAN
		)`)
}

func initializeTpch(tb testing.TB, db *sql.DB) {
	tb.Helper()
	mustExec(tb, db, "INSTALL tpch")
	mustExec(tb, db, "LOAD tpch")
	mustExec(tb, db, fmt.Sprintf("CALL dbgen(sf = %g)", tpchScaleFactor))
}

func analyticsArguments(customerID int) []any {
	return []any{
		sql.Named("customerId", customerID),
		sql.Named("fromDate", time.Date(2020, 1, 1, 0, 0, 0, 0, time.UTC)),
		sql.Named("toDate", time.Date(2024, 1, 1, 0, 0, 0, 0, time.UTC)),
	}
}

func consumeAnalytics(tb testing.TB, rows *sql.Rows) int64 {
	tb.Helper()
	defer closeRows(tb, rows)

	rowCount := 0
	checksum := int64(17)
	for rows.Next() {
		var year int32
		var region string
		var orderCount int64
		var revenue float64
		if err := rows.Scan(&year, &region, &orderCount, &revenue); err != nil {
			tb.Fatal(err)
		}
		checksum = checksum*31 + int64(year)
		checksum = checksum*31 + int64(len(region))
		checksum = checksum*31 + orderCount
		checksum = checksum*31 + int64(math.Float64bits(revenue))
		rowCount++
	}
	if err := rows.Err(); err != nil {
		tb.Fatal(err)
	}
	if rowCount == 0 {
		tb.Fatal("the analytical workload returned no rows")
	}
	return checksum
}

func consumeMaterialization(tb testing.TB, rows *sql.Rows) int64 {
	tb.Helper()
	defer closeRows(tb, rows)

	rowCount := 0
	checksum := int64(17)
	for rows.Next() {
		var id int64
		var eventDate time.Time
		var eventTime time.Time
		var amount float64
		var customer sql.NullString
		var isActive bool
		if err := rows.Scan(&id, &eventDate, &eventTime, &amount, &customer, &isActive); err != nil {
			tb.Fatal(err)
		}
		checksum += id + eventDate.Unix() + eventTime.UnixNano()
		checksum += int64(math.Float64bits(amount))
		if customer.Valid {
			checksum += int64(len(customer.String))
		}
		if isActive {
			checksum++
		}
		rowCount++
	}
	if err := rows.Err(); err != nil {
		tb.Fatal(err)
	}
	if rowCount != materializationRowCount {
		tb.Fatalf("expected %d materialized rows, but read %d", materializationRowCount, rowCount)
	}
	return checksum
}

func benchmarkTpchQuery(b *testing.B, queryNumber int) {
	db := openVerifiedDatabase(b)
	initializeTpch(b, db)
	query := fmt.Sprintf("PRAGMA tpch(%d)", queryNumber)

	b.ReportAllocs()
	b.ResetTimer()

	var checksum int64
	for i := 0; i < b.N; i++ {
		rows, err := db.Query(query)
		if err != nil {
			b.Fatal(err)
		}
		checksum = consumeGenericRows(b, rows)
	}

	realisticResultSink = checksum
}

func consumeGenericRows(tb testing.TB, rows *sql.Rows) int64 {
	tb.Helper()
	defer closeRows(tb, rows)

	columns, err := rows.Columns()
	if err != nil {
		tb.Fatal(err)
	}
	values := make([]any, len(columns))
	destinations := make([]any, len(columns))
	for index := range values {
		destinations[index] = &values[index]
	}

	rowCount := 0
	checksum := int64(17)
	for rows.Next() {
		if err := rows.Scan(destinations...); err != nil {
			tb.Fatal(err)
		}
		for _, value := range values {
			checksum = checksum*31 + int64(len(fmt.Sprint(value)))
		}
		rowCount++
	}
	if err := rows.Err(); err != nil {
		tb.Fatal(err)
	}
	if rowCount == 0 {
		tb.Fatal("the TPC-H workload returned no rows")
	}
	return checksum
}

func createIngestRows() []ingestRow {
	rows := make([]ingestRow, ingestRowCount)
	start := time.Date(2024, 1, 1, 0, 0, 0, 0, time.UTC)
	categories := []string{"north", "south", "east", "west"}
	for index := range rows {
		rows[index] = ingestRow{
			id:        int64(index),
			eventTime: start.Add(time.Duration(index) * time.Second),
			amount:    float64(index%100000) / 100.0,
			category:  categories[index%len(categories)],
			isActive:  index%2 == 0,
		}
	}
	return rows
}

func mustExec(tb testing.TB, db *sql.DB, statement string) {
	tb.Helper()
	if _, err := db.Exec(statement); err != nil {
		tb.Fatal(err)
	}
}

func closeRows(tb testing.TB, rows *sql.Rows) {
	tb.Helper()
	if err := rows.Close(); err != nil {
		tb.Fatal(err)
	}
}

func reportRowsPerSecond(b *testing.B, started time.Time, rowCount int) {
	elapsed := time.Since(started)
	if elapsed > 0 {
		b.ReportMetric(float64(rowCount)/elapsed.Seconds(), "rows/s")
		b.ReportMetric(float64(elapsed.Nanoseconds())/float64(rowCount), "ns/row")
	}
}
