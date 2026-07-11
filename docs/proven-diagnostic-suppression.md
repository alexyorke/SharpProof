# Exact-Proof Diagnostic Suppression

SharpProof can opt in to suppressing a bounded allowlist of compiler and
third-party analyzer warnings when the matching runtime-hazard trigger is
proved unreachable at the same source location. The feature is off by default.

```ini
[*.cs]
sharpproof_suppress_proven_diagnostics = true
sharpproof_suppression_diagnostic_ids = CS8602, CS8629, CS8509
```

Both options are global-and-per-tree. When suppression is enabled and the ID
option is omitted, every supported ID is eligible. Set the ID list to `none`
to keep suppression disabled for a narrower tree. Unknown IDs produce `SP0025`
instead of silently widening the allowlist.

## Exact-Proof Gate

SharpProof reports a Roslyn suppression only when all of these conditions hold:

- Roslyn considers the original diagnostic suppressible: its default severity
  is not error, it is configurable, and source code has not already suppressed
  it.
- The external diagnostic ID has a static `SPS*` suppression descriptor and is
  selected by `sharpproof_suppression_diagnostic_ids`.
- The external diagnostic span is contained by the matching SharpProof
  runtime-hazard span in the same syntax tree.
- The hazard and proof statuses are both `Unreachable`, the proof backend is
  concrete, the unknown reason is `None`, and analysis was not truncated.

`Proven` means the hazard can occur, so it never suppresses a warning.
`Unknown`, `Unsupported`, timeout, disabled-SMT, over-budget, and truncated
results also leave the original diagnostic visible.

The same conservative rule applies when native SMT is unavailable on the
current host: SharpProof leaves the warning visible. The packaged analyzer
loads its supported Windows and macOS x64 native solver through the policy in
[Native SMT Packaging And Platform Support](native-smt-packaging.md).

Method-entry `[Requires]` contracts can contribute proof facts only through a
conservative stable subset. The condition must lower to typed symbolic IR,
reference only parameters of the annotated member, avoid calls and element
reads, and use only immutable `Length` or nullable `HasValue` member facts.
Any assignment, increment, or `ref`/`out` mutation of a referenced parameter in
the member invalidates that entry fact for suppression.

For example, the C# compiler does not interpret SharpProof contracts, but the
opt-in suppressor can discharge its nullable warning from the same exact proof:

```csharp
#nullable enable
using SharpProof.Attributes;

public sealed class Parser
{
    [Requires("text != null")]
    public int Length(string? text) => text.Length;
}
```

Without an exact contract or guard, `CS8602` remains visible:

```csharp
public int UnknownLength(string? text) => text.Length;
```

## Supported Diagnostic IDs

The table is the complete static allowlist. Third-party IDs are effective only
when their producer reports a normal Roslyn diagnostic in the same compilation.

| Suppression | External diagnostic IDs | Required SharpProof proof |
| --- | --- | --- |
| `SPS0001`-`SPS0002` | `CS8602`, `CS8670` | Null-dereference trigger unreachable |
| `SPS0003` | `CS8605` | Unbox-null trigger unreachable |
| `SPS0004` | `CS8629` | Nullable-value-without-value trigger unreachable |
| `SPS0005`-`SPS0007`, `SPS0017`-`SPS0018` | `CS8509`, `CS8524`, `CS8846`, `CS8655`, `CS8847` | Non-exhaustive switch no-match path unreachable |
| `SPS0008` | `S2259` | Null-dereference trigger unreachable |
| `SPS0009` | `S3655` | Nullable-value-without-value trigger unreachable |
| `SPS0010`-`SPS0011` | `V3080`, `V3095` | Null-dereference trigger unreachable |
| `SPS0012`-`SPS0013` | `V3106`, `V3218` | Index-out-of-range trigger unreachable |
| `SPS0014`-`SPS0016` | `V3064`, `V3151`, `V3152` | Divide-by-zero trigger unreachable |

The compiler rule meanings are documented in Microsoft's
[nullable warning reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/nullable-warnings)
and
[pattern-matching warning reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/pattern-matching-warnings).
The third-party rule families come from Sonar's
[`S2259`](https://rules.sonarsource.com/csharp/rspec-2259/) and
[`S3655`](https://rules.sonarsource.com/csharp/rspec-3655/) references and the
[PVS-Studio warning catalog](https://pvs-studio.com/en/docs/warnings/).

## Auditing And Proof Links

Every descriptor justification identifies the exact proof family, links back
to this policy, and points to the runtime-hazard query workflow. Roslyn retains
programmatic suppressions in SARIF and MSBuild binary logs and emits its
suppression audit record at detailed build verbosity.

Use the original diagnostic location from that record with either inspection
form:

```powershell
SharpProof.SymbolicCli explain --project Example.csproj --file Example.cs --line 42
SharpProof.SymbolicCli --project Example.csproj --file Example.cs --line 42 --runtime-hazards --include-unproven-hazards
```

The matching hazard must show `Unreachable`, no unknown reason, and no analysis
truncation. To disable one suppression descriptor independently of the
SharpProof allowlist, configure its `SPS*` ID through the normal Roslyn
suppression controls; the original external diagnostic then remains visible.
