using DuckDB.NET.Data.Common;
using DuckDB.NET.Data.DataChunk.Writer;
using DuckDB.NET.Data.Extensions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace DuckDB.NET.Data;

/// <summary>
/// Appends rows to a DuckDB table.
/// </summary>
/// <remarks>
/// Instances are not thread-safe. Do not call other methods on the same appender from an
/// <see cref="AppendRow{TState}(TState, Action{IDuckDBAppenderRow, TState})"/> callback.
/// </remarks>
public class DuckDBAppender : IDisposable
{
    private bool closed;
    private bool isAppendingRow;
    private bool isFaulted;
    private readonly Native.DuckDBAppender nativeAppender;
    private readonly string qualifiedTableName;

    private ulong rowCount;

    private readonly DuckDBLogicalType[] logicalTypes;
    private readonly DuckDBDataChunk dataChunk;
    private readonly VectorDataWriterBase[] vectorWriters;
    private DuckDBAppenderRow? reusableRow;

    internal DuckDBAppender(Native.DuckDBAppender appender, string qualifiedTableName)
    {
        nativeAppender = appender;
        this.qualifiedTableName = qualifiedTableName;

        var columnCount = NativeMethods.Appender.DuckDBAppenderColumnCount(nativeAppender);

        vectorWriters = new VectorDataWriterBase[columnCount];
        logicalTypes = new DuckDBLogicalType[columnCount];
        var logicalTypeHandles = new IntPtr[columnCount];

        for (ulong index = 0; index < columnCount; index++)
        {
            logicalTypes[index] = NativeMethods.Appender.DuckDBAppenderColumnType(nativeAppender, index);
            logicalTypeHandles[index] = logicalTypes[index].DangerousGetHandle();
        }

        dataChunk = NativeMethods.DataChunks.DuckDBCreateDataChunk(logicalTypeHandles, columnCount);
    }

    /// <summary>
    /// Gets the logical types of the columns in the appender.
    /// </summary>
    internal IReadOnlyList<DuckDBLogicalType> LogicalTypes => logicalTypes;

    /// <summary>
    /// Creates an independent row. The caller must append every column and call
    /// <see cref="IDuckDBAppenderRow.EndRow"/>.
    /// </summary>
    public IDuckDBAppenderRow CreateRow()
    {
        EnsureUsable();
        return new DuckDBAppenderRow(qualifiedTableName, vectorWriters, PrepareRow(), dataChunk, nativeAppender);
    }

    /// <summary>
    /// Appends a complete row using a reusable row instance.
    /// </summary>
    /// <param name="writeRow">A callback that appends every column value. This method calls
    /// <see cref="IDuckDBAppenderRow.EndRow"/> after the callback returns.</param>
    /// <remarks>
    /// The row is valid only during the callback and must not be retained. The callback must not
    /// call other methods on this appender. If the callback fails, all rows added by this appender
    /// are cleared and the appender cannot be reused.
    /// </remarks>
    public void AppendRow(Action<IDuckDBAppenderRow> writeRow)
    {
        ArgumentNullException.ThrowIfNull(writeRow);

        // Pass the callback as state so the adapter remains static and allocation-free.
        AppendRow(writeRow, static (row, callback) => callback(row));
    }

    /// <summary>
    /// Appends a complete row using a reusable row instance without exposing that instance as a
    /// return value. The row passed to <paramref name="writeRow"/> is only valid for the duration of
    /// the callback and must not be retained or used after the callback returns.
    /// </summary>
    /// <typeparam name="TState">The type of value used to populate the row.</typeparam>
    /// <param name="state">The value used to populate the row.</param>
    /// <param name="writeRow">A callback that appends every column value. This method calls
    /// <see cref="IDuckDBAppenderRow.EndRow"/> after the callback returns.</param>
    /// <remarks>
    /// The callback must not call other methods on this appender. If the callback fails, the
    /// appender is cleared and cannot be reused.
    /// </remarks>
    public void AppendRow<TState>(TState state, Action<IDuckDBAppenderRow, TState> writeRow)
    {
        ArgumentNullException.ThrowIfNull(writeRow);
        EnsureUsable();

        DuckDBAppenderRow? row = null;
        isAppendingRow = true;

        try
        {
            row = CreateReusableRow();
            writeRow(row, state);
            row.EndRow();
        }
        catch
        {
            if (row is not null)
            {
                AbortAppendBatch(row);
            }

            throw;
        }
        finally
        {
            isAppendingRow = false;
        }
    }

