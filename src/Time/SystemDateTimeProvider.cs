#nullable enable

using System;

namespace MMP.Herald.Time;
/// <summary>
/// Production clock implementation.
/// </summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset GetUtcNow()
    {
        return DateTimeOffset.UtcNow;
    }
}