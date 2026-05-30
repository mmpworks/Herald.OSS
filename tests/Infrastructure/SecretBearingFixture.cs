#nullable enable

namespace MMP.Herald.OSS.Tests.Infrastructure;

/// <summary>
/// A POCO used by redaction-policy tests.
///
/// <para>
/// Carries a known secret value (<c>Password = "hunter2"</c>) alongside a benign field
/// (<c>Name</c>). Test setup destructs this object into a log event's property tree;
/// the redaction policy is applied; <see cref="SecretScanner"/> then proves the secret
/// never appears anywhere in the final event.
/// </para>
///
/// <para>
/// The canonical secret literal is <c>"hunter2"</c> — tests that assert clean output
/// pass this string to <see cref="SecretScanner.FindAll"/>. Tests that assert the secret
/// IS present (scanner self-tests) also use this value.
/// </para>
/// </summary>
public sealed class SecretBearingFixture
{
    /// <summary>Benign display name. Not subject to redaction.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The secret field. Redaction policy must strip this value before it
    /// reaches any sink or rendered message.
    /// </summary>
    public string Password { get; set; } = "";
}
