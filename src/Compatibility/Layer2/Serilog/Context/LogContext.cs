#nullable enable

// CROSS-PLAN GAP — FLAGGED (Task 3)
//
// MMP.Herald.Serilog.Context.LogContext does NOT exist in Layer-1.
//
// Evidence:
//   - find src/Serilog/Context/ → no such directory
//   - grep for "LogContext" in src/Serilog/ → only LoggerEnrichmentConfiguration.FromLogContext()
//     which is itself a no-op placeholder (P1 comment: "No-op. P1 already wires scope/
//     PushProperty ambient capture via the ...").
//   - grep for "AsyncLocal" in src/Serilog/ → no results
//
// What Serilog.Context.LogContext provides (real Serilog):
//   PushProperty(string name, object? value, bool destructureObjects = false) → IDisposable
//   Push(LogEventProperty property) → IDisposable
//   Reset() → IDisposable
//   Suspend() → IDisposable
//   Clone() → IDisposable
//
// These methods rely on AsyncLocal<T>-backed ambient scope enrichment.
// Herald.OSS has no AsyncLocal enrichment primitive in Layer-1 for P1 scope.
//
// Action required (future plan task):
//   Add MMP.Herald.Serilog.Context.LogContext to Layer-1 with a real
//   AsyncLocal-backed ILogEnricher, then update this file to mirror it.
//
// For now this file declares a structural stub so any corpus code that
// references the type at least compiles. All methods throw NotSupportedException
// at runtime until the Layer-1 primitive exists.
//
// Upgrade note for C# 14: a cleaner async-local scope API may simplify
// the ambient enrichment pattern; revisit when the Layer-1 primitive lands.

using System;

namespace Serilog.Context;

/// <summary>
/// Ambient log-context scope enrichment — structural stub.
///
/// <para>
/// <b>Not yet implemented.</b> <c>MMP.Herald.Serilog.Context.LogContext</c>
/// does not exist in Layer-1 for P1 scope. All methods throw
/// <see cref="NotSupportedException"/> at runtime. See the file header for
/// the cross-plan gap description and upgrade path.
/// </para>
///
/// <para>
/// This stub exists so corpus code that references <c>Serilog.Context.LogContext</c>
/// compiles against the Layer-2 assembly. No runtime behaviour is provided.
/// </para>
/// </summary>
public static class LogContext
{
    private const string GapMessage =
        "Serilog.Context.LogContext is not yet implemented. " +
        "MMP.Herald.Serilog.Context.LogContext does not exist in Layer-1 (P1 scope). " +
        "See src/Compatibility/Layer2/Serilog/Context/LogContext.cs for the upgrade path.";

    /// <summary>
    /// Not implemented. See file header for the cross-plan gap description.
    /// </summary>
    public static IDisposable PushProperty(string name, object? value, bool destructureObjects = false)
        => throw new NotSupportedException(GapMessage);

    /// <summary>
    /// Not implemented. See file header for the cross-plan gap description.
    /// </summary>
    public static IDisposable Reset()
        => throw new NotSupportedException(GapMessage);

    /// <summary>
    /// Not implemented. See file header for the cross-plan gap description.
    /// </summary>
    public static IDisposable Suspend()
        => throw new NotSupportedException(GapMessage);

    /// <summary>
    /// Not implemented. See file header for the cross-plan gap description.
    /// </summary>
    public static IDisposable Clone()
        => throw new NotSupportedException(GapMessage);
}
