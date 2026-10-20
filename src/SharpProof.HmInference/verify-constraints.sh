#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
fixture=$(mktemp -d '/tmp/hm constraint normalization.XXXXXX')
cp global.json "$fixture/global.json"
mkdir -p "$fixture/feed"
cat > "$fixture/NuGet.Config" <<'EOF'
<configuration><packageSources><clear/><add key="fixture" value="./feed"/></packageSources></configuration>
EOF
cat > "$fixture/Input.cs" <<'EOF'
#nullable enable
public class CliBoundRoot{}
public class CliBoundMiddle:CliBoundRoot{}
public class CliBoundLeaf:CliBoundMiddle{}
public interface CliProjectionContract<T>{}
public class CliProjectionDual<T>:CliProjectionContract<T[]>,CliProjectionContract<System.Collections.Generic.List<T>>{}
public static class CliBounds{
    public static CliProjectionDual<int> ProjectionArray(CliProjectionDual<int> value,int[] other)=>Project(value,other);
    private static T Project<T,U>(T value,U other) where T:CliProjectionContract<U> =>value;
    public static CliBoundLeaf? NullableBound(CliBoundLeaf? value)=>NullableRoot(value);
    public static CliBoundLeaf? NullableBoth(CliBoundLeaf? value)=>NullableRoot(NullableMiddle(value));
    private static T NullableRoot<T>(T value) where T:CliBoundRoot? =>value;
    private static T NullableMiddle<T>(T value) where T:CliBoundMiddle? =>value;
    public static object BoundCycle(object left,object right){Keep(left,right);Keep(right,left);return left;}
    public static CliBoundLeaf StrongestBound(CliBoundLeaf value)=>Root(Middle(value));
    public static async System.Threading.Tasks.Task<CliBoundLeaf?> DependentAsync(System.Threading.Tasks.Task<CliBoundLeaf?> value,int ignored){CliBoundRoot other=new();return Optional(await value,other);}
    public static System.Enum? DependentEnum(System.Enum? value,int ignored)=>EnumBound(value);
    private static T EnumBound<T>(T value) where T:System.Enum? =>value;
    private static T Optional<T,U>(T value,U other) where T:U? =>value;
    private static T Keep<T,U>(T value,U other) where T:U=>value;
    private static T Root<T>(T value) where T:CliBoundRoot=>value;
    private static T Middle<T>(T value) where T:CliBoundMiddle=>value;
}
EOF
cat > "$fixture/Bounds.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
EOF
dotnet "$output" "$fixture/Input.cs" > "$fixture/source.log" 2>&1 || { tail -35 "$fixture/source.log"; exit 1; }
dotnet "$output" --project "$fixture/Bounds.csproj" > "$fixture/project.log" 2>&1 || { tail -35 "$fixture/project.log"; exit 1; }
for log in source project; do
  for expected in 'ProjectionArray<T1>(global::CliProjectionDual<T1> value, T1[] other)' 'DependentAsync<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 ignored) where T1 : global::CliBoundRoot' 'DependentEnum<T1>(global::System.Enum? value, T1 ignored)' 'NullableBound<T1>(T1? value) where T1 : global::CliBoundRoot' 'NullableBoth<T1>(T1? value) where T1 : global::CliBoundMiddle' 'BoundCycle<T1>(T1 left, T1 right)' 'StrongestBound<T1>(T1 value) where T1 : global::CliBoundMiddle'; do
    if ! grep -Fq "$expected" "$fixture/$log.log"; then tail -35 "$fixture/$log.log"; exit 1; fi
  done
done
cp "$fixture/source.log" /workspace/SharpProof/artifacts/hm-inference/constraint-cli.txt
cp "$fixture/project.log" /workspace/SharpProof/artifacts/hm-inference/constraint-project.txt
printf '%s\n' 'PASS CliSourceConstraintNormalization' 'PASS CliProjectConstraintNormalization' 'PASS CliNullableConstraintImplication' 'PASS CliDependentNullableAsyncAndEnumBounds' 'PASS CliShapeSelectedInterfaceProjection'
