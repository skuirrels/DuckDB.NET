using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace DuckDB.NET.Data.PreparedStatement;

internal static class ClrToDuckDBConverter
{
    // Weak keys, so caching a reader does not keep types from collectible assemblies alive.
    private static readonly ConditionalWeakTable<Type, Func<object, MapEntry[]>?> MapEntryReaders = new();
    private static readonly MethodInfo DictionaryEntriesMethod =
        typeof(ClrToDuckDBConverter).GetMethod(nameof(GetDictionaryEntries), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly Dictionary<DbType, Func<object, DuckDBValue>> ValueCreators = new()
    {
        { DbType.Guid, value => NativeMethods.Value.DuckDBCreateUuid(((Guid)value).ToHugeInt(false)) },
        { DbType.Currency, value => DecimalToDuckDBValue((decimal)value) },
        { DbType.Boolean, value => NativeMethods.Value.DuckDBCreateBool((bool)value) },
        { DbType.SByte, value => NativeMethods.Value.DuckDBCreateInt8((sbyte)value) },
        { DbType.Int16, value => NativeMethods.Value.DuckDBCreateInt16((short)value) },
        { DbType.Int32, value => NativeMethods.Value.DuckDBCreateInt32((int)value) },
        { DbType.Int64, value => NativeMethods.Value.DuckDBCreateInt64((long)value) },
        { DbType.Byte, value => NativeMethods.Value.DuckDBCreateUInt8((byte)value) },
        { DbType.UInt16, value => NativeMethods.Value.DuckDBCreateUInt16((ushort)value) },
        { DbType.UInt32, value => NativeMethods.Value.DuckDBCreateUInt32((uint)value) },
        { DbType.UInt64, value => NativeMethods.Value.DuckDBCreateUInt64((ulong)value) },
        { DbType.Single, value => NativeMethods.Value.DuckDBCreateFloat((float)value) },
        { DbType.Double, value => NativeMethods.Value.DuckDBCreateDouble((double)value) },
        { DbType.String, value => CreateVarchar((string?)value) },
        { DbType.VarNumeric, value => BigIntegerWithoutTargetType((BigInteger)value) },
        { DbType.Binary, value =>
            {
                var bytes = (byte[])value;
                return NativeMethods.Value.DuckDBCreateBlob(bytes, bytes.Length);
            }
        },
        { DbType.Date, value =>
            {
                var date = value is DateOnly dateOnly ? dateOnly.ToDuckDBDate() : ((DuckDBDateOnly)value).ToDuckDBDate();
                return NativeMethods.Value.DuckDBCreateDate(date);
            }
        },
        { DbType.Time, value =>
            {
                // A TimeSpan is a time of day here: TimeOnly.FromTimeSpan refuses a negative one or one of a day or more.
                var time = value switch
                {
                    TimeOnly timeOnly => timeOnly.ToDuckDBTime(),
                    TimeSpan timeSpan => TimeOnly.FromTimeSpan(timeSpan).ToDuckDBTime(),
                    _ => ((DuckDBTimeOnly)value).ToDuckDBTime()
                };
                return NativeMethods.Value.DuckDBCreateTime(time);
            }
        },
        { DbType.DateTime, value =>
            {
                var dateTime = value is DateTime dt ? dt.ToTimestampStruct(DuckDBType.Timestamp) : ((DuckDBTimestamp)value).ToDuckDBTimestampStruct();
                return NativeMethods.Value.DuckDBCreateTimestamp(dateTime);
            }
        },
        { DbType.DateTimeOffset, value => NativeMethods.Value.DuckDBCreateTimestampTz(((DateTimeOffset)value).ToTimestampStruct()) },
    };

    public static DuckDBValue ToDuckDBValue(this object? item, DuckDBLogicalType logicalType, DuckDBType duckDBType, DbType dbType)
    {
        if (item.IsNull())
        {
            return NativeMethods.Value.DuckDBCreateNullValue();
        }

        return (duckDBType, item) switch
        {
            // An Int128 is a HUGEINT and a UInt128 a UHUGEINT, whatever the target: DuckDB casts the value to the type of
            // the parameter and refuses one that does not fit. First, because the integer targets below would send them
            // as text: neither type is IConvertible. Temporary: once TryConvertTo no longer uses Convert.ChangeType (2.0
            // item 11), these become (DuckDBType.HugeInt, _) and (DuckDBType.UnsignedHugeInt, _) like the other integers.
            (_, Int128 value) => NativeMethods.Value.DuckDBCreateHugeInt(new DuckDBHugeInt(value)),
            (_, UInt128 value) => NativeMethods.Value.DuckDBCreateUHugeInt(new DuckDBUHugeInt(value)),

            (DuckDBType.Boolean, bool value) => NativeMethods.Value.DuckDBCreateBool(value),

            (DuckDBType.TinyInt, _) => TryConvertTo<sbyte>(out var result) ? NativeMethods.Value.DuckDBCreateInt8(result) : CreateVarchar(item.ToString()),
            (DuckDBType.SmallInt, _) => TryConvertTo<short>(out var result) ? NativeMethods.Value.DuckDBCreateInt16(result) : CreateVarchar(item.ToString()),
            (DuckDBType.Integer, _) => TryConvertTo<int>(out var result) ? NativeMethods.Value.DuckDBCreateInt32(result) : CreateVarchar(item.ToString()),
            (DuckDBType.BigInt, _) => TryConvertTo<long>(out var result) ? NativeMethods.Value.DuckDBCreateInt64(result) : CreateVarchar(item.ToString()),

            (DuckDBType.UnsignedTinyInt, _) => TryConvertTo<byte>(out var result) ? NativeMethods.Value.DuckDBCreateUInt8(result) : CreateVarchar(item.ToString()),
            (DuckDBType.UnsignedSmallInt, _) => TryConvertTo<ushort>(out var result) ? NativeMethods.Value.DuckDBCreateUInt16(result) : CreateVarchar(item.ToString()),
            (DuckDBType.UnsignedInteger, _) => TryConvertTo<uint>(out var result) ? NativeMethods.Value.DuckDBCreateUInt32(result) : CreateVarchar(item.ToString()),
            (DuckDBType.UnsignedBigInt, _) => TryConvertTo<ulong>(out var result) ? NativeMethods.Value.DuckDBCreateUInt64(result) : CreateVarchar(item.ToString()),

            (DuckDBType.Float, float value) => NativeMethods.Value.DuckDBCreateFloat(value),
            (DuckDBType.Double, double value) => NativeMethods.Value.DuckDBCreateDouble(value),

            (DuckDBType.Decimal, decimal value) => DecimalToDuckDBValue(value),
            (DuckDBType.HugeInt, BigInteger value) => NativeMethods.Value.DuckDBCreateHugeInt(new DuckDBHugeInt(value)),
            (DuckDBType.UnsignedHugeInt, BigInteger value) => NativeMethods.Value.DuckDBCreateUHugeInt(new DuckDBUHugeInt(value)),
            (DuckDBType.VarInt, BigInteger value) => BigIntegerToDuckDBValue(value),

            (DuckDBType.Varchar, string value) => CreateVarchar(value),
            (DuckDBType.Uuid, Guid value) => NativeMethods.Value.DuckDBCreateUuid(value.ToHugeInt(false)),

            (DuckDBType.Timestamp, DateTime value) => NativeMethods.Value.DuckDBCreateTimestamp(value.ToTimestampStruct(duckDBType)),
            (DuckDBType.TimestampS, DateTime value) => NativeMethods.Value.DuckDBCreateTimestampS(value.ToTimestampStruct(duckDBType)),
            (DuckDBType.TimestampMs, DateTime value) => NativeMethods.Value.DuckDBCreateTimestampMs(value.ToTimestampStruct(duckDBType)),
            (DuckDBType.TimestampNs, DateTime value) => NativeMethods.Value.DuckDBCreateTimestampNs(value.ToTimestampStruct(duckDBType)),
            (DuckDBType.TimestampTz, DateTime value) => NativeMethods.Value.DuckDBCreateTimestampTz(value.ToTimestampStruct(duckDBType)),
            (DuckDBType.TimestampTz, DateTimeOffset value) => NativeMethods.Value.DuckDBCreateTimestampTz(value.ToTimestampStruct()),
            (DuckDBType.TimestampTzNs, DateTime value) => NativeMethods.Value.DuckDBCreateTimestampTzNs(value.ToTimestampStruct(duckDBType)),
            (DuckDBType.TimestampTzNs, DateTimeOffset value) => NativeMethods.Value.DuckDBCreateTimestampTzNs(value.ToTimestampNsStruct()),
            (DuckDBType.Interval, TimeSpan value) => NativeMethods.Value.DuckDBCreateInterval(value),
            // No inferred type (a bare "?"): TimeSpan has no DbType, and its ToString() isn't interval text
            // DuckDB can parse once there are days ("1.01:01:01"). DbType.Time sends it as a time instead.
            (DuckDBType.Invalid, TimeSpan value) when dbType != DbType.Time => NativeMethods.Value.DuckDBCreateInterval(value),
            (DuckDBType.Date, DateTime value) => NativeMethods.Value.DuckDBCreateDate(value.ToDuckDBDate()),
            (DuckDBType.Date, DuckDBDateOnly value) => NativeMethods.Value.DuckDBCreateDate(value.ToDuckDBDate()),
            (DuckDBType.Time, DateTime value) => NativeMethods.Value.DuckDBCreateTime(value.ToDuckDBTime()),
            (DuckDBType.Time, DuckDBTimeOnly value) => NativeMethods.Value.DuckDBCreateTime(value.ToDuckDBTime()),
            (DuckDBType.Date, DateOnly value) => NativeMethods.Value.DuckDBCreateDate(value.ToDuckDBDate()),
            (DuckDBType.Time, TimeOnly value) => NativeMethods.Value.DuckDBCreateTime(value.ToDuckDBTime()),
            (DuckDBType.TimeNs, DateTime value) => NativeMethods.Value.DuckDBCreateTimeNs(DuckDBTimeNs.FromTicks(value.Ticks % TimeSpan.TicksPerDay)),
            (DuckDBType.TimeNs, DuckDBTimeOnly value) => NativeMethods.Value.DuckDBCreateTimeNs(DuckDBTimeNs.FromTicks(value.Ticks)),
            (DuckDBType.TimeNs, TimeOnly value) => NativeMethods.Value.DuckDBCreateTimeNs(DuckDBTimeNs.FromTicks(value.Ticks)),
            (DuckDBType.TimeNs, TimeSpan value) => NativeMethods.Value.DuckDBCreateTimeNs(DuckDBTimeNs.FromTicks(TimeOnly.FromTimeSpan(value).Ticks)),
            (DuckDBType.TimeTz, DateTimeOffset value) => NativeMethods.Value.DuckDBCreateTimeTz(value.ToTimeTzStruct()),
            (DuckDBType.Blob, byte[] value) => NativeMethods.Value.DuckDBCreateBlob(value, value.Length),
            // Dictionaries are collections too, so they must be matched before the LIST and ARRAY cases.
            (DuckDBType.Map, _) when TryGetMapEntries(item, out var entries) => CreateMapValue(logicalType, entries, dbType),
            // Without a MAP target type (for example an inferred parameter), infer MAP(K, V) from a generic dictionary.
            (_, _) when TryGetGenericMapEntries(item, out var entries) => CreateMapFromClrType(item.GetType(), entries, dbType),
            (DuckDBType.List, ICollection value) => CreateCollectionValue(logicalType, value, true, dbType),
            (DuckDBType.Array, ICollection value) => CreateCollectionValue(logicalType, value, false, dbType),
            (_, ICollection value) when item is not byte[] => CreateListFromClrType(value, dbType),
            _ when ValueCreators.TryGetValue(dbType, out var converter) => converter(item),
            _ => CreateVarchar(item.ToString())
        };

        bool TryConvertTo<T>(out T result) where T : struct
        {
            try
            {
                if (item is T parsable)
                {
                    result = parsable;
                    return true;
                }

                result = (T)Convert.ChangeType(item, typeof(T));
                return true;
            }
            catch (Exception)
            {
                result = default;
                return false;
            }
        }
    }

    private static DuckDBValue CreateCollectionValue(DuckDBLogicalType logicalType, ICollection collection, bool isList, DbType dbType)
    {
        using var childType = isList ? NativeMethods.LogicalType.DuckDBListTypeChildType(logicalType) :
                                       NativeMethods.LogicalType.DuckDBArrayTypeChildType(logicalType);

        var values = BuildValues(childType, collection, dbType);

        var value = isList ? NativeMethods.Value.DuckDBCreateListValue(childType, values, collection.Count)
                           : NativeMethods.Value.DuckDBCreateArrayValue(childType, values, collection.Count);

        return ThrowIfNotCreated(value, values, childType, isList ? "list" : "array");
    }

    private static DuckDBValue CreateListFromClrType(ICollection collection, DbType dbType)
    {
        var elementType = collection.GetType().GetInterface(typeof(IEnumerable<>).Name)?.GetGenericArguments()[0];

        if (elementType == null)
        {
            return CreateVarchar(collection.ToString());
        }

        using var childType = elementType.GetLogicalType();
        var values = BuildValues(childType, collection, dbType);

        return ThrowIfNotCreated(NativeMethods.Value.DuckDBCreateListValue(childType, values, collection.Count), values, childType, "list");
    }

    // DuckDB converts every item to the type of the items of the list or array. When one cannot be converted, an
    // item out of range for example, it returns no value and does not say which item or why. Binding that would
    // crash the process: duckdb_bind_value reads the value without testing for it.
    private static DuckDBValue ThrowIfNotCreated(DuckDBValue value, DuckDBValue[] items, DuckDBLogicalType itemType, string kind)
    {
        if (!value.IsInvalid)
        {
            return value;
        }

        // A value that was not created does not own its items, so they are released here.
        foreach (var item in items)
        {
            item.Dispose();
        }

        value.Dispose();

        var itemTypeName = NativeMethods.LogicalType.DuckDBGetTypeId(itemType);

        throw new DuckDBException($"Cannot create the {kind} value of a parameter: DuckDB could not convert every item to {itemTypeName}, the type of its items. " +
                                  "An item may be out of range for that type.");
    }

    private static DuckDBValue CreateMapValue(DuckDBLogicalType mapType, MapEntry[] entries, DbType dbType)
    {
        using var keyType = NativeMethods.LogicalType.DuckDBMapTypeKeyType(mapType);
        using var valueType = NativeMethods.LogicalType.DuckDBMapTypeValueType(mapType);

        var keyDuckDBType = NativeMethods.LogicalType.DuckDBGetTypeId(keyType);
        var valueDuckDBType = NativeMethods.LogicalType.DuckDBGetTypeId(valueType);

        var keys = new DuckDBValue[entries.Length];
        var values = new DuckDBValue[entries.Length];

        try
        {
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];

                if (entry.Key.IsNull())
                {
                    throw new ArgumentException("Cannot bind a dictionary with a null key to a MAP parameter. MAP keys must not be NULL.");
                }

                keys[index] = entry.Key.ToDuckDBValue(keyType, keyDuckDBType, dbType);
                values[index] = entry.Value.ToDuckDBValue(valueType, valueDuckDBType, dbType);
            }
        }
        catch
        {
            DisposeAll(keys);
            DisposeAll(values);
            throw;
        }

