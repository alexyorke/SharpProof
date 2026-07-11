# SharpProof Configuration Profiles

SharpProof ships ready-to-copy analyzer configuration profiles in
`config/profiles`. Choose one adoption mode and one file format.

| Mode | Profile files | Intended use | Contract violations | Conservative unknowns | Optional evidence |
| --- | --- | --- | --- | --- | --- |
| Migration | `sharpproof-migration.editorconfig`, `sharpproof-migration.globalconfig` | First adoption with low noise | warnings | suggestions | disabled |
| Audit | `sharpproof-audit.editorconfig`, `sharpproof-audit.globalconfig` | Interactive review and evidence collection | warnings | warnings | enabled |
| CI | `sharpproof-ci.editorconfig`, `sharpproof-ci.globalconfig` | Build gate for proven violations | errors | warnings | limited |
| Strict | `sharpproof-strict.editorconfig`, `sharpproof-strict.globalconfig` | Maximum enforcement after cleanup | errors | errors | warnings |

Inferred contract adoption hints (`SP0034`-`SP0039`) are high-confidence,
public-scope suggestions in Migration; medium-and-up suggestions across all
members in Audit; disabled in CI; and high-confidence suggestions in Strict.
They remain `suggestion` severity even in Strict because inferred annotations
require review and are not correctness failures.

Each mode has two variants:

- `sharpproof-<mode>.editorconfig` contains diagnostic severities and only
  options that SharpProof permits in per-tree EditorConfig sections. Copy it as
  `.editorconfig` or merge its `[*.cs]` section into an existing file.
- `sharpproof-<mode>.globalconfig` also contains global-only purity, SMT, and
  effect-summary options. Copy it as `.globalconfig`; do not move those
  global-only keys into a per-tree EditorConfig section.

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
