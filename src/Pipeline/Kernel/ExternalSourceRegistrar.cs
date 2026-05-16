#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Coordinates external-source registrations against a pipeline's sink
/// gates. Holds the timestamp lock that prevents a rogue caller from
/// re-registering an existing key under different terms, derives a new
/// key from <c>(referenceSource, rawKey, timestamp)</c>, and pushes the
/// derived key into the named sinks' accepted-source lists.
///
/// <para>
/// <b>Threat model.</b> Defends against in-process callers who can reach
/// the public registrar API but don't already know an existing
/// registration's timestamp. Re-registering the same raw key with a
/// different timestamp throws — the registrar enforces "first writer
/// owns the key for the lifetime of the registrar." Adding more sinks to
/// an existing registration requires re-presenting the original
/// timestamp, so a caller without the timestamp can neither displace
/// nor extend an existing registration.
/// </para>
///
/// <para>
/// <b>Crypto.</b> Derived keys come from <c>HMAC-SHA256(referenceSource,
/// rawKey || timestamp_le_bytes)</c> truncated to 16 bytes (128 bits of
/// entropy) and formatted as 32 lowercase hex chars — the same shape
/// the auto-generated reference token uses. HMAC is overkill for the
/// in-process threat model but the cost is one-shot (per registration,
/// not per event), and the property — "no in-process caller without the
/// reference token can produce a valid derived key" — is what makes the
/// timestamp lock load-bearing.
/// </para>
///
/// <para>
/// <b>Hot reload.</b> The registrar's snapshot is exposed via
/// <see cref="Registrations"/> so a downstream rebuild step can replay
/// every active registration against the new sink gates via
/// <see cref="RebindGates"/>. Without that, a hot reload silently
/// revokes every external source.
/// </para>
///
/// <para>
/// <b>Architectural seam.</b> Herald.OSS does not stamp <c>GenSource</c>
/// on events through its default factories, and no OSS code path
/// constructs a registrar by default. The registrar class is present so
/// downstream commercial wrappers (or an OSS adopter that wants
/// multi-tenant routing) can compose <see cref="GenSourceGatedSink"/>
/// gates with an HMAC-keyed registrar without re-implementing the
/// timestamp lock, persistence boundary, or rebind contract.
/// </para>
/// </summary>
public sealed class ExternalSourceRegistrar
{
    private readonly string _referenceSource;
    private readonly Dictionary<string, GenSourceGatedSink> _gatesByAlias;
    private readonly Dictionary<string, RegistrationEntry> _registrations =
        new(StringComparer.Ordinal);
    private readonly IRegistrarStore _store;
    private readonly object _lock = new();

    /// <param name="referenceSource">
    /// The pipeline's reference token. Used as the HMAC key when deriving
    /// per-registration tokens and as the "always accepted" source on
    /// every gate the registrar owns.
    /// </param>
    /// <param name="gatesByAlias">
    /// Map from sink-alias string to the gate wrapping that sink. The
    /// registrar pushes derived keys into the named gates'
    /// accepted-source lists.
    /// </param>
    /// <param name="store">
    /// Persistence store. Defaults to <see cref="NullRegistrarStore.Instance"/>
    /// when null — registrations live in memory only and disappear on
    /// process restart. Pass <see cref="FileRegistrarStore"/> for
    /// JSON-on-disk persistence, or a downstream cloud-backed
    /// implementation for vault-backed storage.
    /// </param>
    public ExternalSourceRegistrar(
        string referenceSource,
        IReadOnlyDictionary<string, GenSourceGatedSink> gatesByAlias,
        IRegistrarStore? store = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(referenceSource);
        ArgumentNullException.ThrowIfNull(gatesByAlias);

        _referenceSource = referenceSource;
        _gatesByAlias = new Dictionary<string, GenSourceGatedSink>(
            gatesByAlias, StringComparer.OrdinalIgnoreCase);
        _store = store ?? NullRegistrarStore.Instance;
    }

    /// <summary>The persistence store this registrar writes to.</summary>
    public IRegistrarStore Store => _store;

    /// <summary>
    /// The pipeline's reference token. Hot reload reads this so the rebuilt
    /// pipeline can be wired with the same token, which keeps existing
    /// derived keys valid against the new sink gates.
    /// </summary>
    public string ReferenceSource => _referenceSource;

