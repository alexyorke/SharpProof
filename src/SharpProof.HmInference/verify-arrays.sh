#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
evidence=/workspace/SharpProof/artifacts/hm-inference
fixture=$(mktemp -d /tmp/hm-array-construction.XXXXXX)
cp global.json "$fixture/global.json"
package_reference=""
if [ "$#" -eq 3 ]; then
  package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
fi
cat > "$fixture/Arrays.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
cat > "$fixture/Arrays.cs" <<'EOF'
using System.Threading.Tasks;
public static class ArrayConsumer{
    public static object[] Box(int value)=>new object[]{value};
    public static int[,] Rectangle(int first,int second)=>new[,]{{first,second}};
    public static async Task<int?[]> Nullable(Task<int> value,int? other)=>new[]{await value,other};
    public static int[] Numeric(short value,bool tag)=>new int[]{value};
    public static int[] Defaults(int value)=>new[]{value,default};
}
EOF
cat > "$fixture/.editorconfig" <<'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
if [ "$#" -eq 3 ]; then
  NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/Arrays.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || { tail -35 "$fixture/restore.log"; exit 1; }
  NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/Arrays.csproj" --no-restore > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
  log="$fixture/build.log"
  destination="$evidence/package-array-construction.txt"
else
  dotnet build "$fixture/Arrays.csproj" > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
  dotnet "$output" --project "$fixture/Arrays.csproj" --no-restore > "$fixture/cli.log" 2>&1 || { tail -35 "$fixture/cli.log"; exit 1; }
  log="$fixture/cli.log"
  destination="$evidence/cli-array-construction.txt"
fi
cp "$log" "$destination"
for expected in \
  'object[] Box<T1>(T1 value) where T1 : notnull' \
  'T1[,] Rectangle<T1>(T1 first, T1 second)' \
  'global::System.Threading.Tasks.Task<T1?[]> Nullable<T1>(global::System.Threading.Tasks.Task<T1> value, T1? other) where T1 : struct' \
  'int[] Numeric<T1>(short value, T1 tag)' \
  'T1?[] Defaults<T1>(T1 value)'; do
  if ! grep -Fq "$expected" "$log"; then printf 'Missing array signature: %s\n' "$expected"; tail -35 "$log"; exit 1; fi
done
if [ "$#" -eq 1 ]; then printf '%s\n' 'PASS CliArrayConstructionConversionsAndNullableAwait'; fi
