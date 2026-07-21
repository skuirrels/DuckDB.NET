namespace DuckDB.NET.Data.Extensions;

//https://stackoverflow.com/a/5359304/239438
internal static class DateTimeExtensions
{
    public const int TicksPerMicrosecond = 10;
    public const int NanosecondsPerTick = 100;

    public static DuckDBTimeTzStruct ToTimeTzStruct(this DateTimeOffset value)
    {
        var time = NativeMethods.DateTimeHelpers.DuckDBToTime((DuckDBTimeOnly)value.DateTime);
        var timeTz = NativeMethods.DateTimeHelpers.DuckDBCreateTimeTz(time.Micros, (int)value.Offset.TotalSeconds);

        return timeTz;
    }

    public static DuckDBTimestampStruct ToTimestampStruct(this DateTimeOffset value)
    {
        return value.UtcDateTime.ToTimestampStruct(DuckDBType.Timestamp);
    }

    public static DuckDBTimestampStruct ToTimestampStruct(this DateTime value, DuckDBType duckDBType)
    {
        var ticksSinceEpoch = value.Ticks - DateTime.UnixEpoch.Ticks;
        var microseconds = ticksSinceEpoch / TicksPerMicrosecond;

        // duckdb_to_timestamp truncates the time-of-day to microseconds after resolving the date,
        // which is floor division for pre-epoch values with sub-microsecond ticks.
        if (ticksSinceEpoch < 0 && ticksSinceEpoch % TicksPerMicrosecond != 0)
        {
            microseconds--;
        }

        var timestamp = new DuckDBTimestampStruct { Micros = microseconds };

        if (duckDBType == DuckDBType.TimestampNs)
        {
            timestamp.Micros *= 1000;

            timestamp.Micros += value.Nanosecond;
        }

        if (duckDBType == DuckDBType.TimestampMs)
        {
            timestamp.Micros /= 1000;
        }

        if (duckDBType == DuckDBType.TimestampS)
        {
            timestamp.Micros /= 1000000;
        }

        return timestamp;
    }

    public static (DuckDBTimestamp result, int additionalTicks) ToDuckDBTimestamp(this DuckDBTimestampStruct timestamp, DuckDBType duckDBType)
    {
        var additionalTicks = 0;

        if (duckDBType == DuckDBType.TimestampNs)
        {
            additionalTicks = (int)(timestamp.Micros % 1000 / 100);
            timestamp.Micros /= 1000;
        }

        if (duckDBType == DuckDBType.TimestampMs)
        {
            timestamp.Micros *= 1000;
        }

        if (duckDBType == DuckDBType.TimestampS)
        {
            timestamp.Micros *= 1000000;
        }

        var result = DuckDBTimestamp.FromDuckDBTimestampStruct(timestamp);

        return (result, additionalTicks);
    }

    /// Uses the native method corresponding to the timestamp type, as opposed
    /// to comparing with a constant directly.
    public static bool IsFinite(this DuckDBTimestampStruct timestamp, DuckDBType duckDBType)
    {
        return duckDBType switch
        {
            DuckDBType.TimestampNs => NativeMethods.DateTimeHelpers.DuckDBIsFiniteTimestampNs(timestamp),
            DuckDBType.TimestampMs => NativeMethods.DateTimeHelpers.DuckDBIsFiniteTimestampMs(timestamp),
            DuckDBType.TimestampS => NativeMethods.DateTimeHelpers.DuckDBIsFiniteTimestampS(timestamp),
            _ => NativeMethods.DateTimeHelpers.DuckDBIsFiniteTimestamp(timestamp)
        };
    }
}
