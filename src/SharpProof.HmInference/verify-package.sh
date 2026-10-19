#!/usr/bin/env bash
set -euo pipefail
evidence=/workspace/SharpProof/artifacts/hm-inference
packages="$PWD/artifacts/hm-packages"
dotnet pack src/SharpProof.HmInference.CodeFixes/SharpProof.HmInference.CodeFixes.csproj --configuration Debug --no-build --no-restore -p:RepositoryCommit="$SHARPPROOF_HM_SOURCE_COMMIT" -o "$packages" > pack.log 2>&1 || { tail -60 pack.log; exit 1; }
package=$(find "$packages" -name 'SharpProof.HmInference.*.nupkg' | head -1)
test -n "$package"
version=$(HM_PACKAGE="$package" pwsh -NoLogo -NoProfile -Command '
    $archive = [IO.Compression.ZipFile]::OpenRead($env:HM_PACKAGE)
    try {
        $dlls = @($archive.Entries.FullName | Where-Object { $_.EndsWith(".dll") } | Sort-Object)
        $expected = @("analyzers/dotnet/cs/SharpProof.HmInference.Analyzer.dll", "analyzers/dotnet/cs/SharpProof.HmInference.CodeFixes.dll")
        if (@(Compare-Object $dlls $expected).Count -ne 0) { throw "Unexpected package assemblies: $dlls" }
        $entry = $archive.Entries | Where-Object { $_.FullName.EndsWith(".nuspec") }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($spec.package.metadata.repository.commit -cne $env:SHARPPROOF_HM_SOURCE_COMMIT) { throw "Package repository commit does not match the verified source." }
        $spec.package.metadata.version
    } finally { $archive.Dispose() }
')
consumer=$(mktemp -d /tmp/hm-package-consumer.XXXXXX)
cp global.json "$consumer/global.json"
cat > "$consumer/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><RestoreSources>$packages</RestoreSources><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
  <ItemGroup><PackageReference Include="SharpProof.HmInference" Version="$version" PrivateAssets="all" /></ItemGroup>
</Project>
EOF
cat > "$consumer/Input.cs" <<'EOF'
#nullable enable
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
delegate void PackageSink<T>(T value);
sealed class PackageBox<T> { public T Get()=>default!; }
static class Sample {
    public static int Flow(int value) { goto Done; Done:return value; }
    public static int Loop(int value) { for(;;) return value; }
    public static IEnumerable<int> Yield(int value) { yield return value; }
    public static async IAsyncEnumerable<int> AsyncYield(int value) { await Task.Yield(); yield return value; }
    public static async Task<int> Last(IAsyncEnumerable<int> source,int seed) { await foreach(var value in source) seed=value; return seed; }
    public static void Forward(Action<int> action,int value) => action(value);
    public static Func<Task<int>> Capture(int value) => async () => { await Task.Yield(); return value; };
    public static Func<int> Group(PackageBox<int> box) => box.Get;
    public static void Apply(PackageSink<int> sink,int value) => sink(value);
    public static int Identity(int value) => value;
    public static int? NullableIdentity(int? value) => value;
    public static string? NullableReferenceIdentity(string? value) => value;
    public static async Task<int> AsyncIdentity(Task<int> task) => await task;
}
EOF
cat > "$consumer/.editorconfig" <<'EOF'
root = true
[*.cs]
dotnet_diagnostic.HM0001.severity = warning
EOF
NUGET_PACKAGES="$consumer/packages" dotnet build "$consumer/Consumer.csproj" -v:normal > "$consumer/build.log" 2>&1 || { tail -60 "$consumer/build.log"; exit 1; }
if ! grep -q 'warning HM0001' "$consumer/build.log"; then tail -60 "$consumer/build.log"; exit 1; fi
if grep -Eq 'CS8032|AD0001' "$consumer/build.log"; then tail -60 "$consumer/build.log"; exit 1; fi
for expected in 'NullableIdentity<T1>' 'NullableReferenceIdentity<T1>' 'AsyncIdentity<T1>' 'Forward<T1>' 'Capture<T1>' 'Group<T1>' 'Apply<T1>' 'Yield<T1>' 'AsyncYield<T1>' 'Last<T1>' 'Flow<T1>' 'Loop<T1>' 'where T1 : struct' 'where T1 : class'; do
  if ! grep -Fq "$expected" "$consumer/build.log"; then tail -60 "$consumer/build.log"; exit 1; fi
done
mkdir -p "$evidence/packages"
cp "$package" "$evidence/packages/"
cp "$consumer/build.log" "$evidence/package-consumer.txt"
printf '%s\n' 'PASS PortablePackageContents' 'PASS NuGetConsumerAnalyzerLoad' 'PASS NuGetNullableAsyncDiagnostics' 'PASS NuGetDelegateAsyncDiagnostics' 'PASS PackageSourceCommit' 'PASS NuGetEnumerationDiagnostics' 'PASS NuGetControlFlowDiagnostics' | tee "$evidence/package-checks.txt"
