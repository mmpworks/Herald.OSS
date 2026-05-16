#nullable enable

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// No-op store. Used when the operator hasn't wired a real persistence
/// layer — registrations live in memory for the lifetime of the
/// process and disappear on restart. Singleton-shaped so callers can
/// pass <see cref="Instance"/> wherever an <see cref="IRegistrarStore"/>
/// is required.
///
/// <para>
/// Keeping the registrar's call sites unconditional (always call
/// <c>_store.Save(...)</c>, never <c>_store?.Save(...)</c>) lets the
/// JIT inline the no-op away on the in-memory path. The hot path
/// pays nothing extra.
/// </para>
/// </summary>
public sealed class NullRegistrarStore : IRegistrarStore
{
    /// <summary>The shared no-op instance.</summary>
    public static readonly NullRegistrarStore Instance = new();

    private NullRegistrarStore() { }

    public RegistrarSnapshot? Load() => null;

    public void Save(RegistrarSnapshot snapshot) { }
}
