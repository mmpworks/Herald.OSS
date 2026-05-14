#nullable enable

using System;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Represents a single logging scope that can be unwound deterministically.
/// </summary>
public interface ILogScope : IDisposable
{
}