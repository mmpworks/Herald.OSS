// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
//
// InterceptorGenerator — Roslyn incremental source generator that bakes the
// active naming policy's resolved property names into every literal-template
// call site of StructuredLogger.Info / .Warn / .Error / .Debug / .Trace in the
// consumer's compilation. Replaces the runtime resolver hop with a
// switch-on-CurrentPolicyKind + literal-baked LogPropertyCompact.From per slot.
//
// One interceptor method is emitted per qualifying call site. Each interceptor
// carries three baked-name lanes (Pascal / Snake / Camel) selected at dispatch
// time by the active policy's BuiltinPolicy kind. A custom policy falls back
// to the runtime resolver path (the existing typed-args overload) by re-calling
// the same method from inside the interceptor body — interceptor matching is
// keyed by syntactic call-site location, so the recursion is impossible.
//
// Output shape per call site:
//
//   [InterceptsLocation("Foo.cs", 42, 13)]
//   public static void Intercepted_<id>(
//       this StructuredLogger logger,
//       LogCategory category,
//       string template,
//       int arg1, string arg2)
//   {
//       if (!logger.IsInfoAcceptable) return;
//       switch (logger.CurrentPolicyKind)
//       {
//           case BuiltinPolicy.Pascal: { /* pascalNames[i] baked literals */ return; }
//           case BuiltinPolicy.Snake:  { /* snakeNames[i] baked literals  */ return; }
//           case BuiltinPolicy.Camel:  { /* camelNames[i] baked literals  */ return; }
//           default: logger.Info(category, template, arg1, arg2); return;
//       }
//   }
//
// The default lane re-invokes the original method by name. That invocation is
// at a *different* syntactic call site (inside the generated file), so the
// interceptor for the original site does not re-fire — no recursion possible.
//
// Why this generator is AOT-clean:
//   - Compile-time only; no runtime reflection.
//   - Emitted IL is plain C# the ILC analyzer treats identically to
//     hand-written code.
//   - The polyfill InterceptsLocationAttribute is emitted inside the same
//     generated source so a net8 consumer (which lacks the BCL attribute)
//     compiles cleanly; net9+ consumers see the BCL type via the polyfill's
//     `internal` declaration which does not conflict with the public BCL one.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace MMP.Herald.Generators;

[Generator]
public sealed class InterceptorGenerator : IIncrementalGenerator
{
    // Public surface for diagnostics. HRLD0001..HRLD0099 is reserved for
    // MSBuild-property validation; HRLD0050 is the only operator-hint warning
    // V1 ships.
    private static readonly DiagnosticDescriptor InterceptorsEnabledInvalidRule = new(
        id: "HRLD0001",
        title: "Invalid HeraldInterceptorsEnabled value",
        messageFormat:
            "HeraldInterceptorsEnabled is set to '{0}' which is not a recognised value. " +
            "Use 'true' (default) to bake interceptors, or 'false' to disable them and " +
            "leave every call site on the runtime resolver path.",
        category: "Herald.OSS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor StrictModeInvalidRule = new(
        id: "HRLD0002",
        title: "Invalid HeraldStrictMode value",
        messageFormat:
            "HeraldStrictMode is set to '{0}' which is not a recognised value. " +
            "Use 'true' to escalate Herald analyzer warnings to errors, or 'false' (default) " +
            "to leave them as warnings.",
        category: "Herald.OSS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor LargeInterceptorCountRule = new(
        id: "HRLD0050",
        title: "Herald interceptor count exceeded the soft threshold",
        messageFormat:
            "This assembly's Herald interceptor surface is {0} call sites; the soft threshold is {1}. " +
            "Large interceptor counts can slow incremental builds. Consider using [HeraldLog] " +
            "partial methods or grouping callers into fewer hot-path facades.",
        category: "Herald.OSS",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // Threshold for HRLD0050 (assembly-level count of intercepted call sites).
    private const int LargeInterceptorCountSoftLimit = 5000;

    // Names that drive the syntax match.
    private static readonly string[] InterceptableMethodNames =
        new[] { "Info", "Warn", "Error", "Debug", "Trace" };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // -- MSBuild surface ------------------------------------------------
        //
        // HeraldInterceptorsEnabled (default true) — master switch.
        // HeraldStrictMode         (default false) — escalates Herald warnings.
        var msBuildOptions = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
        {
            var enabledRaw = TryGetBuildProperty(provider, "HeraldInterceptorsEnabled");
            var strictRaw = TryGetBuildProperty(provider, "HeraldStrictMode");

            return new MsBuildOptions(
                Enabled: ParseBoolOrDefault(enabledRaw, defaultValue: true),
                EnabledRaw: enabledRaw,
                StrictMode: ParseBoolOrDefault(strictRaw, defaultValue: false),
                StrictModeRaw: strictRaw);
        });

