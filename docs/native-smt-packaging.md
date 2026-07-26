# Native SMT packaging

The Roslyn analyzer package directory contains no Z3 managed assembly, native
library, native-locator file, verifier assembly, or retired engine payload.
This is enforced by package and architecture tests.

Z3 is packaged only below `tools/net8`, alongside `SharpProof.Worker` and
`SharpProof.Worker.Launcher`. The preview ships the native solver and
out-of-process verifier only for Windows x64; Linux and macOS support the
analyzer package but not packaged worker execution. MSBuild starts the launcher only when
`SharpProofVerify=true`; design-time builds skip it. On Windows the launcher
assigns the worker to a Job Object with the configured memory and process
limits. Job Object creation, configuration, and assignment all fail closed;
the launcher never intentionally continues with an uncapped worker.

`SharpProof.Package.Test` packs the NuGet package, inspects the exact analyzer
and worker layouts, and restores an analyzer consumer on every CI host. Windows
additionally enables verification, launches the packaged worker, and validates
its versioned JSON result. A verification request on another host fails with an
explicit unsupported-host build error.
