#nullable enable

using System;

namespace MMP.Herald.Bootstrap;

/// <summary>
/// Source of configuration-reload signals consumed by
/// <see cref="HotReloadableLoggingBootstrap"/>. A reload source watches
/// some external surface (file, KV store, push channel) and invokes the
/// supplied callback with the latest JSON payload when the surface
/// changes.
///
/// <para>
/// <b>Why this seam exists.</b> Today the bootstrap accepts JSON through
/// <see cref="HotReloadableLoggingBootstrap.Reload(string)"/> and grows
/// a hard-coded <see cref="MMP.Herald.Configuration.ConfigurationFileWatcher"/>
/// via <c>WatchFile</c>. Other reload sources (Consul/etcd KV watch, K8s
/// ConfigMap signal, management-API push, Azure App Configuration
/// sentinel) would each land as a parallel <c>WatchXxx(...)</c>
/// overload, each duplicating debounce + error-routing glue.
/// </para>
///
/// <para>
/// The interface keeps the bootstrap blind to its supplier — a future
/// <c>K8sConfigMapReloadSource</c> ships in a separate assembly,
/// implements this contract, and the host wires it without touching
/// OSS source.
/// </para>
/// </summary>
public interface IConfigReloadSource : IDisposable
{
    /// <summary>
    /// Begin observing the source. <paramref name="onConfigChanged"/> is
    /// invoked once per change-debounce window with the latest JSON
    /// payload. Implementations are responsible for their own debouncing,
    /// retry, and rate-limiting; the bootstrap simply hands the JSON to
    /// <see cref="HotReloadableLoggingBootstrap.Reload(string)"/>.
    ///
    /// <para>
    /// The callback may run on a background thread the source owns.
    /// Sources that capture exceptions from <paramref name="onConfigChanged"/>
    /// should report through their own error channel; the bootstrap's
    /// failure-sink reporting happens inside <see cref="HotReloadableLoggingBootstrap.Reload(string)"/>
    /// and does not surface back here.
    /// </para>
    /// </summary>
    /// <param name="onConfigChanged">
    /// Callback receiving the source identifier (file path, KV key,
    /// channel id, etc.) and the latest configuration JSON. Implementations
    /// may pass an empty string for source identifier when no meaningful
    /// path applies.
    /// </param>
    void Start(Action<string, string> onConfigChanged);
}
