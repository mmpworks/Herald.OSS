#nullable enable

namespace MMP.Herald.Diagnostics;

/// <summary>
/// Reserved <see cref="MMP.Herald.Events.LogEvent.GenSource"/> tokens
/// used by Herald.OSS itself to mark events whose origin is the
/// runtime, not the consuming application. Consumers who want to
/// filter or route on event provenance check the GenSource against
/// these constants.
///
/// <para>
/// User-application events default to <c>GenSource = null</c>. Any
/// non-null token whose value starts with the reserved <c>@herald.</c>
/// prefix originates inside the framework. Tokens defined here are
/// the public, stable surface — additional internal markers may
/// appear with the same prefix but are not part of the OSS API.
/// </para>
/// </summary>
public static class HeraldGenSource
{
    /// <summary>
    /// Marker token for runtime notices published through
    /// <see cref="HeraldRuntimeMessages"/>. Announcements, hot-reload
    /// status, naming-policy decisions, and similar framework-emitted
    /// diagnostics carry this stamp on the <see cref="MMP.Herald.Events.LogEvent.GenSource"/>
    /// field. The shape is the same as the auto-generated 32-hex
    /// reference token a pipeline factory would stamp on internal
    /// events — readable, easily grepable, and obviously not a user
    /// value.
    /// </summary>
    public const string RuntimeNotice = "@herald.runtime.notice";
}
