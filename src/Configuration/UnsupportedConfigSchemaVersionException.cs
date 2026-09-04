// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Configuration;

/// <summary>
/// Thrown when a logging configuration JSON declares a <c>"schemaVersion"</c>
/// this build does not understand.
///
/// <para>
/// Without the check, a file written against a newer schema still parses: the
/// deserializer drops every field it does not recognize and the pipeline starts
/// in a shape the operator did not configure. Refusing the load turns a silent
/// misconfiguration into a startup failure the operator can read.
/// </para>
///
/// <para>
/// Callers branch on <see cref="Code"/>, never on <see cref="Exception.Message"/>.
/// </para>
/// </summary>
public sealed class UnsupportedConfigSchemaVersionException : Exception
{
    /// <summary>
    /// Stable machine code for this failure. The string does not change across
    /// releases, so a caller may match on it.
    /// </summary>
    public const string StableCode = "HERALD_CONFIG_SCHEMA_VERSION_UNSUPPORTED";

    /// <summary>The stable machine code, as an instance member for the caller.</summary>
    public string Code => StableCode;

    /// <summary>The version the configuration file declared.</summary>
    public int SchemaVersion { get; }

    /// <summary>The version this build reads.</summary>
    public int SupportedSchemaVersion { get; }

    /// <summary>Construct with the declared version and the version this build reads.</summary>
    public UnsupportedConfigSchemaVersionException(int schemaVersion, int supportedSchemaVersion)
        : base($"{StableCode}: logging configuration declares schemaVersion {schemaVersion}; " +
               $"this build reads schemaVersion {supportedSchemaVersion}. " +
               "Upgrade Herald, or set schemaVersion to the supported value.")
    {
        SchemaVersion = schemaVersion;
        SupportedSchemaVersion = supportedSchemaVersion;
    }
}
