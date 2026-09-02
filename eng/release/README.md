# Release-owner evidence

SharpProof preview release evidence is produced from one immutable source
commit in the canonical Linux amd64 container. There is no reviewer-count or
time-based freeze. GitHub repository protection, tag creation, publishing
environments, and credentials remain owner-controlled external state.

`environment-contract.json` declares that external configuration.
`scripts/Test-SharpProofReleaseConfiguration.ps1` verifies protected tag
rules, environment policies, required variable/secret names, and NuGet OIDC
wiring without reading secret values.

## Candidate evidence

The tag workflow must use the package job's exact six NuGet files. It binds
qualification to:

- the annotated tag and exact repository commit;
- `eng/container/Dockerfile`, `eng/container/toolchain.json`, and
  `compose.yaml`;
- base image, SDK, PowerShell, Z3 archive, managed Z3, and `libz3.so` inputs;
- the exact three-package graph and package/symbol-package identities;
- deterministic Debug/Release, acceptance, fuzz, corpus, performance,
  coverage, mutation, dependency, and publication-plan evidence; and
- five reviewed pilot reports from the same tested package bytes.

All verifier and portable-consumer gates execute in the canonical container.
The analyzer packages remain operating-system-neutral, but no native-host SDK
or MSBuild job serves as release qualification.

## Promotion

The allowlisted preview tag publishes to the protected private environment
first. Public promotion reuses the already-qualified package bytes; it does
not rebuild them. Before any write, the publisher validates the release
manifest and package layout, queries the target V3 feed for conflicting main
packages, and publishes in dependency order:

1. `SharpProof.Attributes`
2. `SharpProof`
3. `SharpProof.Verifier`

Publication is non-overwriting and does not use `--skip-duplicate`. A partial
or conflicting publication requires a new version. `-PlanOnly` performs the
same local validation and records the ordered dry-run plan without network
writes.

## External configuration still required

- protect `master` and annotated `v*` tags;
- configure `nuget.private-preview` with its HTTPS V3 source and API key;
- configure `nuget.org` trusted publishing and `NUGET_USER`;
- select and review the five pilot repositories; and
- qualify Docker Engine on Linux x64 and Docker Desktop's Linux amd64 engine
  on Windows x64 using the same source commit and image inputs.

Secret values never belong in repository evidence. Records name only the
required secret or variable identifiers and immutable workflow artifacts.
