// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

namespace MMP.Herald.Templating;

/// <summary>
/// Closed set of Herald's built-in property-naming policies. Used by both
/// the build-time <c>CompileTimeNameResolver</c> (which bakes policy-applied
/// names into <c>[HeraldLog]</c> output and the multi-policy interceptor)
/// and the runtime dispatch path (which switches on the active policy's
/// kind without an interface call).
///
/// <para>
/// All three built-ins select the same way — template token first, then
/// caller-argument-expression, then <c>argN</c>. They differ only in the
/// casing transform applied to the selected source. <see cref="Custom"/>
/// means "a consumer-supplied policy" — the build-time resolver cannot bake
/// names for it and the runtime dispatcher falls through to
/// <see cref="IPropertyNamingPolicy.ResolveAll"/>.
/// </para>
///
/// <para>
/// <b>Why a new type instead of leaning on policy ids.</b> Two custom
/// policies that both report <c>Id = "kebab"</c> would silently share an
/// id; the enum is exhaustive over the built-in set and never collides.
/// </para>
///
/// <para>
/// <b>Public V1 surface.</b> The four-value shape is the V1 contract — naming,
/// member order, and the meaning of each value are stable. Future built-in
/// policies (KebabCase, ScreamingSnake, etc.) extend additively; the V1
/// four are guaranteed not to be renamed or renumbered.
/// </para>
/// </summary>
public enum BuiltinPolicy
{
    /// <summary>
    /// Default Herald.OSS 1.0+ policy. Token-first source selection with
    /// <c>ToPascalCase</c> applied — uppercase first letter when it's
    /// currently lowercase; leave already-Pascal, all-caps, and underscored
    /// inputs alone. Mirrors <c>PascalCasePolicy</c>.
    /// </summary>
    Pascal = 0,

    /// <summary>
    /// Token-first source selection with <c>ToSnakeCase</c> applied —
    /// inserts underscores at camel/Pascal boundaries with acronym
    /// coalescing, then lowercases the whole result. Mirrors
    /// <c>SnakeCasePolicy</c>.
    /// </summary>
    Snake = 1,

    /// <summary>
    /// Token-first source selection with <c>ToCamelCase</c> applied —
    /// lowercase first letter when it's currently uppercase; leave
    /// already-camel and underscored inputs alone. Mirror of Pascal's
    /// restraint, inverted case test. Mirrors <c>CamelCasePolicy</c>.
    /// </summary>
    Camel = 2,

    /// <summary>
    /// Any consumer-supplied <see cref="IPropertyNamingPolicy"/> outside the
    /// built-in set. Build-time bake is unavailable; runtime dispatch goes
    /// through <see cref="IPropertyNamingPolicy.ResolveAll"/>.
    /// </summary>
    Custom = 3,
}
