using DuckDB.NET.Data.Extensions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DuckDB.NET.Data.DataChunk.Writer;

/// <summary>
/// Writes dictionaries to a MAP vector. A MAP is physically a LIST&lt;STRUCT(key, value)&gt;, so each row
/// is a list entry over the struct child, and keys and values go to the struct's two child vectors.
/// </summary>
internal sealed unsafe class MapVectorDataWriter : ListEntryVectorDataWriter
{
    private delegate bool MapWriter(MapVectorDataWriter writer, object map, ulong rowIndex);

    private static readonly ConditionalWeakTable<Type, MapWriter> MapWriterCache = new();
    private static readonly MethodInfo WriteDictionaryMethod =
        typeof(MapVectorDataWriter).GetMethod(nameof(WriteDictionary), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo WriteGenericDictionaryMethod =
        typeof(MapVectorDataWriter).GetMethod(nameof(WriteGenericDictionary), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo WriteReadOnlyDictionaryMethod =
        typeof(MapVectorDataWriter).GetMethod(nameof(WriteReadOnlyDictionary), BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly DuckDBLogicalType keyType;
    private readonly DuckDBLogicalType valueType;
    private readonly VectorDataWriterBase keyWriter;
    private readonly VectorDataWriterBase valueWriter;
    private Type? cachedMapType;
    private MapWriter? cachedMapWriter;

    public MapVectorDataWriter(IntPtr vector, void* vectorData, DuckDBType columnType, DuckDBLogicalType logicalType) : base(vector, vectorData, columnType)
    {
        keyType = NativeMethods.LogicalType.DuckDBMapTypeKeyType(logicalType);
        valueType = NativeMethods.LogicalType.DuckDBMapTypeValueType(logicalType);

        var entryVector = NativeMethods.Vectors.DuckDBListVectorGetChild(vector);

        keyWriter = VectorDataWriterFactory.CreateWriter(NativeMethods.Vectors.DuckDBStructVectorGetChild(entryVector, 0), keyType);
        valueWriter = VectorDataWriterFactory.CreateWriter(NativeMethods.Vectors.DuckDBStructVectorGetChild(entryVector, 1), valueType);
    }

    internal override bool AppendCollection(ICollection value, ulong rowIndex) => AppendMap(value, rowIndex);

    internal override bool AppendMap(IEnumerable value, ulong rowIndex) => GetMapWriter(value.GetType())(this, value, rowIndex);

    protected override void OnChildVectorReserved()
    {
        keyWriter.InitializeWriter();
        valueWriter.InitializeWriter();
    }

    private MapWriter GetMapWriter(Type mapType)
    {
        if (mapType == cachedMapType)
        {
            return cachedMapWriter!;
        }

        cachedMapWriter = MapWriterCache.GetValue(mapType, CreateMapWriter);
        cachedMapType = mapType;
        return cachedMapWriter;
    }

    private static MapWriter CreateMapWriter(Type mapType)
    {
        MethodInfo? openMethod = null;

        if (mapType.TryGetDictionaryTypes(out var keyType, out var valueType))
        {
            if (mapType.IsGenericType && mapType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                openMethod = WriteDictionaryMethod;
            }
            else if (mapType.ImplementsGenericInterface(typeof(IDictionary<,>)))
            {
                openMethod = WriteGenericDictionaryMethod;
            }
            else
            {
                openMethod = WriteReadOnlyDictionaryMethod;
            }
        }

        if (openMethod is not null)
        {
            return openMethod.MakeGenericMethod(keyType!, valueType!).CreateDelegate<MapWriter>();
        }

        if (typeof(IDictionary).IsAssignableFrom(mapType))
        {
            return WriteNonGenericDictionary;
        }

        return (_, _, _) =>
            throw new InvalidOperationException($"Cannot write {mapType.Name} to {DuckDBType.Map} column. Use a dictionary type.");
    }

    private static bool WriteDictionary<TKey, TValue>(MapVectorDataWriter writer, object map, ulong rowIndex) where TKey : notnull
    {
        var dictionary = (Dictionary<TKey, TValue>)map;
        var count = (ulong)dictionary.Count;
        var index = writer.BeginEntries(rowIndex, count);

        foreach (var pair in dictionary)
        {
            writer.WriteEntry(pair.Key, pair.Value, index++);
        }

        return writer.CompleteListEntry(count, rowIndex);
    }

    private static bool WriteGenericDictionary<TKey, TValue>(MapVectorDataWriter writer, object map, ulong rowIndex)
    {
        var dictionary = (IDictionary<TKey, TValue>)map;
        return writer.WriteEntries(dictionary, (ulong)dictionary.Count, rowIndex);
    }

    private static bool WriteReadOnlyDictionary<TKey, TValue>(MapVectorDataWriter writer, object map, ulong rowIndex)
    {
        var dictionary = (IReadOnlyDictionary<TKey, TValue>)map;
        return writer.WriteEntries(dictionary, (ulong)dictionary.Count, rowIndex);
    }

    private static bool WriteNonGenericDictionary(MapVectorDataWriter writer, object map, ulong rowIndex)
    {
        var dictionary = (IDictionary)map;
        var count = (ulong)dictionary.Count;
        var index = writer.BeginEntries(rowIndex, count);
        var end = index + count;

        var enumerator = dictionary.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (index == end) ThrowCountMismatch();
            writer.WriteEntry(enumerator.Key, enumerator.Value, index++);
        }

        if (index != end) ThrowCountMismatch();
        return writer.CompleteListEntry(count, rowIndex);
    }

    private bool WriteEntries<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> entries, ulong count, ulong rowIndex)
    {
        var index = BeginEntries(rowIndex, count);
        var end = index + count;

        foreach (var pair in entries)
        {
            // Count comes from the collection itself, so guard against implementations whose
            // enumeration does not match it. Writing past the reserved child capacity is unsafe.
            if (index == end) ThrowCountMismatch();
            WriteEntry(pair.Key, pair.Value, index++);
        }

        if (index != end) ThrowCountMismatch();
        return CompleteListEntry(count, rowIndex);
    }

    private ulong BeginEntries(ulong rowIndex, ulong count)
    {
        EnsureChildCapacity(rowIndex, count);
        return ChildOffset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteEntry<TKey, TValue>(TKey key, TValue value, ulong index)
    {
        if (key is null)
        {
            throw new ArgumentException($"Cannot write a null key to a {DuckDBType.Map} column. MAP keys must not be NULL.", nameof(key));
        }

        keyWriter.WriteValue(key, index);
        valueWriter.WriteValue(value, index);
    }

    private static void ThrowCountMismatch()
    {
        throw new InvalidOperationException("The number of dictionary entries does not match its Count.");
    }

    public override void Dispose()
    {
        cachedMapType = null;
        cachedMapWriter = null;
        keyWriter.Dispose();
        valueWriter.Dispose();
        keyType.Dispose();
        valueType.Dispose();
    }
}
