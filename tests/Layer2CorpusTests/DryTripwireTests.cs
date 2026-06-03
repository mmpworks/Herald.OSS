#nullable enable

// DRY tripwire for the Layer-2 Serilog compat mirror.
//
// Purpose:
//   Layer-2 exists solely as a forwarding skin over Layer-1. No computational
//   logic — no if/for/foreach/while, no switch-with-multiple-arms, no complex
//   branching — should appear in any Layer-2 body. Any such statement is a
//   DRY violation: the equivalent logic already lives in Layer-1.
//
// Allowlist rationale (three structural exceptions):
//
//   1. ValueMap.cs — type-dispatch switch expression
//      A single `v switch { ... }` expression that maps L1 value nodes to L2
//      wrappers. This is a pure identity forward with no computation per arm.
//      The comment in the file documents: "This is a type-dispatch switch, not
//      a computation — zero logic per CRIT-FM-L1." Allowlisted by filename.
//
//   2. Log.cs — Logger setter type-unwrap
//      The Logger setter must inspect whether the incoming ILogger is a
//      Core.Logger wrapper (to avoid double-wrap) or a direct L1.ILogger.
//      This is a documented CRIT-FM-L2 structural necessity, not computation
//      or guard logic. The comment block in Log.cs explains the design.
//      Allowlisted by filename.
//
//   3. Configuration/*.cs — ThrowIfNull boundary guards
//      The three Layer-2 configuration classes (LoggerSinkConfiguration,
//      LoggerEnrichmentConfiguration, LoggerDestructuringConfiguration) call
//      ArgumentNullException.ThrowIfNull on user-supplied objects at the API
//      boundary. This is the project's "defensive programming" convention
//      (memory entry: "guard against crashes on arbitrary/unexpected data").
//      Layer-1 also guards these calls, but the Layer-2 guards are intentional
//      belt-and-suspenders for user-facing APIs where Layer-1's guard is buried
//      inside a bridge adapter. Allowlisted by directory prefix.
//
// Preprocessor directives (#if / #endif / #elif / #else) are NOT logic —
// they are compile-time scoping markers. They are excluded from detection.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace MMP.Herald.Compat.Layer2.Tests;

public sealed class DryTripwireTests
{
    // ── Allowlisted files ────────────────────────────────────────────────────

