#nullable enable

using System;

namespace MMP.Herald.Responses;

/// <summary>
/// Immutable Tuple-style response carrier.
/// For type-safe payloads prefer the generic version: TupleResponse&lt;T&gt;.
/// </summary>
public sealed class TupleResponse : ITupleResponse
{
    public bool IsOk { get; }
    public bool IsError => !IsOk;
    public object? Payload { get; }
    public string? Message { get; }
    public int Code { get; }

    private TupleResponse(bool ok, object? payload, string? message, int code) {
        IsOk = ok;
        Payload = payload;
        Message = message;
        Code = code;
    }

    // -- Static factories (composable, predictable) --

    public static TupleResponse Ok(object? payload = null, string? message = null)
        => new(true, payload, message, HeraldErrorCodes.Ok);

    public static TupleResponse Error(int code = 1, string? message = null, object? payload = null) {
        if (code == 0) code = 1; // enforce non-zero on error
        return new(false, payload, message, code);
    }

    public static TupleResponse Create(int code, object? payload = null, string? message = "ok")
        => new(code == 0, payload, message, code);

    /// <summary>
    /// Wrap an exception into a TupleResponse using HeraldExceptionMap to resolve the code.
    /// </summary>
    public static TupleResponse FromException(Exception ex)
        => new(false, ex, ex.Message, HeraldExceptionMap.GetCode(ex));

    /// <summary>
    /// Wrap an exception with a specific code override.
    /// </summary>
    public static TupleResponse FromException(Exception ex, int code)
        => new(false, ex, ex.Message, code == 0 ? 1 : code);

    // -- ITupleResponse --

    public object?[] AsArray() => [Code, Payload, Message];

    public bool PayloadIs(params string[] expected) {
        if (expected.Length == 0) return false;

        var value = Payload;

        foreach (var token in expected)
        {
            foreach (var part in token.Split('|',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (Matches(part, value))
                    return true;
            }
        }
        return false;
    }

    // -- Deconstruction --

    public void Deconstruct(out int code, out object? payload, out string? message) {
        code = Code;
        payload = Payload;
        message = Message;
    }

    public override string ToString()
        => $"TupleResponse {{ Code={Code}, IsOk={IsOk}, Message=\"{Message}\" }}";

    // -- Type matching --
    //
    // P2 dropped the `Type.GetType(string)` fallback so this stays AOT-safe.
    // The shorthand list below covers every built-in primitive callers
    // reach for; anything beyond that must either be named by its
    // runtime simple-name (which the last arm compares via
    // `value.GetType().Name`) or use the strongly-typed variant on
    // `TupleResponse<T>`. The contract shift: "pass a fully-qualified
    // type name and expect reflection to resolve it" no longer works
    // under AOT, but "pass the simple name of a type the payload
    // already carries" still does.

    private static bool Matches(string typeToken, object? value) {
        var lower = typeToken.ToLowerInvariant();

        return lower switch
        {
            "null" => value is null,
            "array" => value is Array or System.Collections.IEnumerable,
            "object" => value is not null,
            "int" or "integer" => value is int,
            "float" or "double" => value is float or double,
            "string" => value is string,
            "bool" or "boolean" => value is bool,
            "long" => value is long,
            "decimal" => value is decimal,
            "datetime" => value is DateTime,
            "exception" => value is Exception,
            _ when value?.GetType().Name.Equals(typeToken, StringComparison.OrdinalIgnoreCase) == true => true,
            _ => false
        };
    }
}

/// <summary>
/// Generic, type-safe TupleResponse.
/// Preferred over the untyped version when the payload type is known at compile time.
/// </summary>
public sealed class TupleResponse<T> : ITupleResponse
{
    public bool IsOk { get; }
    public bool IsError => !IsOk;

    /// <summary>Typed payload. Default(T) on error paths.</summary>
    public T? TypedPayload { get; }

    object? ITupleResponse.Payload => TypedPayload;
    public string? Message { get; }
    public int Code { get; }

    private TupleResponse(bool ok, T? payload, string? message, int code) {
        IsOk = ok;
        TypedPayload = payload;
        Message = message;
        Code = code;
    }

    // -- Static factories --

    public static TupleResponse<T> Ok(T? payload = default, string? message = null)
        => new(true, payload, message, HeraldErrorCodes.Ok);

    public static TupleResponse<T> Error(int code = 1, string? message = null, T? payload = default) {
        if (code == 0) code = 1;
        return new(false, payload, message, code);
    }

    public static TupleResponse<T> FromException(Exception ex)
        => new(false, default, ex.Message, HeraldExceptionMap.GetCode(ex));

    // -- ITupleResponse --

    public object?[] AsArray() => [Code, TypedPayload, Message];

    public bool PayloadIs(params string[] expected) {
        if (expected.Length == 0) return false;
        var value = TypedPayload;

        foreach (var token in expected)
        {
            foreach (var part in token.Split('|',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (MatchesType(part, value))
                    return true;
            }
        }
        return false;
    }

    // -- Deconstruction --

    public void Deconstruct(out int code, out T? payload, out string? message) {
        code = Code;
        payload = TypedPayload;
        message = Message;
    }

    public override string ToString()
        => $"TupleResponse<{typeof(T).Name}> {{ Code={Code}, IsOk={IsOk}, Message=\"{Message}\" }}";

    // -- Implicit conversion from typed to untyped --

    public TupleResponse ToUntyped()
        => IsOk
            ? TupleResponse.Ok(TypedPayload, Message)
            : TupleResponse.Error(Code, Message, TypedPayload);

    // P2 dropped the `Type.GetType(string)` fallback. The generic
    // variant keeps the typeof(T) check (which resolves statically at
    // AOT time) and the simple-name compare against the runtime value's
    // type — both AOT-safe.
    private static bool MatchesType(string typeToken, object? value) {
        var lower = typeToken.ToLowerInvariant();
        return lower switch
        {
            "null" => value is null,
            "object" => value is not null,
            _ when typeof(T).Name.Equals(typeToken, StringComparison.OrdinalIgnoreCase) => value is not null,
            _ when value?.GetType().Name.Equals(typeToken, StringComparison.OrdinalIgnoreCase) == true => true,
            _ => false
        };
    }
}
