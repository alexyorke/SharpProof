# Native SMT packaging

The Roslyn analyzer package directory contains no Z3 managed assembly, native
library, native-locator file, or verifier assembly. This is enforced by package
and architecture tests.

Z3 is packaged only below `tools/net8`, alongside `SharpProof.Worker` and
`SharpProof.Worker.Launcher`. The preview ships the native solver and
out-of-process verifier only for `win-x64`; Windows arm64 and non-Windows
packaged worker execution are unsupported. MSBuild starts the launcher only
when `SharpProofVerify=true`; design-time builds skip it. On the supported
Windows x64 host, the launcher assigns the worker to a Job Object with the
configured memory and process limits. Job Object creation, configuration, and
assignment all fail closed; the launcher never intentionally continues with an
uncapped worker.

`SharpProof.Package.Test` packs the NuGet package and inspects the exact
analyzer and worker layouts. Current package-consumer CI restores and runs
analyzer consumers on Windows x64 (`windows-latest`), Linux x64
(`ubuntu-latest`), and macOS Intel (`macos-15-intel`). Only Windows x64
additionally enables verification, launches the packaged worker, and validates
its versioned JSON result. The full acceptance job also runs on
`windows-latest`; a verification request on a non-Windows host fails with an
explicit unsupported-host build error.
