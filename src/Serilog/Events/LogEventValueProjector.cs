#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using MMP.Herald.Templating;

namespace MMP.Herald.Serilog.Events;

// Guard 1 site: ONLY this file may construct the mirror LogEvent from a native LogEvent.
// Task 9 type-walk enforces this at CI time.
internal static class LogEventValueProjector
{
    private const int MaxDepth = 10;
    private static readonly HeraldPropertyValueFactory _factory = new();

    internal static ILogEventPropertyValueFactory DefaultValueFactory => _factory;
    [RequiresUnreferencedCode(
        "Value projection uses reflection on arbitrary user types. " +
        "The Herald hot path never calls this method (Guard 2).")]
    internal static Dictionary<string, LogEventPropertyValue> Project(
        MMP.Herald.Events.LogEvent native)
    {
        var result = new Dictionary<string, LogEventPropertyValue>(
            native.Properties.Count, StringComparer.Ordinal);

        foreach (var prop in native.Properties)
        {
            var rawValue = prop.ResolvedValue;
            var mode     = prop.CaptureModeOrDefault;

            LogEventPropertyValue value;
            if (mode == LogPropertyCaptureMode.Destructure)
                value = _factory.CreatePropertyValue(rawValue, destructureObjects: true);
            else if (mode == LogPropertyCaptureMode.Stringify)
                value = new ScalarValue(rawValue?.ToString());
            else
                value = new ScalarValue(rawValue);

            result.TryAdd(prop.Name, value);
        }

        return result;
    }
    private sealed class HeraldPropertyValueFactory
        : ILogEventPropertyValueFactory, ILogEventPropertyFactory
    {
        private static readonly HashSet<Type> PrimitiveTypes = new()
        {
            typeof(bool),
            typeof(byte), typeof(sbyte),
            typeof(short), typeof(ushort),
            typeof(int), typeof(uint),
            typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal),
            typeof(char), typeof(string),
            typeof(DateTime), typeof(DateTimeOffset),
            typeof(TimeSpan), typeof(Guid),
            typeof(Uri),
        };

        [RequiresUnreferencedCode("Value projection uses reflection on arbitrary user types.")]
        public LogEventPropertyValue CreatePropertyValue(object? value, bool destructureObjects)
        {
            if (!destructureObjects) return new ScalarValue(value);
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            return Walk(value, depth: 0, visited);
        }

        [RequiresUnreferencedCode("Value projection uses reflection on arbitrary user types.")]
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            => new(name, CreatePropertyValue(value, destructureObjects));
        [RequiresUnreferencedCode("Value projection uses reflection on arbitrary user types.")]
        private static LogEventPropertyValue Walk(
            object? value, int depth, HashSet<object> visited)
        {
            if (value is null) return new ScalarValue(null);
            if (depth >= MaxDepth) return new ScalarValue(value.ToString());
            if (!visited.Add(value)) return new ScalarValue("[cycle detected]");

            var type = value.GetType();
            if (PrimitiveTypes.Contains(type) || type.IsEnum)
            {
                visited.Remove(value);
                return new ScalarValue(value);
            }

            try   { return WalkComplexByType(value, depth, visited); }
            finally { visited.Remove(value); }
        }

        [RequiresUnreferencedCode("Value projection uses reflection on arbitrary user types.")]
        [SuppressMessage("Trimming", "IL2072",
            Justification = "Type is value.GetType(); correct by construction.")]
        private static LogEventPropertyValue WalkComplexByType(
            object value, int depth, HashSet<object> visited)
        {
            var type = value.GetType();
            return WalkComplex(value, type, depth, visited);
        }
        [RequiresUnreferencedCode("Value projection uses reflection on arbitrary user types.")]
        private static LogEventPropertyValue WalkComplex(
            object value,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces |
                                        DynamicallyAccessedMemberTypes.PublicProperties)]
            Type type,
            int depth,
            HashSet<object> visited)
        {
            if (TryProjectDictionary(value, type, depth, visited, out var dictVal))
                return dictVal!;
            if (value is IEnumerable seq && value is not string)
                return ProjectSequence(seq, depth, visited);
            return ProjectStructure(value, type, depth, visited);
        }
        [RequiresUnreferencedCode("Value projection uses reflection on arbitrary user types.")]
        private static bool TryProjectDictionary(
            object value,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
            Type type,
            int depth,
            HashSet<object> visited,
            out LogEventPropertyValue? result)
        {
            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType) continue;
                if (iface.GetGenericTypeDefinition() != typeof(IDictionary<,>)) continue;
                var keyType = iface.GetGenericArguments()[0];
                if (!PrimitiveTypes.Contains(keyType) && !keyType.IsEnum) continue;
                if (value is not IDictionary dict) break;

                var elems = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>();
                foreach (DictionaryEntry entry in dict)
                    elems.Add(new(new ScalarValue(entry.Key), Walk(entry.Value, depth + 1, visited)));

                result = new DictionaryValue(elems);
                return true;
            }
            result = null;
            return false;
        }
        [RequiresUnreferencedCode("Value projection uses reflection on arbitrary user types.")]
        private static SequenceValue ProjectSequence(
            IEnumerable seq, int depth, HashSet<object> visited)
        {
            var elems = new List<LogEventPropertyValue>();
            foreach (var item in seq)
                elems.Add(Walk(item, depth + 1, visited));
            return new SequenceValue(elems);
        }

        [RequiresUnreferencedCode("Value projection uses reflection on arbitrary user types.")]
        private static StructureValue ProjectStructure(
            object value,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
            Type type,
            int depth,
            HashSet<object> visited)
        {
            var props   = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var members = new List<LogEventProperty>(props.Length);
            foreach (var prop in props)
            {
                if (!prop.CanRead) continue;
                object? mv;
                try   { mv = prop.GetValue(value); }
                catch { mv = "[property read failed]"; }
                members.Add(new LogEventProperty(prop.Name, Walk(mv, depth + 1, visited)));
            }
            var tag = IsAnonymous(type) ? null : type.Name;
            return new StructureValue(members, tag);
        }
        private static bool IsAnonymous(Type t)
            => t.Name.StartsWith("<>", StringComparison.Ordinal)
            || t.Name.StartsWith("VB$", StringComparison.Ordinal);
    }
}
