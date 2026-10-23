# Optional SharpProof diagnostics tool

The normal `SharpProof` PackageReference already enables build verification. This
tool is optional and does not add a package reference to your application.
Install the matching version from your configured feed; public package availability
is not implied by this checkout. Create a tool manifest only if the repository does
not already contain one. Teammates restore it with `dotnet tool restore`.

```sh
dotnet new tool-manifest
dotnet tool install SharpProof.Tool --version 1.0.0-preview.1
dotnet sharpproof doctor
dotnet sharpproof explain obj/Debug/net10.0/SharpProof/result.json
dotnet sharpproof explain path/to/result.json --file src/Example.cs --line 12 --json
```

Tool execution requires .NET 10; local-manifest installation/restore also requires a
.NET SDK. `doctor` without `--project` runs a bounded `dotnet --version`
discovery using the caller's normal CLI selection (including `global.json`) and
reports its own runtime and architecture. It does not evaluate projects, restore,
build, or load the solver.
It cannot certify package configuration or native solver availability.

The positional `explain <result.json>` form validates the stored worker protocol and
displays recorded claim and callable outcomes, coverage, vacuity, assumptions,
elapsed time, cache status, and errors. It always labels the output as stored
evidence. Neither successful parsing nor a recorded Proven result establishes
current source freshness or independent request authority. Run your normal consumer
build to obtain fresh proof evidence.
Use your build's printed result path when outputs have been customized.

`--file` matches the exact recorded source path using ordinal comparison; `--line`
matches a recorded start line. JSON includes the same scope warning and filtered
entries. Exit 0 means the diagnostic command completed, not that a contract was
proven. Invalid syntax, malformed/missing evidence, and failed SDK discovery exit 2.
Results can contain private paths, contracts, and counterexample values; review
them before sharing. This tool never rewrites results or runs verification.

Project-aware setup checks are available with
`dotnet sharpproof doctor --project MyLibrary.csproj --verification portable`.
Use `--verification container` for the starter's canonical container configuration.
This mode checks SDK selection, evaluated package/configuration settings, and existing
restore evidence. Explicit container mode checks Docker and the local consumer image
when run outside the canonical container; a build still validates that container's
contract. For a generated container starter, restore/run the tool through its service
to use the same container-owned assets, package feeds, and SDK as the consumer build.
It evaluates a trusted project's MSBuild imports without invoking build targets and
does not restore, build, pull images, or run verification. Exit 1 means a setup check
failed; it does not replace the SDK-only `doctor [--json]` mode.

The commands below require a complete matching evidence set: the result plus sibling
`request.json` and `compiler-manifest.json`. Override companion paths with `--request`
and `--compiler-manifest`; comparison also accepts `--baseline-request` and
`--baseline-compiler-manifest` for its baseline.

```sh
dotnet sharpproof explain --result artifacts/sharpproof/result.json --claim <id>
dotnet sharpproof compare --baseline baseline/result.json --current artifacts/sharpproof/result.json --fail-on-regression
dotnet sharpproof export-test --project MyLibrary.csproj --result artifacts/sharpproof/result.json --claim <id> --output ../MyLibrary.Regressions
```

Named-option `explain` explains claims and typed Unknown detail from validated evidence;
the positional form retains its existing `--file`, `--line`, and `--json` options.
Comparison requires the same assembly identity, framework, and specification selection.
Its regression gate is enabled only by `--fail-on-regression` and does not replace the
consumer's verifier policy. Exit 1 means that enabled gate found a regression.

Export creates a new NUnit project for a replayed direct scalar `Refuted` postcondition
on a public synchronous static method without type parameters. It requires matching
source/reference hashes, supported scalar expressions, and source recorded with C# 13
or earlier. It writes source without executing consumer code or enabling runtime
contracts. Keep the test project outside the consumer's source tree, and run it in a
canonical container workspace exposing both project paths. A container export needs a
writable mounted parent and a new output child, and export/test must retain the same
absolute project-reference paths. See the
[developer tools guide](../../docs/developer-experience.md) for supported frameworks,
export limits, and the expected failing-then-passing regression workflow.

For these evidence commands, exit 0 means processing completed, and exit 2 means invalid
arguments/evidence or unsupported export. Cancellation exits 130. Stored evidence does
not establish current source freshness or authenticate an arbitrary worker binary.
