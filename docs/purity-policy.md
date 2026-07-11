# Purity Classification Policy

SharpProof separates verification contracts from classification policy.
`[Pure]` and `[EnforcePure]` ask the analyzer to verify a body; they do not
bypass analysis or declare an external boundary trusted. The settings and
boundary sources below can change the classification that verification sees,
so they require explicit review.

## Configuration Policy Audit

All classification-policy options are global-only. The analyzer marks them
with `PurityPolicyImpact` metadata, and repository tests fail if the audited set
changes without updating this document.

| Setting | Default | Audited impact | Exact behavior |
| --- | --- | --- | --- |
| `sharpproof_known_impure_methods` | empty | `ForcesImpure` | Exact configured method symbols are impure before generated or built-in purity evidence. |
| `sharpproof_known_pure_methods` | empty | `TrustsPure` | Exact configured method symbols are trusted pure only after higher-priority impure and generated policies. They also exempt that exact member from a configured impure namespace or type. |
| `sharpproof_known_impure_namespaces` | empty | `ForcesImpure` | Members under the configured namespace boundary are impure unless the exact member is configured pure. Parent namespace matches apply to nested namespaces. |
| `sharpproof_known_impure_types` | empty | `ForcesImpure` | Members on the configured type boundary are impure unless the exact member is configured pure. Containing types are checked. |
| `sharpproof_purity_profile` | `balanced` | `ChangesStrictness` | `strict` rejects mutable `this` field reads that balanced policy accepts. See [Profile Semantics](#profile-semantics). |
| `sharpproof_attribute_stub_namespaces` | `SharpProof.Attributes` | `TrustsPure`, `ForcesImpure`, `ChangesAttributeIdentity` | Adds namespaces whose source-only SharpProof attributes are accepted. This can make boundary attributes active, so adding `<global>` or another namespace is a trust decision. |
| `sharpproof_enable_effect_summary_json` | `false` | `TrustsPure`, `ForcesImpure`, `EnablesGeneratedOverrides` | Enables identity-validated additional `*.SharpProof.EffectSummary.json` files. Embedded built-in summaries remain active when this option is false. |

Use exact Roslyn display-style member names, for example:

```ini
is_global = true

sharpproof_known_impure_namespaces = Contoso.Legacy
sharpproof_known_impure_types = Contoso.Legacy.MutableClock
sharpproof_known_impure_methods = Contoso.Legacy.Clock.Now()
sharpproof_known_pure_methods = Contoso.Legacy.MathShim.Abs(int)
sharpproof_purity_profile = strict
```

Method lists accept `;`, `,`, or newline-separated entries. Property getter
entries may use the documented property/getter aliases. An invalid value is
reported as `SP0025`; it is not silently accepted.

SMT modes, SMT budgets, and bounded-analysis limits can change whether a proof
finishes or becomes unknown. They are proof-completeness controls rather than
classification trust policy and are documented separately in
[SMT lifecycle](smt-lifecycle.md) and [analysis limits](analysis-limits.md).
Suggestion, explanation, exception-reporting, and suppression options change
which diagnostics are emitted, not the underlying purity classification.

## Boundary Source Audit

The following stable IDs are the non-configuration sources audited alongside
the option registry.

| Audit ID | Source | Effect | Decision rule |
| --- | --- | --- | --- |
| `member_impure_attribute` | Direct `[Impure]` | Forces impure | Wins over direct or assembly pure trust. |
| `member_pure_external_attribute` | Direct `[PureExternal]` | Trusts pure | Overrides an assembly `[Impure]` default, but not a direct `[Impure]` conflict. |
| `recognized_external_pure_attribute` | JetBrains or Code Contracts `[Pure]` | Trusts pure boundary evidence | An assembly `[Impure]` default remains higher priority because it is an explicit SharpProof boundary. |
| `assembly_impure_attribute` | `[assembly: Impure]` | Forces impure by default | A direct SharpProof boundary on a member can override the assembly default. If both assembly defaults are present, impure wins. |
| `assembly_pure_external_attribute` | `[assembly: PureExternal]` | Trusts pure by default | A direct `[Impure]` member overrides the assembly default. |
| `additional_generated_summary` | Additional effect-summary JSON | Trusts pure or forces impure | Must match the exact assembly and method identity. A compatible additional row outranks an embedded row for the same symbol. |
| `built_in_generated_summary` | Embedded generated effect summaries | Trusts pure or forces impure | Participates by default for compatible metadata methods. |
| `built_in_purity_catalog` | Built-in semantic/member policy | Trusts pure or forces impure | Applies after trusted generated evidence. |

`[PureExternal]` is a trust assertion for a body SharpProof cannot or should not
inspect. `[Impure]` is a conservative boundary assertion. At assembly scope
they provide defaults; at method, constructor, or property scope they provide
direct overrides. Conflicting direct purity attributes also produce `SP0005`.

By contrast, `[Pure]` and `[EnforcePure]` remain verification contracts. A
method carrying either attribute still produces `SP0002` when its analyzed
body or a higher-priority policy source is impure.

## Decision Order

For a method or property getter, the analyzer applies classification policy in
this order before or during body analysis:

1. Resolve direct and assembly boundary attributes. Direct `[Impure]` wins a
   direct conflict. Direct `[PureExternal]` and direct `[Impure]` override the
   opposite assembly default. Assembly `[Impure]` wins an assembly conflict.
2. Apply configured impure namespace/type boundaries. An exact
   `sharpproof_known_pure_methods` entry is the one exemption at this step.
3. Apply an exact `sharpproof_known_impure_methods` entry. If the same member is
   in both exact lists, impure wins.
4. For metadata members, apply trusted generated purity evidence. Compatible
   additional summaries outrank embedded summaries; within the same source
   priority, `impure` outranks `pure`, which outranks
   `conservative_unknown`.
5. Apply built-in impure semantic/member policy.
6. Apply configured and built-in known-pure member policy. A configured-pure
   entry does not override an exact configured-impure entry, trusted generated
   impure evidence, or built-in impure evidence.
7. Analyze the source body or use conservative metadata fallback. The selected
   purity profile affects fallback rules here.

This ordering means a broad configured impure boundary can have a narrow exact
pure exception, while an exact impure entry remains a hard project policy.
Generated rows can correct built-in catalog classifications, but cannot bypass
an explicit exact configured-impure method or a direct boundary attribute.

## Generated Purity Overrides

The analyzer always carries an embedded, generated runtime catalog. Setting
`sharpproof_enable_effect_summary_json = true` additionally loads matching
AdditionalFiles. Additional rows are trusted only when their assembly identity,
artifact source, metadata token, and method-body identity are compatible with
the referenced binary. Missing, stale, malformed, or incompatible inputs are
ignored and reported as `SP0032`.

For the same trusted symbol, an additional row has higher source priority than
the embedded row. A same-priority conflict resolves conservatively in this
order: `impure`, `pure`, `conservative_unknown`. See
[Effect Summary Generation](effect-summary.md) for schema, identity, and
provenance requirements.

## Profile Semantics

`strict` treats a mutable field read through `this` as impure unless another
sound ownership or readonly rule proves it safe. `balanced` and `pragmatic`
currently use the same classification behavior for that rule; they remain
separate adoption labels so migration policy can evolve without silently
changing strict projects. The bundled GlobalConfig profiles select:

- Migration: `pragmatic`
- Audit and CI: `balanced`
- Strict: `strict`

## Reviewing And Auditing A Result

For an impure `SP0002`, inspect these diagnostic properties in SARIF, the
explain report, or test output:

- `sharpproof.impurity.category`
- `sharpproof.impurity.rule`
- `sharpproof.impurity.symbol`
- `sharpproof.impurity.catalog_source`
- `sharpproof.impurity.callee_chain`

Common catalog sources include `config_known_impure`,
`known_impure_namespace_or_type`, `generated_purity_summary`, and `attribute`.
Pure trust shortcuts do not emit `SP0002`. Set
`sharpproof_trusted_boundary_review_mode` to `used` or `all` to emit structured
`SP0040` evidence for the exact shortcuts encountered by a compilation; see
[Trusted Boundary Review](trusted-boundary-review.md). The review mode changes
reporting only and therefore is not one of the seven classification-policy
options above.

The repository audit is mechanical in two places:

1. `AnalyzerConfigurationOptionRegistry.PurityPolicyOptions` is the exact set
   of configuration knobs with non-zero `PurityPolicyImpact`.
2. `PurityPolicyAuditRegistry.BoundarySources` is the stable inventory of
   attribute, generated-summary, and built-in boundary sources.

Tests require every registry entry and audit ID to appear in this document and
exercise the important exact-list precedence rules. The static inventory here
is the policy foundation for the operational review mode and remains the place
to inspect policy sources that no analyzed call used.