        // The MAP value owns the key and value handles from here on and destroys them with itself.
        var mapValue = NativeMethods.Value.DuckDBCreateMapValue(mapType, keys, values);

        if (mapValue.IsInvalid)
        {
            mapValue.Dispose();
            DisposeAll(keys);
            DisposeAll(values);
            throw new InvalidOperationException("DuckDB could not create a MAP value from the dictionary.");
        }

        return mapValue;
    }

    private static DuckDBValue CreateMapFromClrType(Type dictionaryType, MapEntry[] entries, DbType dbType)
    {
        using var mapType = dictionaryType.GetLogicalType();
        return CreateMapValue(mapType, entries, dbType);
    }

    private static bool TryGetMapEntries(object item, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MapEntry[]? entries)
    {
        if (TryGetGenericMapEntries(item, out entries))
        {
            return true;
        }

        if (item is IDictionary dictionary)
        {
            entries = new MapEntry[dictionary.Count];

            var index = 0;
            var enumerator = dictionary.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (index == entries.Length) ThrowCountMismatch();
                entries[index++] = new MapEntry(enumerator.Key, enumerator.Value);
            }

            if (index != entries.Length) ThrowCountMismatch();
            return true;
        }

        return false;
    }

    private static bool TryGetGenericMapEntries(object item, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MapEntry[]? entries)
    {
        if (item is string or byte[] || item is not IEnumerable)
        {
            entries = null;
            return false;
        }

        var reader = MapEntryReaders.GetValue(item.GetType(), static type =>
            type.TryGetDictionaryTypes(out var keyType, out var valueType)
                ? DictionaryEntriesMethod.MakeGenericMethod(keyType, valueType).CreateDelegate<Func<object, MapEntry[]>>()
                : null);

        entries = reader?.Invoke(item);
        return entries is not null;
    }

    private static MapEntry[] GetDictionaryEntries<TKey, TValue>(object item)
    {
        // The type implements IDictionary<,> or IReadOnlyDictionary<,>, so one of the two counts is always there.
        var pairs = (IEnumerable<KeyValuePair<TKey, TValue>>)item;
        var count = pairs is ICollection<KeyValuePair<TKey, TValue>> collection
            ? collection.Count
            : ((IReadOnlyCollection<KeyValuePair<TKey, TValue>>)pairs).Count;

        var entries = new MapEntry[count];
        var index = 0;

        foreach (var pair in pairs)
        {
            if (index == entries.Length) ThrowCountMismatch();
            entries[index++] = new MapEntry(pair.Key, pair.Value);
        }

        if (index != entries.Length) ThrowCountMismatch();
        return entries;
    }

    private static void ThrowCountMismatch() =>
        throw new InvalidOperationException("The number of dictionary entries does not match its Count.");

    private static void DisposeAll(DuckDBValue?[] values)
    {
        foreach (var value in values)
        {
            value?.Dispose();
        }
    }

    private readonly record struct MapEntry(object? Key, object? Value);

    private static DuckDBValue[] BuildValues(DuckDBLogicalType childType, ICollection collection, DbType dbType)
    {
        var childDuckDBType = NativeMethods.LogicalType.DuckDBGetTypeId(childType);
        var values = new DuckDBValue[collection.Count];

        var index = 0;
        foreach (var item in collection)
        {
            values[index++] = item.ToDuckDBValue(childType, childDuckDBType, dbType);
        }

        return values;
    }

    // DuckDB reported no type for the parameter, for example in "SELECT $1" or "$1::UHUGEINT", so pick the smallest
    // type that holds the value exactly: HUGEINT, then UHUGEINT, then BIGNUM.
    private static DuckDBValue BigIntegerWithoutTargetType(BigInteger value)
    {
        if (value >= DuckDBHugeInt.HugeIntMinValue && value <= DuckDBHugeInt.HugeIntMaxValue)
        {
            return NativeMethods.Value.DuckDBCreateHugeInt(new DuckDBHugeInt(value));
        }

        if (value >= DuckDBUHugeInt.HugeIntMinValue && value <= DuckDBUHugeInt.HugeIntMaxValue)
        {
            return NativeMethods.Value.DuckDBCreateUHugeInt(new DuckDBUHugeInt(value));
        }

        return BigIntegerToDuckDBValue(value);
    }

    private static unsafe DuckDBValue BigIntegerToDuckDBValue(BigInteger value)
    {
        // A BIGNUM is built from the magnitude bytes and a sign, which keeps values of any size exact.
        // Going through HUGEINT instead would cap the value at 128 bits.
        var isNegative = value.Sign < 0;
        var magnitude = isNegative ? -value : value;
        var bytes = magnitude.ToByteArray(isUnsigned: true, isBigEndian: true);

        fixed (byte* data = bytes)
        {
            return NativeMethods.Value.DuckDBCreateBignum(new DuckDBBignum((IntPtr)data, (ulong)bytes.Length, isNegative ? (byte)1 : (byte)0));
        }
    }

    // Encoded here and passed as bytes, so a NUL character in the string is kept. Never null: ToDuckDBValue sends a
    // null value as NULL.
    private static DuckDBValue CreateVarchar(string? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return NativeMethods.Value.DuckDBCreateVarchar(Encoding.UTF8.GetBytes(value));
    }

    private static DuckDBValue DecimalToDuckDBValue(decimal value)
    {
        // The value goes to DuckDB as it is in the decimal: the integer, with the scale of the decimal. The writer of
        // the appender starts from the same parts (DecimalVectorDataWriter).
        value.GetParts(out var low, out var high, out var isNegative, out var scale);
        var magnitude = new UInt128(high, low);

        // The width is the number of digits. A value below one has a zero in front of its fraction.
        var width = Math.Max(DecimalExtensions.CountDigits(magnitude), scale + 1);

        var integer = isNegative ? -(Int128)magnitude : (Int128)magnitude;

        return NativeMethods.Value.DuckDBCreateDecimal(new DuckDBDecimal((byte)width, scale, new DuckDBHugeInt(integer)));
    }
}
