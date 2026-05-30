// Source generator that emits the typed-args overloads of StructuredLogger.
//
// Why this generator exists:
//
//   The typed-args API surface — Trace / Debug / Info / Warn / Error
//   each across arity 1..16, in two families (category-less and
//   category-bearing) — is 160 method overloads, all shape-identical.
//   Hand-rolling them was bug-prone in two specific ways:
//
//     1. [OverloadResolutionPriority(N)] is required on every overload to
//        defeat C#'s "fewer omitted optional parameters wins" tiebreaker
//        (which silently picks the lower-arity overload when the call
//        site has N positional args matching both N-arg and N-2-arg
//        overloads). Forgetting it on a single overload reintroduces
//        the bug for callers at that boundary. Shipping 80 hand-written
//        methods makes drift inevitable.
//     2. The arity → LogPropertyBuffer mapping (1→Buffer1, 2→Buffer2,
//        3-4→Buffer4, 5-8→Buffer8, 9-16→Buffer16) needs the same switch
//        in every dispatcher. Keeping it consistent across 16 entries
//        is the kind of thing a generator gets right by construction.
//
//   The generator centralises both concerns. Adding a new arity is a
//   one-line change to MaxArity. Adding a new level (e.g. Critical) is
//   a one-line addition to the Levels array. The arity-to-buffer mapping
//   lives in one place. [OverloadResolutionPriority] is applied to every
//   overload by definition, never forgotten.
//
// Output:
//
//   StructuredLogger.TypedArgs.Generated.cs containing:
//     - The `partial class StructuredLogger` declaration
//     - 160 public overloads (2 families × 16 arities × 5 levels) each with
//         [MethodImpl(AggressiveInlining)]
//         [OverloadResolutionPriority(arity)]
//       The category-less family passes LogCategory.None to the dispatcher;
//       the category-bearing family takes LogCategory as the first parameter
//       and forwards it to the dispatcher.
//     - 5 private dispatchers DispatchTypedN where N ∈ {1, 2, 4, 8, 16},
//       each routing the matching span back through LogCompact() with the
//       category the caller supplied.
//
//   The hand-edited StructuredLogger.TypedArgs.cs becomes a header
//   stub that documents the surface and points readers here.
//
// Why this is AOT-clean:
//
//   The generator runs at compile time only. Its output is plain C# the
//   ILC analyzer treats identically to hand-written code. Each emitted
//   method is `[MethodImpl(AggressiveInlining)]` so AOT inlines them at
//   the call site. Generic instantiations only generate native code for
//   shapes the consumer actually calls — a game using only Info<string,
//   int> pays zero native bytes for the unused 78 overloads.

#nullable enable

using System.Text;
using Microsoft.CodeAnalysis;

namespace MMP.Herald.Generators;

[Generator]
public sealed class TypedArgsOverloadGenerator : IIncrementalGenerator
{
    // Knobs. Bump MaxArity to extend the surface. Levels lists the
    // public log-level method names; each gets the full arity sweep.
    private const int MaxArity = 16;

    // Task 4: method names now match the Serilog vocabulary directly.
    // Verbose/Information/Warning/Fatal replace Trace/Info/Warn/Critical.
    private static readonly string[] Levels =
    {
        "Verbose", "Debug", "Information", "Warning", "Error",
    };

    // After Task 4 the method names ARE the KnownLogLevels member names,
    // so this mapping is identity for all current levels. Kept as a
    // pass-through for forward-compat if a future level name diverges.
    private static string LevelToKnownLogLevelsMember(string level) => level;
    // Previous Task-3 bridge (removed in Task 4):
    //   "Trace" => "Verbose", "Info" => "Information", "Warn" => "Warning"

    // Maps an arity to the InlineArray buffer it should write into.
    // Buffers come in fixed sizes 1, 2, 4, 8, 16; arities that don't
    // match a size exactly use the next-larger buffer and slice.
    private static int BufferSizeFor(int arity) => arity switch
    {
        1                              => 1,
        2                              => 2,
        >= 3 and <= 4                  => 4,
        >= 5 and <= 8                  => 8,
        >= 9 and <= 16                 => 16,
        _ => 16,
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Only emit when the current compilation is Herald.Core itself —
        // i.e., when MMP.Herald.Pipeline.StructuredLogger is being
        // *declared* in source rather than imported from a referenced
        // assembly. Without this guard, the analyzer reference
        // propagates from Core to every downstream project (tests,
        // benchmarks, consumer apps) and the generator emits a second
        // partial-class declaration there, colliding with the imported
        // Core type.
        var isCoreCompilation = context.CompilationProvider.Select(static (c, _) =>
        {
            var symbol = c.GetTypeByMetadataName("MMP.Herald.Pipeline.StructuredLogger");
            return symbol is not null
                && SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, c.Assembly);
        });

