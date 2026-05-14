#nullable enable

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Compiled entry point for a kernel-eligible pipeline. A kernel encapsulates
/// the filter + fan-out work that the decorator chain would otherwise do,
/// but emitted as a single method over a <see cref="LogEventBuffer"/> so the
/// JIT can keep everything in registers.
///
/// The kernel is produced once at pipeline compose time. Hot-reload
/// recompiles the kernel — same boundary as today's chain rebuild.
/// </summary>
public delegate void LogKernel(in LogEventBuffer buffer);
