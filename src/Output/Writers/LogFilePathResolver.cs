#nullable enable

using System;

namespace MMP.Herald.Output.Writers;

/// <summary>
/// Default path resolver that passes paths through unchanged.
/// Does not support virtual path prefixes. Host-specific resolvers
/// handle those.
/// </summary>
public sealed class DefaultLogFilePathResolver : ILogFilePathResolver
{
    public static DefaultLogFilePathResolver Instance { get; } = new();

    public string Resolve(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return filePath;
    }
}