    /// <summary>
    /// Creates a row whose instance may be reused by the next call. This is only safe for internal
    /// callers that create, populate and end each row without exposing the row reference.
    /// </summary>
    internal DuckDBAppenderRow CreateReusableRow()
    {
        var rowIndex = PrepareRow();

        if (reusableRow is null)
        {
            reusableRow = new DuckDBAppenderRow(qualifiedTableName, vectorWriters, rowIndex, dataChunk, nativeAppender);
        }
        else
        {
            reusableRow.Reset(rowIndex);
        }

        return reusableRow;
    }

    private ulong PrepareRow()
    {
        if (closed)
        {
            throw new InvalidOperationException("Appender is already closed");
        }

        if (rowCount % DuckDBGlobalData.VectorSize == 0)
        {
            AppendDataChunk();

            InitVectorWriters();

            rowCount = 0;
        }

        rowCount++;
        return rowCount - 1;
    }

    public void Clear()
    {
        EnsureUsable();

        if (closed)
        {
            throw new InvalidOperationException("Appender is already closed");
        }

        ClearCore();
    }

    private void ClearCore()
    {
        var state = NativeMethods.Appender.DuckDBAppenderClear(nativeAppender);
        if (!state.IsSuccess())
        {
            NativeMethods.Appender.DuckDBAppenderErrorData(nativeAppender).ThrowOnError();
        }

        rowCount = 0;
        NativeMethods.DataChunks.DuckDBDataChunkReset(dataChunk);
        InitVectorWriters();
    }

    public void Close()
    {
        EnsureNotAppendingRow();
        closed = true;

        try
        {
            AppendDataChunk();

            foreach (var logicalType in logicalTypes)
            {
                logicalType.Dispose();
            }

            foreach (var writer in vectorWriters)
            {
                writer?.Dispose();
            }

            dataChunk.Dispose();

            var state = NativeMethods.Appender.DuckDBAppenderClose(nativeAppender);
            if (!state.IsSuccess())
            {
                NativeMethods.Appender.DuckDBAppenderErrorData(nativeAppender).ThrowOnError();
            }
        }
        finally
        {
            nativeAppender.Close();
        }
    }

    public void Dispose()
    {
        if (!closed)
        {
            Close();
        }
    }

    private void InitVectorWriters()
    {
        for (long index = 0; index < vectorWriters.LongLength; index++)
        {
            var vector = NativeMethods.DataChunks.DuckDBDataChunkGetVector(dataChunk, index);

            vectorWriters[index]?.Dispose();
            vectorWriters[index] = VectorDataWriterFactory.CreateWriter(vector, logicalTypes[index]);
        }
    }

    private void AppendDataChunk()
    {
        NativeMethods.DataChunks.DuckDBDataChunkSetSize(dataChunk, rowCount);
        var state = NativeMethods.Appender.DuckDBAppendDataChunk(nativeAppender, dataChunk);

        if (!state.IsSuccess())
        {
            NativeMethods.Appender.DuckDBAppenderErrorData(nativeAppender).ThrowOnError();
        }

        NativeMethods.DataChunks.DuckDBDataChunkReset(dataChunk);
    }

    private void AbortAppendBatch(DuckDBAppenderRow row)
    {
        row.Invalidate();
        isFaulted = true;

        // The native appender cannot roll back only the current row, so discard its whole batch.
        ClearCore();
    }

    private void EnsureNotAppendingRow()
    {
        if (isAppendingRow)
        {
            throw new InvalidOperationException("The appender cannot be used from inside an AppendRow callback");
        }
    }

    private void EnsureUsable()
    {
        EnsureNotAppendingRow();

        if (isFaulted)
        {
            throw new InvalidOperationException("The appender cannot be reused after an AppendRow callback failed");
        }
    }
}
