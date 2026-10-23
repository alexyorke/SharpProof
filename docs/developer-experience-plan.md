# Developer experience delivery checklist

This checklist preserves the seven existing developer-experience areas and tracks
the next six improvements. Checked foundation items describe code and documentation,
not complete test coverage, final-commit CI success, interactive editor qualification,
or publication. The [developer tools guide](developer-experience.md)
describes consumer usage; executable tests and protocol validation remain authoritative.

## Existing foundation

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

## Next six improvements

Every row needs current-candidate evidence. API implementation and fixture tests alone
do not qualify a real editor or publish the official private preview.

| Improvement | Intended behavior | Required acceptance evidence |
| --- | --- | --- |
| Workspace template | Pinned local Tool, optional private editor package, private-feed source configuration, and portable/container setup | Generate/install exact packages, restore, prove/refute, and run generated Compose; Pending |
| Existing-project init | Simple selected project directory, preview by default, explicit apply, portable default, qualified container image; multi-project topology comes from the workspace template | No-write preview, reviewed apply, conflicts/unsupported projects, idempotence, and build; Pending |
| Report formats | Named developer commands offer text, JSON, and Markdown; legacy syntax remains | One machine document for success/status 1, stderr-only invalid-input errors, and syntax compatibility; Pending |
| Consumer baseline CI | Exact PR base SHA, trusted workflow/source evidence, report default, explicit enforcement | Successful exact-base comparison and rejection of missing/wrong/stale/fork-controlled authority; Pending |
| Relocatable regression | Relative project reference and workspace export using the existing Compose service | Move complete workspace, export with captured input validation, fail-before/pass-after, then fresh proof; Pending |
| Optional diagnostic action | SPED0001 exposes conservative companion generation alongside refactoring | Loader/action tests plus all three actual Windows x64 editor sessions; Pending |

The nightly fuzz runner rebuild separately uses the 600-second solution-build
allowance. Retained/rotating invocation limits and worker proof budgets remain
unchanged; see [analysis limits](analysis-limits.md).

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

Evidence from earlier developer-experience changes does not qualify this candidate.
Record actual current run identities, package/image hashes, scope, analyzer mode,
phase results, and source commit rather than carrying forward an earlier local pass.
The [private release receipt](private-preview-release-receipt.md) starts every current
qualification/publication field as Pending; the
[editor runbook](editor-acceptance.md) likewise leaves all three real editors pending.

Before merge or release, retain the final pushed commit, exact image/package identities,
command results, NUnit reports, and CI artifact IDs. Obtain successful PR, package-consumer,
and [complete coverage workflows](development-workflows.md#coverage-collection-and-acceptance)
for that commit.
Targeted test counts and a selected consumer run do not establish full coverage. Coverage shard
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
  a new child as the export output. Workspace mode keeps the library and tests in one
  existing mount and emits relative project references; preserve that directory
  relationship when moving the workspace. Private/local feeds require container-visible sources
  and credentials; host NuGet configuration is not automatically inherited.
- Companion scaffolding requires review and authored clauses before use. Companion
  repair has no Fix All; the existing Result-type correction retains its own Fix All.
  Successful Roslyn action tests do not establish qualification of every editor host.
- Merge, final CI/coverage acceptance, and release publication are separate outcomes.
  The planned official `1.0.0-preview.1` uses the private owner-only initial
  `alexyorke/SharpProof-preview` path. Manual qualification freezes an unsigned private
  candidate without publishing. Exact-candidate editor acceptance precedes the
  separate `stage-registry` promotion; owner-only access evidence and fresh private
  feed/digest read-back precede `finalize` and the release tag. Preserve separate source,
  candidate-workflow, and promotion-workflow SHAs plus the later offline signature;
  public CI validation versions are distinct. All current candidate, editor, access,
  signature, and publication results remain Pending in the receipt. Source approval
  requires all six latest allowed exact-master-SHA workflows, including nightly;
  a newer pending nightly blocks an older pass. Follow the
  [release process](../eng/release/README.md).
