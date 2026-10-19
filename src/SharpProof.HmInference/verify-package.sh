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
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>13.0</LangVersion><RestoreSources>$packages</RestoreSources><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
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
sealed class PackagePatternBox<T> { public T Value=>default!; }
ref struct PackageTargetToken {}
sealed class PackageShapeOuter<T> { public sealed class Item<U> { public T Read()=>default!; } }
sealed class PackageExtensionSource<T> { public T[] Values=[]; }
sealed class PackageImplicitSource<T> { public PackageImplicitEnumerator<T> GetAsyncEnumerator()=>new(); }
sealed class PackageImplicitEnumerator<T> { public T Current=>default!; public PackageImplicitMove<T> MoveNextAsync()=>new(); }
readonly struct PackageImplicitMove<T> {}
static class PackageEnumerationExtensions {
    public static IEnumerator<T> GetEnumerator<T>(this PackageExtensionSource<T> source)=>((IEnumerable<T>)source.Values).GetEnumerator();
    public static System.Runtime.CompilerServices.TaskAwaiter<bool> GetAwaiter<T>(this PackageImplicitMove<T> value)=>Task.FromResult(false).GetAwaiter();
}
sealed class PackageAwaitBox<T> { public System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter()=>Task.FromResult(default(T)!).GetAwaiter(); }
[System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(PackageBuilder<>))]
readonly struct PackageTask<T> {
    private readonly Task<T> task;
    public PackageTask(Task<T> task){this.task=task;}
    public System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter()=>task.GetAwaiter();
}
struct PackageBuilder<T> {
    private TaskCompletionSource<T> completion;
    public static PackageBuilder<T> Create()=>new(){completion=new(TaskCreationOptions.RunContinuationsAsynchronously)};
    public PackageTask<T> Task=>new(completion.Task);
    public void SetResult(T value)=>completion.SetResult(value);
    public void SetException(Exception error)=>completion.SetException(error);
    public void SetStateMachine(System.Runtime.CompilerServices.IAsyncStateMachine state)=>ArgumentNullException.ThrowIfNull(state);
    public void Start<S>(ref S state) where S:System.Runtime.CompilerServices.IAsyncStateMachine=>state.MoveNext();
    public void AwaitOnCompleted<A,S>(ref A awaiter,ref S state) where A:System.Runtime.CompilerServices.INotifyCompletion where S:System.Runtime.CompilerServices.IAsyncStateMachine=>awaiter.OnCompleted(state.MoveNext);
    public void AwaitUnsafeOnCompleted<A,S>(ref A awaiter,ref S state) where A:System.Runtime.CompilerServices.ICriticalNotifyCompletion where S:System.Runtime.CompilerServices.IAsyncStateMachine=>awaiter.UnsafeOnCompleted(state.MoveNext);
}
static class Sample {
    public static int Pair(KeyValuePair<int,bool> pair) { var(value,flag)=pair;return value; }
    public static (int,bool) AssignPair(KeyValuePair<int,bool> pair,int value,bool flag) => (value,flag)=pair;
    public static int Flow(int value) { goto Done; Done:return value; }
    public static int Loop(int value) { for(;;) return value; }
    public static IEnumerable<int> Yield(int value) { yield return value; }
    public static async IAsyncEnumerable<int> AsyncYield(int value) { await Task.Yield(); yield return value; }
    public static async Task<int> Last(IAsyncEnumerable<int> source,int seed) { await foreach(var value in source) seed=value; return seed; }
    public static void Forward(Action<int> action,int value) => action(value);
    public static Func<Task<int>> Capture(int value) => async () => { await Task.Yield(); return value; };
    public static Func<int> Group(PackageBox<int> box) => box.Get;
    public static void Apply(PackageSink<int> sink,int value) => sink(value);
    public static T? Maybe<T>(T value)=>value;
    public static int ImportedNullable(int value)=>Maybe(value);
#nullable disable
    public static int ImportedNullableLegacy(int value)=>Maybe(value);
#nullable enable
    public static async Task<int> ImportedNullableAsync(int value)=>await Task.FromResult(Maybe(value));
    public static async PackageTask<int> CustomBuilderIdentity(int value)=>value;
    private static object BoxValue(object value)=>value;
    private static object? BoxOptional(object? value)=>value;
    private static long? WidenOptional(long? value)=>value;
    private static T FirstValue<T>(IEnumerable<T> values) { using var e=values.GetEnumerator();return e.MoveNext()?e.Current:default!; }
    public static object BoxArgument(int value)=>BoxValue(value);
    public static async Task<object?> BoxAwaitedArgument(Task<int?> value)=>BoxOptional(await value);
    public static async Task<long?> WidenAwaitedArgument(Task<int?> value,bool ignored)=>WidenOptional(await value);
    public static int ArrayArgument(int[] values)=>FirstValue(values);
    public static int PatternList(int[] values,int fallback)=>values is [var first,..]?first:fallback;
    public static int PatternProperty(PackagePatternBox<int> value,int fallback)=>value is {Value:var item}?item:fallback;
    public static int PatternNullable(int? value,int fallback)=>value is {} captured?captured:fallback;
    public static int PatternSwitch(object value)=>value switch {string text=>text.Length,int number=>number,_=>-1};
    public static int ExtensionEnumeration(PackageExtensionSource<int> source,int seed){foreach(var value in source)seed=value;return seed;}
    public static async Task<int> ImplicitAwaitEnumeration(PackageImplicitSource<int> source,int seed){await foreach(var value in source)seed=value;return seed;}
    public static PackageShapeOuter<int>.Item<string> NestedShape(PackageShapeOuter<int>.Item<string> value)=>value;
    public static int SpanShape(ReadOnlySpan<int> values,int index)=>values[index];
    public static Span<int> SpanTailShape(Span<int> values,int start)=>values[start..];
    public static ref int RefShape(Span<int> values,int index)=>ref values[index];
    public static void RefForeachShape(Span<int> values,int replacement){foreach(ref var item in values)item=replacement;}
    public static PackageTargetToken RefLikeTarget(PackageTargetToken value,bool ignored)=>value;
    public static int Identity(int value) => value;
    public static int? NullableIdentity(int? value) => value;
    public static string? NullableReferenceIdentity(string? value) => value;
    public static int? ConditionalElement(int[]? xs,int i)=>xs?[i];
    public static int CoalesceAssign(int? value,int fallback)=>value??=fallback;
    public static async Task<int> CustomAwait(PackageAwaitBox<int> task)=>await task;
    public static async Task<int> AsyncIdentity(Task<int> task) => await task;
}
EOF
cp src/SharpProof.HmInference/Samples/CustomBuilders.cs.txt "$consumer/CustomBuilders.cs"
cp src/SharpProof.HmInference/Samples/LocalFunctions.cs.txt "$consumer/LocalFunctions.cs"
cp src/SharpProof.HmInference/Samples/ReferenceCalls.cs.txt "$consumer/ReferenceCalls.cs"
cp src/SharpProof.HmInference/Samples/ExpressionTrees.cs.txt "$consumer/ExpressionTrees.cs"
cp src/SharpProof.HmInference/Samples/StructuralValues.cs.txt "$consumer/StructuralValues.cs"
cp src/SharpProof.HmInference/Samples/CollectionExpressions.cs.txt "$consumer/CollectionExpressions.cs"
cat > "$consumer/.editorconfig" <<'EOF'
root = true
[*.cs]
dotnet_diagnostic.HM0001.severity = warning
EOF
NUGET_PACKAGES="$consumer/packages" dotnet build "$consumer/Consumer.csproj" -v:normal > "$consumer/build.log" 2>&1 || { tail -60 "$consumer/build.log"; exit 1; }
if ! grep -q 'warning HM0001' "$consumer/build.log"; then tail -60 "$consumer/build.log"; exit 1; fi
if grep -Eq 'CS8032|AD0001' "$consumer/build.log"; then tail -60 "$consumer/build.log"; exit 1; fi
for expected in 'NullableIdentity<T1>' 'NullableReferenceIdentity<T1>' 'AsyncIdentity<T1>' 'ConditionalElement<T1>' 'CoalesceAssign<T1>' 'CustomAwait<T1>' 'global::PackageTask<T1> CustomBuilderIdentity<T1>' 'T1? ImportedNullable<T1>' 'T1 ImportedNullableLegacy<T1>' 'ImportedNullableAsync<T1>' 'Forward<T1>' 'Capture<T1>' 'Group<T1>' 'Apply<T1>' 'Yield<T1>' 'AsyncYield<T1>' 'Last<T1>' 'Flow<T1>' 'Loop<T1>' 'Pair<T1, T2>' 'AssignPair<T1, T2>' 'where T1 : struct' 'where T1 : class' 'BoxArgument<T1>' 'BoxAwaitedArgument<T1>' 'WidenAwaitedArgument<T1>' 'ArrayArgument<T1>' 'where T1 : notnull' 'PatternList<T1>' 'PatternProperty<T1>' 'PatternNullable<T1>' 'PatternSwitch<T1>' 'ExtensionEnumeration<T1>' 'ImplicitAwaitEnumeration<T1>' 'NestedShape<T1, T2>' 'SpanShape<T1>' 'SpanTailShape<T1>' 'RefShape<T1>' 'RefForeachShape<T1>' 'global::PackageTargetToken RefLikeTarget<T1>(global::PackageTargetToken value, T1 ignored)' 'CoupledAwaiter<T1>(global::SampleTaggedAwaitable<T1> awaited, T1 value)' 'CapturedRead<T1>(int value, T1 ignored)' 'AwaitCaptured<T1>(global::System.Threading.Tasks.Task<int> pending, T1 result)' 'LocalScheme<T1>(T1 value)' 'RefRead<T1>(T1 value)' 'OutCopy<T1>(T1 value)' 'ref T1 DelegateBorrow<T1>(global::SampleRefBorrow<T1> borrow, ref T1 value)' 'QuotedCapture<T1>(T1 value)' 'QuotedApply<T1, T2>' 'NullableQuote<T1>(T1? value)' 'AnonymousValue<T1>(T1 value)' 'RecordReplacement<T1>(global::SampleWithRecord<T1> value, T1 replacement)' 'StructReplacement<T1>(global::SampleWithStruct<T1> value, T1 replacement)' 'ParamsList<T1>(T1 value)' 'ParamsSpan<T1>(T1 value)' 'SpreadElements<T1>(T1[] values, T1 last)'; do
  if ! grep -Fq "$expected" "$consumer/build.log"; then tail -60 "$consumer/build.log"; exit 1; fi
