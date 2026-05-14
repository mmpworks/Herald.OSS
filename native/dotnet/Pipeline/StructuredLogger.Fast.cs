#nullable enable

using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Pipeline;

// -----------------------------------------------------------------------
// Fast tuple-param overloads — the recommended shape for hot-path logging.
//
// Each overload accepts 1–8 (name, value) tuples, fills a stack-allocated
// LogPropertyBufferN inline, and dispatches via the compact kernel path.
// Zero heap allocation at the call site when the pipeline is kernel-
// eligible.
//
// Example:
//
//     _logger.InfoFast(Category,
//         "User {Name} from {City} did {Action} on {Resource}",
//         ("Name", name), ("City", city),
//         ("Action", action), ("Resource", resource));
//
// This replaces the five-line LogPropertyBuffer4 construction pattern the
// user would otherwise write by hand. Level-typed aliases delegate to a
// single LogFast that does the buffer-fill + dispatch so the actual work
// lives once per arity.
//
// Arity coverage: 1, 2, 3, 4, 5, 6, 7, 8 tuples. For larger property
// counts, fall back to InfoCompact with a larger LogPropertyBufferN (16)
// or the LogProperty[] API.
//
// Cognitive-complexity note: these methods intentionally look identical
// across levels. The level constant is the only difference. Splitting by
// level is what keeps the call site one line (`InfoFast` instead of
// `LogFast(Info, ...)`).
// -----------------------------------------------------------------------

public sealed partial class StructuredLogger
{
    // ── Arity 1 ────────────────────────────────────────────────────

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void LogFast(LogLevel level, LogCategory category, string template,
        (string Name, object? Value) p1)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer1();
        buf[0] = new(p1.Name, p1.Value);
        LogCompact(level, category, template, buf);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void InfoFast(LogCategory category, string template,
        (string Name, object? Value) p1) =>
        LogFast(KnownLogLevels.Info, category, template, p1);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void DebugFast(LogCategory category, string template,
        (string Name, object? Value) p1) =>
        LogFast(KnownLogLevels.Debug, category, template, p1);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void WarnFast(LogCategory category, string template,
        (string Name, object? Value) p1) =>
        LogFast(KnownLogLevels.Warn, category, template, p1);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void ErrorFast(LogCategory category, string template,
        (string Name, object? Value) p1) =>
        LogFast(KnownLogLevels.Error, category, template, p1);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void TraceFast(LogCategory category, string template,
        (string Name, object? Value) p1) =>
        LogFast(KnownLogLevels.Trace, category, template, p1);

