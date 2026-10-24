# Interactive editor acceptance

The private preview requires real Windows x64 sessions in all three editors below.
Every row is pending. Roslyn workspace tests, analyzer loader tests, a command-line
SDK, or a successful MSBuild.exe proof do not establish interactive editor acceptance.
Record exact versions rather than inferring compatibility from a product name.

| Required editor | Required host | Interactive result | Evidence |
| --- | --- | --- | --- |
| Visual Studio stable channel | .NET 10-capable C# host on Windows x64 | Pending | Pending |
| Visual Studio Code stable with the Microsoft C# extension | Windows x64; record extension/compiler host versions | Pending | Pending |
| JetBrains Rider stable | Windows x64; record Rider/compiler host versions | Pending | Pending |

Installed-version inventory does not establish an accepted editor session. An
interrupted UI attempt earns no pass; keep unexecuted steps Pending and retain any
actual host/interruption observations only in private evidence.

Use exact candidate packages in an isolated consumer workspace on a fixed local NTFS
volume. Do not build the SharpProof repository on the Windows host; repository build,
test, and packing still use the canonical Linux amd64 container. Editor acceptance
does not expand the [verifier support boundary](preview-support.md).

## Use the frozen candidate before promotion

Editor acceptance before promotion uses the exact private candidate archives as an
isolated local feed, rather than assuming the official feed/image has been published.
Download the candidate by its immutable numeric artifact ID from the trusted
qualification run, validate the API archive digest, and verify every package hash
against the unsigned qualification manifest. Record both source/workflow identities
and label this evidence **candidate, before promotion**. The qualification manifest is not
the later offline release signature.

In a clean private fixture copy, map `SharpProof*` to that local feed, keep the required
external dependency source, and retain the configuration diff privately. Restore both
the consumer and matching local tool from those candidate bytes. Do not substitute
public CI validation versions or an existing installed package with the same version.

The fixture may use strict native Windows x64 verification on an already supported
.NET 10/fixed-local-NTFS host. Repository build/test/pack commands still stay in the
canonical container. A container alternative must load the exact candidate image and
use [Test-SharpProofPrivatePreviewImage.ps1](../eng/scripts/Test-SharpProofPrivatePreviewImage.ps1)
in the canonical maintainer environment. Its parameters are `-ArchivePath`
(`consumer-image.tar`), `-BuildMetadataPath` (`consumer-image.json`), `-ExpectedDigest`,
`-ExpectedSourceCommit`, `-ImageReference`, `-OutputPath`, and `-Load` to load the exact
archive. Use its verified local alias/original-digest mapping; a mutable local tag
alone does not establish candidate image identity. Keep its receipt with the run.

After promotion, fresh private-feed restore and registry pull by the exact published
digest remain separate checks before the release tag. Candidate-local editor results
do not establish those publication/read-back outcomes.

## Record the candidate before opening an editor

Create a private evidence directory for each editor and record:

- Windows edition/build, x64 process architecture, installed .NET SDK/runtime versions,
  and the SDK selected by the fixture's pinned `global.json`.
- Editor edition/channel/version, C# extension/plugin versions, the actual language
  service/compiler version where exposed, and relevant analyzer settings.
- Public source commit, candidate workflow commit, immutable candidate artifact ID/API
  digest, unsigned manifest hash, exact package IDs/versions, archive hashes, and local
  feed identity. Record the later release signature separately; it does not exist at
  this stage before promotion. A different locally packed candidate is development
  feedback and cannot replace the frozen promotion candidate.
- Fixture commit or source hashes, expected diagnostics, and the result/report paths.
  Use a fresh restore cache or demonstrate that restored bytes match the candidate.

The workspace template supplies pinned tools and an optional private editor package.
Use the [developer guide](developer-experience.md) to create the shared app/tests
fixture; `init` instead covers a simple existing project and does not construct that
workspace topology. For acceptance before promotion, restore through the isolated candidate
feed above without recording credentials.
Start each editor from a clean copy of the same fixture; do not share generated output
or an edited fixture between editor runs.

## Run the same acceptance sequence in each editor

1. Open the fixture as a C# project/workspace using that editor's normal project model.
   Use its package-management or project-file UI to install the exact candidate
   package references from the isolated local feed and observe the editor's restore.
   Retain this `package-installation` observation separately from a command-line restore.
   Allow restore and language analysis to complete. Record the effective SDK, analyzer
   paths/package versions, the optional package's emitted
   `build_property.SharpProofEditorSuggestions` marker, and a clean analyzer-load log.
   The optional package carries the compiler-only suggestion analyzer separately from
   the Workspaces code-fix assembly. AD0001, CS8032, assembly-load
   failures, or silently disabled analyzers fail this step.
