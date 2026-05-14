// Copyright (c) 2026 MMP LLC
// Licensed under the MIT License. See LICENSE in the project root.
#nullable enable

namespace MMP.Herald.Configuration.Sinks;

/// <summary>
/// One entry from a sink's <c>__properties = [ … ]</c> block — the v2
/// mmpform contract that names every property a sink consumes, the
/// declared data type for each, and the default value the sink starts
/// with when an operator hasn't overridden it.
///
/// <para>Core does not interpret these values. It only ships them
/// between the embedded mmpform resource (the contract source of truth),
/// the JSON config a pipeline carries, and the sink's own
/// <c>CreateSink</c> code. The sink author owns the meaning of each
/// property; the contract is the agreement that nothing in transit
/// silently drops a key.</para>
/// </summary>
/// <param name="Name">
/// Property identifier (matches the binding name on the corresponding
/// mmpform control — e.g. <c>logDirectory</c>, <c>fileNamePattern</c>).
/// </param>
/// <param name="Type">
/// Declared type as written in the mmpform: <c>string</c>, <c>int</c>,
/// <c>float</c>, <c>bool</c>, or <c>string[]</c> (single-pick from a
/// list of options).
/// </param>
/// <param name="Default">
/// Default value coerced to the CLR type that matches <see cref="Type"/>:
/// <see cref="string"/> for <c>string</c> / <c>string[]</c>, <see cref="long"/>
/// for <c>int</c>, <see cref="double"/> for <c>float</c>,
/// <see cref="bool"/> for <c>bool</c>. <c>null</c> when the mmpform
/// omitted a default or wrote an empty string for one of the numeric
/// types.
/// </param>
public sealed record MmpformPropertyDefinition(string Name, string Type, object? Default);
