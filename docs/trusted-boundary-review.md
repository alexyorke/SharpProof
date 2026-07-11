# Trusted Boundary Review

SharpProof can emit an opt-in, compilation-wide audit of the pure trust
shortcuts that callable analysis actually encounters. The review is designed
for code review, migration audits, and SARIF inspection; it does not change a
purity classification.

Set the global-only option in a `.globalconfig` file:

```ini
is_global = true

sharpproof_trusted_boundary_review_mode = used
dotnet_diagnostic.SP0040.severity = suggestion
```

The allowed modes are:

| Mode | Behavior |
| --- | --- |
| `off` | Default. Emit no trusted-boundary review diagnostics. |
| `used` | Report only the pure shortcut selected by policy resolution. |
| `all` | Report selected shortcuts and pure candidates overridden by a stronger boundary, configured policy, or generated summary. |

The bundled Audit GlobalConfig selects `all`. Migration, CI, and Strict keep
the mode off so adding SharpProof does not create a new informational stream.
Per-tree `.editorconfig` values are invalid and produce `SP0025` because trust
policy is resolved once per compilation.

## SP0040 Evidence

`SP0040` is reported at the first stable source use of an exact referenced
symbol. Repeated calls to the same symbol with the same trust source and value
produce one diagnostic, including when Roslyn runs analyzer callbacks
concurrently. Unused configuration entries and attributes are not reported;
the static inventory remains in the
[purity classification policy](purity-policy.md).

Every diagnostic carries these structured properties:

| Property | Meaning |
| --- | --- |
| `sharpproof.trusted_boundary.symbol` | Exact Roslyn display symbol observed by analysis. |
| `sharpproof.trusted_boundary.source` | Stable source ID for the pure trust candidate. |
| `sharpproof.trusted_boundary.value` | Exact configured entry, attribute metadata name, summary path, generated symbol key, or built-in rule ID. |
| `sharpproof.trusted_boundary.disposition` | `applied` or `overridden`. |
| `sharpproof.trusted_boundary.overridden_by` | Stable source ID of the stronger policy, empty when applied. |
| `sharpproof.trusted_boundary.override_value` | Exact configured value, attribute, summary path/key, or built-in rule that won. |
| `sharpproof.trusted_boundary.classification` | Classification supplied by the candidate; currently `pure`. |

Normal baseline identity and evidence-schema properties are also included, so
`SP0040` can be suppressed through a reviewed
[SharpProof baseline](baselines.md) without disabling the mode globally.

## Reported Trust Sources

The review uses stable IDs shared with the policy audit where applicable:

| Source ID | Reported value |
| --- | --- |
| `member_pure_external_attribute` | Exact `SharpProof.Attributes.PureExternalAttribute` metadata name. |
| `recognized_external_pure_attribute` | Exact JetBrains or Code Contracts pure attribute metadata name. |
| `assembly_pure_external_attribute` | Exact assembly attribute metadata name. |
| `config_known_pure_method` | Original `sharpproof_known_pure_methods` entry, including a matched getter alias. |
| `additional_generated_summary` | Path of the identity-compatible additional summary. |
| `built_in_generated_summary` | Exact embedded generated-summary symbol key. |
| `built_in_purity_catalog` | Built-in semantic rule ID, including implicit metadata value-type construction. |

In `all` mode, `overridden_by` can additionally identify direct or assembly
`[Impure]`, direct `[PureExternal]`, configured impure namespace/type/member
policy, a stronger generated summary, or built-in impure policy. The
`override_value` property preserves the exact winning attribute, configured
entry, summary path/key, or built-in rule.

## Example

```ini
is_global = true

sharpproof_known_pure_methods = Contoso.Legacy.Clock.Read(int)
sharpproof_trusted_boundary_review_mode = all
```

For two calls to `Contoso.Legacy.Clock.Read(int)`, SharpProof emits one
`SP0040`. If an identity-compatible additional summary classifies that method
as impure, the configured-pure candidate has disposition `overridden`, source
`config_known_pure_method`, `overridden_by=additional_generated_summary`, and
the exact additional-file path in `override_value`.

Use `used` for a concise record of trust that affected the run. Use `all` when
reviewing stale allowlists, redundant annotations, generated-summary
precedence, or direct contracts that intentionally supersede broader policy.
