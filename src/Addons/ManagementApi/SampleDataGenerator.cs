#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Generates realistic sample log data for dashboard previews, development,
/// and integration testing. Produces NDJSON files in Herald's native format
/// with proper rolling file naming conventions.
///
/// The sample data tells a coherent story: a game server starting up,
/// players joining, combat encounters, quest progress, economy transactions,
/// network events, and operational incidents — complete with correlation IDs,
/// service identity, structured properties, and realistic timing.
///
/// Usage:
///   // Get sample data as a list of JSON strings:
///   var lines = SampleDataGenerator.Generate(count: 100);
///
///   // Write sample rolled log files to a directory:
///   SampleDataGenerator.WriteSampleRolledFiles("logs/sample");
///   // Creates: logs/sample/game-server_20260410.log (100 entries)
///   //          logs/sample/game-server_20260411.log (100 entries)
///
///   // Get sample data as a single NDJSON string:
///   string ndjson = SampleDataGenerator.GenerateNdjson(count: 100);
/// </summary>
public static class SampleDataGenerator
{
    private static readonly string[] Levels = ["trace", "debug", "info", "info", "info", "warn", "error"];
    private static readonly string[] LevelDisplays = ["TRC", "DBG", "INF", "INF", "INF", "WRN", "ERR"];
    private static readonly string[] Categories = ["App", "App", "App", "Combat", "Quest", "Economy", "Network"];

    private static readonly string[] PlayerNames = ["Kael", "Lyra", "Thorne", "Mira", "Dax", "Elara", "Voss", "Zara"];
    private static readonly string[] Regions = ["us-east-1", "eu-west-2", "ap-south-1"];
    private static readonly string[] Quests = ["dragon_slayer", "merchant_road", "crystal_cavern", "shadow_keep"];
    private static readonly string[] Items = ["Iron Sword", "Health Potion", "Dragon Scale", "Mithril Shield", "Fire Staff"];
    private static readonly string[] Enemies = ["Goblin Scout", "Cave Troll", "Shadow Knight", "Ice Dragon", "Bandit Captain"];
    private static readonly string[] Carriers = ["FedEx", "UPS", "DHL", "USPS"];

    /// <summary>
    /// Generate a list of sample log entries as JSON strings (one per line).
    /// Each entry is a complete Herald NDJSON record with realistic game server data.
    /// </summary>
    /// <param name="count">Number of log entries to generate (default: 100).</param>
    /// <param name="baseTime">Starting timestamp. Defaults to today at midnight UTC.</param>
    /// <param name="serviceName">Service name for the service.name context field.</param>
    public static List<string> Generate(
        int count = 100,
        DateTimeOffset? baseTime = null,
        string serviceName = "game-server")
    {
        var lines = new List<string>(count);
        var time = baseTime ?? new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var rng = new Random(42); // deterministic for reproducibility
        var correlationId = $"session_{Guid.NewGuid():N}"[..24];
        var frameNumber = 0;

        for (var i = 0; i < count; i++)
        {
            // Advance time by 50-5000ms per entry
            time = time.AddMilliseconds(rng.Next(50, 5000));

            var scenario = PickScenario(rng, i, count);
            var entry = BuildEntry(scenario, time, rng, serviceName, ref correlationId, ref frameNumber);
            lines.Add(entry);
        }

        return lines;
    }

    /// <summary>
    /// Generate sample data as a single NDJSON string (newline-delimited JSON).
    /// </summary>
    public static string GenerateNdjson(int count = 100, DateTimeOffset? baseTime = null,
        string serviceName = "game-server")
    {
        var lines = Generate(count, baseTime, serviceName);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Write two sample rolled log files to a directory, simulating a daily
    /// roll-over. Each file contains <paramref name="entriesPerFile"/> entries.
    ///
    /// File naming follows Herald's rolling convention:
    ///   {directory}/{serviceName}_{yyyyMMdd}.log
    /// </summary>
    /// <param name="directory">Target directory (created if it doesn't exist).</param>
    /// <param name="entriesPerFile">Entries per file (default: 100).</param>
    /// <param name="serviceName">Service name used in file names and log context.</param>
    /// <returns>The two file paths that were created.</returns>
    public static (string File1, string File2) WriteSampleRolledFiles(
        string directory,
        int entriesPerFile = 100,
        string serviceName = "game-server")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);

