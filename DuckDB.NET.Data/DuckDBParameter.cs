using DuckDB.NET.Data.PreparedStatement;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace DuckDB.NET.Data;

public class DuckDBParameter : DbParameter
{
    private const DbType DefaultDbType = DbType.String;

    private object? value;
    private string parameterName = string.Empty;
    private int bindingMetadataVersion;

    public override DbType DbType { get; set; }

    [AllowNull]
    [DefaultValue("")]
    public override string ParameterName
    {
        get => parameterName;
        set
        {
            var newValue = value ?? string.Empty;
            if (string.Equals(parameterName, newValue, StringComparison.Ordinal))
            {
                return;
            }

            parameterName = newValue;
            bindingMetadataVersion++;
        }
    }

    public override object? Value
    {
        get => value;
        set
        {
            if (this.value != value)
            {
                this.value = value;
                DbType = DuckDBTypeMap.GetDbTypeForValue(value);
            }
        }
    }

    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }

    [AllowNull]
    [DefaultValue("")]
    public override string SourceColumn { get; set; }
    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }

    internal int BindingMetadataVersion => bindingMetadataVersion;

    public DuckDBParameter()
        : this (string.Empty, DefaultDbType, null)
    { }

    public DuckDBParameter(object value) 
        : this(string.Empty, DuckDBTypeMap.GetDbTypeForValue(value), value)
    {
    }

    public DuckDBParameter(DbType type, object? value) 
        : this(string.Empty, type, value)
    {
    }

    public DuckDBParameter(string name, object? value) 
        : this(name, DuckDBTypeMap.GetDbTypeForValue(value), value)
    {
    }

    public DuckDBParameter(string name, DbType type, object? value)
    {
        DbType = type;
        ParameterName = name;
        this.value = value;
        SourceColumn = string.Empty;
    }

    public override void ResetDbType() => DbType = DefaultDbType;

    internal virtual bool TryBindScalarValue(
        DuckDBPreparedStatement statement,
        long index,
        DuckDBType duckDBType,
        out DuckDBState result)
        => value.TryBindScalarValue(statement, index, duckDBType, DbType, out result);

    internal virtual DuckDBValue ToDuckDBValue(DuckDBLogicalType logicalType, DuckDBType duckDBType)
        => value.ToDuckDBValue(logicalType, duckDBType, DbType);
}

/// <summary>
/// A DuckDB parameter that keeps its value in typed storage so repeated prepared executions do
/// not need to box common value types.
/// </summary>
/// <typeparam name="T">The CLR type stored by the parameter.</typeparam>
public sealed class DuckDBParameter<T> : DuckDBParameter
{
    private static readonly DbType DefaultTypedDbType = DuckDBTypeMap.GetDbTypeForType(typeof(T));
    private T typedValue;

    /// <summary>
    /// Gets or sets the strongly typed parameter value.
    /// </summary>
    public T TypedValue
    {
        get => typedValue;
        set => typedValue = value;
    }

    public override object? Value
    {
        get => typedValue;
        set
        {
            if (value is T typed)
            {
                typedValue = typed;
                return;
            }

            if (value is null && default(T) is null)
            {
                typedValue = default!;
                return;
            }

            throw new InvalidCastException(
                $"Parameter '{ParameterName}' requires a value assignable to {typeof(T).Name}.");
        }
    }

    public DuckDBParameter(T value)
        : this(string.Empty, value)
    {
    }

    public DuckDBParameter(string name, T value)
        : base(name, DefaultTypedDbType, null)
    {
        typedValue = value;
    }

    public override void ResetDbType() => DbType = DefaultTypedDbType;

    internal override bool TryBindScalarValue(
        DuckDBPreparedStatement statement,
        long index,
        DuckDBType duckDBType,
        out DuckDBState result)
    {
        if (ClrToDuckDBConverter.TryBindTypedScalarValue(typedValue, statement, index, duckDBType, out result))
        {
            return true;
        }

        return ((object?)typedValue).TryBindScalarValue(statement, index, duckDBType, DbType, out result);
    }

    internal override DuckDBValue ToDuckDBValue(DuckDBLogicalType logicalType, DuckDBType duckDBType)
        => ((object?)typedValue).ToDuckDBValue(logicalType, duckDBType, DbType);
}
