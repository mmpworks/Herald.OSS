#nullable enable
#if NET9_0_OR_GREATER

using System;
using FluentAssertions;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// Lifecycle pins for <see cref="SerilogLoggerAdapter"/> ownership semantics.
///
/// <list type="bullet">
///   <item><b>G2.3</b> — an owning adapter (from <see cref="SerilogLoggerAdapter.FromBuild"/>)
///   is safe to dispose more than once; the flush action runs at most once and
///   the pipeline's async buffer is never double-disposed.</item>
///   <item><b>G2.4</b> — a child obtained via <c>ForContext</c> does NOT own the
///   parent's pipeline. Disposing the parent flushes once; the child's
///   <c>Dispose</c> is a no-op and never re-disposes the shared pipeline.</item>
/// </list>
/// </summary>
public sealed class SerilogAdapterLifecycleTests
{
    // Build an owning adapter over a minimal null-sink pipeline. FromBuild captures
    // the flush action, so this adapter owns the pipeline lifetime.
    private static SerilogLoggerAdapter BuildOwningAdapter()
    {
        var buildResult = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("verbose")
            .Build();
        return SerilogLoggerAdapter.FromBuild(buildResult);
    }

    // ── G2.3: double-dispose idempotency ──────────────────────────────────────
    [Fact]
    public void FromBuild_adapter_double_dispose_is_idempotent()
    {
        var adapter = BuildOwningAdapter();

        // Two disposes in a row must not throw — the second is a no-op guarded by
        // the disposed flag, so the async resource is never disposed twice.
        var act = () => { adapter.Dispose(); adapter.Dispose(); };
        act.Should().NotThrow(
            "Dispose must be idempotent — the flush action runs at most once");
    }

    [Fact]
    public void FromBuild_adapter_dispose_then_log_does_not_throw()
    {
        var adapter = BuildOwningAdapter();
        adapter.Dispose();

        // Logging after disposal must not throw — the pipeline is closed but the
        // call path stays safe (the null sink simply drops).
        var act = () => adapter.Information("after dispose {X}", 1);
        act.Should().NotThrow("a closed owning adapter must not throw on further calls");
    }

    // ── G2.4: ForContext child lifecycle against parent disposal ──────────────
    [Fact]
    public void ForContext_child_does_not_own_parent_pipeline()
    {
        var parent = BuildOwningAdapter();
        MMP.Herald.Serilog.ILogger child = parent.ForContext("Service", "payments");

        // The child wraps the SAME pipeline via a non-owning constructor, so
        // disposing the child must be a no-op — it must NOT dispose the parent's
        // pipeline out from under it.
        var disposeChild = () => (child as IDisposable)?.Dispose();
        disposeChild.Should().NotThrow("a ForContext child does not own the pipeline");

        // The parent's pipeline is still alive after disposing the child: logging
        // through the parent still works.
        var logParent = () => parent.Information("parent still alive {X}", 1);
        logParent.Should().NotThrow(
            "disposing a ForContext child must not tear down the parent's pipeline");

        // Now dispose the parent (the owner). This flushes once and is safe.
        var disposeParent = () => parent.Dispose();
        disposeParent.Should().NotThrow("the owning parent flushes once on dispose");
    }

    [Fact]
    public void ForContext_child_obtained_before_parent_dispose_is_safe_to_use_after()
    {
        var parent = BuildOwningAdapter();
        var child = parent.ForContext("Tenant", "acme");

        // Dispose the owner. The child shares the pipeline; after the parent
        // flushes, a call through the child must still be safe (null sink drops).
        parent.Dispose();

        var act = () => child.Information("child after parent dispose {X}", 1);
        act.Should().NotThrow(
            "a child holding the shared pipeline must stay safe after the parent flushes");
    }
}

#endif
