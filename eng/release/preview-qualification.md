# SharpProof preview qualification matrix

The preview candidate is qualified on a local trusted Windows x64 build host.
Every row below is executable in `SharpProof.Package.Test`; release evidence
must record a green run from the exact candidate commit.

Reference Visual Studio host for `preview.1`:

- Visual Studio 2022 Build Tools MSBuild x64
- file version `17.14.51.32402`
- product version `17.14.51+25f168cee22fc5419a38fc8d2ffb6a8c0381b7a0`

| Behavior | Command-line MSBuild | Visual Studio MSBuild | Executable evidence |
|---|---|---|---|
| Ordinary packaged verification | Required | Required | `PackageReferenceRunsWorkerAndPublishesResult` and the full package suite |
| Percent-containing local path | Required | Required | `VerifierLaunchPreservesPercentCharactersInPaths` |
| Local paths beyond 260 characters | Required | Required | `LongLocalPublicationPathsWorkInDotNetAndVisualStudioMsBuild` |
| Overlong project path classification | Required | Required | `OverlongProjectDirectoryFailsBeforeCompilerLaunch` |
| Cache and SARIF publication | Required | Required | the long-path matrix publishes both cache and SARIF |
| Cooperative concurrent publication | Required | Required | `ConcurrentInvocationsUseIsolatedWorkerFiles` and `VisualStudioMsBuildSerializesCooperativePublications` |
| Verifier cancellation | Required | Same compiled task | `CanceledVerifierTaskDoesNotLaunchAProcess` and `ActiveVerifierTaskCancellationStopsTheProcess`; Visual Studio loads that packaged task in every Visual Studio row |

Rider, Windows ARM64, hostile local filesystem mutation, and remote or UNC
publication are outside this preview matrix. The normative boundary is
`docs/preview-support.md`.
