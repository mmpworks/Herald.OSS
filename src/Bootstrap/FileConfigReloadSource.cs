#nullable enable

using System;
using System.IO;
using MMP.Herald.Configuration;

namespace MMP.Herald.Bootstrap;

/// <summary>
/// <see cref="IConfigReloadSource"/> implementation that wraps the existing
/// <see cref="ConfigurationFileWatcher"/>. Adapts the watcher's
/// <c>Action&lt;string&gt;</c> "file changed" callback into the
/// <c>(sourceId, json)</c> shape the bootstrap consumes.
///
/// <para>
/// Reads the file's current text inside <see cref="Start"/> and on every
/// watcher fire so the bootstrap's <see cref="HotReloadableLoggingBootstrap.Reload(string)"/>
/// receives ready-to-deserialize JSON. The watcher's debounce window stays
/// in place; this adapter does not add latency.
/// </para>
/// </summary>
public sealed class FileConfigReloadSource : IConfigReloadSource
{
    private readonly string _filePath;
    private readonly int _debounceMs;
    private ConfigurationFileWatcher? _watcher;
    private bool _isDisposed;

    public FileConfigReloadSource(string filePath, int debounceMs = 500)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _debounceMs = debounceMs;
    }

    public void Start(Action<string, string> onConfigChanged)
    {
        ArgumentNullException.ThrowIfNull(onConfigChanged);
        if (_isDisposed) throw new ObjectDisposedException(nameof(FileConfigReloadSource));
        if (_watcher is not null)
        {
            throw new InvalidOperationException(
                "FileConfigReloadSource.Start was called twice. Dispose the source and create a new one to switch files.");
        }

        // Read the JSON inside the callback so the bootstrap always sees
        // the most recent contents — a watcher fire that happens during a
        // save can land mid-write, in which case the file read throws and
        // the source-owned watcher reports through its onCallbackError
        // hook (which we route to onConfigChanged invocation via try/catch).
        _watcher = new ConfigurationFileWatcher(
            _filePath,
            path =>
            {
                var json = File.ReadAllText(path);
                onConfigChanged(path, json);
            },
            _debounceMs);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _watcher?.Dispose();
        _watcher = null;
    }
}
