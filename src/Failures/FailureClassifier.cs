#nullable enable

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace MMP.Herald.Failures;

/// <summary>
/// Classifies exceptions as Transient, Permanent, or Unknown.
/// Used by CircuitBreakerLogger to make smarter recovery decisions:
/// - Transient failures count toward the threshold normally
/// - Permanent failures trip the circuit immediately
/// - Unknown failures are treated as transient (safe default)
/// </summary>
public static class FailureClassifier
{
    public static FailureCategory Classify(Exception exception) {
        return exception switch
        {
            // Transient: likely to resolve on retry
            TimeoutException => FailureCategory.Transient,
            SocketException => FailureCategory.Transient,
            HttpRequestException httpEx when IsTransientHttpStatus(httpEx) => FailureCategory.Transient,
            OperationCanceledException => FailureCategory.Transient,

            // Permanent: will not resolve on retry
            HttpRequestException httpEx when IsPermanentHttpStatus(httpEx) => FailureCategory.Permanent,
            UnauthorizedAccessException => FailureCategory.Permanent,
            System.Security.Authentication.AuthenticationException => FailureCategory.Permanent,
            NotSupportedException => FailureCategory.Permanent,
            FormatException => FailureCategory.Permanent,

            // IO errors: some transient, some permanent
            System.IO.IOException ioEx => ClassifyIoException(ioEx),

            _ => FailureCategory.Unknown
        };
    }

    private static bool IsTransientHttpStatus(HttpRequestException httpEx) {
        if (httpEx.StatusCode is null)
        {
            return true; // No status = connection-level failure = transient
        }

        var code = (int)httpEx.StatusCode.Value;
        return code >= 500 || IsRecoverableClientStatus(code);
    }

    /// <summary>
    /// 408 (Request Timeout) and 429 (Too Many Requests) are 4xx responses that
    /// the server expects the caller to repeat. Every other 4xx is a defect in
    /// the request itself and will fail again the same way.
    /// </summary>
    private static bool IsRecoverableClientStatus(int code) {
        return code is 408 or 429;
    }

    private static bool IsPermanentHttpStatus(HttpRequestException httpEx) {
        if (httpEx.StatusCode is null)
        {
            return false;
        }

        var code = (int)httpEx.StatusCode.Value;
        return code is >= 400 and < 500 && !IsRecoverableClientStatus(code);
    }

    private static FailureCategory ClassifyIoException(System.IO.IOException ioEx) {
        // Disk full, broken pipe = transient (may clear)
        // File not found, path too long = permanent
        if (ioEx.InnerException is SocketException)
        {
            return FailureCategory.Transient;
        }

        return FailureCategory.Unknown;
    }
}
