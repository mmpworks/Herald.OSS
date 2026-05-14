#nullable enable

namespace MMP.Herald.Addons.ManagementApi.Entities;

/// <summary>
/// Identity for any mutable thing in a Herald pipeline. Tenant + Pipeline +
/// Key disambiguates the resource within the management API surface; Kind
/// selects which <see cref="IEntityKindPolicy"/> handles serialize / restore /
/// validate / clear for that resource.
///
/// E1 introduces the type alongside the policy interface. Per-tenant scoping
/// at the policy level lands with L1 (Lease) and L2 (PolicyResolver); for E1
/// the policy contract only uses Kind. Tenant and Pipeline travel with the
/// record so L1 wiring does not need to introduce a new identity type.
/// </summary>
public sealed record EntityRef(string Kind, string Tenant, string Pipeline, string Key);