    /// <summary>
    /// Snapshot of currently-active registrations. The registrar's
    /// internal state copies into a fresh dictionary on every read so
    /// callers can iterate without taking the registrar's lock. Used by
    /// hot reload to replay registrations against new sink gates.
    /// </summary>
    public IReadOnlyDictionary<string, RegistrationEntry> Registrations
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, RegistrationEntry>(
                    _registrations, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>
    /// Register an external source. On first registration of
    /// <paramref name="rawKey"/>, derives a key, stores the entry, and
    /// pushes the derived key into each of the named sinks' gates.
    /// On a subsequent registration with the same <paramref name="rawKey"/>:
    /// the <paramref name="timestamp"/> must match the original; otherwise
    /// the call throws. Matching timestamps add any new sink aliases to
    /// the existing registration (idempotent for already-named sinks).
    /// </summary>
    /// <returns>
    /// The derived key. External callers stamp this on events they
    /// construct directly so the named sinks accept those events.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="rawKey"/> is empty, or one of the sink aliases
    /// doesn't correspond to a registered gate.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A registration with the same <paramref name="rawKey"/> exists with
    /// a different timestamp (the lock is on first-writer-wins).
    /// </exception>
    public string Register(string rawKey, long timestamp, params string[] sinkAliases)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawKey);
        ArgumentNullException.ThrowIfNull(sinkAliases);
        if (sinkAliases.Length == 0)
            throw new ArgumentException(
                "Must specify at least one sink alias.", nameof(sinkAliases));

        // Validate every alias up front so a partial registration doesn't
        // half-apply on a bad alias.
        foreach (var alias in sinkAliases)
        {
            if (!_gatesByAlias.ContainsKey(alias))
                throw new ArgumentException(
                    $"No gate registered for sink alias '{alias}'.", nameof(sinkAliases));
        }

