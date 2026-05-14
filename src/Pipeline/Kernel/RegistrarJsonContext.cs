#nullable enable

using System.Text.Json.Serialization;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Source-generated JSON serializer context for the persisted registrar
/// snapshot. Reflection-free so the persistence path stays AOT-clean
/// alongside the rest of Core.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RegistrarSnapshot))]
[JsonSerializable(typeof(RegistrarPersistedEntry))]
internal sealed partial class RegistrarJsonContext : JsonSerializerContext
{
}
