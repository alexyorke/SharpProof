# SharpProof.Symbolic package sample

This console sample queries the invariant at a source location through the
supported `SharpProofAnalysisSession` API.

The preview package is not published to NuGet.org yet. From the repository root,
build the local feed, restore this sample from that feed, and run it:

```powershell
.\scripts\Invoke-SharpProofDotnet.ps1 build SharpProof.sln --configuration Release
$manifest = Get-Content .\scripts\package-projects.json -Raw | ConvertFrom-Json
foreach ($project in $manifest.projects) {
    .\scripts\Invoke-SharpProofDotnet.ps1 pack $project --configuration Release --no-build --output artifacts\nuget
}
.\scripts\Invoke-SharpProofDotnet.ps1 restore .\samples\SharpProof.Symbolic\SharpProof.Symbolic.Sample.csproj --source .\artifacts\nuget
.\scripts\Invoke-SharpProofDotnet.ps1 run --project .\samples\SharpProof.Symbolic\SharpProof.Symbolic.Sample.csproj --no-restore
```

The query analyzes the supplied C# text without executing it and prints compact
Z3-backed proof facts for the selected source point.
