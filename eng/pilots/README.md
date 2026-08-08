# Preview pilot libraries

These five projects are packaged-adoption pilots, not product projects. They
pin real public libraries and consume SharpProof only from the candidate local
NuGet feed. Two are effect-heavy, two are contract-heavy, and one uses strict
mixed mode.

Run:

```powershell
./scripts/Test-SharpProofPilots.ps1 `
  -PackageSource artifacts/pilots/packages `
  -OutputPath artifacts/pilots/report.json
```

The report binds the Git commit and package version and records build status,
diagnostic counts, typed Unknown reasons, wall time, observed process-tree peak
working set, false-positive review count, and setup friction. An advisory
Unknown caused by a documented unsupported external callee is a reviewed
limitation, not a false proof or a release defect. The strict pilot must finish
with every selected claim proven.
