#nullable enable

using System.Runtime.CompilerServices;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Zero-allocation builders for small property counts. Each buffer uses
/// <see cref="InlineArrayAttribute"/> to inline N <see cref="LogPropertyCompact"/>
/// entries directly into the struct layout — declaring a local goes on the
/// stack, no heap allocation for the buffer itself.
///
/// <para>
/// <b>Usage pattern.</b> The C# compiler emits implicit conversions to
/// <c>Span&lt;LogPropertyCompact&gt;</c> and <c>ReadOnlySpan&lt;LogPropertyCompact&gt;</c>
/// for any <see cref="InlineArrayAttribute"/>-decorated struct, so
/// <c>InfoCompact(category, template, buffer)</c> works directly.
/// </para>
/// <code>
/// var buf = new LogPropertyBuffer4();
/// buf[0] = new LogPropertyCompact("Name", name);
/// buf[1] = new LogPropertyCompact("City", city);
/// buf[2] = new LogPropertyCompact("Action", action);
/// buf[3] = new LogPropertyCompact("Resource", resource);
/// logger.InfoCompact(category, template, buf);
/// </code>
///
/// <para>
/// <c>stackalloc</c> is not available for <see cref="LogPropertyCompact"/>
/// because it contains reference fields (<c>string Name</c>,
/// <c>object? Value</c>) and is therefore a managed type by C# rules. This
/// inline-array pattern gives the same stack-only behavior without the
/// managed-type restriction.
/// </para>
///
/// <para>
/// Four is the headline size because most structured log calls have
/// four-or-fewer properties. Sizes 1 / 2 / 8 / 16 ship alongside so the
/// common range is covered; pick the smallest buffer that fits the call.
/// </para>
/// </summary>
[InlineArray(1)]
public struct LogPropertyBuffer1
{
    private LogPropertyCompact _element0;
}

/// <summary>Inline 2-property buffer. See <see cref="LogPropertyBuffer1"/> for usage.</summary>
[InlineArray(2)]
public struct LogPropertyBuffer2
{
    private LogPropertyCompact _element0;
}

/// <summary>Inline 4-property buffer. See <see cref="LogPropertyBuffer1"/> for usage.</summary>
[InlineArray(4)]
public struct LogPropertyBuffer4
{
    private LogPropertyCompact _element0;
}

/// <summary>Inline 8-property buffer. See <see cref="LogPropertyBuffer1"/> for usage.</summary>
[InlineArray(8)]
public struct LogPropertyBuffer8
{
    private LogPropertyCompact _element0;
}

/// <summary>Inline 16-property buffer. See <see cref="LogPropertyBuffer1"/> for usage.</summary>
[InlineArray(16)]
public struct LogPropertyBuffer16
{
    private LogPropertyCompact _element0;
}

/// <summary>
/// Inline 8-slot buffer for the full-shape <see cref="MMP.Herald.Templating.LogProperty"/>
/// type. Same <see cref="InlineArrayAttribute"/> pattern as the
/// <see cref="LogPropertyBuffer8"/> (which holds the kernel-friendly
/// <c>LogPropertyCompact</c>). Used by <c>DispatchViaKernel</c> to
/// avoid the heap allocation in <c>CopyPropertiesToSpan</c> when a
/// caller hands in an <c>IReadOnlyList&lt;LogProperty&gt;</c> with
/// eight-or-fewer entries — the 99 %+ case.
/// </summary>
[InlineArray(8)]
public struct LogPropertyKernelBuffer8
{
    private MMP.Herald.Templating.LogProperty _element0;
}