    // Fully allowlisted files (structural exceptions — all logic in these files
    // is documented and intentional; see file-level comments above).
    private static readonly HashSet<string> AllowlistedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ValueMap.cs",  // type-dispatch switch — documented CRIT-FM-L1 exception
        "Log.cs",       // Logger setter type-unwrap — documented CRIT-FM-L2 exception
    };

    // Files in the Configuration/ subdirectory where ThrowIfNull boundary guards
    // are permitted (defensive programming convention — see allowlist rationale above).
    // Only ThrowIfNull is allowed in these files; other logic patterns are still violations.
    private static readonly HashSet<string> ThrowIfNullPermittedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "LoggerSinkConfiguration.cs",
        "LoggerEnrichmentConfiguration.cs",
        "LoggerDestructuringConfiguration.cs",
    };

    // ── Patterns that indicate a logic violation ─────────────────────────────

    // Checks a trimmed source line for forbidden logic constructs.
    // The caller is responsible for stripping inline comments before calling this.
    // Returns true when the line contains logic that must NOT appear in Layer-2.
    private static bool IsLogicViolation(string trimmedCodePart, bool throwIfNullPermitted)
    {
        // Blank lines are not violations.
        if (string.IsNullOrWhiteSpace(trimmedCodePart))
            return false;

        // Full-line comments and XML-doc comments are not violations.
        if (trimmedCodePart.StartsWith("//", StringComparison.Ordinal))
            return false;

        // Preprocessor directives are compile-time scoping — not logic.
        if (trimmedCodePart.StartsWith("#", StringComparison.Ordinal))
            return false;

        // Namespace, type, and member declarations are structural — not logic.
        if (trimmedCodePart.StartsWith("namespace ", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("using ", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("[assembly:", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("[RequiresUnreferencedCode", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("[MethodImpl", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("public ", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("internal ", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("private ", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("protected ", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("sealed ", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("static ", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("abstract ", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("override ", StringComparison.Ordinal) ||
            trimmedCodePart.StartsWith("partial ", StringComparison.Ordinal))
            return false;

        // Structural braces — not logic.
        if (trimmedCodePart is "{" or "}" or "};")
            return false;

        // — Forbidden control-flow patterns — ────────────────────────────────

        // if/for/foreach/while branching
        if (StartsWithKeyword(trimmedCodePart, "if") ||
            StartsWithKeyword(trimmedCodePart, "else") ||
            StartsWithKeyword(trimmedCodePart, "for") ||
            StartsWithKeyword(trimmedCodePart, "foreach") ||
            StartsWithKeyword(trimmedCodePart, "while"))
            return true;

        // switch statement with a block body (switch expressions on one line
        // are handled at the file level via the ValueMap.cs allowlist)
        if (StartsWithKeyword(trimmedCodePart, "switch") && trimmedCodePart.Contains('{'))
            return true;

        // ThrowIfEmpty / ArgumentNullException.Throw* calls —
        // these guards must live in Layer-1, not Layer-2 (DRY — CRIT-FM-L1 rule).
        // EXCEPTION: ThrowIfNull is permitted in Configuration/*.cs boundary guards
        // (see allowlist rationale at the top of this file).
        if (trimmedCodePart.Contains("ThrowIfEmpty", StringComparison.Ordinal) ||
            trimmedCodePart.Contains("ArgumentNullException.Throw", StringComparison.Ordinal))
        {
            // ThrowIfNull at a configuration boundary is a permitted defensive guard.
            if (throwIfNullPermitted &&
                trimmedCodePart.Contains("ThrowIfNull", StringComparison.Ordinal) &&
                !trimmedCodePart.Contains("ThrowIfEmpty", StringComparison.Ordinal))
            {
                return false; // permitted exception
            }

            return true;
        }

        return false;
    }

    // Returns true when the trimmed line starts with 'keyword' followed by a
    // separator character (space, '(', or end-of-string). Avoids false matches
    // on identifiers that start with the keyword (e.g. "format", "foreach2").
    private static bool StartsWithKeyword(string line, string keyword)
    {
        if (!line.StartsWith(keyword, StringComparison.Ordinal))
            return false;

        if (line.Length == keyword.Length)
            return true;

        var next = line[keyword.Length];
        return next is ' ' or '(' or '\t';
    }

    // ── Locate Layer-2 source dir ────────────────────────────────────────────

    // Walks up from the test assembly's directory to find the repo root,
    // then returns the Layer-2 source directory.
    private static string FindLayer2Dir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Herald.OSS.csproj")))
            {
                var layer2 = Path.Combine(dir.FullName, "src", "Compatibility", "Layer2");
                if (Directory.Exists(layer2))
                    return layer2;

                throw new DirectoryNotFoundException(
                    $"Found Herald.OSS.csproj at '{dir.FullName}' but " +
                    $"src/Compatibility/Layer2 does not exist.");
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate Herald.OSS.csproj — cannot resolve Layer-2 source dir. " +
            "Run the test from within the Herald.OSS repo tree.");
    }

    // ── Test ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Layer2_source_contains_no_logic_statements()
    {
        // Arrange
        var layer2Dir = FindLayer2Dir();
        var violations = new List<string>();

        // Act: walk every .cs file in Layer-2 (excluding obj/ directories)
        var sourceFiles = Directory
            .EnumerateFiles(layer2Dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                                    StringComparison.Ordinal) &&
                        !f.Contains("/obj/", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(sourceFiles.Count > 0, "Sanity: Layer-2 source dir must contain .cs files.");

        foreach (var file in sourceFiles)
        {
            var fileName = Path.GetFileName(file);

            // Skip fully allowlisted files (documented structural exceptions).
            if (AllowlistedFileNames.Contains(fileName))
                continue;

            // Determine whether ThrowIfNull is a permitted boundary guard in this file.
            var throwIfNullPermitted = ThrowIfNullPermittedFiles.Contains(fileName);

            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                // Strip inline comments before checking — a comment that mentions
                // "if" or "ThrowIfNull" is not a logic violation.
                var raw = lines[i];
                var inlineCommentIdx = raw.IndexOf("//", StringComparison.Ordinal);
                var codePart = inlineCommentIdx >= 0
                    ? raw[..inlineCommentIdx]
                    : raw;

                var trimmed = codePart.Trim();

                if (IsLogicViolation(trimmed, throwIfNullPermitted))
                {
                    var relativePath = Path.GetRelativePath(layer2Dir, file)
                                          .Replace('\\', '/');
                    violations.Add($"{relativePath}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        // Report all violations in one shot so the developer sees the full picture.
        Assert.True(
            violations.Count == 0,
            $"Layer-2 DRY tripwire: {violations.Count} logic violation(s) found.\n" +
            "Layer-2 must be a pure forwarding skin — all computation lives in Layer-1.\n" +
            "To suppress a legitimate exception, add the filename to the allowlist above\n" +
            "with a documented rationale.\n" +
            "Violations:\n  " + string.Join("\n  ", violations));
    }
}

