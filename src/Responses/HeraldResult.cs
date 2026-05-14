#nullable enable

using System;
using System.Threading.Tasks;

namespace MMP.Herald.Responses;

/// <summary>
/// Convenience wrapper that runs Herald operations and catches exceptions
/// into TupleResponse values. Prevents exceptions from bubbling up to
/// application code when callers prefer result-oriented error handling.
///
/// Usage:
///   var result = HeraldResult.Try(() => builder.Build());
///   if (result.IsError) HandleError(result.Code, result.Message);
///
///   var asyncResult = await HeraldResult.TryAsync(() => FlushAsync());
/// </summary>
public static class HeraldResult
{
    /// <summary>
    /// Execute an action, returning Ok on success or an error TupleResponse on exception.
    /// </summary>
    public static TupleResponse Try(Action action) {
        try
        {
            action();
            return TupleResponse.Ok();
        }
        catch (Exception ex)
        {
            return TupleResponse.FromException(ex);
        }
    }

    /// <summary>
    /// Execute a function, returning Ok with the result or an error TupleResponse on exception.
    /// </summary>
    public static TupleResponse Try<T>(Func<T> func) {
        try
        {
            var result = func();
            return TupleResponse.Ok(result);
        }
        catch (Exception ex)
        {
            return TupleResponse.FromException(ex);
        }
    }

    /// <summary>
    /// Execute a function, returning a typed TupleResponse&lt;T&gt;.
    /// </summary>
    public static TupleResponse<T> TryTyped<T>(Func<T> func) {
        try
        {
            var result = func();
            return TupleResponse<T>.Ok(result);
        }
        catch (Exception ex)
        {
            return TupleResponse<T>.FromException(ex);
        }
    }

    /// <summary>
    /// Execute an async action, returning Ok on success or an error TupleResponse on exception.
    /// </summary>
    public static async Task<TupleResponse> TryAsync(Func<Task> func) {
        try
        {
            await func().ConfigureAwait(false);
            return TupleResponse.Ok();
        }
        catch (Exception ex)
        {
            return TupleResponse.FromException(ex);
        }
    }

    /// <summary>
    /// Execute an async function, returning Ok with the result or an error on exception.
    /// </summary>
    public static async Task<TupleResponse<T>> TryAsync<T>(Func<Task<T>> func) {
        try
        {
            var result = await func().ConfigureAwait(false);
            return TupleResponse<T>.Ok(result);
        }
        catch (Exception ex)
        {
            return TupleResponse<T>.FromException(ex);
        }
    }
}