        // Validate MSBuild property values up front so an operator sees one
        // clear diagnostic per property rather than a flood of downstream
        // failures.
        context.RegisterSourceOutput(msBuildOptions, static (ctx, options) =>
        {
            if (options.EnabledRaw is not null && !IsBoolLike(options.EnabledRaw))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    InterceptorsEnabledInvalidRule, Location.None, options.EnabledRaw));
            }
            if (options.StrictModeRaw is not null && !IsBoolLike(options.StrictModeRaw))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    StrictModeInvalidRule, Location.None, options.StrictModeRaw));
            }
        });

        // -- Consumer-vs-Core compilation gate ------------------------------
        //
        // Same trick TypedArgsOverloadGenerator uses: only emit interceptors
        // when the current compilation is NOT Herald.OSS itself. Herald.OSS
        // has no business intercepting its own call sites — every internal
        // dispatch is already on the runtime resolver path by design.
        var isCoreCompilation = context.CompilationProvider.Select(static (c, _) =>
        {
            var symbol = c.GetTypeByMetadataName("MMP.Herald.Pipeline.StructuredLogger");
            return symbol is not null
                && SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, c.Assembly);
        });

        // Assembly name hash for the interceptor host class name. Avoids
        // collision across multi-assembly consumers where two assemblies
        // would otherwise emit the same MMP.Herald.Generated.HeraldInterceptors
        // class and confuse the compiler at link time.
        var assemblyHash = context.CompilationProvider.Select(static (c, _) =>
            HashAssemblyName(c.AssemblyName ?? "anonymous"));

        // -- Syntax candidate collection ------------------------------------
        //
        // Match every invocation that LOOKS LIKE a logger.<Info|Warn|...>(...) call.
        // The semantic filter inside Transform is what verifies the target
        // method is actually StructuredLogger's typed-args overload.
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidateInvocation(node),
                transform: static (ctx, ct) => TransformCandidate(ctx, ct))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        // We need to emit ONE file per consumer assembly (containing every
        // interceptor) rather than one file per call site, so the host class
        // collects all locations in a single batch. Collect() materialises
        // the candidate list once per re-pipeline-run.
        var collected = candidates.Collect()
            .Combine(msBuildOptions)
            .Combine(isCoreCompilation)
            .Combine(assemblyHash);

        context.RegisterSourceOutput(collected, static (ctx, pair) =>
        {
            var (((sites, options), isCore), asmHash) = pair;
            if (isCore) return;            // never intercept Herald.OSS itself
            if (!options.Enabled) return;  // master switch off
            if (sites.IsDefaultOrEmpty) return;

            // HRLD0050 soft cap (warning only).
            if (sites.Length > LargeInterceptorCountSoftLimit)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    LargeInterceptorCountRule, Location.None,
                    sites.Length, LargeInterceptorCountSoftLimit));
            }

            var source = EmitInterceptorFile(sites, asmHash);
            ctx.AddSource("HeraldInterceptors.g.cs", SourceText.From(source, Encoding.UTF8));

            // Assembly-level build-assertion attribute. AOT-safe: a plain
            // attribute with init-only string properties.
            var assertion = EmitBuildAssertion(options, sites.Length);
            ctx.AddSource("HeraldBuildAssertion.g.cs", SourceText.From(assertion, Encoding.UTF8));
        });
    }

    // -- MSBuild property helpers -----------------------------------------

    private static string? TryGetBuildProperty(
        AnalyzerConfigOptionsProvider provider, string propertyName)
    {
        return provider.GlobalOptions.TryGetValue("build_property." + propertyName, out var v)
            && !string.IsNullOrWhiteSpace(v)
            ? v.Trim()
            : null;
    }

    private static bool ParseBoolOrDefault(string? raw, bool defaultValue)
    {
        if (raw is null) return defaultValue;
        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return defaultValue;
    }

    private static bool IsBoolLike(string raw) =>
        raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        raw.Equals("false", StringComparison.OrdinalIgnoreCase);

    private static string HashAssemblyName(string name)
    {
        // Stable FNV-1a 32-bit hash so two builds of the same assembly emit
        // the same interceptor host class name. Hex-formatted; 8 chars.
        unchecked
        {
            const uint fnvOffset = 2166136261;
            const uint fnvPrime = 16777619;
            var hash = fnvOffset;
            for (var i = 0; i < name.Length; i++)
            {
                hash ^= name[i];
                hash *= fnvPrime;
            }
            return hash.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // -- Syntax filtering / model extraction ------------------------------

    private static bool IsCandidateInvocation(SyntaxNode node)
    {
        // Fast pre-filter — runs on every syntax node. Keep cheap.
        // Match shape: <expr>.<Info|Warn|Error|Debug|Trace>(<args>) where
        // the second positional arg (after a possible LogCategory) is a
        // string-literal template. The semantic verification happens in
        // Transform below.
        if (node is not InvocationExpressionSyntax inv) return false;
        if (inv.Expression is not MemberAccessExpressionSyntax member) return false;

        var methodName = member.Name.Identifier.ValueText;
        foreach (var n in InterceptableMethodNames)
        {
            if (string.Equals(n, methodName, StringComparison.Ordinal))
            {
                // At least one arg (the template) and at most 17 (category +
                // template + 16 typed args).
                var argCount = inv.ArgumentList.Arguments.Count;
                return argCount >= 1 && argCount <= 18;
            }
        }
        return false;
    }

    private static CallSiteModel? TransformCandidate(
        GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var inv = (InvocationExpressionSyntax)ctx.Node;
        var member = (MemberAccessExpressionSyntax)inv.Expression;
        var methodName = member.Name.Identifier.ValueText;

        // Bind the call to find the actual method symbol.
        var symbolInfo = ctx.SemanticModel.GetSymbolInfo(inv, ct);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (methodSymbol is null) return null;

        // Must be a method on MMP.Herald.Pipeline.StructuredLogger.
        var receiverType = methodSymbol.ReceiverType;
        if (receiverType is null) return null;
        if (receiverType.ToDisplayString() != "MMP.Herald.Pipeline.StructuredLogger") return null;
        if (!string.Equals(methodSymbol.Name, methodName, StringComparison.Ordinal)) return null;

        // Must be the typed-args generic overload with at least one T arg
        // (arity 1..16). Skip zero-property overloads — they have nothing to
        // bake. Skip exception-bearing overloads (parameter 1 is Exception).
        if (methodSymbol.TypeArguments.Length == 0) return null;
        if (methodSymbol.TypeArguments.Length > 16) return null;

        // Inspect the method parameters to determine shape (category-less vs
        // category-bearing) and to confirm trailing CAE string? params.
        var parameters = methodSymbol.Parameters;
        if (parameters.Length < 2) return null;

        bool hasCategory;
        int templateParamIndex;
        if (parameters[0].Type.ToDisplayString() == "MMP.Herald.Events.LogCategory")
        {
            hasCategory = true;
            templateParamIndex = 1;
        }
        else if (parameters[0].Type.SpecialType == SpecialType.System_String)
        {
            hasCategory = false;
            templateParamIndex = 0;
        }
        else
        {
            return null;
        }

        if (templateParamIndex >= parameters.Length) return null;
        if (parameters[templateParamIndex].Type.SpecialType != SpecialType.System_String) return null;

        // Trailing CAE parameters: one per typed slot. The method must have
        // 2 + 2*N parameters (or 1 + 2*N for category-less). Anything else
        // means this isn't the typed-args overload we recognize.
        var arity = methodSymbol.TypeArguments.Length;
        var expectedParamCount = templateParamIndex + 1 + 2 * arity;
        if (parameters.Length != expectedParamCount) return null;

        // Resolve the call-site source position FROM THE ARGUMENT, not from
        // the invocation itself. C# interceptor matching uses the position of
        // the method-name identifier in the member-access expression.
        var location = member.Name.GetLocation();
        if (!location.IsInSource) return null;

        var lineSpan = location.GetLineSpan();
        var filePath = lineSpan.Path;
        // Roslyn line/column numbers are 0-based; InterceptsLocation expects 1-based.
        var line = lineSpan.StartLinePosition.Line + 1;
        var column = lineSpan.StartLinePosition.Character + 1;
        if (string.IsNullOrEmpty(filePath)) return null;

        // Template must be a string literal — non-literal templates can't be
        // resolved at build time, so they stay on the runtime path.
        var args = inv.ArgumentList.Arguments;
        if (args.Count <= templateParamIndex) return null;
        var templateArgExpr = args[templateParamIndex].Expression;
        var templateConst = ctx.SemanticModel.GetConstantValue(templateArgExpr, ct);
        if (!templateConst.HasValue || templateConst.Value is not string templateLiteral)
        {
            return null;
        }

        // Skip call sites that pass explicit nameN: overrides — those callers
        // want the CAE override honored, which the build-time literal bake
        // can't represent. Falling through to the runtime resolver preserves
        // the documented "explicit nameN: overrides win" contract.
        foreach (var a in args)
        {
            var nameColon = a.NameColon?.Name.Identifier.ValueText;
            if (nameColon is null) continue;
            if (nameColon.Length > 4 &&
                nameColon.StartsWith("name", StringComparison.Ordinal) &&
                char.IsDigit(nameColon[4]))
            {
                return null;
            }
        }

        // Caller-argument-expression names: pull from the syntactic args
        // following the template. The compiler will pass these as the CAE
        // strings; we extract them at build time so the baked literals
        // match what the runtime resolver would have seen.
        var argExprBuilder = ImmutableArray.CreateBuilder<string>(arity);
        for (var i = 0; i < arity; i++)
        {
            var syntacticIndex = templateParamIndex + 1 + i;
            if (syntacticIndex >= args.Count) return null;
            argExprBuilder.Add(args[syntacticIndex].Expression.ToString());
        }
        var argExprs = argExprBuilder.MoveToImmutable();

        // Typed-arg type names — fully-qualified so the emitted interceptor
        // signature matches across namespaces.
        var typeArgBuilder = ImmutableArray.CreateBuilder<string>(arity);
        foreach (var t in methodSymbol.TypeArguments)
        {
            typeArgBuilder.Add(t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }
        var typeArgs = typeArgBuilder.MoveToImmutable();

        return new CallSiteModel(
            MethodName: methodName,
            HasCategory: hasCategory,
            Arity: arity,
            TypeArgs: typeArgs,
            ArgExpressions: argExprs,
            Template: templateLiteral,
            FilePath: filePath,
            Line: line,
            Column: column);
    }

    // -- Emission --------------------------------------------------------

    private static string EmitInterceptorFile(
        ImmutableArray<CallSiteModel> sites, string assemblyHash)
    {
        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by MMP.Herald.Generators.InterceptorGenerator.");
        sb.AppendLine("// One interceptor per literal-template logger call site in this");
        sb.AppendLine("// assembly. Property names are pre-resolved per built-in policy");
        sb.AppendLine("// (Pascal / Snake / Camel) and selected at dispatch time by the");
        sb.AppendLine("// active StructuredLogger.CurrentPolicyKind.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS9270   // experimental warning on pre-stable interceptor APIs");
        sb.AppendLine();

        // Polyfill the attribute when the BCL doesn't ship it. The polyfill is
        // emitted as `internal` so a net9+ consumer (which has the public
        // System.Runtime.CompilerServices.InterceptsLocationAttribute) doesn't
        // collide — internal-vs-public type lookups inside the same assembly
        // resolve to the internal one for any reference that lives in the same
        // generated file, and the public BCL one remains accessible elsewhere.
        // Polyfill the attribute when the BCL doesn't ship it (pre-net9).
        // `file` scope keeps it from conflicting with the public BCL type on
        // net9+. The discard assignments suppress CS9113 (primary-ctor unread
        // parameter) under the consumer's TreatWarningsAsErrors — the C#
        // compiler reads only the attribute's existence + ctor signature
        // when validating [InterceptsLocation], it does not call into the
        // body.
        sb.AppendLine("namespace System.Runtime.CompilerServices");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
        sb.AppendLine("    file sealed class InterceptsLocationAttribute : global::System.Attribute");
        sb.AppendLine("    {");
        sb.AppendLine("        public InterceptsLocationAttribute(string filePath, int line, int column) { _ = filePath; _ = line; _ = column; }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        // Host class. `file`-scoped so two assemblies that happen to land on
        // the same hash never collide at link time; the assembly-hash suffix is
        // belt-and-braces for readability in stack traces.
        sb.AppendLine("namespace MMP.Herald.Generated");
        sb.AppendLine("{");
        sb.Append("    file static class HeraldInterceptors_").Append(assemblyHash).AppendLine();
        sb.AppendLine("    {");

        for (var siteIndex = 0; siteIndex < sites.Length; siteIndex++)
        {
            EmitInterceptor(sb, sites[siteIndex], siteIndex);
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitInterceptor(StringBuilder sb, CallSiteModel site, int siteIndex)
    {
        // Pre-resolve names for every built-in policy. Bytes-identical to what
        // the runtime resolver would produce on a cold miss.
        var caeNames = site.ArgExpressions.ToArray();
        var pascalNames = CompileTimeNameResolver.Resolve(site.Template, caeNames, BuiltinPolicyKind.Pascal);
        var snakeNames  = CompileTimeNameResolver.Resolve(site.Template, caeNames, BuiltinPolicyKind.Snake);
        var camelNames  = CompileTimeNameResolver.Resolve(site.Template, caeNames, BuiltinPolicyKind.Camel);

        // [InterceptsLocation(...)].
        sb.Append("        [global::System.Runtime.CompilerServices.InterceptsLocation(");
        AppendStringLiteral(sb, site.FilePath);
        sb.Append(", ").Append(site.Line);
        sb.Append(", ").Append(site.Column);
        sb.AppendLine(")]");

        // public static void Intercepted_<id>...<T1, ..., Tn>(this StructuredLogger logger, ...)
        sb.Append("        public static void Intercepted_").Append(siteIndex);
        if (site.Arity > 0)
        {
            sb.Append('<');
            for (var i = 0; i < site.Arity; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('T').Append(i + 1);
            }
            sb.Append('>');
        }
        sb.AppendLine("(");
        sb.AppendLine("            this global::MMP.Herald.Pipeline.StructuredLogger logger,");
        if (site.HasCategory)
        {
            sb.AppendLine("            global::MMP.Herald.Events.LogCategory category,");
        }
        sb.Append("            string template");
        for (var i = 0; i < site.Arity; i++)
        {
            sb.Append(",").AppendLine();
            sb.Append("            T").Append(i + 1).Append(" arg").Append(i + 1);
        }
        // Trailing CAE parameters mirror the intercepted method so the
        // interceptor signature is binding-equivalent. We never read them
        // (the literals are baked) but they must be present.
        for (var i = 0; i < site.Arity; i++)
        {
            sb.Append(",").AppendLine();
            sb.Append("            [global::System.Runtime.CompilerServices.CallerArgumentExpression(\"arg")
              .Append(i + 1).Append("\")] string? name").Append(i + 1).Append(" = null");
        }
        sb.AppendLine(")");
        sb.AppendLine("        {");

        // Level guard — direct field read instead of IsEnabled().
        var acceptField = "Is" + site.MethodName + "Acceptable";
        sb.Append("            if (!logger.").Append(acceptField).AppendLine(") return;");

        // Bookkeeping: arms the first-dispatch announcement gate and bumps
        // the compile-time-resolutions counter. Inlined to a single field
        // touch under JIT so the hot path stays one acceptable-bool read
        // plus this one call.
        sb.AppendLine("            logger.RecordInterceptedDispatch();");

        // Three-arm switch on CurrentPolicyKind. Each arm bakes its own
        // literal-property-name list.
        sb.AppendLine("            switch (logger.CurrentPolicyKind)");
        sb.AppendLine("            {");
        EmitPolicyArm(sb, site, "global::MMP.Herald.Templating.BuiltinPolicy.Pascal", pascalNames);
        EmitPolicyArm(sb, site, "global::MMP.Herald.Templating.BuiltinPolicy.Snake",  snakeNames);
        EmitPolicyArm(sb, site, "global::MMP.Herald.Templating.BuiltinPolicy.Camel",  camelNames);
        sb.AppendLine("                default:");
        sb.Append("                    logger.").Append(site.MethodName).Append('(');
        if (site.HasCategory) sb.Append("category, ");
        sb.Append("template");
        for (var i = 0; i < site.Arity; i++)
        {
            sb.Append(", arg").Append(i + 1);
        }
        sb.AppendLine(");");
        sb.AppendLine("                    return;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
    }

    private static void EmitPolicyArm(
        StringBuilder sb, CallSiteModel site, string policyEnumValue, string[] bakedNames)
    {
        sb.Append("                case ").Append(policyEnumValue).AppendLine(":");
        sb.AppendLine("                {");

        // Choose a buffer size that fits the arity exactly or rounds up.
        var bufferSize = PickBufferSize(site.Arity);
        var bufferType = "global::MMP.Herald.Pipeline.Kernel.LogPropertyBuffer" + bufferSize;

        sb.Append("                    var __buf = new ").Append(bufferType).AppendLine("();");
        for (var i = 0; i < site.Arity; i++)
        {
            sb.Append("                    __buf[").Append(i)
              .Append("] = global::MMP.Herald.Pipeline.Kernel.LogPropertyCompact.From(");
            AppendStringLiteral(sb, bakedNames[i]);
            sb.Append(", arg").Append(i + 1).AppendLine(");");
        }

        if (site.Arity == bufferSize)
        {
            sb.Append("                    logger.LogCompact(global::MMP.Herald.Levels.KnownLogLevels.")
              .Append(site.MethodName).Append(", ")
              .Append(site.HasCategory ? "category" : "global::MMP.Herald.Events.LogCategory.None")
              .AppendLine(", template, __buf);");
        }
        else
        {
            sb.Append("                    global::System.ReadOnlySpan<global::MMP.Herald.Pipeline.Kernel.LogPropertyCompact> __span = ")
              .Append("((global::System.Span<global::MMP.Herald.Pipeline.Kernel.LogPropertyCompact>)__buf).Slice(0, ")
              .Append(site.Arity).AppendLine(");");
            sb.Append("                    logger.LogCompact(global::MMP.Herald.Levels.KnownLogLevels.")
              .Append(site.MethodName).Append(", ")
              .Append(site.HasCategory ? "category" : "global::MMP.Herald.Events.LogCategory.None")
              .AppendLine(", template, __span);");
        }

        sb.AppendLine("                    return;");
        sb.AppendLine("                }");
    }

    private static int PickBufferSize(int arity) => arity switch
    {
        1 => 1,
        2 => 2,
        >= 3 and <= 4 => 4,
        >= 5 and <= 8 => 8,
        _ => 16,
    };

    // Emits a properly-escaped C# string literal.
    private static void AppendStringLiteral(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\r': sb.Append("\\r");  break;
                case '\n': sb.Append("\\n");  break;
                case '\t': sb.Append("\\t");  break;
                case '\0': sb.Append("\\0");  break;
                default:
                    if (c < 0x20 || c == 0x7F)
                    {
                        sb.Append("\\u").Append(((int)c).ToString(
                            "X4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }

    // -- Build-assertion emitter -----------------------------------------

    private static string EmitBuildAssertion(MsBuildOptions options, int siteCount)
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Assembly-level marker emitted by Herald.OSS's interceptor generator.");
        sb.AppendLine("// Read at runtime via Assembly.GetCustomAttribute<HeraldBuildAssertionAttribute>()");
        sb.AppendLine("// to confirm a build was produced with the expected interceptor surface.");
        sb.AppendLine();
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.Append("[assembly: global::MMP.Herald.Build.HeraldBuildAssertionAttribute(");
        sb.AppendLine();
        sb.Append("    InterceptorsEnabled = ").Append(options.Enabled ? "true" : "false").AppendLine(",");
        sb.Append("    StrictMode = ").Append(options.StrictMode ? "true" : "false").AppendLine(",");
        // V1 always bakes all three built-ins; the property carries that explicit
        // contract so an operator inspecting a built assembly can see at a glance
        // which lanes are wired.
        sb.AppendLine("    BakedPolicies = \"Pascal,Snake,Camel\",");
        sb.Append("    InterceptedCallSites = ").Append(siteCount).AppendLine(")]");
        return sb.ToString();
    }

    // -- Models -----------------------------------------------------------

    private sealed record CallSiteModel(
        string MethodName,
        bool HasCategory,
        int Arity,
        ImmutableArray<string> TypeArgs,
        ImmutableArray<string> ArgExpressions,
        string Template,
        string FilePath,
        int Line,
        int Column);

    private readonly record struct MsBuildOptions(
        bool Enabled,
        string? EnabledRaw,
        bool StrictMode,
        string? StrictModeRaw);
}
