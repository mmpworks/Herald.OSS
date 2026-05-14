#nullable enable

using System;

namespace MMP.Herald.Testing;

/// <summary>
/// Exception thrown when a log assertion fails during testing.
/// </summary>
public sealed class LogAssertionException : Exception
{
    public LogAssertionException(string message) : base(message) { }
    public LogAssertionException(string message, Exception innerException) : base(message, innerException) { }
}
