#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Output.Aliases;

namespace MMP.Herald.Output.Rendering;
/// <summary>
/// Mutable registry for output transformers.
/// </summary>
public sealed class LogOutputTransformerRegistry : ILogOutputTransformerRegistry
{
    private readonly Dictionary<string, ILogOutputTransformer> _transformers =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(LogOutputAlias alias, ILogOutputTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(transformer);

        _transformers[alias.Key] = transformer;
    }

    public ILogOutputTransformer Get(LogOutputAlias alias)
    {
        ArgumentNullException.ThrowIfNull(alias);

        if (_transformers.TryGetValue(alias.Key, out var transformer))
        {
            return transformer;
        }

        throw new KeyNotFoundException(
            $"No output transformer is registered for alias '{alias.Key}'.");
    }
}