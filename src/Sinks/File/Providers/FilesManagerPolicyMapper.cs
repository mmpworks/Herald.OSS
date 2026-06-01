// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Configuration.Sinks;
using MMP.RollingFiles;

namespace Herald.Sinks.File.Providers;

/// <summary>
/// Translates the v2 sink-property bag (mmpform-driven) into a
/// <see cref="Resolved"/> record carrying the values
/// <see cref="FilesManagerPolicy"/> needs. The factory layer
/// (<see cref="LogSinkFileWriterFactory"/>) validates required fields
/// and constructs the policy.
///
/// <para>
/// The bag's keys mirror <c>configuration-text_file.mmpform</c> /
/// <c>configuration-json_file.mmpform</c>. Anything missing falls
/// through to the policy record's own defaults — those are sensible
/// for a "single file forever" sink, which matches the mmpform's
/// default state where <c>rollingLogsEnabled = false</c>.
/// </para>
///
/// <para>
/// The mapper carries every documented mmpform key plus the new keys
/// that <see cref="FilesManagerPolicy"/> exposes
/// (<c>compression</c>, <c>diskFull</c>, <c>cleanRollOnStartup</c>,
/// <c>pathTokens</c>) so a future mmpform that lights those fields
/// up reaches the policy automatically. The keys default to "off"
/// when absent, which preserves today's behaviour.
/// </para>
///
/// <para>
/// <b>Pass-4a realignment.</b> File now follows the 16-sink shape: the
/// mapper stays pure (returns a <see cref="Resolved"/> record with
/// nullable required fields); the factory validates and throws
/// <see cref="ArgumentException"/> with the operator-facing field name
/// in the message. The old <see cref="InvalidOperationException"/>
/// throws moved upstream into the factory so the exception kind
/// matches the other 16 sinks. Bag-reader primitives come from
/// <see cref="SinkPropertyBag"/> in Herald.OSS; no per-sink reader
/// helpers stay here.
/// </para>
///
/// <para>
/// Stays internal: the bag's interpretation is owned by this package
/// — Core never reaches into the dictionary. Mirrors the boundary
/// the sibling <see cref="FileSinkRuntimeConfig"/> helper kept.
/// </para>
/// </summary>
internal static class FilesManagerPolicyMapper
{
    // ── Property keys (mirrors the mmpform __properties block) ────
    private const string KeyDirectory          = "logDirectory";
    private const string KeyTemplate           = "logFileTemplate";
    private const string KeyExtension          = "logExtension";
    private const string KeyActiveFileName     = "activeFileName";
    private const string KeyRollingEnabled     = "rollingLogsEnabled";
    private const string KeyRollingInterval    = "rollingInterval";
    private const string KeyMaxFileSize        = "maxFileSize";
    private const string KeyMaxMessages        = "maxMessagesPerFile";
    private const string KeyMaxRetainedFiles   = "maxRetainedFiles";
    private const string KeyTotalSizeCap       = "totalSizeCap";
    private const string KeyRetentionDays      = "retentionDays";
    private const string KeyNamePattern        = "namePattern";
    private const string KeyNamePatternLegacy  = "fileNamePattern"; // json_file's older spelling
    private const string KeyStartMinute        = "startMinute";
    private const string KeyCaptureDurationMin = "captureDurationMinutes";
    private const string KeyCompression        = "compression";
    private const string KeyDiskFull           = "diskFull";
    private const string KeyCleanRollOnStartup = "cleanRollOnStartup";
    private const string KeyShareForWriting    = "shareForWriting";
    private const string KeyPathTokens         = "pathTokens";
    private const string KeyLocale             = "locale";

    /// <summary>
    /// Resolved File-sink config. <see cref="Directory"/> and
    /// <see cref="FileNameTemplate"/> stay nullable so the factory can
    /// fail with a field-named <see cref="ArgumentException"/>; every
    /// other field carries a typed value or null per the policy's own
    /// default. <see cref="BagPresent"/> tells the factory whether the
    /// bag had any keys at all — when false, the factory routes the
    /// definition through the legacy <c>Path</c> + <c>RollingPolicy</c>
    /// path instead of constructing a <see cref="FilesManagerPolicy"/>.
    /// </summary>
    public readonly record struct Resolved(
        bool BagPresent,
        string? Directory,
        string? FileNameTemplate,
        string Extension,
        string? ActiveFileName,
        bool RollingEnabled,
        RollingInterval Interval,
        long? MaxBytesPerFile,
        long? MaxMessagesPerFile,
        int? MaxRetainedFiles,
        long? TotalSizeCapBytes,
        int? RetentionDays,
        CompressionMode Compression,
        DiskFullStrategy DiskFull,
        int StartMinute,
        int CaptureDurationMinutes,
        string? FileNameSuffix,
        string? Locale,
        bool ShareForWriting,
        bool CleanRollOnStartup,
        System.Collections.Generic.IReadOnlyDictionary<string, string>? PathTokens);

