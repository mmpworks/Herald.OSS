#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using MMP.Herald.Failures;
using MMP.Herald.Pipeline;
using MMP.Herald.Testing;

namespace MMP.Herald.Responses;

/// <summary>
/// Instance-based exception-to-code registry. Each pipeline can own its own
/// registry with isolated code ranges, or share the global default.
///
/// Thread-safe: all mutations and reads are protected by a lock.
///
/// Usage:
///   // Per-pipeline registry
///   var registry = new ExceptionRegistry();
///   registry.Register&lt;QuestNotFoundException&gt;(5000, "QuestNotFound", "Gameplay", "...");
///
///   // Or start from Herald's built-in codes
///   var registry = ExceptionRegistry.CreateWithDefaults();
///   registry.Register&lt;QuestNotFoundException&gt;(5000, "QuestNotFound", "Gameplay", "...");
///
///   // Resolve codes
///   int code = registry.GetCode(ex);
/// </summary>
public sealed class ExceptionRegistry
{
    private readonly Dictionary<Type, int> _typeToCode;
    private readonly Dictionary<int, HeraldExceptionMap.ManifestEntry> _codeToEntry;
    private readonly object _sync = new();

    /// <summary>
    /// Create an empty registry. Register your own exception types from scratch.
    /// </summary>
    public ExceptionRegistry()
    {
        _typeToCode = new Dictionary<Type, int>();
        _codeToEntry = new Dictionary<int, HeraldExceptionMap.ManifestEntry>();
    }

    /// <summary>
    /// Create a registry pre-loaded with Herald's built-in exception codes.
    /// Use this when you want the standard codes plus your own additions.
    /// </summary>
    public static ExceptionRegistry CreateWithDefaults()
    {
        var registry = new ExceptionRegistry();
        HeraldExceptionMap.PopulateDefaults(registry._typeToCode, registry._codeToEntry);
        return registry;
    }

    // -- Core API: same signatures as HeraldExceptionMap --

    /// <summary>
    /// Resolve the best error code for a given exception.
    /// Checks registered types first, then falls back to BCL heuristics.
    /// </summary>
    public int GetCode(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        lock (_sync)
        {
            if (_typeToCode.TryGetValue(ex.GetType(), out var code))
                return code;
        }

        return HeraldExceptionMap.ClassifyBclException(ex);
    }

    /// <summary>Get the human-readable name for an error code, or null.</summary>
    public string? GetName(int code)
    {
        lock (_sync)
        {
            return _codeToEntry.TryGetValue(code, out var entry) ? entry.Name : null;
        }
    }

    /// <summary>Get the full manifest entry for an error code, or null.</summary>
    public HeraldExceptionMap.ManifestEntry? GetEntry(int code)
    {
        lock (_sync)
        {
            return _codeToEntry.TryGetValue(code, out var entry) ? entry : null;
        }
    }

    /// <summary>
    /// The complete manifest snapshot, including user-registered codes.
    /// </summary>
    public IReadOnlyDictionary<int, HeraldExceptionMap.ManifestEntry> Manifest
    {
        get
        {
            lock (_sync)
            {
                return new ReadOnlyDictionary<int, HeraldExceptionMap.ManifestEntry>(
                    new Dictionary<int, HeraldExceptionMap.ManifestEntry>(_codeToEntry));
            }
        }
    }

    /// <summary>All manifest entries sorted by code.</summary>
    public IReadOnlyList<HeraldExceptionMap.ManifestEntry> GetAllEntries()
    {
        lock (_sync)
        {
            var list = new List<HeraldExceptionMap.ManifestEntry>(_codeToEntry.Values);
            list.Sort((a, b) => a.Code.CompareTo(b.Code));
            return list;
        }
    }

    // -- Registration --

    /// <summary>
    /// Register a custom exception type with an error code.
    /// </summary>
    public void Register<TException>(
        int code, string name, string category, string description)
        where TException : Exception
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var exceptionType = typeof(TException);

        lock (_sync)
        {
            if (_typeToCode.ContainsKey(exceptionType))
                throw new ArgumentException(
                    $"Exception type '{exceptionType.Name}' is already registered.");

            if (_codeToEntry.ContainsKey(code))
                throw new ArgumentException(
                    $"Error code {code} is already registered as '{_codeToEntry[code].Name}'.");

            _typeToCode[exceptionType] = code;
            _codeToEntry[code] = new HeraldExceptionMap.ManifestEntry(
                code, name, exceptionType.Name, category, description);
        }
    }

    /// <summary>
    /// Register using a pre-built ManifestEntry.
    /// </summary>
    public void Register<TException>(HeraldExceptionMap.ManifestEntry entry)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(entry);

        var exceptionType = typeof(TException);

        lock (_sync)
        {
            if (_typeToCode.ContainsKey(exceptionType))
                throw new ArgumentException(
                    $"Exception type '{exceptionType.Name}' is already registered.");

            if (_codeToEntry.ContainsKey(entry.Code))
                throw new ArgumentException(
                    $"Error code {entry.Code} is already registered as '{_codeToEntry[entry.Code].Name}'.");

            _typeToCode[exceptionType] = entry.Code;
            _codeToEntry[entry.Code] = entry;
        }
    }

    /// <summary>
    /// Update an existing entry's metadata.
    /// </summary>
    public bool Update(int code, string name, string category, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        lock (_sync)
        {
            if (!_codeToEntry.TryGetValue(code, out var existing))
                return false;

            _codeToEntry[code] = existing with
            {
                Name = name,
                Category = category,
                Description = description
            };
            return true;
        }
    }

    /// <summary>
    /// Remove a registration by code. Built-in codes (below 5000) are protected.
    /// </summary>
    public bool Remove(int code)
    {
        lock (_sync)
        {
            if (code < HeraldErrorCodes.UserDefined)
                return false;

            if (!_codeToEntry.Remove(code))
                return false;

            Type? typeToRemove = null;
            foreach (var kvp in _typeToCode)
            {
                if (kvp.Value == code)
                {
                    typeToRemove = kvp.Key;
                    break;
                }
            }

            if (typeToRemove is not null)
                _typeToCode.Remove(typeToRemove);

            return true;
        }
    }

    /// <summary>
    /// Remove a registration by exception type. Built-in codes are protected.
    /// </summary>
    public bool Remove<TException>() where TException : Exception
    {
        var exceptionType = typeof(TException);

        lock (_sync)
        {
            if (!_typeToCode.TryGetValue(exceptionType, out var code))
                return false;

            if (code < HeraldErrorCodes.UserDefined)
                return false;

            _typeToCode.Remove(exceptionType);
            _codeToEntry.Remove(code);
            return true;
        }
    }

    /// <summary>Check whether an exception type is registered.</summary>
    public bool Has<TException>() where TException : Exception
    {
        lock (_sync)
        {
            return _typeToCode.ContainsKey(typeof(TException));
        }
    }

    /// <summary>Check whether an error code is registered.</summary>
    public bool Has(int code)
    {
        lock (_sync)
        {
            return _codeToEntry.ContainsKey(code);
        }
    }
}
