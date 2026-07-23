using DuckDB.NET.Data.Connection;

namespace DuckDB.NET.Data.PreparedStatement;

internal class PreparedStatement : IDisposable
{
    protected readonly DuckDBPreparedStatement Statement;

    internal PreparedStatement(DuckDBPreparedStatement statement)
    {
        Statement = statement;
    }

    protected virtual bool RequiresClearBindings => false;

    protected virtual long ParameterCount => NativeMethods.PreparedStatements.DuckDBParams(Statement);

    public static IEnumerable<DuckDBResult> PrepareMultiple(DuckDBNativeConnection connection, string query, DuckDBParameterCollection parameters, bool useStreamingMode)
    {
        var statementCount = NativeMethods.ExtractStatements.DuckDBExtractStatements(connection, query, out var extractedStatements);

        using (extractedStatements)
        {
            if (statementCount <= 0)
            {
                var error = NativeMethods.ExtractStatements.DuckDBExtractStatementsError(extractedStatements);
                throw new DuckDBException(error);
            }

            for (int index = 0; index < statementCount; index++)
            {
                var status = NativeMethods.ExtractStatements.DuckDBPrepareExtractedStatement(connection, extractedStatements, index, out var statement);

                if (status.IsSuccess())
                {
                    using var preparedStatement = new PreparedStatement(statement);
                    yield return preparedStatement.Execute(parameters, useStreamingMode, connection);
                }
                else
                {
                    var errorMessage = NativeMethods.PreparedStatements.DuckDBPrepareError(statement);
                    statement.Dispose();

                    if (string.IsNullOrEmpty(errorMessage))
                    {
                        errorMessage = "DuckDBQuery failed";
                    }

                    throw new DuckDBException(errorMessage, UdfExceptionStore.Retrieve(connection));
                }
            }
        }
    }

