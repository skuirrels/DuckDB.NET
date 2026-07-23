using DuckDB.NET.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DuckDB.NET.Data.DataChunk.Writer;

internal sealed unsafe class ListVectorDataWriter : VectorDataWriterBase
{
    private delegate void CollectionWriter(ListVectorDataWriter writer, ICollection collection, ulong startIndex);

    private static readonly ConditionalWeakTable<Type, CollectionWriterPlan> CollectionWriterCache = new();
    private static readonly MethodInfo WriteArrayMethod =
        typeof(ListVectorDataWriter).GetMethod(nameof(WriteArray), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo WriteListMethod =
        typeof(ListVectorDataWriter).GetMethod(nameof(WriteList), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo WriteEnumerableMethod =
        typeof(ListVectorDataWriter).GetMethod(nameof(WriteEnumerable), BindingFlags.Static | BindingFlags.NonPublic)!;

    private ulong offset = 0;
    private readonly ulong arraySize;
    private readonly DuckDBLogicalType childType;
    private readonly VectorDataWriterBase listItemWriter;
    private Type? cachedCollectionType;
    private CollectionWriterPlan? cachedCollectionWriterPlan;

    private bool IsList => ColumnType == DuckDBType.List;
    private ulong vectorReservedSize = DuckDBGlobalData.VectorSize;

    public ListVectorDataWriter(IntPtr vector, void* vectorData, DuckDBType columnType, DuckDBLogicalType logicalType) : base(vector, vectorData, columnType)
    {
        childType = IsList ? NativeMethods.LogicalType.DuckDBListTypeChildType(logicalType) : NativeMethods.LogicalType.DuckDBArrayTypeChildType(logicalType);
        var childVector = IsList ? NativeMethods.Vectors.DuckDBListVectorGetChild(vector) : NativeMethods.Vectors.DuckDBArrayVectorGetChild(vector);

        arraySize = IsList ? 0 : (ulong)NativeMethods.LogicalType.DuckDBArrayVectorGetSize(logicalType);
        listItemWriter = VectorDataWriterFactory.CreateWriter(childVector, childType);
    }

    internal override bool AppendCollection(ICollection value, ulong rowIndex)
    {
        var count = (ulong)value.Count;

        ResizeVector(rowIndex % DuckDBGlobalData.VectorSize, count);

        ValidateArraySize(count);

        var collectionWriter = GetCollectionWriter(value.GetType());
        if (collectionWriter is not null)
        {
            collectionWriter(this, value, offset);
        }
        else
        {
            WriteItemsFallback(value);
        }

        var duckDBListEntry = new DuckDBListEntry(offset, count);
        var result = !IsList || AppendValueInternal(duckDBListEntry, rowIndex);

        offset += count;

        if (IsList)
        {
            NativeMethods.Vectors.DuckDBListVectorSetSize(Vector, offset);
        }

        return result;
    }

    private CollectionWriter? GetCollectionWriter(Type collectionType)
    {
        if (collectionType == cachedCollectionType)
        {
            return cachedCollectionWriterPlan!.Writer;
        }

        cachedCollectionType = collectionType;
        cachedCollectionWriterPlan = CollectionWriterCache.GetValue(collectionType, CreateCollectionWriter);
        return cachedCollectionWriterPlan.Writer;
    }

    private void ValidateArraySize(ulong count)
    {
        if (!IsList && count != arraySize)
        {
            throw new InvalidOperationException(
                $"Column has Array size of {arraySize} but the specified value has size of {count}");
        }
    }

    private void WriteItemsFallback(IEnumerable items)
    {
        var index = 0ul;

        foreach (var item in items)
        {
            listItemWriter.WriteValue(item, offset + (index++));
        }
    }

    private static CollectionWriterPlan CreateCollectionWriter(Type collectionType)
    {
        MethodInfo? openMethod = null;
        Type? elementType = null;

        if (collectionType.IsSZArray)
        {
            openMethod = WriteArrayMethod;
            elementType = collectionType.GetElementType();
        }
        else if (collectionType.IsGenericType &&
                 collectionType.GetGenericTypeDefinition() == typeof(List<>))
        {
            openMethod = WriteListMethod;
            elementType = collectionType.GetGenericArguments()[0];
        }
        else
        {
            elementType = GetEnumerableElementType(collectionType);
            openMethod = elementType is null ? null : WriteEnumerableMethod;
        }

        return new CollectionWriterPlan(
            openMethod is null || elementType is null
                ? null
                : openMethod.MakeGenericMethod(elementType).CreateDelegate<CollectionWriter>());
    }

    private static Type? GetEnumerableElementType(Type collectionType)
    {
        Type? elementType = null;

        foreach (var interfaceType in collectionType.GetInterfaces())
        {
            if (!interfaceType.IsGenericType ||
                interfaceType.GetGenericTypeDefinition() != typeof(IEnumerable<>))
            {
                continue;
            }

            var candidateType = interfaceType.GetGenericArguments()[0];
            if (elementType is not null && elementType != candidateType)
            {
                return null;
            }

            elementType = candidateType;
        }

        return elementType;
    }

    private static void WriteArray<T>(
        ListVectorDataWriter writer,
        ICollection collection,
        ulong startIndex)
    {
        var values = (T[])collection;

        for (var index = 0; index < values.Length; index++)
        {
            writer.listItemWriter.WriteValue(values[index], startIndex + (ulong)index);
        }
    }

    private static void WriteList<T>(
        ListVectorDataWriter writer,
        ICollection collection,
        ulong startIndex)
    {
        var values = (List<T>)collection;

        for (var index = 0; index < values.Count; index++)
        {
            writer.listItemWriter.WriteValue(values[index], startIndex + (ulong)index);
        }
    }

    private static void WriteEnumerable<T>(
        ListVectorDataWriter writer,
        ICollection collection,
        ulong startIndex)
    {
        var index = 0ul;

        foreach (var value in (IEnumerable<T>)collection)
        {
            writer.listItemWriter.WriteValue(value, startIndex + index++);
        }
    }

    private sealed record CollectionWriterPlan(CollectionWriter? Writer);

    private void ResizeVector(ulong rowIndex, ulong count)
    {
        //If writing to a list column we need to make sure that enough space is allocated. Not needed for Arrays as DuckDB does it for us.
        if (!IsList || offset + count <= vectorReservedSize) return;

        var factor = 2d;

        if (rowIndex > DuckDBGlobalData.VectorSize * 0.25 && rowIndex < DuckDBGlobalData.VectorSize * 0.5)
        {
            factor = 1.75;
        }

        if (rowIndex > DuckDBGlobalData.VectorSize * 0.5 && rowIndex < DuckDBGlobalData.VectorSize * 0.75)
        {
            factor = 1.5;
        }

        if (rowIndex > DuckDBGlobalData.VectorSize * 0.75)
        {
            factor = 1.25;
        }

        vectorReservedSize = (ulong)Math.Max(vectorReservedSize * factor, offset + count);
        var state = NativeMethods.Vectors.DuckDBListVectorReserve(Vector, vectorReservedSize);

        if (!state.IsSuccess())
        {
            throw new DuckDBException($"Failed to reserve {vectorReservedSize} for the list vector");
        }

        listItemWriter.InitializeWriter();
    }

    public override void Dispose()
    {
        cachedCollectionType = null;
        cachedCollectionWriterPlan = null;
        listItemWriter.Dispose();
        childType.Dispose();
    }
}
