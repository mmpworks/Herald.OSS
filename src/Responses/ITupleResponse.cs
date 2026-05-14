#nullable enable

namespace MMP.Herald.Responses;

/// <summary>
/// CUPID-aligned result carrier (Predictable, Composable, Idiomatic).
/// Mirrors the PHP Tuple3Response / ITupleResponse pattern.
///
/// Every Herald operation that can fail returns one of these instead of throwing,
/// giving callers a machine-friendly code, a human-readable message, and an
/// optional typed payload - all in a single object.
/// </summary>
public interface ITupleResponse
{
    /// <summary>True when the operation succeeded (code == 0).</summary>
    bool IsOk { get; }

    /// <summary>True when the operation failed.</summary>
    bool IsError { get; }

    /// <summary>The main payload (can be null).</summary>
    object? Payload { get; }

    /// <summary>Human-readable message (null when none).</summary>
    string? Message { get; }

    /// <summary>Machine-friendly status code. 0 = success.</summary>
    int Code { get; }

    /// <summary>Returns [code, payload, message] array (for interop/legacy).</summary>
    object?[] AsArray();

    /// <summary>
    /// Type-safe payload check. Supports:
    /// <list type="bullet">
    ///   <item>Built-in shorthand tokens (<c>null</c>, <c>array</c>, <c>object</c>, <c>int</c>/<c>integer</c>, <c>float</c>/<c>double</c>, <c>string</c>, <c>bool</c>/<c>boolean</c>, <c>long</c>, <c>decimal</c>, <c>datetime</c>, <c>exception</c>).</item>
    ///   <item>The simple name (not fully-qualified) of the payload's runtime type — compared case-insensitively against <c>payload.GetType().Name</c>.</item>
    ///   <item>Unions via <c>|</c> (e.g. <c>"int|long"</c>).</item>
    /// </list>
    /// AOT-safe: no reflection over type names. Callers that need
    /// fully-qualified-name matching should use <c>TupleResponse&lt;T&gt;</c>
    /// with a compile-time <c>T</c>.
    /// </summary>
    bool PayloadIs(params string[] expected);
}
