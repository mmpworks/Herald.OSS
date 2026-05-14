#nullable enable

using System;

namespace MMP.Herald.Routing;

/// <summary>
/// Opt out of auto-registration into <see cref="LogSinkProviderRegistry.Default"/>.
///
/// <para>
/// Apply to an <see cref="ILogSinkProvider"/> implementation when the sink
/// requires explicit configuration before it is safe to instantiate with a
/// default ctor — for example a provider that points at a confined path
/// resolver, holds a process-wide HTTP client the host must own, or carries
/// credentials. The <c>MMP.Herald.Generators.SinkAutoRegistrationGenerator</c>
/// skips any type carrying this attribute; consumers wire the provider up
/// manually through their host's bootstrap code instead.
/// </para>
///
/// <para>
/// The attribute is honoured at compile time by the generator; it has no
/// runtime effect. Removing it does not unregister an existing instance — a
/// process restart is required for a different auto-registration shape to
/// take effect, which matches how Herald handles license-rotation today.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NoAutoRegisterAttribute : Attribute
{
}
