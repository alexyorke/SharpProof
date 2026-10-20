#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
evidence=/workspace/SharpProof/artifacts/hm-inference
fixture=$(mktemp -d /tmp/hm-partial-methods.XXXXXX)
cp global.json "$fixture/global.json"
package_reference=""
if [ "$#" -eq 3 ]; then
  package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
fi
cat > "$fixture/Partials.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
cat > "$fixture/Definition.cs" <<'EOF'
using System.Threading.Tasks;
public partial class PartialConsumer{
    public static partial int Identity(int value);
    public static partial string? Nullable(string? value);
    public static partial Task<int> Await(Task<int> value);
    public static partial int Recursive(int value,int count);
    public static int Call()=>Identity(3);
}
EOF
cat > "$fixture/Implementation.cs" <<'EOF'
using System.Threading.Tasks;
public partial class PartialConsumer{
    public static partial int Identity(int value)=>value;
    public static partial string? Nullable(string? value)=>value;
    public static async partial Task<int> Await(Task<int> value)=>await value;
    public static partial int Recursive(int value,int count)=>count==0?value:Recursive(value,count-1);
}
EOF
cat > "$fixture/.editorconfig" <<'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
if [ "$#" -eq 3 ]; then
  NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/Partials.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || { tail -35 "$fixture/restore.log"; exit 1; }
  NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/Partials.csproj" --no-restore > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
  log="$fixture/build.log"
  destination="$evidence/package-partial-methods.txt"
else
  dotnet build "$fixture/Partials.csproj" > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
  dotnet "$output" --project "$fixture/Partials.csproj" --no-restore > "$fixture/cli.log" 2>&1 || { tail -35 "$fixture/cli.log"; exit 1; }
  log="$fixture/cli.log"
  destination="$evidence/cli-partial-methods.txt"
fi
cp "$log" "$destination"
for expected in \
  'T1 Identity<T1>(T1 value)' \
  'T1? Nullable<T1>(T1? value) where T1 : class' \
  'global::System.Threading.Tasks.Task<T1> Await<T1>(global::System.Threading.Tasks.Task<T1> value)' \
  'T1 Recursive<T1>(T1 value, int count)'; do
  if ! grep -Fq "$expected" "$log"; then printf 'Missing partial signature: %s\n' "$expected"; tail -35 "$log"; exit 1; fi
done
if [ "$#" -eq 1 ]; then printf '%s\n' 'PASS CliPartialMethodPairsNullableAsyncAndRecursion'; fi
