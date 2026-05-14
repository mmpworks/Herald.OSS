#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Responses;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Registry of pipeline components assembled during Build().
/// Each decorator type occupies exactly one slot (the pipeline is a linear chain
/// with one decorator per position). Components self-register during assembly.
///
/// Thread Safety:
///   Uses ConcurrentDictionary for both type-based and name-based storage.
///   All operations (Register, Unregister, Get, TryGet, Require, Has) are safe
///   for concurrent access from any thread at any time.
///   RebuildFrom() creates a new PipelineAccessor for the new pipeline;
///   the old accessor remains valid for in-flight reads.
///
/// Five access strategies:
///   Get&lt;T&gt;()            - returns null if absent (quick check, chain with ?.)
///   TryGet&lt;T&gt;()         - returns TupleResponse&lt;T&gt; (safe result pattern, never throws)
///   Require&lt;T&gt;()        - throws if absent (strict, for required dependencies)
///   GetOrDefault&lt;T&gt;(def) - returns the default if absent (caller supplies fallback)
///   Get&lt;T&gt;(name)        - name-based lookup for plugin/custom components
///
/// Mutation:
///   Register&lt;T&gt;(component)  - add or replace by type
///   Register(name, component)  - add or replace by name
///   Unregister&lt;T&gt;()          - remove by type, returns TupleResponse with the removed component
///   Unregister(name)           - remove by name, returns TupleResponse with the removed component
///
/// Thread-safe: uses ConcurrentDictionary for both type-based and name-based storage.
/// Safe for concurrent reads and writes from any thread at any time.
/// </summary>
public sealed class PipelineAccessor
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, object> _components = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _named = new(StringComparer.OrdinalIgnoreCase);

    // -- Type-based registration (known pipeline types) --

    /// <summary>
    /// Register a pipeline component by its compile-time type.
    /// Later calls with the same type overwrite earlier ones.
    /// </summary>
    public PipelineAccessor Register<T>(T component) where T : class {
        _components[typeof(T)] = component;
        return this;
    }

    /// <summary>
    /// Register a component by its runtime type. Used when the concrete type
    /// is not known at compile time (e.g., iterating a list of decorators).
    /// </summary>
    public PipelineAccessor Register(object component) {
        ArgumentNullException.ThrowIfNull(component);
        _components[component.GetType()] = component;
        return this;
    }

    // -- Name-based registration (extensible, future-proof) --

    /// <summary>
    /// Register a component by name. Use for custom/plugin pipeline components
    /// whose types are not known at compile time.
    ///
    ///   accessor.Register("myPlugin.rateLimiter", rateLimiter);
    ///   var limiter = accessor.Get("myPlugin.rateLimiter");
    /// </summary>
    public PipelineAccessor Register(string name, object component) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(component);
        _named[name] = component;
        return this;
    }

    // -- Type-based queries --

    /// <summary>
    /// Get a component by type. Returns null if absent.
    /// Quick and simple -- chain with ?. for no-op on absence.
    /// </summary>
    public T? Get<T>() where T : class =>
        _components.TryGetValue(typeof(T), out var c) ? (T)c : null;

    /// <summary>
    /// Get a component by type as a TupleResponse. Never throws.
    /// Returns Ok with the component as payload, or Error with a descriptive message.
    /// Use when you want to handle absence without null checks or exceptions.
    ///
    ///   var (code, async, message) = result.Pipeline.TryGet&lt;AsyncLogger&gt;();
    ///   if (code == 0) { /* use async */ }
    ///   else { logger.Warn(LogCategory.App, "No async logger: {message}", ...); }
    /// </summary>
    public TupleResponse<T> TryGet<T>() where T : class =>
        _components.TryGetValue(typeof(T), out var c) && c is T typed
            ? TupleResponse<T>.Ok(typed)
            : TupleResponse<T>.Error(
                HeraldErrorCodes.Pipeline.ComponentNotFound,
                $"Pipeline component {typeof(T).Name} is not registered.");

    /// <summary>
    /// Get a component by type. Throws if absent.
    /// Use when the component is required for correct operation.
    /// </summary>
    public T Require<T>() where T : class =>
        _components.TryGetValue(typeof(T), out var c)
            ? (T)c
            : throw new InvalidOperationException(
                $"Pipeline component {typeof(T).Name} is not present. " +
                $"Ensure it was configured during Build().");

    /// <summary>
    /// Get a component by type, or return the caller-supplied default if absent.
    /// </summary>
    public T GetOrDefault<T>(T defaultValue) where T : class =>
        _components.TryGetValue(typeof(T), out var c) ? (T)c : defaultValue;

    /// <summary>Check if a component type is registered.</summary>
    public bool Has<T>() where T : class =>
        _components.ContainsKey(typeof(T));

    // -- Name-based queries --

    /// <summary>
    /// Get a component by name. Returns null if absent.
    /// </summary>
    public object? Get(string name) =>
        _named.TryGetValue(name, out var c) ? c : null;

    /// <summary>
    /// Get a named component cast to a specific type. Returns null if absent or wrong type.
    /// </summary>
    public T? Get<T>(string name) where T : class =>
        _named.TryGetValue(name, out var c) && c is T typed ? typed : null;

    /// <summary>
    /// Get a named component as a TupleResponse. Never throws.
    /// </summary>
    public TupleResponse<T> TryGet<T>(string name) where T : class =>
        _named.TryGetValue(name, out var c) && c is T typed
            ? TupleResponse<T>.Ok(typed)
            : TupleResponse<T>.Error(
                HeraldErrorCodes.Pipeline.ComponentNotFound,
                $"Pipeline component '{name}' is not registered or is not of type {typeof(T).Name}.");

    /// <summary>Check if a named component is registered.</summary>
    public bool Has(string name) =>
        _named.ContainsKey(name);

    // -- Removal --

    /// <summary>
    /// Remove a component by type. Returns Ok with the removed component,
    /// or Error if the type was not registered.
    /// </summary>
    public TupleResponse<T> Unregister<T>() where T : class
    {
        if (_components.TryRemove(typeof(T), out var removed) && removed is T typed)
            return TupleResponse<T>.Ok(typed, $"{typeof(T).Name} removed from pipeline.");
        return TupleResponse<T>.Error(
            HeraldErrorCodes.Pipeline.ComponentNotFound,
            $"Pipeline component {typeof(T).Name} was not registered.");
    }

    /// <summary>
    /// Remove a component by name. Returns Ok with the removed component,
    /// or Error if the name was not registered.
    /// </summary>
    public TupleResponse Unregister(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_named.TryRemove(name, out var removed))
            return TupleResponse.Ok(removed, $"'{name}' removed from pipeline.");
        return TupleResponse.Error(
            HeraldErrorCodes.Pipeline.ComponentNotFound,
            $"Pipeline component '{name}' was not registered.");
    }

    // -- Introspection --

    /// <summary>All registered component types.</summary>
    public IEnumerable<Type> ComponentTypes => _components.Keys;

    /// <summary>All registered component names.</summary>
    public IEnumerable<string> ComponentNames => _named.Keys;

    /// <summary>All registered component instances (both typed and named).</summary>
    public IEnumerable<object> AllComponents
    {
        get
        {
            foreach (var c in _components.Values) yield return c;
            foreach (var c in _named.Values) yield return c;
        }
    }
}
