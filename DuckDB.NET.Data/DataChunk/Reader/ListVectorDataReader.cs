namespace DuckDB.NET.Data.DataChunk.Reader;

internal sealed class ListVectorDataReader : VectorDataReaderBase
{
    private readonly ulong arraySize;
    private readonly VectorDataReaderBase listDataReader;
    private Type? cachedListType;
    private IListFactory? cachedListFactory;
    private IListMaterializer? cachedListMaterializer;

    public bool IsList => DuckDBType == DuckDBType.List;

    internal unsafe ListVectorDataReader(IntPtr vector, void* dataPointer, ulong* validityMaskPointer, DuckDBType columnType, DuckDBLogicalType logicalColumnType, string columnName) 
                    : base(dataPointer, validityMaskPointer, columnType, columnName)
    {
        using var childType = IsList ? NativeMethods.LogicalType.DuckDBListTypeChildType(logicalColumnType) : NativeMethods.LogicalType.DuckDBArrayTypeChildType(logicalColumnType);

        var childVector = IsList ? NativeMethods.Vectors.DuckDBListVectorGetChild(vector) : NativeMethods.Vectors.DuckDBArrayVectorGetChild(vector);

        arraySize = IsList ? 0 : (ulong)NativeMethods.LogicalType.DuckDBArrayVectorGetSize(logicalColumnType);
        listDataReader = VectorDataReaderFactory.CreateReader(childVector, childType, columnName);
    }

    protected override Type GetColumnType()
    {
        return typeof(List<>).MakeGenericType(listDataReader.ClrType);
    }

    protected override Type GetColumnProviderSpecificType()
    {
        return typeof(List<>).MakeGenericType(listDataReader.ProviderSpecificClrType);
    }

    internal override unsafe object GetValue(ulong offset, Type targetType)
    {
        switch (DuckDBType)
        {
            case DuckDBType.List:
                {
                    var listData = (DuckDBListEntry*)DataPointer + offset;

                    return GetList(targetType, listData->Offset, listData->Length);
                }
            case DuckDBType.Array:
                return GetList(targetType, offset * arraySize, arraySize);
            default:
                return base.GetValue(offset, targetType);
        }
    }

    private object GetList(Type returnType, ulong listOffset, ulong length)
    {
        var listType = returnType.GetGenericArguments()[0];
        var allowNulls = listType.AllowsNullValue(out _, out var nullableType);
        var list = CreateList(returnType, length);

        // Keep the established fast paths free of an interface dispatch. Other List<T>
        // shapes use the cached typed materializer below to avoid per-element boxing.
        return list switch
        {
            List<int> typedList => BuildList(typedList),
            List<int?> typedList => BuildList(typedList),
            List<float> typedList => BuildList(typedList),
            List<float?> typedList => BuildList(typedList),
            List<double> typedList => BuildList(typedList),
            List<double?> typedList => BuildList(typedList),
            List<decimal> typedList => BuildList(typedList),
            List<decimal?> typedList => BuildList(typedList),
            _ when cachedListMaterializer is { } materializer =>
                materializer.Materialize(list, listDataReader, listOffset, length, allowNulls),
            _ => BuildListCommon(list, nullableType ?? listType)
        };

        List<T> BuildList<T>(List<T> result)
        {
            for (ulong index = 0; index < length; index++)
            {
                var childOffset = listOffset + index;
                if (listDataReader.IsValid(childOffset))
                {
                    result.Add(listDataReader.GetValueStrict<T>(childOffset));
                }
                else
                {
                    result.Add(allowNulls
                        ? default!
                        : throw new InvalidCastException("The list contains null value"));
                }
            }

            return result;
        }

        IList BuildListCommon(IList result, Type targetType)
        {
            for (ulong index = 0; index < length; index++)
            {
                var childOffset = listOffset + index;
                if (listDataReader.IsValid(childOffset))
                {
                    result.Add(listDataReader.GetValue(childOffset, targetType));
                }
                else
                {
                    result.Add(allowNulls
                        ? null
                        : throw new InvalidCastException("The list contains null value"));
                }
            }

            return result;
        }
    }

    private IList CreateList(Type returnType, ulong length)
    {
        if (returnType != cachedListType)
        {
            cachedListType = returnType;
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(List<>))
            {
                cachedListFactory = CreateListFactory(returnType);
                cachedListMaterializer = CreateListMaterializer(returnType);
            }
            else
            {
                cachedListFactory = null;
                cachedListMaterializer = null;
            }
        }