    // ── Arity 2 ────────────────────────────────────────────────────

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void LogFast(LogLevel level, LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer2();
        buf[0] = new(p1.Name, p1.Value);
        buf[1] = new(p2.Name, p2.Value);
        LogCompact(level, category, template, buf);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void InfoFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2) =>
        LogFast(KnownLogLevels.Info, category, template, p1, p2);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void DebugFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2) =>
        LogFast(KnownLogLevels.Debug, category, template, p1, p2);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void WarnFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2) =>
        LogFast(KnownLogLevels.Warn, category, template, p1, p2);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void ErrorFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2) =>
        LogFast(KnownLogLevels.Error, category, template, p1, p2);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void TraceFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2) =>
        LogFast(KnownLogLevels.Trace, category, template, p1, p2);

    // ── Arity 3 ────────────────────────────────────────────────────

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void LogFast(LogLevel level, LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3)
    {
        if (!IsEnabled(level)) return;
        // Arity 3 uses LogPropertyBuffer4 — no LogPropertyBuffer3 because
        // the power-of-two sizes pack better; InlineArray wastes at most
        // one slot and keeps span semantics clean.
        var buf = new LogPropertyBuffer4();
        buf[0] = new(p1.Name, p1.Value);
        buf[1] = new(p2.Name, p2.Value);
        buf[2] = new(p3.Name, p3.Value);
        System.ReadOnlySpan<LogPropertyCompact> span = ((System.ReadOnlySpan<LogPropertyCompact>)buf)[..3];
        LogCompact(level, category, template, span);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void InfoFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3) =>
        LogFast(KnownLogLevels.Info, category, template, p1, p2, p3);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void DebugFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3) =>
        LogFast(KnownLogLevels.Debug, category, template, p1, p2, p3);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void WarnFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3) =>
        LogFast(KnownLogLevels.Warn, category, template, p1, p2, p3);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void ErrorFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3) =>
        LogFast(KnownLogLevels.Error, category, template, p1, p2, p3);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void TraceFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3) =>
        LogFast(KnownLogLevels.Trace, category, template, p1, p2, p3);

    // ── Arity 4 ────────────────────────────────────────────────────

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void LogFast(LogLevel level, LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer4();
        buf[0] = new(p1.Name, p1.Value);
        buf[1] = new(p2.Name, p2.Value);
        buf[2] = new(p3.Name, p3.Value);
        buf[3] = new(p4.Name, p4.Value);
        LogCompact(level, category, template, buf);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void InfoFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4) =>
        LogFast(KnownLogLevels.Info, category, template, p1, p2, p3, p4);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void DebugFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4) =>
        LogFast(KnownLogLevels.Debug, category, template, p1, p2, p3, p4);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void WarnFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4) =>
        LogFast(KnownLogLevels.Warn, category, template, p1, p2, p3, p4);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void ErrorFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4) =>
        LogFast(KnownLogLevels.Error, category, template, p1, p2, p3, p4);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void TraceFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4) =>
        LogFast(KnownLogLevels.Trace, category, template, p1, p2, p3, p4);

    // ── Arity 5 — 8 buffer, 5 slots used ───────────────────────────

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void LogFast(LogLevel level, LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer8();
        buf[0] = new(p1.Name, p1.Value);
        buf[1] = new(p2.Name, p2.Value);
        buf[2] = new(p3.Name, p3.Value);
        buf[3] = new(p4.Name, p4.Value);
        buf[4] = new(p5.Name, p5.Value);
        System.ReadOnlySpan<LogPropertyCompact> span = ((System.ReadOnlySpan<LogPropertyCompact>)buf)[..5];
        LogCompact(level, category, template, span);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void InfoFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5) =>
        LogFast(KnownLogLevels.Info, category, template, p1, p2, p3, p4, p5);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void DebugFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5) =>
        LogFast(KnownLogLevels.Debug, category, template, p1, p2, p3, p4, p5);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void WarnFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5) =>
        LogFast(KnownLogLevels.Warn, category, template, p1, p2, p3, p4, p5);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void ErrorFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5) =>
        LogFast(KnownLogLevels.Error, category, template, p1, p2, p3, p4, p5);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void TraceFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5) =>
        LogFast(KnownLogLevels.Trace, category, template, p1, p2, p3, p4, p5);

    // ── Arity 6 — 8 buffer, 6 slots used ───────────────────────────

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void LogFast(LogLevel level, LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer8();
        buf[0] = new(p1.Name, p1.Value);
        buf[1] = new(p2.Name, p2.Value);
        buf[2] = new(p3.Name, p3.Value);
        buf[3] = new(p4.Name, p4.Value);
        buf[4] = new(p5.Name, p5.Value);
        buf[5] = new(p6.Name, p6.Value);
        System.ReadOnlySpan<LogPropertyCompact> span = ((System.ReadOnlySpan<LogPropertyCompact>)buf)[..6];
        LogCompact(level, category, template, span);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void InfoFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6) =>
        LogFast(KnownLogLevels.Info, category, template, p1, p2, p3, p4, p5, p6);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void DebugFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6) =>
        LogFast(KnownLogLevels.Debug, category, template, p1, p2, p3, p4, p5, p6);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void WarnFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6) =>
        LogFast(KnownLogLevels.Warn, category, template, p1, p2, p3, p4, p5, p6);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void ErrorFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6) =>
        LogFast(KnownLogLevels.Error, category, template, p1, p2, p3, p4, p5, p6);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void TraceFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6) =>
        LogFast(KnownLogLevels.Trace, category, template, p1, p2, p3, p4, p5, p6);

    // ── Arity 7 — 8 buffer, 7 slots used ───────────────────────────

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void LogFast(LogLevel level, LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer8();
        buf[0] = new(p1.Name, p1.Value);
        buf[1] = new(p2.Name, p2.Value);
        buf[2] = new(p3.Name, p3.Value);
        buf[3] = new(p4.Name, p4.Value);
        buf[4] = new(p5.Name, p5.Value);
        buf[5] = new(p6.Name, p6.Value);
        buf[6] = new(p7.Name, p7.Value);
        System.ReadOnlySpan<LogPropertyCompact> span = ((System.ReadOnlySpan<LogPropertyCompact>)buf)[..7];
        LogCompact(level, category, template, span);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void InfoFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7) =>
        LogFast(KnownLogLevels.Info, category, template, p1, p2, p3, p4, p5, p6, p7);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void DebugFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7) =>
        LogFast(KnownLogLevels.Debug, category, template, p1, p2, p3, p4, p5, p6, p7);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void WarnFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7) =>
        LogFast(KnownLogLevels.Warn, category, template, p1, p2, p3, p4, p5, p6, p7);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void ErrorFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7) =>
        LogFast(KnownLogLevels.Error, category, template, p1, p2, p3, p4, p5, p6, p7);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void TraceFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7) =>
        LogFast(KnownLogLevels.Trace, category, template, p1, p2, p3, p4, p5, p6, p7);

    // ── Arity 8 ────────────────────────────────────────────────────

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void LogFast(LogLevel level, LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7,
        (string Name, object? Value) p8)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer8();
        buf[0] = new(p1.Name, p1.Value);
        buf[1] = new(p2.Name, p2.Value);
        buf[2] = new(p3.Name, p3.Value);
        buf[3] = new(p4.Name, p4.Value);
        buf[4] = new(p5.Name, p5.Value);
        buf[5] = new(p6.Name, p6.Value);
        buf[6] = new(p7.Name, p7.Value);
        buf[7] = new(p8.Name, p8.Value);
        LogCompact(level, category, template, buf);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void InfoFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7,
        (string Name, object? Value) p8) =>
        LogFast(KnownLogLevels.Info, category, template, p1, p2, p3, p4, p5, p6, p7, p8);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void DebugFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7,
        (string Name, object? Value) p8) =>
        LogFast(KnownLogLevels.Debug, category, template, p1, p2, p3, p4, p5, p6, p7, p8);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void WarnFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7,
        (string Name, object? Value) p8) =>
        LogFast(KnownLogLevels.Warn, category, template, p1, p2, p3, p4, p5, p6, p7, p8);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void ErrorFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7,
        (string Name, object? Value) p8) =>
        LogFast(KnownLogLevels.Error, category, template, p1, p2, p3, p4, p5, p6, p7, p8);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void TraceFast(LogCategory category, string template,
        (string Name, object? Value) p1,
        (string Name, object? Value) p2,
        (string Name, object? Value) p3,
        (string Name, object? Value) p4,
        (string Name, object? Value) p5,
        (string Name, object? Value) p6,
        (string Name, object? Value) p7,
        (string Name, object? Value) p8) =>
        LogFast(KnownLogLevels.Trace, category, template, p1, p2, p3, p4, p5, p6, p7, p8);
}
