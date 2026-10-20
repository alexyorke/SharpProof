#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
evidence=/workspace/SharpProof/artifacts/hm-inference
fixture=$(mktemp -d /tmp/hm-caller-constraints.XXXXXX)
cp global.json "$fixture/global.json"
package_reference=""
if [ "$#" -eq 3 ]; then
  package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
fi
cat > "$fixture/Constraints.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
cat > "$fixture/Definitions.cs" <<'EOF'
using System.Threading.Tasks;
public static partial class ConstraintConsumer{
    public static partial object? Partial(object? value,int tag);
    public static partial Task<object?> PartialAsync(object? value,int tag);
}
EOF
cat > "$fixture/Methods.cs" <<'EOF'
using System.Threading.Tasks;
public static partial class ConstraintConsumer{
    public class Base{}
    public class BaseSource{public static implicit operator Base(BaseSource value)=>new();}
    public struct Value{public int Number;}
    public class StructSource{public static implicit operator Value(StructSource value)=>default;}
    public struct ManagedSource{public object Payload;public static implicit operator Value(ManagedSource value)=>default;}
    static void NeedClass<T>(T value)where T:class{}
    static void NeedNew<T>(T value)where T:new(){}
    static void NeedBase<T>(T value)where T:Base{}
    static void NeedStruct<T>(T value)where T:struct{}
    static void NeedUnmanaged<T>(T value)where T:unmanaged{}
    public static partial object Partial(object value,int tag)=>value;
    public static async partial Task<object> PartialAsync(object value,int tag){await Task.Yield();return value;}
    public static object Reference(object value,int tag){NeedClass(value);return value;}
    public static object Constructor(object value,int tag){NeedNew(value);return value;}
    public static Base BaseCase(Base value,int tag){NeedBase(value);return value;}
    public static Value StructCase(Value value,int tag){NeedStruct(value);return value;}
    public static Value UnmanagedCase(Value value,int tag){NeedUnmanaged(value);return value;}
}
EOF
cat > "$fixture/Callers.cs" <<'EOF'
using System.Threading.Tasks;
public static partial class ConstraintConsumer{
    public static object? UsePartial()=>Partial(42,1);
    public static Task<object?> UseAsync()=>PartialAsync(42,1);
    public static object UseReference()=>Reference(42,1);
    public static object UseConstructor()=>Constructor("payload",1);
    public static Base UseBase()=>BaseCase(new BaseSource(),1);
    public static Value UseStruct()=>StructCase(new StructSource(),1);
    public static Value UseValue()=>UnmanagedCase(new ManagedSource{Payload=new object()},1);
}
EOF
cat > "$fixture/.editorconfig" <<'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
if [ "$#" -eq 3 ]; then
  NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/Constraints.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || { tail -35 "$fixture/restore.log"; exit 1; }
  NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/Constraints.csproj" --no-restore > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
  log="$fixture/build.log"
  destination="$evidence/package-caller-constraints.txt"
else
  dotnet build "$fixture/Constraints.csproj" > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
  dotnet "$output" --project "$fixture/Constraints.csproj" --no-restore > "$fixture/cli.log" 2>&1 || { tail -35 "$fixture/cli.log"; exit 1; }
  log="$fixture/cli.log"
  destination="$evidence/cli-caller-constraints.txt"
fi
cp "$log" "$destination"
for expected in \
  'object Partial<T1>(object value, T1 tag)' \
  'global::System.Threading.Tasks.Task<object> PartialAsync<T1>(object value, T1 tag)' \
  'object Reference<T1>(object value, T1 tag)' \
  'object Constructor<T1>(object value, T1 tag)' \
  'global::ConstraintConsumer.Base BaseCase<T1>(global::ConstraintConsumer.Base value, T1 tag)' \
  'global::ConstraintConsumer.Value StructCase<T1>(global::ConstraintConsumer.Value value, T1 tag)' \
  'global::ConstraintConsumer.Value UnmanagedCase<T1>(global::ConstraintConsumer.Value value, T1 tag)'; do
  if ! grep -Fq "$expected" "$log"; then printf 'Missing constrained caller signature: %s\n' "$expected"; tail -35 "$log"; exit 1; fi
done
if [ "$#" -eq 1 ]; then printf '%s\n' 'PASS CliCallerConstraintRefinementBoxingAndConversions'; fi
