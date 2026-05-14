#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Stores nested logging scopes in AsyncLocal so they flow with the logical call path.
///
/// Hot-path allocation note:
/// every accepted log call inside a scope asks for the merged context.
/// The merge walks parent-to-child and previously allocated a fresh
/// Dictionary and a List every call. Each innermost ScopeNode now caches
/// its merged view on first request, so the second and later log calls
/// inside the same scope reuse that snapshot without re-walking.
/// The cache lives on the node and dies with it on scope dispose, so
/// stale reads cannot outlive the scope that produced them.
/// </summary>
public sealed class AsyncLocalLogScopeProvider : ILogScopeProvider
{
    private readonly AsyncLocal<ScopeNode?> _current = new();

    public ILogScope BeginScope(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var parent = _current.Value;
        var node = new ScopeNode(parent, values);
        _current.Value = node;

        return new Scope(this, node);
    }

    public IReadOnlyDictionary<string, object?> GetMergedContext()
    {
        // Fast path — no active scope. Skip every allocation and the walk.
        // This is the common case on every accepted log call for a logger
        // that never calls BeginScope.
        var innermost = _current.Value;
        return innermost is null
            ? MMP.Herald.Events.LogEvent.EmptyContext
            : innermost.GetMerged();
    }

    private void EndScope(ScopeNode node)
    {
        if (!ReferenceEquals(_current.Value, node))
        {
            return;
        }

        _current.Value = node.Parent;
    }

    private sealed class Scope : ILogScope
    {
        private readonly AsyncLocalLogScopeProvider _owner;
        private readonly ScopeNode _node;
        private bool _isDisposed;

        public Scope(AsyncLocalLogScopeProvider owner, ScopeNode node)
        {
            _owner = owner;
            _node = node;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _owner.EndScope(_node);
        }
    }

    // Sealed class (not record) so we can hold a mutable merge cache
    // without it participating in equality.
    private sealed class ScopeNode
    {
        public ScopeNode? Parent { get; }
        public IReadOnlyDictionary<string, object?> Values { get; }

        // Volatile publication of the merged snapshot. Benign race under
        // AsyncLocal capture across Task continuations: two threads may
        // compute identical snapshots; whichever assigns last wins, and
        // readers either see null (rebuild) or a fully-constructed map.
        private volatile IReadOnlyDictionary<string, object?>? _merged;

        public ScopeNode(ScopeNode? parent, IReadOnlyDictionary<string, object?> values)
        {
            Parent = parent;
            Values = values;
        }

        public IReadOnlyDictionary<string, object?> GetMerged()
        {
            var cached = _merged;
            if (cached is not null)
            {
                return cached;
            }

            var built = BuildMerged();
            _merged = built;
            return built;
        }

        private IReadOnlyDictionary<string, object?> BuildMerged()
        {
            // Single-scope case: no merge work. Return Values directly so
            // the common BeginScope pattern avoids the Dictionary allocation
            // entirely when no parent exists.
            if (Parent is null)
            {
                return Values;
            }

            // Multi-scope case: walk once, merge outer-first so inner values
            // override. Allocation here is paid once per scope node and
            // amortized across every log call inside that scope.
            var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
            MergeChain(this, merged);
            return merged;
        }

        // Recursive walk is simpler than building a List just to reverse it.
        // Depth is bounded by the user's BeginScope nesting, which is shallow
        // in practice; recursion stays well below stack pressure.
        private static void MergeChain(ScopeNode node, Dictionary<string, object?> target)
        {
            if (node.Parent is not null)
            {
                MergeChain(node.Parent, target);
            }

            foreach (var pair in node.Values)
            {
                target[pair.Key] = pair.Value;
            }
        }
    }
}
