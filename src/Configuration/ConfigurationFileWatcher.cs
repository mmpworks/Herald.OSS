#nullable enable

using System;
using System.IO;
using System.Threading;

namespace MMP.Herald.Configuration;

/// <summary>
/// Watches a configuration file for changes and fires a callback with debouncing.
/// FileSystemWatcher often fires multiple events per save; the debounce timer
/// coalesces rapid notifications into a single callback invocation.
/// </summary>
public sealed class ConfigurationFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Action<string> _onConfigChanged;
    private readonly Action<Exception, string>? _onCallbackError;
    private readonly Timer _debounceTimer;
    private readonly int _debounceMs;
    private volatile bool _isDisposed;

    public ConfigurationFileWatcher(
        string filePath,
        Action<string> onConfigChanged,
        int debounceMs = 500,
        Action<Exception, string>? onCallbackError = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(onConfigChanged);

        if (debounceMs <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(debounceMs), debounceMs, "Debounce interval must be greater than zero.");
        }

        _onConfigChanged = onConfigChanged;
        _onCallbackError = onCallbackError;
        _debounceMs = debounceMs;

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath))
            ?? throw new InvalidOperationException($"Cannot determine directory for '{filePath}'.");
        var fileName = Path.GetFileName(filePath);

        _debounceTimer = new Timer(OnDebounceElapsed, filePath, Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_isDisposed)
        {
            return;
        }

        _debounceTimer.Change(_debounceMs, Timeout.Infinite);
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_isDisposed || state is not string filePath)
        {
            return;
        }

        try
        {
            _onConfigChanged(filePath);
        }
        catch (Exception ex)
        {
            // Never let the watcher thread die. Report via the optional
            // callback if the caller provided one; otherwise the exception
            // is intentionally consumed (caller's HotReloadableLoggingBootstrap
            // handles errors internally).
            _onCallbackError?.Invoke(ex, filePath);
        }
    }

    /// <summary>
    /// Allows manual trigger of the reload callback, bypassing the file watcher.
    /// Useful as a fallback when FileSystemWatcher is unreliable on certain platforms.
    /// </summary>
    public void TriggerManualReload(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        _onConfigChanged(filePath);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFileChanged;
        _watcher.Dispose();
        _debounceTimer.Dispose();
    }
}
