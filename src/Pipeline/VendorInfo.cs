#nullable enable

using System.Reflection;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Rich vendor metadata for pipeline components, sinks, and plugins.
/// Travels with the component — when the vendor updates their package,
/// the metadata updates automatically.
///
/// Built-in Herald components use <see cref="MMP"/>.
/// Third-party plugins populate from their assembly metadata or explicitly.
/// The Dashboard renders this in the component detail panel.
/// </summary>
public sealed record VendorInfo(
    string Name,
    string? License = null,
    string? Copyright = null,
    string? Info = null,
    VendorContact? Contact = null)
{
    /// <summary>Built-in Herald vendor.</summary>
    public static VendorInfo MMP { get; } = new("MMP",
        License: "MIT",
        Copyright: "2026 MMP",
        Info: "Herald Structured Logging Framework",
        Contact: new VendorContact(
            Website: "https://github.com/mmp/herald"));

    /// <summary>Default for third-party plugins when no vendor info is provided.</summary>
    public static VendorInfo ThirdParty { get; } = new("Third-Party");

    /// <summary>Create vendor info from just a name (convenience for plugins).</summary>
    public static VendorInfo From(string name) => new(name);

    /// <summary>
    /// Create vendor info by inspecting an assembly's metadata attributes.
    /// Extracts name, copyright, and description from the assembly.
    /// </summary>
    public static VendorInfo FromAssembly(System.Reflection.Assembly assembly)
    {
        var name = assembly.GetName().Name ?? "Unknown";
        var ns = assembly.GetName().Name;
        if (ns is not null)
        {
            var firstDot = ns.IndexOf('.');
            if (firstDot > 0) name = ns[..firstDot];
        }

        var copyright = assembly.GetCustomAttribute<System.Reflection.AssemblyCopyrightAttribute>()?.Copyright;
        var description = assembly.GetCustomAttribute<System.Reflection.AssemblyDescriptionAttribute>()?.Description;

        return new VendorInfo(name, Copyright: copyright, Info: description);
    }
}

/// <summary>
/// Contact information for a vendor.
/// </summary>
public sealed record VendorContact(
    string? Website = null,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null,
    string? Country = null,
    string? SupportUrl = null,
    string? DocumentationUrl = null);
