// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
//
// Per-arity entry points used by the interceptor generator (V1.1 forward-compat
// seam R1). Today these are no-op forwarders — buffer construction moves from
// the caller to the callee, the public LogCompact(span) path takes the event
// from there. Behaviour is identical to constructing the buffer in the caller
// and calling LogCompact directly.
//
// V1.1's interceptor generator will target these methods to skip caller-side
// buffer + span construction; the JIT can specialise the per-arity entry
// through the typed args without having to inline a generic-buffer-construction
// site at every call.
//
// Sizing rule: arities 1..4 use LogPropertyBuffer4; 5..8 use LogPropertyBuffer8.
// Two slot sizes cover every emitted lane today and leave the door open for
// 9..16 (LogPropertyBuffer16) without churning the API. The Slice(0, N) on
// the implicit Span conversion is what lets a smaller arity reuse a larger
// buffer without dragging unused defaulted slots into the dispatch.

#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Pipeline;

public sealed partial class StructuredLogger
{
    /// <summary>
    /// Per-arity entry point used by the interceptor generator. Skips
    /// caller-side buffer construction by materializing the appropriately-
    /// sized LogPropertyBuffer internally. Equivalent to constructing the
    /// buffer in the caller and calling LogCompact(span); the internal
    /// construction lets the JIT specialize through the typed args.
    /// </summary>
    internal void LogCompact1<T1>(
        LogLevel level, LogCategory category, string template,
        string name1, T1 arg1)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer4();
        buf[0] = LogPropertyCompact.From(name1, arg1);
        var span = ((Span<LogPropertyCompact>)buf).Slice(0, 1);
        LogCompact(level, category, template, span);
    }

    /// <summary>
    /// Per-arity entry point used by the interceptor generator. Skips
    /// caller-side buffer construction by materializing the appropriately-
    /// sized LogPropertyBuffer internally. Equivalent to constructing the
    /// buffer in the caller and calling LogCompact(span); the internal
    /// construction lets the JIT specialize through the typed args.
    /// </summary>
    internal void LogCompact2<T1, T2>(
        LogLevel level, LogCategory category, string template,
        string name1, T1 arg1,
        string name2, T2 arg2)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer4();
        buf[0] = LogPropertyCompact.From(name1, arg1);
        buf[1] = LogPropertyCompact.From(name2, arg2);
        var span = ((Span<LogPropertyCompact>)buf).Slice(0, 2);
        LogCompact(level, category, template, span);
    }

    /// <summary>
    /// Per-arity entry point used by the interceptor generator. Skips
    /// caller-side buffer construction by materializing the appropriately-
    /// sized LogPropertyBuffer internally. Equivalent to constructing the
    /// buffer in the caller and calling LogCompact(span); the internal
    /// construction lets the JIT specialize through the typed args.
    /// </summary>
    internal void LogCompact3<T1, T2, T3>(
        LogLevel level, LogCategory category, string template,
        string name1, T1 arg1,
        string name2, T2 arg2,
        string name3, T3 arg3)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer4();
        buf[0] = LogPropertyCompact.From(name1, arg1);
        buf[1] = LogPropertyCompact.From(name2, arg2);
        buf[2] = LogPropertyCompact.From(name3, arg3);
        var span = ((Span<LogPropertyCompact>)buf).Slice(0, 3);
        LogCompact(level, category, template, span);
    }

    /// <summary>
    /// Per-arity entry point used by the interceptor generator. Skips
    /// caller-side buffer construction by materializing the appropriately-
    /// sized LogPropertyBuffer internally. Equivalent to constructing the
    /// buffer in the caller and calling LogCompact(span); the internal
    /// construction lets the JIT specialize through the typed args.
    /// </summary>
    internal void LogCompact4<T1, T2, T3, T4>(
        LogLevel level, LogCategory category, string template,
        string name1, T1 arg1,
        string name2, T2 arg2,
        string name3, T3 arg3,
        string name4, T4 arg4)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer4();
        buf[0] = LogPropertyCompact.From(name1, arg1);
        buf[1] = LogPropertyCompact.From(name2, arg2);
        buf[2] = LogPropertyCompact.From(name3, arg3);
        buf[3] = LogPropertyCompact.From(name4, arg4);
        LogCompact(level, category, template, buf);
    }

    /// <summary>
    /// Per-arity entry point used by the interceptor generator. Skips
    /// caller-side buffer construction by materializing the appropriately-
    /// sized LogPropertyBuffer internally. Equivalent to constructing the
    /// buffer in the caller and calling LogCompact(span); the internal
    /// construction lets the JIT specialize through the typed args.
    /// </summary>
    internal void LogCompact5<T1, T2, T3, T4, T5>(
        LogLevel level, LogCategory category, string template,
        string name1, T1 arg1,
        string name2, T2 arg2,
        string name3, T3 arg3,
        string name4, T4 arg4,
        string name5, T5 arg5)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer8();
        buf[0] = LogPropertyCompact.From(name1, arg1);
        buf[1] = LogPropertyCompact.From(name2, arg2);
        buf[2] = LogPropertyCompact.From(name3, arg3);
        buf[3] = LogPropertyCompact.From(name4, arg4);
        buf[4] = LogPropertyCompact.From(name5, arg5);
        var span = ((Span<LogPropertyCompact>)buf).Slice(0, 5);
        LogCompact(level, category, template, span);
    }

    /// <summary>
    /// Per-arity entry point used by the interceptor generator. Skips
    /// caller-side buffer construction by materializing the appropriately-
    /// sized LogPropertyBuffer internally. Equivalent to constructing the
    /// buffer in the caller and calling LogCompact(span); the internal
    /// construction lets the JIT specialize through the typed args.
    /// </summary>
    internal void LogCompact6<T1, T2, T3, T4, T5, T6>(
        LogLevel level, LogCategory category, string template,
        string name1, T1 arg1,
        string name2, T2 arg2,
        string name3, T3 arg3,
        string name4, T4 arg4,
        string name5, T5 arg5,
        string name6, T6 arg6)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer8();
        buf[0] = LogPropertyCompact.From(name1, arg1);
        buf[1] = LogPropertyCompact.From(name2, arg2);
        buf[2] = LogPropertyCompact.From(name3, arg3);
        buf[3] = LogPropertyCompact.From(name4, arg4);
        buf[4] = LogPropertyCompact.From(name5, arg5);
        buf[5] = LogPropertyCompact.From(name6, arg6);
        var span = ((Span<LogPropertyCompact>)buf).Slice(0, 6);
        LogCompact(level, category, template, span);
    }

    /// <summary>
    /// Per-arity entry point used by the interceptor generator. Skips
    /// caller-side buffer construction by materializing the appropriately-
    /// sized LogPropertyBuffer internally. Equivalent to constructing the
    /// buffer in the caller and calling LogCompact(span); the internal
    /// construction lets the JIT specialize through the typed args.
    /// </summary>
    internal void LogCompact7<T1, T2, T3, T4, T5, T6, T7>(
        LogLevel level, LogCategory category, string template,
        string name1, T1 arg1,
        string name2, T2 arg2,
        string name3, T3 arg3,
        string name4, T4 arg4,
        string name5, T5 arg5,
        string name6, T6 arg6,
        string name7, T7 arg7)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer8();
        buf[0] = LogPropertyCompact.From(name1, arg1);
        buf[1] = LogPropertyCompact.From(name2, arg2);
        buf[2] = LogPropertyCompact.From(name3, arg3);
        buf[3] = LogPropertyCompact.From(name4, arg4);
        buf[4] = LogPropertyCompact.From(name5, arg5);
        buf[5] = LogPropertyCompact.From(name6, arg6);
        buf[6] = LogPropertyCompact.From(name7, arg7);
        var span = ((Span<LogPropertyCompact>)buf).Slice(0, 7);
        LogCompact(level, category, template, span);
    }

    /// <summary>
    /// Per-arity entry point used by the interceptor generator. Skips
    /// caller-side buffer construction by materializing the appropriately-
    /// sized LogPropertyBuffer internally. Equivalent to constructing the
    /// buffer in the caller and calling LogCompact(span); the internal
    /// construction lets the JIT specialize through the typed args.
    /// </summary>
    internal void LogCompact8<T1, T2, T3, T4, T5, T6, T7, T8>(
        LogLevel level, LogCategory category, string template,
        string name1, T1 arg1,
        string name2, T2 arg2,
        string name3, T3 arg3,
        string name4, T4 arg4,
        string name5, T5 arg5,
        string name6, T6 arg6,
        string name7, T7 arg7,
        string name8, T8 arg8)
    {
        if (!IsEnabled(level)) return;
        var buf = new LogPropertyBuffer8();
        buf[0] = LogPropertyCompact.From(name1, arg1);
        buf[1] = LogPropertyCompact.From(name2, arg2);
        buf[2] = LogPropertyCompact.From(name3, arg3);
        buf[3] = LogPropertyCompact.From(name4, arg4);
        buf[4] = LogPropertyCompact.From(name5, arg5);
        buf[5] = LogPropertyCompact.From(name6, arg6);
        buf[6] = LogPropertyCompact.From(name7, arg7);
        buf[7] = LogPropertyCompact.From(name8, arg8);
        LogCompact(level, category, template, buf);
    }
}
