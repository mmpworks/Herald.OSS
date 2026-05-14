#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Disposes multiple async resources in reverse registration order.
/// </summary>
public sealed class AsyncResourceGroup : IAsyncDisposable
{
    private readonly IReadOnlyList<IAsyncDisposable> _resources;

    public AsyncResourceGroup(IReadOnlyList<IAsyncDisposable> resources)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception>? exceptions = null;

        for (var index = _resources.Count - 1; index >= 0; index -= 1)
        {
            try
            {
                await _resources[index].DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException(exceptions);
        }
    }
}