2. With portable advisory analysis selected, confirm the starter's deliberate SP0027
   warning and its recorded source location. Change the demonstration argument from
   -1 to 0, save, and confirm the warning disappears; revert the fixture afterward.
   Editor silence is portable feedback and does not establish a native proof.
3. In the starter's `Counter.Next`, change `Contract.Result<int>()` to
   `Contract.Result<long>()` inside its direct `Ensures` prologue. Confirm SP0024,
   preview the Result-type correction, and apply it. The diff must change only the
   eligible type argument back to `int`, preserve the predicate, and remove that usage
   diagnostic. Exercise Result Fix All on two independently eligible methods and
   inspect both edits.
4. With the matching optional `SharpProof.CodeFixes` package installed privately,
   select the starter's top-level `Counter` class without a companion. Confirm SPED0001
   and **Generate SharpProof contract companion** through the editor's diagnostic
   action UI. Revert the generated companion before also invoking the existing
   refactoring where the editor exposes it.
   Preview before applying; retain the preview and resulting source diff.
5. Check the new companion's `ContractFor` target, receiver, names, parameter/ref kinds,
   generic constraints, and nullable annotations against the target. Bodies must be
   editable throwing placeholders, with no guessed clauses or assumptions. Add reviewed
   clauses and reanalyze. Remove the unambiguous `Next` companion member and exercise SPCF0004
   repair; the preview must preserve the other members. Companion actions have no
   Fix All.
6. Exercise the negative fixtures: ambiguous/partially populated overload families,
   unsupported nested/inaccessible shapes, incompatible nullable constraints, and a
   source-generated companion. Ineligible actions must be absent; no generated document
   or unrelated source may be edited. Retain the reason/log or UI state for each case.
7. Remove the optional editor package, reload the project, and confirm SPED0001 is
   absent while the ordinary SharpProof portable diagnostics remain available. Restore
   the candidate configuration before the remaining steps.
8. Save, close, and reopen the editor. Confirm diagnostics/actions still load from the
   exact candidate rather than stale assemblies. Inspect the complete source diff for
   unintended predicate, visibility, receiver, or generated-file changes.
9. Run the fixture's strict native Windows x64 build on the supported host, or its
   canonical-container build using the qualified candidate mapping above. Inspect
   its matching request, result, and compiler manifest: expected selected claims, completed run, reviewed
   assumptions/vacuity, and expected outcomes. For a supported direct Refuted
   postcondition, export a regression, observe its assertion fail, fix the source,
   and require both the test and fresh strict build to pass. This is separate from
   typing-time diagnostics.
10. Use Undo or reviewed source restoration in the editor to restore the clean fixture
    source after the exercises. Check its original source hashes and reload it; retain
    this `undo-source-restoration` observation and the final diff. Keep generated
    regression/evidence output separate from the clean fixture source.

## Retain a result without overstating it

For every step record `pass`, `fail`, or `blocked`, the source/package hashes,
reproduction steps, and private evidence paths. Include diagnostic/action screenshots,
preview/applied diffs, analyzer-load logs, exact build/test results, and native evidence.
Review screenshots/logs for credentials before retention. A missing action is a failed
or blocked observation, not an assumed host limitation or a passing test.

The [promotion receipt contract](../eng/scripts/SharpProof.PrivatePreviewPromotionReceipts.psm1)
requires 26 scenarios per editor: 21 use `interactive-ui`, and the five strict-proof
and regression observations use `canonical-container` or
`native-consumer-windows-x64`. Only `companion-refactoring` may be
`not-applicable`, after actual UI inspection records that action's absence and a
reason; the diagnostic generation action must still pass. Pending or synthetic
observations cannot qualify the candidate.

Retain `editor-acceptance.json` and hashed `evidence/` attachments only in the private
mirror's `private-preview-receipts/<candidateArtifactId>` directory. The owner manually
dispatches `.github/workflows/private-preview-receipts.yml` with `receipt_directory`
set to that path. Preserve its numeric artifact ID/API digest and use it as
`editor_acceptance_artifact_id` for promotion. The intake run must use the same SHA
as the executing promotion workflow. For finalization, add actual
`access-acceptance.json` and its evidence and create a new combined intake artifact.

All required steps must pass in all three editor sessions against the exact release
candidate before the [private release receipt](private-preview-release-receipt.md)
can claim interactive acceptance. Until then, keep all three editor rows pending or
record their specific failures; do not turn implementation tests into a supported-host
claim.
