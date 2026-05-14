#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Manages ambient logging scopes and exposes the merged scope context for event creation.
/// </summary>
public interface ILogScopeProvider
{
    ILogScope BeginScope(IReadOnlyDictionary<string, object?> values);

    IReadOnlyDictionary<string, object?> GetMergedContext();
}