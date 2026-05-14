#nullable enable

namespace MMP.Herald.Events;

/// <summary>
/// Optional identifier for a specific kind of log event.
/// Enables fine-grained filtering: enable or suppress individual event types
/// without changing the category or level.
///
/// Usage:
///   public static class GameEvents
///   {
///       public static readonly LogEventId CombatHit = new(1001, "CombatHit");
///       public static readonly LogEventId SpellCast = new(1002, "SpellCast");
///       public static readonly LogEventId QuestComplete = new(2001, "QuestComplete");
///   }
///
///   logger.Info(LogCategory.App, "Strike landed", eventId: GameEvents.CombatHit);
/// </summary>
public sealed record LogEventId(int Id, string? Name = null)
{
    public override string ToString() => Name is not null ? $"{Name}({Id})" : Id.ToString();
}
