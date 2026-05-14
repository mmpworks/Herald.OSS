#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Templating;

namespace MMP.Herald.Pooling;

/// <summary>
/// Thread-local collection pools for zero-contention reuse on the hot path.
/// Each pool retains one instance per thread. Collections are cleared on rent
/// and returned after use. Oversized collections are discarded.
///
/// Uses ThreadStaticPool&lt;T&gt; to eliminate duplicated rent/return logic.
/// Each pool type has its own [ThreadStatic] storage slot.
/// </summary>
internal static class CollectionPool
{
    private const int MaxRetainedCapacity = 64;

    // -- Pool instances (shared logic, separate storage per type) --

    private static readonly ThreadStaticPool<Dictionary<string, object?>> ContextDictPool = new(
        factory: () => new Dictionary<string, object?>(StringComparer.Ordinal),
        clear: static d => d.Clear(),
        shouldRetain: static d => d.Count <= MaxRetainedCapacity);

    private static readonly ThreadStaticPool<Dictionary<string, LogProperty>> PropertyLookupPool = new(
        factory: () => new Dictionary<string, LogProperty>(StringComparer.Ordinal),
        clear: static d => d.Clear(),
        shouldRetain: static d => d.Count <= MaxRetainedCapacity);

    private static readonly ThreadStaticPool<List<LogProperty>> PropertyListPool = new(
        factory: static () => new List<LogProperty>(),
        clear: static l => l.Clear(),
        shouldRetain: static l => l.Capacity <= MaxRetainedCapacity);

    private static readonly ThreadStaticPool<HashSet<string>> StringSetPool = new(
        factory: () => new HashSet<string>(StringComparer.Ordinal),
        clear: static s => s.Clear(),
        shouldRetain: static s => s.Count <= MaxRetainedCapacity);

    private static readonly ThreadStaticPool<List<KeyValuePair<string, object?>>> ContextPairsPool = new(
        factory: static () => new List<KeyValuePair<string, object?>>(),
        clear: static l => l.Clear(),
        shouldRetain: static l => l.Capacity <= MaxRetainedCapacity);

    private static readonly ThreadStaticPool<List<string>> StringListPool = new(
        factory: static () => new List<string>(),
        clear: static l => l.Clear(),
        shouldRetain: static l => l.Capacity <= MaxRetainedCapacity);

    // -- ThreadStatic storage slots (one per pool type per thread) --

    [ThreadStatic] private static Dictionary<string, object?>? t_contextDict;
    [ThreadStatic] private static Dictionary<string, LogProperty>? t_propertyLookup;
    [ThreadStatic] private static List<LogProperty>? t_propertyList;
    [ThreadStatic] private static HashSet<string>? t_stringSet;
    [ThreadStatic] private static List<KeyValuePair<string, object?>>? t_contextPairs;
    [ThreadStatic] private static List<string>? t_stringList;

    // -- Public API (thin wrappers delegating to pool + slot) --

    public static Dictionary<string, object?> RentContextDictionary() =>
        ContextDictPool.Rent(ref t_contextDict);

    public static void ReturnContextDictionary(Dictionary<string, object?> dict) =>
        ContextDictPool.Return(ref t_contextDict, dict);

    public static Dictionary<string, LogProperty> RentPropertyLookup() =>
        PropertyLookupPool.Rent(ref t_propertyLookup);

    public static void ReturnPropertyLookup(Dictionary<string, LogProperty> dict) =>
        PropertyLookupPool.Return(ref t_propertyLookup, dict);

    public static List<LogProperty> RentPropertyList() =>
        PropertyListPool.Rent(ref t_propertyList);

    public static void ReturnPropertyList(List<LogProperty> list) =>
        PropertyListPool.Return(ref t_propertyList, list);

    public static HashSet<string> RentStringSet() =>
        StringSetPool.Rent(ref t_stringSet);

    public static void ReturnStringSet(HashSet<string> set) =>
        StringSetPool.Return(ref t_stringSet, set);

    public static List<KeyValuePair<string, object?>> RentContextPairs() =>
        ContextPairsPool.Rent(ref t_contextPairs);

    public static void ReturnContextPairs(List<KeyValuePair<string, object?>> list) =>
        ContextPairsPool.Return(ref t_contextPairs, list);

    public static List<string> RentStringList() =>
        StringListPool.Rent(ref t_stringList);

    public static void ReturnStringList(List<string> list) =>
        StringListPool.Return(ref t_stringList, list);
}