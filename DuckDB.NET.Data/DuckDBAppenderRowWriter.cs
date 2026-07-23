using System.Runtime.CompilerServices;

namespace DuckDB.NET.Data;

/// <summary>
/// A stack-only writer for a single appender row.
/// </summary>
/// <remarks>
/// Instances are valid only for the duration of a
/// <see cref="DuckDBAppender.AppendRowScoped{TState}(TState, DuckDBAppenderRowWriterAction{TState})"/>
/// callback. The stack-only type cannot be boxed, captured, or stored on the managed heap.
/// Row completion is performed by the appender after the callback returns.
/// </remarks>
public ref struct DuckDBAppenderRowWriter
{
    private readonly DuckDBAppenderRow row;

    internal DuckDBAppenderRowWriter(DuckDBAppenderRow row)
    {
        this.row = row;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendNullValue() => row.AppendNullValue();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(bool? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(byte[]? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(Span<byte> value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(string? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(decimal? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(Guid? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(BigInteger? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(sbyte? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(short? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(int? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(long? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(byte? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(ushort? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(uint? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(ulong? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue<TEnum>(TEnum? value) where TEnum : Enum => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(float? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(double? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(DateOnly? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(TimeOnly? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(DuckDBDateOnly? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(DuckDBTimeOnly? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(DateTime? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(DateTimeOffset? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue(TimeSpan? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendValue<T>(IEnumerable<T>? value) => row.AppendValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendDefault() => row.AppendDefault();
}

/// <summary>
/// Writes one complete row through a stack-only <see cref="DuckDBAppenderRowWriter"/>.
/// </summary>
public delegate void DuckDBAppenderRowWriterAction<TState>(ref DuckDBAppenderRowWriter row, TState state);