        return cachedListFactory?.Create(checked((int)length))
               ?? Activator.CreateInstance(returnType) as IList
               ?? throw new ArgumentException(
                   $"The type '{returnType.Name}' specified in parameter {nameof(returnType)} cannot be instantiated as an IList.");
    }

    private static IListFactory CreateListFactory(Type returnType)
    {
        var factoryType = typeof(ListFactory<>).MakeGenericType(returnType.GetGenericArguments()[0]);
        return (IListFactory)Activator.CreateInstance(factoryType)!;
    }

    private static IListMaterializer CreateListMaterializer(Type returnType)
    {
        var elementType = returnType.GetGenericArguments()[0];
        var nullableElementType = Nullable.GetUnderlyingType(elementType);

        Type materializerType;
        if (nullableElementType != null)
        {
            materializerType = (nullableElementType.IsEnum
                    ? typeof(NullableConvertedListMaterializer<>)
                    : typeof(NullableListMaterializer<>))
                .MakeGenericType(nullableElementType);
        }
        else if (elementType.IsEnum || (!elementType.IsValueType && elementType != typeof(string)))
        {
            materializerType = typeof(ConvertedListMaterializer<>).MakeGenericType(elementType);
        }
        else
        {
            materializerType = typeof(ListMaterializer<>).MakeGenericType(elementType);
        }

        return (IListMaterializer)Activator.CreateInstance(materializerType)!;
    }

    internal override void Reset(IntPtr vector)
    {
        base.Reset(vector);
        var childVector = IsList
            ? NativeMethods.Vectors.DuckDBListVectorGetChild(vector)
            : NativeMethods.Vectors.DuckDBArrayVectorGetChild(vector);
        listDataReader.Reset(childVector);
    }

    public override void Dispose()
    {
        listDataReader.Dispose();
        base.Dispose();
    }

    private interface IListFactory
    {
        IList Create(int capacity);
    }

    private sealed class ListFactory<T> : IListFactory
    {
        public IList Create(int capacity) => new List<T>(capacity);
    }

    private interface IListMaterializer
    {
        object Materialize(IList list, VectorDataReaderBase reader, ulong offset, ulong length, bool allowNulls);
    }

    private sealed class ListMaterializer<T> : IListMaterializer
    {
        public object Materialize(IList list, VectorDataReaderBase reader, ulong offset, ulong length, bool allowNulls)
        {
            var result = (List<T>)list;

            for (ulong index = 0; index < length; index++)
            {
                var childOffset = offset + index;
                if (reader.IsValid(childOffset))
                {
                    result.Add(reader.GetValueStrict<T>(childOffset));
                }
                else
                {
                    result.Add(allowNulls
                        ? default!
                        : throw new InvalidCastException("The list contains null value"));
                }
            }

            return result;
        }
    }

    private sealed class NullableListMaterializer<T> : IListMaterializer where T : struct
    {
        public object Materialize(IList list, VectorDataReaderBase reader, ulong offset, ulong length, bool allowNulls)
        {
            var result = (List<T?>)list;

            for (ulong index = 0; index < length; index++)
            {
                var childOffset = offset + index;
                result.Add(reader.IsValid(childOffset)
                    ? reader.GetValueStrict<T>(childOffset)
                    : allowNulls
                        ? null
                        : throw new InvalidCastException("The list contains null value"));
            }

            return result;
        }
    }

    private sealed class NullableConvertedListMaterializer<T> : IListMaterializer where T : struct
    {
        public object Materialize(IList list, VectorDataReaderBase reader, ulong offset, ulong length, bool allowNulls)
        {
            var result = (List<T?>)list;

            for (ulong index = 0; index < length; index++)
            {
                var childOffset = offset + index;
                result.Add(reader.IsValid(childOffset)
                    ? (T)reader.GetValue(childOffset, typeof(T))
                    : allowNulls
                        ? null
                        : throw new InvalidCastException("The list contains null value"));
            }

            return result;
        }
    }

    private sealed class ConvertedListMaterializer<T> : IListMaterializer
    {
        public object Materialize(IList list, VectorDataReaderBase reader, ulong offset, ulong length, bool allowNulls)
        {
            var result = (List<T>)list;

            for (ulong index = 0; index < length; index++)
            {
                var childOffset = offset + index;
                result.Add(reader.IsValid(childOffset)
                    ? (T)reader.GetValue(childOffset, typeof(T))
                    : allowNulls
                        ? default!
                        : throw new InvalidCastException("The list contains null value"));
            }

            return result;
        }
    }
}