    /// <summary>
    /// Read every documented mmpform key into a <see cref="Resolved"/>
    /// record. Returns a record with <see cref="Resolved.BagPresent"/>
    /// = <c>false</c> when the definition carries no bag — the factory
    /// uses that signal to fall through to the legacy path.
    /// </summary>
    public static Resolved From(LoggingRuntimeSinkDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var bag = definition.Properties;
        if (bag is null)
        {
            return new Resolved(
                BagPresent: false,
                Directory: null,
                FileNameTemplate: null,
                Extension: ".log",
                ActiveFileName: null,
                RollingEnabled: false,
                Interval: RollingInterval.None,
                MaxBytesPerFile: null,
                MaxMessagesPerFile: null,
                MaxRetainedFiles: null,
                TotalSizeCapBytes: null,
                RetentionDays: null,
                Compression: CompressionMode.None,
                DiskFull: DiskFullStrategy.Stop,
                StartMinute: 0,
                CaptureDurationMinutes: 60,
                FileNameSuffix: null,
                Locale: null,
                ShareForWriting: false,
                CleanRollOnStartup: false,
                PathTokens: null);
        }

        // Roll-trigger fields only flow through when rolling is on.
        // When the mmpform's rollingLogsEnabled toggle is off, we hand
        // FilesManager an interval/size/count of None so it produces
        // the "single file forever" shape — same as the legacy
        // RollingFileLineWriter behaviour for that toggle state.
        var rollingOn = SinkPropertyBag.ReadBool(bag, KeyRollingEnabled) == true;

        return new Resolved(
            BagPresent:             true,
            Directory:              SinkPropertyBag.ReadString(bag, KeyDirectory),
            FileNameTemplate:       SinkPropertyBag.ReadString(bag, KeyTemplate),
            Extension:              NormaliseExtension(SinkPropertyBag.ReadString(bag, KeyExtension)),
            ActiveFileName:         SinkPropertyBag.ReadString(bag, KeyActiveFileName),
            RollingEnabled:         rollingOn,
            Interval:               rollingOn
                                        ? SinkPropertyBag.ReadEnum<RollingInterval>(bag, KeyRollingInterval) ?? RollingInterval.None
                                        : RollingInterval.None,
            MaxBytesPerFile:        rollingOn
                                        ? SinkPropertyBag.ParseHumanByteSize(SinkPropertyBag.ReadString(bag, KeyMaxFileSize))
                                        : null,
            MaxMessagesPerFile:     rollingOn ? SinkPropertyBag.ReadLong(bag, KeyMaxMessages) : null,
            MaxRetainedFiles:       SinkPropertyBag.ReadInt(bag, KeyMaxRetainedFiles),
            TotalSizeCapBytes:      SinkPropertyBag.ParseHumanByteSize(SinkPropertyBag.ReadString(bag, KeyTotalSizeCap)),
            RetentionDays:          SinkPropertyBag.ReadInt(bag, KeyRetentionDays),
            Compression:            SinkPropertyBag.ReadEnum<CompressionMode>(bag, KeyCompression) ?? CompressionMode.None,
            DiskFull:               SinkPropertyBag.ReadEnum<DiskFullStrategy>(bag, KeyDiskFull) ?? DiskFullStrategy.Stop,
            StartMinute:            SinkPropertyBag.ReadInt(bag, KeyStartMinute) ?? 0,
            CaptureDurationMinutes: SinkPropertyBag.ReadInt(bag, KeyCaptureDurationMin) ?? 60,
            FileNameSuffix:         SinkPropertyBag.ReadString(bag, KeyNamePattern)
                                        ?? SinkPropertyBag.ReadString(bag, KeyNamePatternLegacy),
            Locale:                 SinkPropertyBag.ReadString(bag, KeyLocale),
            ShareForWriting:        SinkPropertyBag.ReadBool(bag, KeyShareForWriting) == true,
            // Carried through to the policy; the runtime support for
            // CleanRollOnStartup lands in a later FilesManager phase.
            // Keeping it on the policy now means a future runtime
            // upgrade picks it up without a mapper change.
            CleanRollOnStartup:     SinkPropertyBag.ReadBool(bag, KeyCleanRollOnStartup) == true,
            // Token values are taken as-is. PathTokens validation
            // (rejecting "/" / ".." / etc.) lives in
            // FilesManagerPolicy / FilesManager at expansion time —
            // the mapper does not duplicate it because a single
            // rejection point keeps the contract auditable.
            PathTokens:             SinkPropertyBag.ReadStringMap(bag, KeyPathTokens));
    }

    /// <summary>
    /// Project a <see cref="Resolved"/> into a
    /// <see cref="FilesManagerPolicy"/>. The factory has already
    /// validated <see cref="Resolved.Directory"/> and
    /// <see cref="Resolved.FileNameTemplate"/> at this point — the
    /// non-null assertions here are documentation of that contract.
    /// </summary>
    public static FilesManagerPolicy ToPolicy(Resolved resolved) =>
        new(
            Directory: resolved.Directory!,
            FileNameTemplate: resolved.FileNameTemplate!,
            Extension: resolved.Extension,
            ActiveFileName: resolved.ActiveFileName,
            Interval: resolved.Interval,
            MaxBytesPerFile: resolved.MaxBytesPerFile,
            MaxMessagesPerFile: resolved.MaxMessagesPerFile,
            MaxRetainedFiles: resolved.MaxRetainedFiles,
            TotalSizeCapBytes: resolved.TotalSizeCapBytes,
            RetentionDays: resolved.RetentionDays,
            Compression: resolved.Compression,
            DiskFull: resolved.DiskFull,
            StartMinute: resolved.StartMinute,
            CaptureDurationMinutes: resolved.CaptureDurationMinutes,
            FileNameSuffix: resolved.FileNameSuffix,
            Locale: resolved.Locale,
            ShareForWriting: resolved.ShareForWriting,
            CleanRollOnStartup: resolved.CleanRollOnStartup,
            PathTokens: resolved.PathTokens);

    // The mmpform stores the extension without a leading dot
    // ("log", "ndjson"); FilesManagerPolicy expects ".log" / ".ndjson".
    // Either form is accepted on the way in. Stays local because no
    // other sink shape needs this normalisation — File is the only
    // consumer of file-system extensions today.
    private static string NormaliseExtension(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return ".log";
        return raw.StartsWith('.') ? raw : "." + raw;
    }
}
