#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
fixture=$(mktemp -d '/tmp/hm builder layout.XXXXXX')
cp global.json "$fixture/global.json"
mkdir -p "$fixture/feed"
printf '%s\n' '<configuration><packageSources><clear/><add key="fixture" value="./feed"/></packageSources></configuration>' > "$fixture/NuGet.Config"
sed -n '/internal const string Support = """/,/        """;/p' src/SharpProof.HmInference/BuilderLayoutChecks.cs | sed '1d;$d;s/^        //' > "$fixture/LayoutSupport.cs"
test -s "$fixture/LayoutSupport.cs"
cat > "$fixture/Input.cs" <<'EOF'
public static class LayoutWorkflow{
    public static void LayoutContextual(System.Action<System.Func<int,LayoutTask<int>>> sink,string ignored){sink(static async value=>{await new LayoutSignal();return value;});}
    private static System.Func<int,LayoutTask<int>>? CallContextual(){System.Func<int,LayoutTask<int>>? saved=null;LayoutContextual(value=>saved=value,"tag");return saved;}
    public static void LayoutCallback(System.Action<System.Func<int,LayoutTask<int>>> sink,string ignored){sink(static async value=>{await new LayoutSignal();return value;});}
    public static LayoutTask<int?> LayoutLocalCall(int? value,string ignored){static LayoutTask<U?> Forward<U>(U? item) where U:struct=>Local(item);static async LayoutTask<T?> Local<T>(T? item) where T:struct{await new LayoutSignal();return item;}return Forward(value);}
    public static async LayoutTask<int> LayoutCapture(int value,string ignored){await new LayoutSignal();return value;}
    public static async LayoutTask<int> LayoutPhantom(LayoutPayload<int,string> value,string ignored){await new LayoutSignal();return value.Value;}
}
EOF
cat > "$fixture/Layout.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>13.0</LangVersion><Optimize>true</Optimize><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
EOF
dotnet "$output" --project "$fixture/Layout.csproj" --configuration Release > "$fixture/project.log" 2>&1 || { tail -40 "$fixture/project.log"; exit 1; }
for expected in 'LayoutContextual<T1>(global::System.Action<global::System.Func<int, global::LayoutTask<int>>> sink, T1 ignored)' 'LayoutCallback<T1, T2>(global::System.Action<global::System.Func<T1, global::LayoutTask<T1>>> sink, T2 ignored) where T1 : unmanaged' 'LayoutCapture<T1, T2>(T1 value, T2 ignored) where T1 : unmanaged' 'LayoutPhantom<T1, T2, T3>(global::LayoutPayload<T1, T2> value, T3 ignored) where T1 : unmanaged' 'LayoutLocalCall<T1, T2>(T1? value, T2 ignored) where T1 : unmanaged'; do
  if ! grep -Fq "$expected" "$fixture/project.log"; then tail -40 "$fixture/project.log"; exit 1; fi
done
cp "$fixture/project.log" /workspace/SharpProof/artifacts/hm-inference/builder-layout-cli.txt
printf '%s\n' 'PASS CliUnmanagedStateMachinePhysicalLayout' 'PASS CliContextualCustomBuilderRegistration' 'PASS CliNestedUnmanagedStateMachineLayouts' 'PASS CliGenericLocalNullableLayouts'


for language in 8.0 9.0; do
  sed -i 's/static async value=>/async value=>/g' "$fixture/Input.cs"
  dotnet "$output" --project "$fixture/Layout.csproj" --configuration Release --property "LangVersion=$language" > "$fixture/legacy-$language.log" 2>&1 || { tail -40 "$fixture/legacy-$language.log"; exit 1; }
  for expected in 'LayoutContextual<T1>(global::System.Action<global::System.Func<int, global::LayoutTask<int>>> sink, T1 ignored)' 'LayoutCallback<T1, T2>(global::System.Action<global::System.Func<T1, global::LayoutTask<T1>>> sink, T2 ignored) where T1 : unmanaged' 'LayoutLocalCall<T1, T2>(T1? value, T2 ignored) where T1 : unmanaged'; do
    if ! grep -Fq "$expected" "$fixture/legacy-$language.log"; then tail -40 "$fixture/legacy-$language.log"; exit 1; fi
  done
  cp "$fixture/legacy-$language.log" "/workspace/SharpProof/artifacts/hm-inference/builder-layout-csharp-$language-cli.txt"
  printf '%s\n' "PASS CliCSharp"$language"NestedAsyncLayouts"
done
