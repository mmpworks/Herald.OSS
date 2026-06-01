// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Threading;
using MMP.Herald.Output.Writers;
using MMP.RollingFiles;

namespace Herald.Sinks.File.Providers;

/// <summary>
/// Adapts <see cref="FilesManager"/> to the in-Core
/// <see cref="ILineWriter"/> contract. The sink providers in this
/// package were built against <c>ILineWriter</c>; the shim keeps that
/// surface intact while every actual write routes through the new
/// <c>MMP.RollingFiles</c> library.
///
/// <para>
/// <b>Lazy construction.</b> The legacy <c>RollingFileLineWriter</c>
/// did not open the file or touch the filesystem at construction time
/// — the first <c>WriteLine</c> created the directory and opened the
/// stream. <see cref="FilesManager"/> is eager: its constructor opens
/// the active file. The shim restores the legacy timing by deferring
/// the <c>FilesManager</c> build until the first <c>WriteLine</c>, so
/// pipeline commits and configuration round-trips do not perform any
/// filesystem IO. That keeps tests that commit a v2 config without
/// also producing log events from accidentally writing to disk.
/// </para>
///
/// <para>
/// <b>Concurrency contract.</b> <c>WriteLine</c> is safe to call from
/// many threads — <see cref="LazyInitializer.EnsureInitialized{T}(ref T, ref bool, ref object, Func{T})"/>
/// gives the publish correct memory-model semantics on every
/// architecture (x86, ARM64). What is <i>not</i> safe is calling
/// <see cref="Dispose"/> concurrently with <c>WriteLine</c> on the
/// same instance: the active <see cref="FilesManager"/> reference is
/// single-owner and disposal nulls it. Callers must coordinate
/// disposal with their own quiescence (no in-flight writes) — the
/// sink lifecycle in <c>Herald.Sinks.File</c> already does this
/// because the pipeline drains before sinks are disposed.
/// </para>
///
/// <para>Stays internal because <see cref="FilesManager"/> is the
/// canonical public surface for any caller that does not already need
/// <c>ILineWriter</c>. Outside callers should depend on
/// <c>MMP.RollingFiles</c> directly.</para>
/// </summary>
internal sealed class FilesManagerLineWriter : ILineWriter, IDisposable
{
    private readonly FilesManagerPolicy _policy;

    // Not readonly — LazyInitializer.EnsureInitialized takes the lock
    // by `ref object`. The instance never reassigns it; the field is
    // logically readonly.
    private object _sync = new();

    private FilesManager? _manager;
    private bool _initialized;
    private bool _disposed;

    public FilesManagerLineWriter(FilesManagerPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public void WriteLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureManager().AppendLine(line);
    }

    // Cognitive Complexity note: LazyInitializer wraps the
    // double-checked-locking pattern with the correct memory-model
    // semantics for the publish (the constructor's stores are
    // visible to other threads before the manager reference is). A
    // hand-rolled `Volatile.Read` + plain `??=` would be wrong on
    // ARM64 because the constructor's field stores can reorder past
    // the reference store.
    private FilesManager EnsureManager()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return LazyInitializer.EnsureInitialized(
            ref _manager,
            ref _initialized,
            ref _sync,
            () => new FilesManager(_policy))!;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _manager?.Dispose();
            _manager = null;
        }
    }
}
