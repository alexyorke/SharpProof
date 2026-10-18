# Optional SharpProof diagnostics tool

The normal `SharpProof` PackageReference already enables build verification. This
tool is optional and does not add a package reference to your application.

```sh
dotnet new tool-manifest
dotnet tool install SharpProof.Tool --version 1.0.0-preview.1
dotnet sharpproof doctor
dotnet sharpproof explain obj/Debug/net10.0/SharpProof/result.json
dotnet sharpproof explain path/to/result.json --file src/Example.cs --line 12 --json
```

Requires .NET 10. `doctor` runs a bounded `dotnet --version` discovery using the
caller's normal CLI selection (including `global.json`) and reports its own runtime
and architecture. It does not evaluate projects, restore, build, or load the solver.
It cannot certify package configuration or native solver availability.

`explain` validates the stored worker protocol and displays recorded claim and
callable outcomes, coverage, vacuity, assumptions, elapsed time, cache status, and
errors. It always labels the output as stored evidence. Neither successful parsing
nor a recorded Proven result establishes current source freshness or independent
request authority. Run your normal consumer build to obtain fresh proof evidence.
Use your build's printed result path when outputs have been customized.

`--file` matches the exact recorded source path using ordinal comparison; `--line`
matches a recorded start line. JSON includes the same scope warning and filtered
entries. Exit 0 means the diagnostic command completed, not that a contract was
proven. Invalid syntax, malformed/missing evidence, and failed SDK discovery exit 2.
Results can contain private paths, contracts, and counterexample values; review
them before sharing. This tool never rewrites results or runs verification.
