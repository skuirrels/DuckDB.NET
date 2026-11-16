using DuckDB.NET.Data.Common;

namespace DuckDB.NET.Data.DataChunk.Writer;

/// <summary>
/// Shared list_entry_t handling for vectors that are physically lists (LIST and MAP).
/// Tracks the child offset, grows the child vector when needed and writes each row's entry.
/// </summary>
internal abstract unsafe class ListEntryVectorDataWriter(IntPtr vector, void* vectorData, DuckDBType columnType)
    : VectorDataWriterBase(vector, vectorData, columnType)
{
    private ulong childReservedSize = DuckDBGlobalData.VectorSize;

    /// <summary>
    /// The child index at which the next row's entries start.
    /// </summary>
    protected ulong ChildOffset { get; private set; }

    /// <summary>
    /// Called after the child vector has been reallocated so child writers can refresh their data pointers.
    /// </summary>
    protected abstract void OnChildVectorReserved();

    /// <summary>
    /// Makes sure the child vector can hold <paramref name="count"/> more entries.
    /// </summary>
    protected void EnsureChildCapacity(ulong rowIndex, ulong count)
    {
        if (ChildOffset + count <= childReservedSize) return;

        rowIndex %= DuckDBGlobalData.VectorSize;

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

        childReservedSize = (ulong)Math.Max(childReservedSize * factor, ChildOffset + count);
        var state = NativeMethods.Vectors.DuckDBListVectorReserve(Vector, childReservedSize);

        if (!state.IsSuccess())
        {
            throw new DuckDBException($"Failed to reserve {childReservedSize} for the list vector");
        }

        OnChildVectorReserved();
    }

    /// <summary>
    /// Writes the list entry for <paramref name="rowIndex"/> and advances the child offset and size.
    /// </summary>
    protected bool CompleteListEntry(ulong count, ulong rowIndex)
    {
        var result = AppendValueInternal(new DuckDBListEntry(ChildOffset, count), rowIndex);

        ChildOffset += count;
        NativeMethods.Vectors.DuckDBListVectorSetSize(Vector, ChildOffset);

        return result;
    }

    /// <summary>
    /// Advances the child offset without writing a list entry. Used by fixed-size ARRAY vectors.
    /// </summary>
    protected void AdvanceChildOffset(ulong count) => ChildOffset += count;
}
