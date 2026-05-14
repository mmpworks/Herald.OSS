// Lines 1-11
#nullable enable

using System;

namespace MMP.Herald.Time;
/// <summary>
/// Time abstraction for deterministic tests and dependency injection.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset GetUtcNow();
}