done
mkdir -p "$evidence/packages"
cp "$package" "$evidence/packages/"
cp "$consumer/build.log" "$evidence/package-consumer.txt"
printf '%s\n' 'PASS PortablePackageContents' 'PASS NuGetConsumerAnalyzerLoad' 'PASS NuGetNullableAsyncDiagnostics' 'PASS NuGetDelegateAsyncDiagnostics' 'PASS PackageSourceCommit' 'PASS NuGetEnumerationDiagnostics' 'PASS NuGetControlFlowDiagnostics' 'PASS NuGetDeconstructionDiagnostics' 'PASS NuGetCustomAwaitAndNullableOperations' 'PASS NuGetUnconstrainedNullableSchemes' 'PASS NuGetCustomAsyncBuilders' 'PASS NuGetArgumentConversions' 'PASS NuGetPatternInference' 'PASS NuGetExtensionAndImplicitAwaitEnumeration' 'PASS NuGetNestedAndRefLikeTypeShapes' 'PASS NuGetRefLikeTargetCompatibility' 'PASS NuGetCustomBuilderAwaiterConstraints' 'PASS NuGetLocalFunctionCaptureInference' 'PASS NuGetReferenceCallsAndDelegates' 'PASS NuGetExpressionTreeInference' 'PASS NuGetAnonymousAndWithValues' 'PASS NuGetCollectionExpressionsAndParams' | tee "$evidence/package-checks.txt"