        lock (_lock)
        {
            if (_registrations.TryGetValue(rawKey, out var existing))
            {
                if (existing.Timestamp != timestamp)
                {
                    throw new InvalidOperationException(
                        $"Key '{rawKey}' is already registered with a different " +
                        $"timestamp. Re-registration requires the original timestamp; " +
                        $"this is the registrar's anti-replay lock.");
                }

                // Same key, same timestamp — add any newly-named sinks. Track
                // additions so we can roll back if the persistence store
                // refuses the write.
                var addedAliases = new List<string>();
                foreach (var alias in sinkAliases)
                {
                    if (existing.SinkAliases.Add(alias))
                    {
                        _gatesByAlias[alias].RegisterAcceptedSource(existing.DerivedKey);
                        addedAliases.Add(alias);
                    }
                }

                if (addedAliases.Count > 0)
                {
                    PersistOrRollbackExtension(existing, addedAliases);
                }
                return existing.DerivedKey;
            }

            // First registration of this raw key. Derive, store, push.
            var derived = DeriveKey(_referenceSource, rawKey, timestamp);
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pushed = new List<string>();
            foreach (var alias in sinkAliases)
            {
                if (aliases.Add(alias))
                {
                    _gatesByAlias[alias].RegisterAcceptedSource(derived);
                    pushed.Add(alias);
                }
            }

            var entry = new RegistrationEntry(
                DerivedKey: derived,
                Timestamp: timestamp,
                SinkAliases: aliases);
            _registrations[rawKey] = entry;

            PersistOrRollbackNew(rawKey, entry, pushed);
            return derived;
        }
    }

    // Persist after a fresh registration; on Save failure, undo the
    // in-memory changes so persistence and runtime stay consistent.
    private void PersistOrRollbackNew(
        string rawKey, RegistrationEntry entry, List<string> pushed)
    {
        try
        {
            _store.Save(BuildSnapshot());
        }
        catch
        {
            foreach (var alias in pushed)
            {
                if (_gatesByAlias.TryGetValue(alias, out var gate))
                    gate.RevokeAcceptedSource(entry.DerivedKey);
            }
            _registrations.Remove(rawKey);
            throw;
        }
    }

    // Persist after extending an existing registration with new sink
    // aliases. On Save failure, undo just the alias additions — the
    // base registration stays.
    private void PersistOrRollbackExtension(
        RegistrationEntry existing, List<string> addedAliases)
    {
        try
        {
            _store.Save(BuildSnapshot());
        }
        catch
        {
            foreach (var alias in addedAliases)
            {
                existing.SinkAliases.Remove(alias);
                if (_gatesByAlias.TryGetValue(alias, out var gate))
                    gate.RevokeAcceptedSource(existing.DerivedKey);
            }
            throw;
        }
    }

    /// <summary>
    /// Withdraw a registration. The original timestamp must match the
    /// stored one — same lock semantics as <see cref="Register"/>. The
    /// derived key is revoked from every gate that had it. Returns true
    /// if a registration was removed; false when the raw key wasn't
    /// registered.
    /// </summary>
    public bool TryRevoke(string rawKey, long timestamp)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawKey);

        lock (_lock)
        {
            if (!_registrations.TryGetValue(rawKey, out var existing))
                return false;

            if (existing.Timestamp != timestamp)
            {
                throw new InvalidOperationException(
                    $"Key '{rawKey}' has a different timestamp. Revocation " +
                    $"requires the original timestamp.");
            }

            // Snapshot what we'll remove so we can roll back on Save
            // failure. Aliases that aren't currently in _gatesByAlias
            // (e.g. removed by a config change between registration and
            // revoke) get skipped — the in-memory entry still gets
            // removed if the store accepts the write.
            var revokedFromGates = new List<string>();
            foreach (var alias in existing.SinkAliases)
            {
                if (_gatesByAlias.TryGetValue(alias, out var gate))
                {
                    gate.RevokeAcceptedSource(existing.DerivedKey);
                    revokedFromGates.Add(alias);
                }
            }
            _registrations.Remove(rawKey);

            try
            {
                _store.Save(BuildSnapshot());
            }
            catch
            {
                // Roll back: re-insert the entry and re-push to gates.
                _registrations[rawKey] = existing;
                foreach (var alias in revokedFromGates)
                {
                    if (_gatesByAlias.TryGetValue(alias, out var gate))
                        gate.RegisterAcceptedSource(existing.DerivedKey);
                }
                throw;
            }
            return true;
        }
    }

    /// <summary>
    /// Build the current state as a persistable <see cref="RegistrarSnapshot"/>.
    /// Called from inside the registrar's lock so the snapshot reflects
    /// a single consistent moment.
    /// </summary>
    private RegistrarSnapshot BuildSnapshot()
    {
        var entries = new RegistrarPersistedEntry[_registrations.Count];
        var i = 0;
        foreach (var pair in _registrations)
        {
            var aliases = new string[pair.Value.SinkAliases.Count];
            var j = 0;
            foreach (var alias in pair.Value.SinkAliases)
                aliases[j++] = alias;
            entries[i++] = new RegistrarPersistedEntry(
                RawKey: pair.Key,
                Timestamp: pair.Value.Timestamp,
                SinkAliases: aliases);
        }
        return new RegistrarSnapshot(
            SchemaVersion: RegistrarSnapshot.CurrentSchemaVersion,
            ReferenceToken: _referenceSource,
            Registrations: entries);
    }

    /// <summary>
    /// Replay every active registration against a fresh set of gates.
    /// Hot reload calls this after rebuilding the pipeline so each new
    /// sink gate inherits the external sources that were active before
    /// the rebuild. The supplied dictionary replaces the internal
    /// alias→gate map; existing registration entries stay valid because
    /// they only carry the derived key (not the gate identity).
    /// </summary>
    public void RebindGates(IReadOnlyDictionary<string, GenSourceGatedSink> newGatesByAlias)
    {
        ArgumentNullException.ThrowIfNull(newGatesByAlias);

        lock (_lock)
        {
            _gatesByAlias.Clear();
            foreach (var pair in newGatesByAlias)
            {
                _gatesByAlias[pair.Key] = pair.Value;
            }

            foreach (var (_, entry) in _registrations)
            {
                foreach (var alias in entry.SinkAliases)
                {
                    if (_gatesByAlias.TryGetValue(alias, out var gate))
                    {
                        gate.RegisterAcceptedSource(entry.DerivedKey);
                    }
                }
            }
        }
    }

    private static string DeriveKey(string referenceSource, string rawKey, long timestamp)
    {
        // HMAC-SHA256 with the reference token as the key; the message is
        // the raw key followed by 8 little-endian timestamp bytes. Truncate
        // to 16 bytes / 32 hex chars to match the reference shape.
        var keyBytes = Encoding.UTF8.GetBytes(referenceSource);
        var rawKeyByteCount = Encoding.UTF8.GetByteCount(rawKey);
        var msg = new byte[rawKeyByteCount + sizeof(long)];
        Encoding.UTF8.GetBytes(rawKey, 0, rawKey.Length, msg, 0);
        BitConverter.TryWriteBytes(msg.AsSpan(rawKeyByteCount), timestamp);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(msg);
        // Truncate to the first 16 bytes — 128 bits is collision-resistant
        // for the in-process scope and matches Guid.ToString("N") shape.
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    /// <summary>
    /// Snapshot of one registration. Exposed via <see cref="Registrations"/>
    /// for hot reload's replay path.
    /// </summary>
    public sealed record RegistrationEntry(
        string DerivedKey,
        long Timestamp,
        HashSet<string> SinkAliases);
}
