#nullable enable

using System;

namespace MMP.Herald;

/// <summary>
/// Thrown by <see cref="HeraldEditionGate.Require(HeraldEdition, string)"/>
/// when the running edition does not include the requested feature's
/// minimum edition. Distinct from any licensing exception type so sibling
/// Herald.OSS does NOT take a forward dependency on a licensing assembly:
/// licensing types remain server-side and IP-firewalled out of OSS.
///
/// <para>
/// Server-side handlers that previously caught a licensing exception when
/// a feature was unavailable should catch <see cref="HeraldEditionRequirementException"/>
/// alongside it — the gate (this exception) and the verifier (the licensing
/// exception) report two different concerns: "the running edition is too
/// low for this feature" versus "the license token is missing or invalid."
/// </para>
/// </summary>
public sealed class HeraldEditionRequirementException : Exception
{
    /// <summary>Name of the feature whose gate was crossed.</summary>
    public string Feature { get; }

    /// <summary>Edition required to use the feature.</summary>
    public HeraldEdition Required { get; }

    /// <summary>Edition the running process actually reports.</summary>
    public HeraldEdition Current { get; }

    public HeraldEditionRequirementException(
        string feature,
        HeraldEdition required,
        HeraldEdition current)
        : base(BuildMessage(feature, required, current))
    {
        Feature = feature;
        Required = required;
        Current = current;
    }

    private static string BuildMessage(string feature, HeraldEdition required, HeraldEdition current)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        ArgumentNullException.ThrowIfNull(required);
        ArgumentNullException.ThrowIfNull(current);

        return $"{feature} requires the {required.Name} edition. " +
               $"Current edition: {current.Name}. " +
               $"Install a Herald module that advertises the {required.Name} tier or use a lower-tier alternative.";
    }
}