        context.RegisterSourceOutput(isCoreCompilation, static (ctx, isCore) =>
        {
            if (!isCore) return;
            var sb = new StringBuilder(64 * 1024);
            EmitFile(sb);
            ctx.AddSource("StructuredLogger.TypedArgs.Generated.cs", sb.ToString());
        });
    }

    private static void EmitFile(StringBuilder sb)
    {
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by MMP.Herald.Generators.TypedArgsOverloadGenerator.");
        sb.AppendLine("// Do not edit by hand — change the generator and rebuild.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using MMP.Herald.Events;");
        sb.AppendLine("using MMP.Herald.Levels;");
        sb.AppendLine("using MMP.Herald.Pipeline.Kernel;");
        sb.AppendLine();
        sb.AppendLine("namespace MMP.Herald.Pipeline;");
        sb.AppendLine();
        sb.AppendLine("public sealed partial class StructuredLogger");
        sb.AppendLine("{");

        foreach (var level in Levels)
        {
            sb.Append("    // ── ").Append(level).Append(" (category-less, defaults to LogCategory.None)").AppendLine(" ──");
            sb.AppendLine();
            for (var arity = 1; arity <= MaxArity; arity++)
            {
                EmitOverload(sb, level, arity, withCategory: false);
            }

            sb.Append("    // ── ").Append(level).Append(" (category-bearing)").AppendLine(" ──");
            sb.AppendLine();
            for (var arity = 1; arity <= MaxArity; arity++)
            {
                EmitOverload(sb, level, arity, withCategory: true);
            }
        }

        sb.AppendLine("    // ── Dispatchers (one per InlineArray buffer size) ─────────────────");
        sb.AppendLine();
        EmitDispatcher(sb, 1);
        EmitDispatcher(sb, 2);
        EmitDispatcher(sb, 4);
        EmitDispatcher(sb, 8);
        EmitDispatcher(sb, 16);

        sb.AppendLine("}");
    }

    private static void EmitOverload(StringBuilder sb, string level, int arity, bool withCategory)
    {
        // Inline the buffer-fill into each public overload so the JIT
        // can specialize per-T-set and hit LogPropertyCompact.From<T>'s
        // primitive arms without going through an object?-typed
        // dispatcher (which would force boxing at the parameter
        // boundary, defeating the typed slot).
        var typeArgs = JoinT(arity);                                   // T1, T2, ...
        var argsDecl = string.Join(", ",
            EnumerateArgs(arity, i => $"T{i} arg{i}"));                // T1 arg1, T2 arg2, ...
        var bufferSize = BufferSizeFor(arity);

        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
        sb.Append("    [OverloadResolutionPriority(").Append(arity).AppendLine(")]");
        sb.Append("    public void ").Append(level).Append('<').Append(typeArgs).AppendLine(">(");
        if (withCategory)
        {
            sb.AppendLine("        LogCategory category,");
        }
        sb.AppendLine("        string template,");
        sb.Append("        ").Append(argsDecl).AppendLine(",");
        for (var i = 1; i <= arity; i++)
        {
            sb.Append("        [CallerArgumentExpression(\"arg").Append(i).Append("\")] string? name").Append(i).Append(" = null");
            sb.AppendLine(i < arity ? "," : ")");
        }
        sb.AppendLine("    {");
        sb.Append("        if (!Is").Append(level).AppendLine("Acceptable) return;");

        // Phase 4: consult the active naming policy via the resolver-cache
        // fast/slow split. On cache hit (the steady-state path) the
        // params-array allocation is short-circuited by the `??` operator.
        // On cold miss the params-array is constructed, the policy runs
        // once, and the result is cached for every subsequent dispatch
        // through this template.
        sb.Append("        var __names = TryGetCachedNames(template) ?? ResolveAndCacheNames(template");
        for (var i = 1; i <= arity; i++)
        {
            sb.Append(", name").Append(i);
        }
        sb.AppendLine(");");

        sb.Append("        var buf = new LogPropertyBuffer").Append(bufferSize).AppendLine("();");
        for (var i = 1; i <= arity; i++)
        {
            sb.Append("        buf[").Append(i - 1)
              .Append("] = LogPropertyCompact.From(__names[").Append(i - 1)
              .Append("], arg").Append(i).AppendLine(");");
        }
        sb.AppendLine("        System.ReadOnlySpan<LogPropertyCompact> span = ((System.Span<LogPropertyCompact>)buf).Slice(0, " + arity + ");");
        sb.Append("        LogCompact(KnownLogLevels.").Append(LevelToKnownLogLevelsMember(level))
          .Append(", ").Append(withCategory ? "category" : "LogCategory.None")
          .AppendLine(", template, span);");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void EmitDispatcher(StringBuilder sb, int bufferSize)
    {
        // Dispatchers are no longer used — each public overload inlines
        // the buffer-fill and calls LogCompact directly so the typed
        // path preserves T1..Tn all the way to LogPropertyCompact.From<T>.
        // The method is intentionally left empty so the generator's
        // EmitDispatcher hook stays callable; downstream tooling that
        // looked up "DispatchTyped{N}" by name now finds nothing.
        // Kept as a placeholder to preserve the generator's public
        // shape; the file body grows by zero bytes per dispatcher.
    }

    // ── Tiny helpers — kept inside the generator so the source stays
    // self-contained and System.Linq isn't required at generator runtime.

    private static string JoinT(int n)
    {
        var sb = new StringBuilder(n * 4);
        for (var i = 1; i <= n; i++)
        {
            if (i > 1) sb.Append(", ");
            sb.Append('T').Append(i);
        }
        return sb.ToString();
    }

    private static System.Collections.Generic.IEnumerable<string> EnumerateArgs(
        int n, System.Func<int, string> fmt)
    {
        for (var i = 1; i <= n; i++) yield return fmt(i);
    }
}
