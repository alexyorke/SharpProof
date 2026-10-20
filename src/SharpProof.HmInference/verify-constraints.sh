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
public class CliBoundRoot{}
public class CliBoundMiddle:CliBoundRoot{}
public class CliBoundLeaf:CliBoundMiddle{}
public static class CliBounds{
    public static object BoundCycle(object left,object right){Keep(left,right);Keep(right,left);return left;}
    public static CliBoundLeaf StrongestBound(CliBoundLeaf value)=>Root(Middle(value));
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
  for expected in 'BoundCycle<T1>(T1 left, T1 right)' 'StrongestBound<T1>(T1 value) where T1 : global::CliBoundMiddle'; do
    if ! grep -Fq "$expected" "$fixture/$log.log"; then tail -35 "$fixture/$log.log"; exit 1; fi
  done
done
cp "$fixture/source.log" /workspace/SharpProof/artifacts/hm-inference/constraint-cli.txt
cp "$fixture/project.log" /workspace/SharpProof/artifacts/hm-inference/constraint-project.txt
printf '%s\n' 'PASS CliSourceConstraintNormalization' 'PASS CliProjectConstraintNormalization'
