#nullable enable

namespace MMP.Herald.Quick;

/// <summary>
/// Wraps a builder registration with a name for lookup, removal, and
/// config preservation. Plugins use this to ensure their registrations
/// are discoverable and can survive Reset() when marked as protected.
///
/// Usage:
///   builder.WithEventProcessor("myPlugin.redaction", processor, protect: true);
///   builder.WithoutEventProcessor("myPlugin.redaction");
///   builder.Inspect().HasProcessor("myPlugin.redaction");
/// </summary>
public sealed record NamedRegistration<T>(
    string Name,
    T Value,
    bool IsProtected = false) where T : class;
