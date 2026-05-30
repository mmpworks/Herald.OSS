---
gap-id: destructuring-policy
serilog-surface: IDestructuringPolicy (Destructure.ByTransforming / Destructure.With)
herald-status: carries-over (tree-bridge; throws loud at registration if bridge unreachable)
population-rank: high
regression-test-id: G-SEC.1
---

<!-- Heather T-H2: STANDALONE companion. SECURITY-CRITICAL — must carry the
     redaction-must-fire callout. A no-op'd redaction policy is a PII regression. -->

# Migrating a Custom Destructuring Policy

## Security contract, first

Herald never silently drops your redaction. If a policy cannot be bridged to the value-model tree, registration throws — loudly, at startup, before any log event is processed. There is no path where a policy appears to register and then silently fails to run.

The consequence of getting this wrong is not a build error or a test failure — it is PII flowing to a sink with no exception, no failed build, and no obvious signal. The loud-fail-at-registration contract is what prevents that.

## What you have in Serilog

Serilog supports two destructuring policy forms. Most codebases use one or the other.

**Form 1 — `ByTransforming<T>` (preferred):** maps a type to a projection object.

```csharp
.Destructure.ByTransforming<UserCredentials>(c => new { c.Username })
// password stripped — the projection object has no password field
```

**Form 2 — raw `IDestructuringPolicy`:** returns a `LogEventPropertyValue` tree node.

```csharp
public class PasswordStrippingPolicy : IDestructuringPolicy
{
    public bool TryDestructure(object value, ILogEventPropertyValueFactory factory,
        out LogEventPropertyValue result)
    {
        if (value is UserCredentials creds)
        {
            result = factory.CreateForDestructuredValue(new { creds.Username });
            return true;
        }
        result = null;
        return false;
    }
}
```

## Path 1 — ByTransforming (the worked example)

`ByTransforming<T>(Func<T, object>)` maps cleanly onto Herald's projection path. The projection lambda is identical; the anonymous object you return becomes the value-model tree node.

```csharp
// Before
.Destructure.ByTransforming<UserCredentials>(c => new { c.Username })

// After — identical call, recompile against the shim
.Destructure.ByTransforming<UserCredentials>(c => new { c.Username })
```

No code change. Recompile and verify (see [Verify](#verify-the-redaction-actually-fires) below).

## Path 2 — raw IDestructuringPolicy (tree bridge)

Herald's native destructuring policy returns a **string** projection; Serilog's returns a **tree** node. The raw-policy form bridges to the value-model tree via an adapter.

The bridge accepts `IDestructuringPolicy` and routes its output through the same tree-projection path as `ByTransforming`. The call is the same:

```csharp
.Destructure.With(new PasswordStrippingPolicy())
```

<!-- FILL AFTER P4: confirm bridge adapter is shipped and the exact registration throws on
     unreachable bridge (G-SEC.1 must be green before this note is removed). -->

If the bridge cannot be constructed for your policy (a corner case — the tree and your policy's output type are incompatible), registration throws at startup. The error message names the policy type and the reason. It does not fall back to a no-op.

## Step-by-step

1. Update the `using` directives in your policy file:
   ```csharp
   // Before
   using Serilog.Core;
   using Serilog.Events;

   // After (Layer 1)
   using MMP.Herald.Serilog.Core;
   using MMP.Herald.Serilog.Events;
   ```

2. For `ByTransforming`: no other changes. Rebuild.

3. For raw `IDestructuringPolicy`: rebuild and start the application. If registration throws, the error message tells you what went wrong.

4. Run verification below before declaring the migration done.

## Verify the redaction actually fires

A field-name check is not enough. The secret value must be absent from the **full serialized output** — not just absent from the top-level property dictionary. A leaked value can appear inside a nested `StructureValue`, inside a formatted message string, or inside an exception.

The assertion shape (matches G-SEC.1 in the test suite):

```csharp
var output = SerializeEventToJson(logEvent);
Assert.DoesNotContain("actualPasswordValue", output);
// Not just: Assert.False(logEvent.Properties.ContainsKey("Password"))
```

Run this check in your own test suite before cutting over to Layer 2.

## Deep dive

For the wire path, bridge adapter implementation, and the tree-projection mapping see [worked-examples/S5-destructuring-policy.md](../worked-examples/S5-destructuring-policy.md).
