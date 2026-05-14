#nullable enable

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Optional companion for <see cref="IConfigurablePipelineDecorator"/> that
/// lets a user-registered custom decorator contribute a fragment to the
/// kernel fast path. Implementations wrap the downstream <see cref="LogKernel"/>
/// and return a new kernel that performs the decorator's work before (or
/// after) dispatching.
///
/// <para>
/// A custom decorator that does not implement this interface forces the
/// pipeline to use the decorator chain — today's behaviour. Decorators that
/// do implement it let the pipeline stay on the kernel path, with the
/// decorator's work inlined alongside enrichment and fan-out.
/// </para>
/// </summary>
public interface IKernelDecorator
{
    /// <summary>
    /// Return a kernel that runs this decorator's logic, then forwards to
    /// <paramref name="next"/>. The returned kernel must not retain the
    /// buffer past its call — same contract as <see cref="IKernelSink"/>
    /// and <see cref="IKernelEnricher"/>.
    /// </summary>
    LogKernel Wrap(LogKernel next);
}