    public static ReusablePreparedStatement? TryPrepareReusable(DuckDBNativeConnection connection, string query)
    {
        var statementCount = NativeMethods.ExtractStatements.DuckDBExtractStatements(connection, query, out var extractedStatements);

        using (extractedStatements)
        {
            if (statementCount <= 0)
            {
                var error = NativeMethods.ExtractStatements.DuckDBExtractStatementsError(extractedStatements);
                throw new DuckDBException(error);
            }

            // DuckDB can expand one logical command into dependent statements. Dynamic PIVOT,
            // IMPORT and some PRAGMAs require earlier statements to execute before later ones can
            // be prepared. Keep those commands on the existing per-execution path.
            if (statementCount != 1)
            {
                return null;
            }

            var status = NativeMethods.ExtractStatements.DuckDBPrepareExtractedStatement(connection, extractedStatements, 0, out var statement);
            if (status.IsSuccess())
            {
                return new ReusablePreparedStatement(statement);
            }

            var errorMessage = NativeMethods.PreparedStatements.DuckDBPrepareError(statement);
            statement.Dispose();

            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "DuckDBQuery failed";
            }

            throw new DuckDBException(errorMessage, UdfExceptionStore.Retrieve(connection));
        }
    }

    internal DuckDBResult Execute(DuckDBParameterCollection parameterCollection, bool useStreamingMode, DuckDBNativeConnection connection)
    {
        if (RequiresClearBindings)
        {
            var clearState = NativeMethods.PreparedStatements.DuckDBClearBindings(Statement);
            if (!clearState.IsSuccess())
            {
                var errorMessage = NativeMethods.PreparedStatements.DuckDBPrepareError(Statement);
                throw new InvalidOperationException($"Unable to clear prepared statement bindings: {errorMessage}");
            }
        }

        BindParameters(parameterCollection);

        var status = useStreamingMode
            ? NativeMethods.PreparedStatements.DuckDBExecutePreparedStreaming(Statement, out var queryResult)
            : NativeMethods.PreparedStatements.DuckDBExecutePrepared(Statement, out queryResult);

        if (!status.IsSuccess())
        {
            var errorMessage = NativeMethods.Query.DuckDBResultError(ref queryResult);
            var errorType = NativeMethods.Query.DuckDBResultErrorType(ref queryResult);
            queryResult.Close();

            if (string.IsNullOrEmpty(errorMessage))
            {
                errorMessage = "DuckDB execution failed";
            }

            if (errorType == DuckDBErrorType.Interrupt)
            {
                throw new OperationCanceledException();
            }

            var innerException = UdfExceptionStore.Retrieve(connection);
            throw innerException != null
                ? new DuckDBException(errorMessage, innerException)
                : new DuckDBException(errorMessage, errorType);
        }

        return queryResult;
    }

    protected virtual void BindParameters(DuckDBParameterCollection parameterCollection)
    {
        var expectedParameters = ValidateParameterCount(parameterCollection);

        // Index-based iteration over the typed collection avoids the per-execution allocations of
        // OfType<>().Any(...) and of the boxed List enumerator that `foreach (DuckDBParameter ...)`
        // over the non-generic collection would produce. BindParameters runs on every execution.
        var count = parameterCollection.Count;

        var hasNamedParameters = HasNamedParameters(parameterCollection);

        if (hasNamedParameters)
        {
            for (var i = 0; i < count; i++)
            {
                var param = parameterCollection[i];
                if (TryGetParameterIndex(param.ParameterName, out var index))
                {
                    BindParameter(index, param);
                }
            }
        }
        else
        {
            for (var i = 0; i < expectedParameters; ++i)
            {
                var param = parameterCollection[i];
                BindParameter(i + 1, param);
            }
        }
    }

    protected virtual bool TryGetParameterIndex(string parameterName, out long index)
    {
        if (string.IsNullOrEmpty(parameterName))
        {
            index = 0;
            return false;
        }

        var state = NativeMethods.PreparedStatements.DuckDBBindParameterIndex(Statement, out var nativeIndex, parameterName);
        index = state.IsSuccess() ? nativeIndex : 0;

        return index > 0;
    }

    protected virtual void BindParameter(long index, DuckDBParameter parameter)
    {
        using var parameterLogicalType = NativeMethods.PreparedStatements.DuckDBParamLogicalType(Statement, index);
        BindParameter(index, parameter, parameterLogicalType);
    }

    protected void BindParameter(long index, DuckDBParameter parameter, DuckDBLogicalType parameterLogicalType)
    {
        var duckDBType = NativeMethods.LogicalType.DuckDBGetTypeId(parameterLogicalType);

        DuckDBState result;
        if (!parameter.TryBindScalarValue(Statement, index, duckDBType, out result))
        {
            using var duckDBValue = parameter.ToDuckDBValue(parameterLogicalType, duckDBType);
            result = NativeMethods.PreparedStatements.DuckDBBindValue(Statement, index, duckDBValue);
        }

        if (!result.IsSuccess())
        {
            var errorMessage = NativeMethods.PreparedStatements.DuckDBPrepareError(Statement);
            throw new InvalidOperationException($"Unable to bind parameter {index}: {errorMessage}");
        }
    }

    protected long ValidateParameterCount(DuckDBParameterCollection parameterCollection)
    {
        var expectedParameters = ParameterCount;
        if (parameterCollection.Count < expectedParameters)
        {
            throw new InvalidOperationException(
                $"Invalid number of parameters. Expected {expectedParameters}, got {parameterCollection.Count}");
        }

        return expectedParameters;
    }

    protected static bool HasNamedParameters(DuckDBParameterCollection parameterCollection)
    {
        for (var index = 0; index < parameterCollection.Count; index++)
        {
            if (!string.IsNullOrEmpty(parameterCollection[index].ParameterName))
            {
                return true;
            }
        }

        return false;
    }

    public virtual void Dispose()
    {
        Statement.Dispose();
    }
}

internal sealed class ReusablePreparedStatement : PreparedStatement
{
    private readonly long cachedParameterCount;
    private readonly DuckDBLogicalType[] cachedParameterTypes;
    private readonly Dictionary<string, long> cachedParameterIndices = new(StringComparer.Ordinal);
    private DuckDBParameterCollection? cachedParameterCollection;
    private int cachedParameterCollectionVersion = -1;
    private int[]? cachedParameterMetadataVersions;
    private ParameterBinding[]? cachedBindingPlan;

