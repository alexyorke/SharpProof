# SharpProof Configuration Profiles

SharpProof ships ready-to-copy analyzer configuration profiles in
`config/profiles`. Choose one adoption mode and one file format.

| Mode | Profile files | Intended use | Contract violations | Conservative unknowns | Optional evidence |
| --- | --- | --- | --- | --- | --- |
| Migration | `sharpproof-migration.editorconfig`, `sharpproof-migration.globalconfig` | First adoption with low noise | warnings | suggestions | disabled |
| Audit | `sharpproof-audit.editorconfig`, `sharpproof-audit.globalconfig` | Interactive review and evidence collection | warnings | warnings | enabled |
| CI | `sharpproof-ci.editorconfig`, `sharpproof-ci.globalconfig` | Build gate for proven violations | errors | warnings | limited |
| Strict | `sharpproof-strict.editorconfig`, `sharpproof-strict.globalconfig` | Maximum enforcement after cleanup | errors | errors | warnings |

Inferred contract adoption hints (`SP0034`-`SP0039` and `SP0046`) are high-confidence,
public-scope suggestions in Migration; medium-and-up suggestions across all
members in Audit; disabled in CI; and high-confidence suggestions in Strict.
They remain `suggestion` severity even in Strict because inferred annotations
require review and are not correctness failures.
The bundled suggestion-kind sets include nullable contract adoption through the
`nullability` kind where inferred suggestions are enabled.

All bundled profiles keep exact-proof suppression of external diagnostics off.
That feature changes diagnostics owned by the compiler or another analyzer and
therefore requires an explicit project decision; see
[exact-proof diagnostic suppression](proven-diagnostic-suppression.md).

The Audit GlobalConfig enables
`sharpproof_trusted_boundary_review_mode = all`, producing `SP0040` suggestions
for both applied and overridden pure trust candidates. The other GlobalConfig
profiles keep this reporting mode off. EditorConfig variants contain the
explicit `SP0040` severity but cannot set the global-only mode. See
[trusted boundary review](trusted-boundary-review.md).

Each mode has two variants:

- `sharpproof-<mode>.editorconfig` contains diagnostic severities and only
  options that SharpProof permits in per-tree EditorConfig sections. Copy it as
  `.editorconfig` or merge its `[*.cs]` section into an existing file.
- `sharpproof-<mode>.globalconfig` also contains global-only purity, SMT, and
  effect-summary options. Copy it as `.globalconfig`; do not move those
  global-only keys into a per-tree EditorConfig section.

The GlobalConfig variants also select classification policy: Migration uses
`pragmatic`, Audit and CI use `balanced`, and Strict uses `strict`. Review any
known-pure/impure entries, accepted attribute stub namespaces, assembly
boundary attributes, or additional generated summaries separately; see the
[purity classification policy audit](purity-policy.md).

For example:

```powershell
Copy-Item config/profiles/sharpproof-migration.editorconfig .editorconfig
```

Or, when compilation-wide settings should be part of the profile:

```powershell
Copy-Item config/profiles/sharpproof-ci.globalconfig .globalconfig
```

Local EditorConfig entries closer to a source file can override diagnostic
severities and global-and-tree SharpProof options. Keep global-only options in
the selected GlobalConfig or equivalent MSBuild properties. See
[`contracts.md`](contracts.md#configuration) for every supported key, value,
default, and scope.

The profiles intentionally list every public `SP*` diagnostic. This makes
profile behavior reviewable when new diagnostics are added: repository tests
fail until each adoption mode assigns the new rule an explicit severity.
They also configure the compiler and .NET SDK diagnostics used for catalog
items that SharpProof deliberately delegates instead of duplicating. See the
[common C# bug coverage matrix](common-bug-coverage.md) for those boundaries.
SP0048-SP0076 are profile-enabled rather than descriptor-default diagnostics,
which keeps package upgrades quiet until a project selects an adoption policy.
