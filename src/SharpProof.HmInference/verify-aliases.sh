#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
evidence=/workspace/SharpProof/artifacts/hm-inference
fixture=$(mktemp -d /tmp/hm-alias-imports.XXXXXX)
cp global.json "$fixture/global.json"
mkdir -p "$fixture/Library" "$fixture/App"
cat > "$fixture/Library/Library.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><NuGetAudit>false</NuGetAudit><Nullable>enable</Nullable></PropertyGroup></Project>
EOF
cat > "$fixture/Library/Types.cs" <<'EOF'
namespace AliasDomain;
public class Box<T>{public T Value=default!;public class Inner<U>{}}
EOF
package_reference=""
if [ "$#" -eq 3 ]; then
  package_reference="<PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" />"
fi
cat > "$fixture/App/App.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
<ItemGroup><ProjectReference Include="../Library/Library.csproj" Aliases="Left" />$package_reference</ItemGroup>
</Project>
EOF
cat > "$fixture/App/Globals.cs" <<'EOF'
extern alias Left;
global using AliasBox=Left::AliasDomain.Box<int>;
global using AliasNested=Left::AliasDomain.Box<int>.Inner<string>;
EOF
cat > "$fixture/App/Methods.cs" <<'EOF'
using System.Threading.Tasks;
public static class AliasConsumer{
    public static AliasBox AliasIdentity(AliasBox value)=>value;
    public static AliasNested AliasNestedIdentity(AliasNested value)=>value;
    public static async Task<AliasBox> AliasAsync(Task<AliasBox> value)=>await value;
}
EOF
cat > "$fixture/App/.editorconfig" <<'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
if [ "$#" -eq 3 ]; then
  NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/App/App.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || { tail -35 "$fixture/restore.log"; exit 1; }
  NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/App/App.csproj" --no-restore > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
  log="$fixture/build.log"
  destination="$evidence/package-alias-imports.txt"
else
  dotnet build "$fixture/App/App.csproj" > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
  dotnet "$output" --project "$fixture/App/App.csproj" --no-restore > "$fixture/cli.log" 2>&1 || { tail -35 "$fixture/cli.log"; exit 1; }
  log="$fixture/cli.log"
  destination="$evidence/cli-alias-imports.txt"
fi
cp "$log" "$destination"
for expected in \
  'Left::AliasDomain.Box<T1> AliasIdentity<T1>(Left::AliasDomain.Box<T1> value)' \
  'Left::AliasDomain.Box<T1>.Inner<T2> AliasNestedIdentity<T1, T2>(Left::AliasDomain.Box<T1>.Inner<T2> value)' \
  'global::System.Threading.Tasks.Task<Left::AliasDomain.Box<T1>> AliasAsync<T1>(global::System.Threading.Tasks.Task<Left::AliasDomain.Box<T1>> value)'; do
  if ! grep -Fq "$expected" "$log"; then printf 'Missing shared alias signature: %s\n' "$expected"; tail -35 "$log"; exit 1; fi
done
if [ "$#" -eq 1 ]; then printf '%s\n' 'PASS CliGlobalClosedAliasImports'; fi
