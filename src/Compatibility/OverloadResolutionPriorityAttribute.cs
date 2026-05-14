// Polyfill for System.Runtime.CompilerServices.OverloadResolutionPriorityAttribute,
// which the .NET 9 BCL added and the C# 13 compiler respects when picking
// between two applicable overloads. We declare it here under
// `#if !NET9_0_OR_GREATER` so the net8 build of Herald.Core has the type
// available; net9+ builds use the BCL definition.
//
// What it solves for Herald: the typed-args overloads on
// `StructuredLogger` come in arities 1 / 2 / 3 / 4. Each one has
// `[CallerArgumentExpression]` `name1..nameN` parameters with
// `string?` defaults so the compiler auto-fills property names from the
// caller's argument expressions.
//
// Without OverloadResolutionPriority, a call like
//   logger.Info(template, a, b, c, d)
// silently binds to `Info<T1, T2>(template, a, b, name1: c, name2: d)`
// instead of `Info<T1, T2, T3, T4>(template, a, b, c, d)`. Both overloads
// are applicable, and the C# rule "fewer omitted optional parameters
// wins" picks the lower-arity one — the third and fourth values get
// silently consumed as overrides for the auto-derived name parameters
// of the first two args. The kernel then sees a 2-property event whose
// names happen to match the values the caller intended as the 3rd and 4th
// arguments. The bug is invisible from the call site without inspecting
// the IL.
//
// Tagging each typed-args overload with [OverloadResolutionPriority(arity)]
// breaks the tie in favour of the higher-arity overload, so a
// 5-positional-arg call lands on `Info<T1, T2, T3, T4>` as the developer
// intended. Same fix applies at every (lower-arity, higher-arity)
// boundary across Trace / Debug / Info / Warn / Error.
//
// Effective scope:
//   - Compiler-side: the C# 13+ compiler honours the attribute regardless
//     of whether the calling project's target framework includes it in
//     the BCL. Tagging the Herald methods is enough.
//   - Runtime-side: nothing reads this attribute at runtime; it's a pure
//     compile-time hint. Net8 callers using a net8 SDK (C# 12) ignore the
//     attribute and still hit the overload-resolution bug — those callers
//     can either upgrade SDK or use named arguments at the call site as
//     the workaround.

#if !NET9_0_OR_GREATER

namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill of <c>System.Runtime.CompilerServices.OverloadResolutionPriorityAttribute</c>
/// for net8 targets. See the file header for context.
/// </summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property,
    AllowMultiple = false,
    Inherited     = false)]
internal sealed class OverloadResolutionPriorityAttribute : Attribute
{
    public OverloadResolutionPriorityAttribute(int priority)
    {
        Priority = priority;
    }

    public int Priority { get; }
}

#endif
