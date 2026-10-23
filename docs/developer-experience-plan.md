# Developer experience delivery checklist

This checklist covers the seven developer-experience areas in this change. Checked
items describe implemented code and documentation. They do not claim complete test
coverage, final-commit CI success, or publication. The [developer tools guide](developer-experience.md)
describes consumer usage; executable tests and protocol validation remain authoritative.

## Implemented scope

- [x] **CLI workflow:** project-aware doctor, complete-evidence explanation, baseline
  comparison with an opt-in regression gate, and NUnit regression export. Existing
  SDK-only doctor and positional explanation remain supported. See
  [command routing](../src/SharpProof.Tool/DeveloperCommands.cs),
  [doctor](../src/SharpProof.Tool/SetupDoctor.cs), and
  [exporter](../src/SharpProof.Tool/NUnitExporter.cs).
- [x] **Readable diagnostics and evidence:** run status, selected coverage, vacuity,
  assumptions, source labels, and typed Unknown details are available where recorded.
  Candidate models retain their distinction from replay-confirmed counterexamples.
  Source labels do not replace semantic IDs or protocol authority. See
  [reporting](../src/SharpProof.Reporting/VerificationReport.cs) and
  [verification evidence](verification-results.md).
- [x] **Safe editor assistance:** contract-companion generation, conservative
  missing-member scaffolding, and the existing Result-type correction. Companions
  contain editable signatures and throwing placeholders, not inferred contracts.
  Ambiguous or unsupported shapes are declined. See
  [scaffolding](../src/SharpProof.CodeFixes/CompanionScaffolding.cs) and
  [editor guidance](developer-experience.md#editor-assistance).
- [x] **Portable and container templates:** version-matched public SharpProof facade,
  explicit advisory/no-verifier or strict/verifier configuration, pinned SDK and C# 13,
  and retained container evidence with CI upload. See the
  [template definition](../src/SharpProof.Templates/content/.template.config/template.json).
- [x] **Standalone canonical consumer image:** a Linux amd64 consumer target and
  entrypoint permit verification without the SharpProof checkout; generated Compose
  uses an immutable qualified image reference and isolated compiler/package volumes.
  See the [image](../eng/container/Dockerfile) and
  [entrypoint](../eng/container/consumer-entrypoint.sh).
- [x] **Optional packages and release integration:** Tool, Templates, and CodeFixes
  are versioned with the core packages. Qualification, exact artifact identities,
  digest-bound image checks, and separate optional-package publication paths are wired
  into the [package workflow](../.github/workflows/package-consumers.yml). The core
  three-package graph remains unchanged.
- [x] **Contract-authoring guidance:** the [first proof](getting-started.md#your-first-proof),
  [public API](public-api.md), [companion rules](diagnostic-examples.md#contractfor-diagnostics),
  and [developer workflow](developer-experience.md) connect static clauses, evidence
  review, editor scaffolds, and a failing-then-passing runtime regression.

## Verification and retained evidence

Run repository commands in the canonical Linux amd64 container. A host Git worktree
with external Git metadata uses the [host wrapper](container-development.md#finite-tasks-from-the-host-checkout)
for the same tooling command and arguments. Run these checks serially for one worktree:

```text
docker compose run --rm tooling test -Target src/SharpProof.Tool.Tests/SharpProof.Tool.Tests.csproj
docker compose run --rm tooling test -Target src/SharpProof.DeveloperExperience.Tests/SharpProof.DeveloperExperience.Tests.csproj
docker compose run --rm tooling test -Target src/SharpProof.CodeFixes.Tests/SharpProof.CodeFixes.Tests.csproj
docker compose run --rm tooling test -Target src/SharpProof.Worker.Tests/SharpProof.Worker.Tests.csproj -TestFilter FullyQualifiedName~Protocol
docker compose run --rm tooling docs
docker compose run --rm tooling layout
docker compose run --rm tooling format-check
docker compose run --rm --build lint
docker compose run --rm -e SHARPPROOF_CONSUMER_IMAGE tooling developer-experience -Configuration Release
docker compose run --rm tooling pr
```

The developer-experience task requires the qualified immutable consumer image reference
in `SHARPPROOF_CONSUMER_IMAGE`. Its normal mode keeps repository analyzers enabled;
`-Fast` is development feedback only. The package workflow additionally exercises the
generated consumer inside the standalone candidate image. Installing a template and
building it directly inside tooling alone does not establish that image workflow.
The generated digest-based Compose pull additionally requires that exact image
reference to be available from the registry; running a locally loaded candidate tag
does not establish published-digest availability.

The retained local run `artifacts/developer-experience/71284e13e51740d59030881baf45a9a4`
records 28 phases with their expected exits, including exact local package installation,
both generated policies, comparison, a strict Refuted build, an NUnit assertion that
fails before the fix and passes after it, and a netstandard2.0 regression on net8.0.
Its package manifests record base source identity
`fe2a05235ae423d1ce391293b4bcfb80c9b7356f`. This is prior local workflow evidence, not
qualification of subsequent commits. Other local reports cover targeted tests, docs,
layout, formatting, lint, and a standalone image build. These ignored artifacts are
available in the working checkout and are not part of a cloned documentation set.

Before merge or release, retain the final pushed commit, exact image/package identities,
command results, NUnit reports, and CI artifact IDs. Obtain successful PR, package-consumer,
and [complete coverage workflows](development-workflows.md#coverage-collection-and-acceptance)
for that commit.
Targeted test counts and the 28-phase run do not establish full coverage. Coverage shard
collection is incomplete until its authenticated merge and coverage gate pass.

## Boundaries and remaining decisions

- Doctor reads trusted MSBuild imports and existing restore evidence. It never restores,
  builds, pulls images, or proves native loading. Dependency changes require a fresh
  restore before its package checks are meaningful.
- Complete-evidence commands check recorded consistency; they do not authenticate an
  arbitrary worker binary or prove that current edited source was verified. Comparison
  requires a reviewed baseline for the same assembly, framework, and specifications.
- The export snapshot records compiler options, source trees, and reference metadata,
  so compiler-option or unrelated-source edits can change the artifact digest and miss
  the complete worker-response cache even when the lowered verification model is equivalent.
  Exact artifact-byte binding and the other existing cache inputs remain required,
  without changing callable/claim identities or proof semantics or promising broader
  reuse or a performance improvement.
- Export supports direct replayed scalar `Refuted` postconditions on public synchronous
  static methods and public containing types without type parameters. Its C# 13,
  expression, compiler-input, and runtime limits are listed in the
  [export guide](developer-experience.md#export-an-nunit-regression). No runtime contract
  mode is added. Keep generated tests outside consumer source globs; the starter's
  project-only Compose mount needs an extra writable sibling-parent bind mount, with
  a new child as the export output. Keep absolute project-reference mount paths stable
  between export and test. Private/local feeds also require container-visible sources
  and credentials; host NuGet configuration is not automatically inherited.
- Companion scaffolding requires review and authored clauses before use. Companion
  repair has no Fix All; the existing Result-type correction retains its own Fix All.
  Successful Roslyn action tests do not establish qualification of every editor host.
- Merge, final CI/coverage acceptance, and release publication are separate outcomes.
  Choose a release version/tag, qualified image digest, target feed, package ownership,
  and required environment approvals before publishing. This implementation checklist
  does not create a release version/tag, publish packages/images, or configure accounts.
  Follow the [release process](../eng/release/README.md).
