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

    // For a TIMESTAMP_TZ_NS: the instant in nanoseconds.
    public static DuckDBTimestampStruct ToTimestampNsStruct(this DateTimeOffset value)
    {
        return value.UtcDateTime.ToTimestampStruct(DuckDBType.TimestampNs);
    }

    // A TIME_NS holds every tick of a TimeOnly.
    public static DuckDBTimeNs ToDuckDBTimeNs(this TimeOnly value) => new() { Nanos = value.Ticks * NanosecondsPerTick };

    // The date is dropped.
    public static DuckDBTimeNs ToDuckDBTimeNs(this DateTime value) => new() { Nanos = value.Ticks % TimeSpan.TicksPerDay * NanosecondsPerTick };

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

        if (duckDBType is DuckDBType.TimestampNs or DuckDBType.TimestampTzNs)
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

        if (duckDBType is DuckDBType.TimestampNs or DuckDBType.TimestampTzNs)
        {
            // What is below a tick is dropped toward minus infinity, so that a moment before 1970 keeps its date and
            // time of day: a nanosecond before 1970 is 1969-12-31 23:59:59.9999999, not 1970-01-01. DuckDB 2.0 casts
            // a TIMESTAMP_NS to a TIMESTAMP the same way.
            var ticks = Math.DivRem(timestamp.Micros, NanosecondsPerTick, out var belowTick);
            if (belowTick < 0)
            {
                ticks--;
            }

            var micros = Math.DivRem(ticks, TicksPerMicrosecond, out var ticksBelowMicro);
            if (ticksBelowMicro < 0)
            {
                micros--;
                ticksBelowMicro += TicksPerMicrosecond;
            }

            additionalTicks = (int)ticksBelowMicro;
            timestamp.Micros = micros;
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
            DuckDBType.TimestampNs or DuckDBType.TimestampTzNs => NativeMethods.DateTimeHelpers.DuckDBIsFiniteTimestampNs(timestamp),
            DuckDBType.TimestampMs => NativeMethods.DateTimeHelpers.DuckDBIsFiniteTimestampMs(timestamp),
            DuckDBType.TimestampS => NativeMethods.DateTimeHelpers.DuckDBIsFiniteTimestampS(timestamp),
            _ => NativeMethods.DateTimeHelpers.DuckDBIsFiniteTimestamp(timestamp)
        };
    }
}
