// Lines 1-34
#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MMP.Herald.Predicates;
/// <summary>
/// Base DSL record for data-driven log predicates.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PredicateSpec.True), "true")]
[JsonDerivedType(typeof(PredicateSpec.False), "false")]
[JsonDerivedType(typeof(PredicateSpec.AllOf), "all_of")]
[JsonDerivedType(typeof(PredicateSpec.AnyOf), "any_of")]
[JsonDerivedType(typeof(PredicateSpec.Not), "not")]
[JsonDerivedType(typeof(PredicateSpec.LevelEquals), "level_equals")]
[JsonDerivedType(typeof(PredicateSpec.LevelAtOrAbove), "level_at_or_above")]
[JsonDerivedType(typeof(PredicateSpec.CategoryEquals), "category_equals")]
[JsonDerivedType(typeof(PredicateSpec.CategoryIn), "category_in")]
[JsonDerivedType(typeof(PredicateSpec.MessageContains), "message_contains")]
[JsonDerivedType(typeof(PredicateSpec.ContextHasKey), "context_has_key")]
[JsonDerivedType(typeof(PredicateSpec.ContextValueEquals), "context_value_equals")]
[JsonDerivedType(typeof(PredicateSpec.HasEventId), "has_event_id")]
[JsonDerivedType(typeof(PredicateSpec.EventIdEquals), "event_id_equals")]
[JsonDerivedType(typeof(PredicateSpec.EventIdIn), "event_id_in")]
public abstract record PredicateSpec
{
    public sealed record True : PredicateSpec;
    public sealed record False : PredicateSpec;
    public sealed record AllOf(IReadOnlyList<PredicateSpec> Items) : PredicateSpec;
    public sealed record AnyOf(IReadOnlyList<PredicateSpec> Items) : PredicateSpec;
    public sealed record Not(PredicateSpec Item) : PredicateSpec;
    public sealed record LevelEquals(string LevelKey) : PredicateSpec;
    public sealed record LevelAtOrAbove(string LevelKey) : PredicateSpec;
    public sealed record CategoryEquals(string Category) : PredicateSpec;
    public sealed record CategoryIn(IReadOnlyList<string> Categories) : PredicateSpec;
    public sealed record MessageContains(string Value) : PredicateSpec;
    public sealed record ContextHasKey(string Key) : PredicateSpec;
    public sealed record ContextValueEquals(string Key, string Value) : PredicateSpec;
    public sealed record HasEventId : PredicateSpec;
    public sealed record EventIdEquals(int Id) : PredicateSpec;
    public sealed record EventIdIn(IReadOnlyList<int> Ids) : PredicateSpec;
}