        var day1 = new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero);

        var file1 = Path.Combine(directory, $"{serviceName}_{day1:yyyyMMdd}.log");
        var file2 = Path.Combine(directory, $"{serviceName}_{day2:yyyyMMdd}.log");

        var lines1 = Generate(entriesPerFile, day1, serviceName);
        var lines2 = Generate(entriesPerFile, day2, serviceName);

        File.WriteAllText(file1, string.Join('\n', lines1) + '\n');
        File.WriteAllText(file2, string.Join('\n', lines2) + '\n');

        return (file1, file2);
    }

    /// <summary>
    /// Get the file paths that <see cref="WriteSampleRolledFiles"/> would create,
    /// without actually writing them. Useful for the dashboard to list available samples.
    /// </summary>
    public static (string File1, string File2) GetSampleFilePaths(
        string directory, string serviceName = "game-server")
    {
        var file1 = Path.Combine(directory, $"{serviceName}_20260410.log");
        var file2 = Path.Combine(directory, $"{serviceName}_20260411.log");
        return (file1, file2);
    }

    /// <summary>
    /// Load a pre-generated sample log file from embedded resources.
    /// These are the same files shipped at SampleLogs/game-server_20260410.log
    /// and game-server_20260411.log.
    ///
    /// Returns the NDJSON content as a string, or null if the resource is not found.
    /// </summary>
    /// <param name="file">1 for April 10, 2 for April 11.</param>
    public static string? LoadEmbeddedSample(int file = 1)
    {
        var date = file == 2 ? "20260411" : "20260410";
        var resourceName = $"MMP.Herald.src.Addons.ManagementApi.SampleLogs.game-server_{date}.log";

        using var stream = typeof(SampleDataGenerator).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            // Try alternative naming (namespace dots vs path separators)
            var names = typeof(SampleDataGenerator).Assembly.GetManifestResourceNames();
            var match = Array.Find(names, n => n.Contains($"game-server_{date}"));
            if (match is null) return null;
            using var fallback = typeof(SampleDataGenerator).Assembly.GetManifestResourceStream(match);
            if (fallback is null) return null;
            using var reader2 = new StreamReader(fallback);
            return reader2.ReadToEnd();
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// List all embedded sample resource names. Useful for debugging resource naming.
    /// </summary>
    public static string[] ListEmbeddedResources()
    {
        return Array.FindAll(
            typeof(SampleDataGenerator).Assembly.GetManifestResourceNames(),
            n => n.Contains("SampleLogs") || n.Contains("game-server"));
    }

    // ── Scenario Selection ───────────────────────────────────────────

    private enum Scenario
    {
        Startup, PlayerJoin, PlayerLeave, CombatHit, CombatDeath,
        QuestStart, QuestProgress, QuestComplete,
        ItemPurchase, TradeComplete, CurrencyEarned,
        FrameTick, PhysicsStep, NetworkPing,
        ConfigReload, LevelChange,
        Warning, Error, Recovery
    }

    private static Scenario PickScenario(Random rng, int index, int total)
    {
        // First 5 entries: startup sequence
        if (index < 5) return (Scenario)Math.Min(index, 1) == 0 ? Scenario.Startup : Scenario.PlayerJoin;

        // Last 3 entries: shutdown-ish
        if (index >= total - 3) return Scenario.PlayerLeave;

        // Sprinkle errors at ~15% and ~75% through the file
        if (index == (int)(total * 0.15)) return Scenario.Error;
        if (index == (int)(total * 0.16)) return Scenario.Recovery;
        if (index == (int)(total * 0.75)) return Scenario.Warning;

        // Config reload at ~50%
        if (index == total / 2) return Scenario.ConfigReload;
        if (index == total / 2 + 1) return Scenario.LevelChange;

        // The rest: weighted random from gameplay scenarios
        return rng.Next(100) switch
        {
            < 20 => Scenario.FrameTick,
            < 30 => Scenario.PhysicsStep,
            < 40 => Scenario.CombatHit,
            < 45 => Scenario.CombatDeath,
            < 55 => Scenario.QuestProgress,
            < 60 => Scenario.QuestStart,
            < 65 => Scenario.QuestComplete,
            < 75 => Scenario.ItemPurchase,
            < 80 => Scenario.TradeComplete,
            < 85 => Scenario.CurrencyEarned,
            < 95 => Scenario.NetworkPing,
            _ => Scenario.PlayerJoin
        };
    }

    // ── Entry Builder ────────────────────────────────────────────────

    private static string BuildEntry(Scenario scenario, DateTimeOffset time, Random rng,
        string serviceName, ref string correlationId, ref int frameNumber)
    {
        var sb = new StringBuilder(512);
        sb.Append('{');

        // Time
        AppendString(sb, "time", time.ToString("O", CultureInfo.InvariantCulture));

        // Level + category
        string level, levelDisplay, levelKey, category;
        string messageTemplate, message;
        var props = new Dictionary<string, object>();
        var context = new Dictionary<string, object>
        {
            ["service.name"] = serviceName,
            ["service.version"] = "2.1.0",
            ["service.region"] = Regions[rng.Next(Regions.Length)],
            ["service.environment"] = "production",
            ["correlationId"] = correlationId,
            ["machineName"] = $"prod-{rng.Next(1, 8):D2}",
            ["processId"] = 12345 + rng.Next(100),
            ["threadId"] = rng.Next(1, 16)
        };

        switch (scenario)
        {
            case Scenario.Startup:
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "App";
                messageTemplate = "Service started, version {version}, strategy {strategy}";
                props["version"] = "2.1.0";
                props["strategy"] = "Default";
                message = "Service started, version 2.1.0, strategy Default";
                break;

            case Scenario.PlayerJoin:
                var player = PlayerNames[rng.Next(PlayerNames.Length)];
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "App";
                messageTemplate = "Player {player} joined from {region}";
                props["player"] = player;
                props["region"] = Regions[rng.Next(Regions.Length)];
                message = $"Player {player} joined from {props["region"]}";
                correlationId = $"player_{Guid.NewGuid():N}"[..20];
                break;

            case Scenario.PlayerLeave:
                var leaver = PlayerNames[rng.Next(PlayerNames.Length)];
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "App";
                messageTemplate = "Player {player} disconnected, session {duration}s";
                var duration = rng.Next(120, 3600);
                props["player"] = leaver;
                props["duration"] = duration;
                message = $"Player {leaver} disconnected, session {duration}s";
                break;

            case Scenario.CombatHit:
                var attacker = PlayerNames[rng.Next(PlayerNames.Length)];
                var target = Enemies[rng.Next(Enemies.Length)];
                var damage = rng.Next(5, 150);
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "Combat";
                messageTemplate = "{attacker} dealt {damage} damage to {target}";
                props["attacker"] = attacker;
                props["damage"] = damage;
                props["target"] = target;
                message = $"{attacker} dealt {damage} damage to {target}";
                break;

            case Scenario.CombatDeath:
                var killer = PlayerNames[rng.Next(PlayerNames.Length)];
                var victim = Enemies[rng.Next(Enemies.Length)];
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "Combat";
                messageTemplate = "{target} defeated by {attacker}, loot: {lootGold} gold";
                var loot = rng.Next(10, 500);
                props["target"] = victim;
                props["attacker"] = killer;
                props["lootGold"] = loot;
                message = $"{victim} defeated by {killer}, loot: {loot} gold";
                break;

            case Scenario.QuestStart:
                var quest = Quests[rng.Next(Quests.Length)];
                var questPlayer = PlayerNames[rng.Next(PlayerNames.Length)];
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "Quest";
                messageTemplate = "Quest {questId} started by {player}";
                props["questId"] = quest;
                props["player"] = questPlayer;
                message = $"Quest {quest} started by {questPlayer}";
                break;

            case Scenario.QuestProgress:
                var qp = Quests[rng.Next(Quests.Length)];
                var progress = rng.Next(10, 100);
                level = "debug"; levelDisplay = "DBG"; levelKey = "debug"; category = "Quest";
                messageTemplate = "Quest {questId} progress: {percent}%";
                props["questId"] = qp;
                props["percent"] = progress;
                message = $"Quest {qp} progress: {progress}%";
                break;

            case Scenario.QuestComplete:
                var cq = Quests[rng.Next(Quests.Length)];
                var cp = PlayerNames[rng.Next(PlayerNames.Length)];
                var reward = rng.Next(100, 2000);
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "Quest";
                messageTemplate = "Quest {questId} completed by {player}, reward: {reward} gold";
                props["questId"] = cq;
                props["player"] = cp;
                props["reward"] = reward;
                message = $"Quest {cq} completed by {cp}, reward: {reward} gold";
                break;

            case Scenario.ItemPurchase:
                var item = Items[rng.Next(Items.Length)];
                var buyer = PlayerNames[rng.Next(PlayerNames.Length)];
                var price = rng.Next(10, 500);
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "Economy";
                messageTemplate = "{player} purchased {item} for {price} gold";
                props["player"] = buyer;
                props["item"] = item;
                props["price"] = price;
                message = $"{buyer} purchased {item} for {price} gold";
                break;

            case Scenario.TradeComplete:
                var seller = PlayerNames[rng.Next(PlayerNames.Length)];
                var tradeBuyer = PlayerNames[rng.Next(PlayerNames.Length)];
                var tradeItem = Items[rng.Next(Items.Length)];
                var tradePrice = rng.Next(50, 1000);
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "Economy";
                messageTemplate = "Trade: {seller} sold {item} to {buyer} for {price} gold";
                props["seller"] = seller;
                props["item"] = tradeItem;
                props["buyer"] = tradeBuyer;
                props["price"] = tradePrice;
                message = $"Trade: {seller} sold {tradeItem} to {tradeBuyer} for {tradePrice} gold";
                break;

            case Scenario.CurrencyEarned:
                var earner = PlayerNames[rng.Next(PlayerNames.Length)];
                var amount = rng.Next(5, 200);
                var source = new[] { "quest_reward", "loot_drop", "trade", "daily_bonus" }[rng.Next(4)];
                level = "debug"; levelDisplay = "DBG"; levelKey = "debug"; category = "Economy";
                messageTemplate = "{player} earned {amount} gold from {source}";
                props["player"] = earner;
                props["amount"] = amount;
                props["source"] = source;
                message = $"{earner} earned {amount} gold from {source}";
                break;

            case Scenario.FrameTick:
                frameNumber++;
                var fps = 58 + rng.Next(5);
                var deltaMs = 1000.0 / fps;
                level = "trace"; levelDisplay = "TRC"; levelKey = "trace"; category = "App";
                messageTemplate = "Frame {frame}: {deltaMs}ms, {bodies} active bodies";
                var bodies = rng.Next(50, 200);
                props["frame"] = frameNumber;
                props["deltaMs"] = Math.Round(deltaMs, 1);
                props["bodies"] = bodies;
                message = $"Frame {frameNumber}: {deltaMs:F1}ms, {bodies} active bodies";
                break;

            case Scenario.PhysicsStep:
                var collisions = rng.Next(0, 30);
                level = "trace"; levelDisplay = "TRC"; levelKey = "trace"; category = "App";
                messageTemplate = "Physics step: {collisions} collisions, {stepMs}ms";
                var stepMs = rng.Next(1, 8);
                props["collisions"] = collisions;
                props["stepMs"] = stepMs;
                message = $"Physics step: {collisions} collisions, {stepMs}ms";
                break;

            case Scenario.NetworkPing:
                var rtt = rng.Next(15, 200);
                level = rtt > 150 ? "warn" : "debug";
                levelDisplay = rtt > 150 ? "WRN" : "DBG";
                levelKey = level;
                category = "Network";
                messageTemplate = "Server ping: {rttMs}ms to {endpoint}";
                var endpoint = $"game-{rng.Next(1, 5)}.example.com";
                props["rttMs"] = rtt;
                props["endpoint"] = endpoint;
                message = $"Server ping: {rtt}ms to {endpoint}";
                break;

            case Scenario.ConfigReload:
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "App";
                messageTemplate = "Configuration reloaded: strategy changed to {strategy}";
                props["strategy"] = "FilterEarly";
                message = "Configuration reloaded: strategy changed to FilterEarly";
                break;

            case Scenario.LevelChange:
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "App";
                messageTemplate = "Minimum level changed from {oldLevel} to {newLevel}";
                props["oldLevel"] = "info";
                props["newLevel"] = "debug";
                message = "Minimum level changed from info to debug";
                break;

            case Scenario.Warning:
                level = "warn"; levelDisplay = "WRN"; levelKey = "warn"; category = "Network";
                messageTemplate = "Connection pool near capacity: {active}/{max} connections";
                var active = rng.Next(80, 100);
                props["active"] = active;
                props["max"] = 100;
                message = $"Connection pool near capacity: {active}/100 connections";
                break;

            case Scenario.Error:
                level = "error"; levelDisplay = "ERR"; levelKey = "error"; category = "Network";
                messageTemplate = "Failed to deliver to OTLP collector: {reason}";
                props["reason"] = "connection_refused";
                props["endpoint"] = "otlp.example.com:4317";
                props["consecutiveFailures"] = 3;
                message = "Failed to deliver to OTLP collector: connection_refused";
                context["exception"] = "System.Net.Http.HttpRequestException: Connection refused";
                break;

            case Scenario.Recovery:
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "Network";
                messageTemplate = "OTLP collector recovered after {downtime}s, {buffered} events replayed";
                var downtime = rng.Next(5, 30);
                var buffered = rng.Next(10, 50);
                props["downtime"] = downtime;
                props["buffered"] = buffered;
                message = $"OTLP collector recovered after {downtime}s, {buffered} events replayed";
                break;

            default:
                level = "info"; levelDisplay = "INF"; levelKey = "info"; category = "App";
                messageTemplate = "Event {index}";
                props["index"] = frameNumber;
                message = $"Event {frameNumber}";
                break;
        }

        // Build JSON
        sb.Append(',');
        AppendString(sb, "level", levelDisplay);
        sb.Append(',');
        AppendString(sb, "levelKey", levelKey);
        sb.Append(',');
        AppendString(sb, "levelRank", GetRank(levelKey));
        sb.Append(',');
        AppendString(sb, "category", category);
        sb.Append(',');
        AppendString(sb, "messageTemplate", messageTemplate);
        sb.Append(',');
        AppendString(sb, "message", message);

        // Properties
        sb.Append(",\"properties\":{");
        var first = true;
        foreach (var (k, v) in props)
        {
            if (!first) sb.Append(',');
            AppendProperty(sb, k, v);
            first = false;
        }
        sb.Append('}');

        // Context
        sb.Append(",\"context\":{");
        first = true;
        foreach (var (k, v) in context)
        {
            if (!first) sb.Append(',');
            AppendString(sb, k, v.ToString()!);
            first = false;
        }
        sb.Append('}');

        sb.Append('}');
        return sb.ToString();
    }

    // ── JSON Helpers ─────────────────────────────────────────────────

    private static void AppendString(StringBuilder sb, string key, string value)
    {
        sb.Append('"').Append(JsonEscape(key)).Append("\":\"").Append(JsonEscape(value)).Append('"');
    }

    private static void AppendProperty(StringBuilder sb, string name, object value)
    {
        sb.Append('"').Append(JsonEscape(name)).Append("\":{\"value\":");
        if (value is int or long or double or float or decimal)
            sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        else
            sb.Append('"').Append(JsonEscape(value.ToString()!)).Append('"');
        sb.Append('}');
    }

    private static string JsonEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string GetRank(string levelKey) => levelKey switch
    {
        "trace" => "0",
        "debug" => "1",
        "info" => "2",
        "warn" => "3",
        "error" => "4",
        _ => "2"
    };
}
