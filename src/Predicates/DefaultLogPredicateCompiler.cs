#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Levels;

namespace MMP.Herald.Predicates;
/// <summary>
/// Default compiler for the logging predicate DSL.
/// </summary>
public sealed class DefaultLogPredicateCompiler : ILogPredicateCompiler
{
    public ILogPredicate Compile(PredicateSpec spec, ILogLevelRegistry levelRegistry)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(levelRegistry);

        return spec switch
        {
            PredicateSpec.True => new CompiledLogPredicate(static _ => true),

            PredicateSpec.False => new CompiledLogPredicate(static _ => false),

            PredicateSpec.AllOf allOf => new CompiledLogPredicate(logEvent =>
            {
                foreach (var item in allOf.Items)
                {
                    var predicate = Compile(item, levelRegistry);
                    if (!predicate.Evaluate(logEvent))
                    {
                        return false;
                    }
                }

                return true;
            }),

            PredicateSpec.AnyOf anyOf => new CompiledLogPredicate(logEvent =>
            {
                foreach (var item in anyOf.Items)
                {
                    var predicate = Compile(item, levelRegistry);
                    if (predicate.Evaluate(logEvent))
                    {
                        return true;
                    }
                }

                return false;
            }),

            PredicateSpec.Not not => new CompiledLogPredicate(logEvent =>
            {
                var predicate = Compile(not.Item, levelRegistry);
                return !predicate.Evaluate(logEvent);
            }),

            PredicateSpec.LevelEquals levelEquals => new CompiledLogPredicate(logEvent =>
                string.Equals(
                    logEvent.Level.Key,
                    levelEquals.LevelKey,
                    StringComparison.OrdinalIgnoreCase)),

            PredicateSpec.LevelAtOrAbove levelAtOrAbove => new CompiledLogPredicate(logEvent =>
            {
                var targetLevel = ResolveLevel(levelRegistry, levelAtOrAbove.LevelKey);
                return levelRegistry.IsAtOrAbove(logEvent.Level, targetLevel);
            }),

            PredicateSpec.CategoryEquals categoryEquals => new CompiledLogPredicate(logEvent =>
                string.Equals(
                    logEvent.Category.Value,
                    categoryEquals.Category,
                    StringComparison.OrdinalIgnoreCase)),

            PredicateSpec.CategoryIn categoryIn => new CompiledLogPredicate(logEvent =>
            {
                foreach (var category in categoryIn.Categories)
                {
                    if (string.Equals(
                        logEvent.Category.Value,
                        category,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }),

            PredicateSpec.MessageContains messageContains => new CompiledLogPredicate(logEvent =>
                logEvent.Message.Contains(messageContains.Value, StringComparison.OrdinalIgnoreCase)),

            PredicateSpec.ContextHasKey contextHasKey => new CompiledLogPredicate(logEvent =>
                logEvent.Context.ContainsKey(contextHasKey.Key)),

            PredicateSpec.ContextValueEquals contextValueEquals => new CompiledLogPredicate(logEvent =>
            {
                if (!logEvent.Context.TryGetValue(contextValueEquals.Key, out var rawValue))
                {
                    return false;
                }

                var text = rawValue?.ToString() ?? string.Empty;

                return string.Equals(
                    text,
                    contextValueEquals.Value,
                    StringComparison.OrdinalIgnoreCase);
            }),

            PredicateSpec.HasEventId => new CompiledLogPredicate(
                static logEvent => logEvent.EventId is not null),

            PredicateSpec.EventIdEquals eventIdEquals => new CompiledLogPredicate(
                logEvent => logEvent.EventId?.Id == eventIdEquals.Id),

            PredicateSpec.EventIdIn eventIdIn => new CompiledLogPredicate(logEvent =>
            {
                if (logEvent.EventId is null) return false;
                var id = logEvent.EventId.Id;

                foreach (var candidate in eventIdIn.Ids)
                {
                    if (candidate == id) return true;
                }

                return false;
            }),

            _ => throw new InvalidOperationException(
                $"Unsupported predicate spec type '{spec.GetType().Name}'.")
        };
    }

    private static LogLevel ResolveLevel(ILogLevelRegistry levelRegistry, string levelKey)
    {
        foreach (var registeredLevel in levelRegistry.GetRegisteredLevels())
        {
            if (string.Equals(
                registeredLevel.Level.Key,
                levelKey,
                StringComparison.OrdinalIgnoreCase))
            {
                return registeredLevel.Level;
            }
        }

        throw new KeyNotFoundException(
            $"No log level with key '{levelKey}' exists in the registry.");
    }
}