    public ReusablePreparedStatement(DuckDBPreparedStatement statement)
        : base(statement)
    {
        cachedParameterCount = NativeMethods.PreparedStatements.DuckDBParams(statement);
        cachedParameterTypes = new DuckDBLogicalType[cachedParameterCount];

        try
        {
            for (var index = 0; index < cachedParameterTypes.Length; index++)
            {
                cachedParameterTypes[index] = NativeMethods.PreparedStatements.DuckDBParamLogicalType(statement, index + 1);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    protected override bool RequiresClearBindings => true;

    protected override long ParameterCount => cachedParameterCount;

    protected override bool TryGetParameterIndex(string parameterName, out long index)
    {
        if (cachedParameterIndices.TryGetValue(parameterName, out index))
        {
            return index > 0;
        }

        var found = base.TryGetParameterIndex(parameterName, out index);
        cachedParameterIndices.Add(parameterName, index);

        return found;
    }

    protected override void BindParameters(DuckDBParameterCollection parameterCollection)
    {
        var bindingPlan = GetOrCreateBindingPlan(parameterCollection);

        for (var index = 0; index < bindingPlan.Length; index++)
        {
            var binding = bindingPlan[index];
            BindParameter(binding.Index, binding.Parameter);
        }
    }

    protected override void BindParameter(long index, DuckDBParameter parameter)
    {
        BindParameter(index, parameter, cachedParameterTypes[index - 1]);
    }

    private ParameterBinding[] GetOrCreateBindingPlan(DuckDBParameterCollection parameterCollection)
    {
        if (IsBindingPlanCurrent(parameterCollection))
        {
            return cachedBindingPlan!;
        }

        var expectedParameters = ValidateParameterCount(parameterCollection);
        var hasNamedParameters = HasNamedParameters(parameterCollection);
        var plan = new ParameterBinding[hasNamedParameters
            ? parameterCollection.Count
            : checked((int)expectedParameters)];
        var bindingCount = 0;

        if (hasNamedParameters)
        {
            for (var parameterIndex = 0; parameterIndex < parameterCollection.Count; parameterIndex++)
            {
                var parameter = parameterCollection[parameterIndex];
                if (TryGetParameterIndex(parameter.ParameterName, out var nativeIndex))
                {
                    plan[bindingCount++] = new ParameterBinding(nativeIndex, parameter);
                }
            }

            if (bindingCount != plan.Length)
            {
                Array.Resize(ref plan, bindingCount);
            }
        }
        else
        {
            for (var parameterIndex = 0; parameterIndex < expectedParameters; parameterIndex++)
            {
                plan[bindingCount++] = new ParameterBinding(
                    parameterIndex + 1,
                    parameterCollection[parameterIndex]);
            }
        }

        cachedParameterCollection = parameterCollection;
        cachedParameterCollectionVersion = parameterCollection.Version;
        cachedParameterMetadataVersions = new int[parameterCollection.Count];
        for (var index = 0; index < cachedParameterMetadataVersions.Length; index++)
        {
            cachedParameterMetadataVersions[index] = parameterCollection[index].BindingMetadataVersion;
        }

        cachedBindingPlan = plan;
        return plan;
    }

    private bool IsBindingPlanCurrent(DuckDBParameterCollection parameterCollection)
    {
        if (!ReferenceEquals(cachedParameterCollection, parameterCollection) ||
            cachedParameterCollectionVersion != parameterCollection.Version ||
            cachedBindingPlan is null ||
            cachedParameterMetadataVersions is null ||
            cachedParameterMetadataVersions.Length != parameterCollection.Count)
        {
            return false;
        }

        for (var index = 0; index < cachedParameterMetadataVersions.Length; index++)
        {
            if (cachedParameterMetadataVersions[index] != parameterCollection[index].BindingMetadataVersion)
            {
                return false;
            }
        }

        return true;
    }

    public override void Dispose()
    {
        foreach (var parameterType in cachedParameterTypes)
        {
            parameterType?.Dispose();
        }

        base.Dispose();
    }

    private readonly record struct ParameterBinding(long Index, DuckDBParameter Parameter);
}
