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
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>13.0</LangVersion><AllowUnsafeBlocks>true</AllowUnsafeBlocks><RestoreSources>$packages</RestoreSources><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
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
cp src/SharpProof.HmInference/Samples/UserOperators.cs.txt "$consumer/UserOperators.cs"
cp src/SharpProof.HmInference/Samples/Interpolation.cs.txt "$consumer/Interpolation.cs"
cp src/SharpProof.HmInference/Samples/Resources.cs.txt "$consumer/Resources.cs"
cat > "$consumer/.editorconfig" <<'EOF'
root = true
[*.cs]
dotnet_diagnostic.HM0001.severity = warning
EOF
cat > "$consumer/NullableBounds.cs" <<'EOF'
#nullable enable
public interface PackageProjectionContract<T>{}
public class PackageProjectionDual<T>:PackageProjectionContract<T[]>,PackageProjectionContract<System.Collections.Generic.List<T>>{}
public class PackageParameterCursor<T>{public bool MoveNext()=>false;public T Current=>default!;}
public class PackageParameterSequence<T,E> where E:PackageParameterCursor<T>{public E GetEnumerator()=>default!;}
public class PackageAsyncParameterCursor<T>{public System.Threading.Tasks.ValueTask<bool> MoveNextAsync()=>new(false);public T Current=>default!;}
public class PackageAsyncParameterSequence<T,E> where E:PackageAsyncParameterCursor<T>{public E GetAsyncEnumerator()=>default!;}
public class PackageTaggedParameterSequence<Tag,E> where E:PackageParameterCursor<int>{public E GetEnumerator()=>default!;}
public class PackageTaggedAsyncParameterSequence<Tag,E> where E:PackageAsyncParameterCursor<int>{public E GetAsyncEnumerator()=>default!;}
[System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(PackageStateBuilder<>))]
public readonly struct PackageStateTask<T>(System.Threading.Tasks.Task<T> task){public System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter()=>task.GetAwaiter();}
public struct PackageStateBuilder<T>{
    private System.Runtime.CompilerServices.AsyncTaskMethodBuilder<T> inner;
    public static PackageStateBuilder<T> Create()=>new(){inner=System.Runtime.CompilerServices.AsyncTaskMethodBuilder<T>.Create()};
    public PackageStateTask<T> Task=>new(inner.Task);
    public void SetResult(T value)=>inner.SetResult(value);
    public void SetException(System.Exception exception)=>inner.SetException(exception);
    public void SetStateMachine(System.Runtime.CompilerServices.IAsyncStateMachine state)=>inner.SetStateMachine(state);
    public void Start<S>(ref S state) where S:T,System.Runtime.CompilerServices.IAsyncStateMachine=>inner.Start(ref state);
    public void AwaitOnCompleted<A,S>(ref A awaiter,ref S state) where A:System.Runtime.CompilerServices.INotifyCompletion where S:System.Runtime.CompilerServices.IAsyncStateMachine=>inner.AwaitOnCompleted(ref awaiter,ref state);
    public void AwaitUnsafeOnCompleted<A,S>(ref A awaiter,ref S state) where A:System.Runtime.CompilerServices.ICriticalNotifyCompletion where S:System.Runtime.CompilerServices.IAsyncStateMachine=>inner.AwaitUnsafeOnCompleted(ref awaiter,ref state);
}
public static class PackageNullableBounds{
    public static async PackageStateTask<System.Runtime.CompilerServices.IAsyncStateMachine> BuilderStateMachine(System.Runtime.CompilerServices.IAsyncStateMachine value,int ignored){await System.Threading.Tasks.Task.Yield();return value;}
    public static int RigidParameterEnumeration<E>(PackageTaggedParameterSequence<int,E> values,int seed) where E:PackageParameterCursor<int>{foreach(var item in values)seed=item;return seed;}
    public static async System.Threading.Tasks.Task<int> RigidAsyncParameterEnumeration<E>(PackageTaggedAsyncParameterSequence<int,E> values,int seed) where E:PackageAsyncParameterCursor<int>{await foreach(var item in values)seed=item;return seed;}
    public static int ParameterEnumeration(PackageParameterSequence<int,PackageParameterCursor<int>> values,int seed){foreach(var item in values)seed=item;return seed;}
    public static async System.Threading.Tasks.Task<int> AsyncParameterEnumeration(PackageAsyncParameterSequence<int,PackageAsyncParameterCursor<int>> values,int seed){await foreach(var item in values)seed=item;return seed;}
    public static PackageProjectionDual<int> ProjectionArray(PackageProjectionDual<int> value,int[] other)=>Project(value,other);
    private static T Project<T,U>(T value,U other) where T:PackageProjectionContract<U> =>value;
    public static async System.Threading.Tasks.Task<PackageBoundLeaf?> DependentAsync(System.Threading.Tasks.Task<PackageBoundLeaf?> value,int ignored){PackageBoundRoot other=new();return Optional(await value,other);}
    public static System.Enum? DependentEnum(System.Enum? value,int ignored)=>EnumBound(value);
    private static T EnumBound<T>(T value) where T:System.Enum? =>value;
    private static T Optional<T,U>(T value,U other) where T:U? =>value;
    public static PackageBoundLeaf? NullableBound(PackageBoundLeaf? value)=>Root(value);
    public static PackageBoundLeaf? NullableBoth(PackageBoundLeaf? value)=>Root(Middle(value));
    public static PackageBoundLeaf LegacyBound(PackageBoundLeaf value)=>Legacy(value);
    private static T Root<T>(T value) where T:PackageBoundRoot? =>value;
    private static T Middle<T>(T value) where T:PackageBoundMiddle? =>value;
#nullable disable
    private static T Legacy<T>(T value) where T:class=>value;
}
EOF
cat > "$consumer/ConstraintBounds.cs" <<'EOF'
public interface PackageCovariant<out T>{}
public interface PackageContravariant<in T>{}
public class PackageProducer<T>:PackageCovariant<T>{}
public class PackageConsumer<T>:PackageContravariant<T>{}
public class PackageSplitProducer<T>:PackageCovariant<T[]>,PackageCovariant<int>{}
public class PackageSplitConsumer<T>:PackageContravariant<System.Collections.Generic.IEnumerable<T>>,PackageContravariant<int>{}
public class PackageExactSplit<T>:PackageCovariant<System.Collections.Generic.List<T>>,PackageCovariant<System.Collections.Generic.IEnumerable<object>>{}
public class PackageBoundRoot{}
public class PackageBoundMiddle:PackageBoundRoot{}
public class PackageBoundLeaf:PackageBoundMiddle{}
public class PackageBoundBox<T>{}
public class PackageBoundBoxLeaf<T>:PackageBoundBox<T>{}
public static class PackageBounds{
    public static PackageExactSplit<string> PreferredProjection(PackageExactSplit<string> value,System.Collections.Generic.IEnumerable<object> other)=>Produce(value,other);
    public static PackageSplitProducer<string> ConvertingProjection(PackageSplitProducer<string> value,object other)=>Produce(value,other);
    public static PackageSplitConsumer<object> ContravariantProjection(PackageSplitConsumer<object> value,System.Collections.Generic.List<string> other)=>Consume(value,other);
    public static PackageSplitProducer<string> FixedProjection(PackageSplitProducer<string> value,int other)=>Produce(value,other);
    public static int[] VariantArrayIdentity(int[] value,int other)=>PreserveSequence(value,other);
    public static string[] VariantArrayReference(string[] value,object other)=>PreserveSequence(value,other);
    private static T PreserveSequence<T,U>(T value,U other) where T:System.Collections.Generic.IEnumerable<U> =>value;
    public static PackageProducer<string> CovariantBound(PackageProducer<string> value,object other)=>Produce(value,other);
    public static PackageConsumer<object> ContravariantBound(PackageConsumer<object> value,string other)=>Consume(value,other);
    private static T Produce<T,U>(T value,U other) where T:PackageCovariant<U> =>value;
    private static T Consume<T,U>(T value,U other) where T:PackageContravariant<U> =>value;
    public static object BoundCycle(object left,object right){Keep(left,right);Keep(right,left);return left;}
    public static PackageBoundLeaf StrongestBound(PackageBoundLeaf value)=>Root(Middle(value));
    public static PackageBoundBoxLeaf<int> BoundProjection(PackageBoundBoxLeaf<int> value,int first,int second){Box(value,first);Leaf(value,second);return value;}
    private static T Keep<T,U>(T value,U other) where T:U=>value;
    private static T Root<T>(T value) where T:PackageBoundRoot=>value;
    private static T Middle<T>(T value) where T:PackageBoundMiddle=>value;
    private static T Box<T,U>(T value,U other) where T:PackageBoundBox<U> =>value;
    private static T Leaf<T,U>(T value,U other) where T:PackageBoundBoxLeaf<U> =>value;
}
EOF
cat > "$consumer/Unsafe.cs" <<'EOF'
public class PackagePin<TTag,TValue>{public TValue Value=default!;public ref TValue GetPinnableReference()=>ref Value;}
public unsafe struct PackageBuffer<TTag>{public fixed int Values[4];public TTag Tag;}
public unsafe static class PointerPackageProbe {
    public static int PinPattern(PackagePin<int,int> value){fixed(int* pointer=value){return *pointer;}}
    public static bool PinVoid(int[] values){fixed(void* pointer=values){return pointer==null;}}
    public static int PinBuffer(ref PackageBuffer<int> value){fixed(int* pointer=value.Values){return pointer[0];}}
    public static int PointerRead(int* value)=>*value;
    public static int* PointerStore(int* destination,int value){*destination=value;return destination;}
    public static int PointerStack(int value){var buffer=stackalloc[]{value};return buffer[0];}
    public static int IndirectPointer(delegate*<int,int> pointer,int value)=>pointer(value);
    public static int NativePointer(delegate* unmanaged[Cdecl]<int,int> pointer,int value)=>pointer(value);
}
EOF
NUGET_PACKAGES="$consumer/packages" dotnet build "$consumer/Consumer.csproj" -v:normal > "$consumer/build.log" 2>&1 || { tail -60 "$consumer/build.log"; exit 1; }
for expected in 'PreferredProjection<T1>(global::PackageExactSplit<T1> value, global::System.Collections.Generic.IEnumerable<object> other)' 'ConvertingProjection<T1>(global::PackageSplitProducer<T1> value, object other)' 'ContravariantProjection<T1, T2>(global::PackageSplitConsumer<T1> value, global::System.Collections.Generic.List<T2> other) where T1 : class where T2 : class, T1' 'FixedProjection<T1>(global::PackageSplitProducer<T1> value, int other)' 'VariantArrayIdentity<T1>(T1[] value, T1 other)' 'VariantArrayReference<T1, T2>(T1[] value, T2 other) where T1 : class, T2 where T2 : class' 'CovariantBound<T1, T2>(global::PackageProducer<T1> value, T2 other) where T1 : class, T2 where T2 : class' 'ContravariantBound<T1, T2>(global::PackageConsumer<T1> value, T2 other) where T1 : class where T2 : class, T1' 'BuilderStateMachine<T1>(global::System.Runtime.CompilerServices.IAsyncStateMachine value, T1 ignored)' 'RigidParameterEnumeration<E, T1>(global::PackageTaggedParameterSequence<T1, E> values, int seed)' 'RigidAsyncParameterEnumeration<E, T1>(global::PackageTaggedAsyncParameterSequence<T1, E> values, int seed)' 'ParameterEnumeration<T1>(global::PackageParameterSequence<T1, global::PackageParameterCursor<T1>> values, T1 seed)' 'AsyncParameterEnumeration<T1>(global::PackageAsyncParameterSequence<T1, global::PackageAsyncParameterCursor<T1>> values, T1 seed)' 'ProjectionArray<T1>(global::PackageProjectionDual<T1> value, T1[] other)' 'DependentAsync<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 ignored) where T1 : global::PackageBoundRoot' 'DependentEnum<T1>(global::System.Enum? value, T1 ignored)' 'NullableBound<T1>(T1? value) where T1 : global::PackageBoundRoot' 'NullableBoth<T1>(T1? value) where T1 : global::PackageBoundMiddle' 'LegacyBound<T1>(T1 value) where T1 : class?' 'BoundCycle<T1>(T1 left, T1 right)' 'StrongestBound<T1>(T1 value) where T1 : global::PackageBoundMiddle' 'BoundProjection<T1>(global::PackageBoundBoxLeaf<T1> value, T1 first, T1 second)' 'PinPattern<T1>(global::PackagePin<T1, int> value)' 'PinVoid<T1>(T1[] values) where T1 : unmanaged' 'PinBuffer<T1>(ref global::PackageBuffer<T1> value)' 'IndirectPointer<T1, T2>(delegate*<T1, T2> pointer, T1 value)' 'NativePointer<T1, T2>(delegate* unmanaged[Cdecl]<T1, T2> pointer, T1 value)'; do
  if ! grep -Fq "$expected" "$consumer/build.log"; then tail -60 "$consumer/build.log"; exit 1; fi
done
if ! grep -q 'warning HM0001' "$consumer/build.log"; then tail -60 "$consumer/build.log"; exit 1; fi
if grep -Eq 'CS8032|AD0001' "$consumer/build.log"; then tail -60 "$consumer/build.log"; exit 1; fi
for expected in 'PreferredProjection<T1>(global::PackageExactSplit<T1> value, global::System.Collections.Generic.IEnumerable<object> other)' 'ConvertingProjection<T1>(global::PackageSplitProducer<T1> value, object other)' 'ContravariantProjection<T1, T2>(global::PackageSplitConsumer<T1> value, global::System.Collections.Generic.List<T2> other) where T1 : class where T2 : class, T1' 'FixedProjection<T1>(global::PackageSplitProducer<T1> value, int other)' 'VariantArrayIdentity<T1>(T1[] value, T1 other)' 'VariantArrayReference<T1, T2>(T1[] value, T2 other) where T1 : class, T2 where T2 : class' 'CovariantBound<T1, T2>(global::PackageProducer<T1> value, T2 other) where T1 : class, T2 where T2 : class' 'ContravariantBound<T1, T2>(global::PackageConsumer<T1> value, T2 other) where T1 : class where T2 : class, T1' 'BuilderStateMachine<T1>(global::System.Runtime.CompilerServices.IAsyncStateMachine value, T1 ignored)' 'RigidParameterEnumeration<E, T1>(global::PackageTaggedParameterSequence<T1, E> values, int seed)' 'RigidAsyncParameterEnumeration<E, T1>(global::PackageTaggedAsyncParameterSequence<T1, E> values, int seed)' 'ParameterEnumeration<T1>(global::PackageParameterSequence<T1, global::PackageParameterCursor<T1>> values, T1 seed)' 'AsyncParameterEnumeration<T1>(global::PackageAsyncParameterSequence<T1, global::PackageAsyncParameterCursor<T1>> values, T1 seed)' 'ProjectionArray<T1>(global::PackageProjectionDual<T1> value, T1[] other)' 'DependentAsync<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 ignored) where T1 : global::PackageBoundRoot' 'DependentEnum<T1>(global::System.Enum? value, T1 ignored)' 'PointerRead<T1>(T1* value) where T1 : unmanaged' 'PointerStore<T1>(T1* destination, T1 value) where T1 : unmanaged' 'PointerStack<T1>(T1 value) where T1 : unmanaged' 'NullableIdentity<T1>' 'NullableReferenceIdentity<T1>' 'AsyncIdentity<T1>' 'ConditionalElement<T1>' 'CoalesceAssign<T1>' 'CustomAwait<T1>' 'global::PackageTask<T1> CustomBuilderIdentity<T1>' 'T1? ImportedNullable<T1>' 'T1 ImportedNullableLegacy<T1>' 'ImportedNullableAsync<T1>' 'Forward<T1>' 'Capture<T1>' 'Group<T1>' 'Apply<T1>' 'Yield<T1>' 'AsyncYield<T1>' 'Last<T1>' 'Flow<T1>' 'Loop<T1>' 'Pair<T1, T2>' 'AssignPair<T1, T2>' 'where T1 : struct' 'where T1 : class' 'BoxArgument<T1>' 'BoxAwaitedArgument<T1>' 'WidenAwaitedArgument<T1>' 'ArrayArgument<T1>' 'where T1 : notnull' 'PatternList<T1>' 'PatternProperty<T1>' 'PatternNullable<T1>' 'PatternSwitch<T1>' 'ExtensionEnumeration<T1>' 'ImplicitAwaitEnumeration<T1>' 'NestedShape<T1, T2>' 'SpanShape<T1>' 'SpanTailShape<T1>' 'RefShape<T1>' 'RefForeachShape<T1>' 'global::PackageTargetToken RefLikeTarget<T1>(global::PackageTargetToken value, T1 ignored)' 'CoupledAwaiter<T1>(global::SampleTaggedAwaitable<T1> awaited, T1 value)' 'CapturedRead<T1>(int value, T1 ignored)' 'AwaitCaptured<T1>(global::System.Threading.Tasks.Task<int> pending, T1 result)' 'LocalScheme<T1>(T1 value)' 'RefRead<T1>(T1 value)' 'OutCopy<T1>(T1 value)' 'ref T1 DelegateBorrow<T1>(global::SampleRefBorrow<T1> borrow, ref T1 value)' 'QuotedCapture<T1>(T1 value)' 'QuotedApply<T1, T2>' 'NullableQuote<T1>(T1? value)' 'AnonymousValue<T1>(T1 value)' 'RecordReplacement<T1>(global::SampleWithRecord<T1> value, T1 replacement)' 'StructReplacement<T1>(global::SampleWithStruct<T1> value, T1 replacement)' 'ParamsList<T1>(T1 value)' 'ParamsSpan<T1>(T1 value)' 'SpreadElements<T1>(T1[] values, T1 last)' 'AddBoxes<T1>' 'AddNullableBoxes<T1>' 'UnwrapNullableBox<T1>' 'UnwrapAwaitedBox<T1>' 'UnwrapAwaitedNullableBox<T1>' 'UnwrapNullableReferenceBox<T1>' 'UnwrapAwaitedNullableReferenceBox<T1>' 'CaptureFormat<T1>' 'CaptureNullableFormat<T1>' 'CaptureAwaitedFormat<T1>' 'GenericHandlerFormat<T1>' 'ForwardedHandlerFormat<T1>' 'SpanPackedFormat<T1, T2>' 'ListPackedFormat<T1, T2>' 'BuilderPackedFormat<T1, T2>' 'SpanAppendFormat<T1>' 'ListAppendFormat<T1>' 'BuilderAppendFormat<T1>' 'BoxedElements<T1>' 'WidenedElements<T1>' 'FormattedElements<T1>' 'OptionalAddElement<T1>' 'ArrayParamsAddElement<T1>' 'SpanParamsAddElement<T1>' 'ExtensionAddElement<T1>' 'RefExtensionAddElement<T1>' 'NullableResource<T1, T2>' 'ResourcePayload<T1>' 'AsyncResourcePayload<T1>' 'AsyncResourceContract<T1, T2>' 'CustomResourcePayload<T1>'; do
  if ! grep -Fq "$expected" "$consumer/build.log"; then tail -60 "$consumer/build.log"; exit 1; fi
done
sed -n '/internal const string Support = """/,/        """;/p' src/SharpProof.HmInference/BuilderLayoutChecks.cs | sed '1d;$d;s/^        //' > "$consumer/BuilderLayoutSupport.cs"
test -s "$consumer/BuilderLayoutSupport.cs"
cat > "$consumer/BuilderLayoutProbe.cs" <<'EOF'
public static class PackageLayoutWorkflow{
    public static void PackageLayoutCallback(System.Action<System.Func<int,LayoutTask<int>>> sink,string ignored){sink(static async value=>{await new LayoutSignal();return value;});}
    public static LayoutTask<int?> PackageLayoutLocalCall(int? value,string ignored){static LayoutTask<U?> Forward<U>(U? item) where U:struct=>Local(item);static async LayoutTask<T?> Local<T>(T? item) where T:struct{await new LayoutSignal();return item;}return Forward(value);}
    public static async LayoutTask<int> PackageLayoutCapture(int value,string ignored){await new LayoutSignal();return value;}
    public static async LayoutTask<int> PackageLayoutPhantom(LayoutPayload<int,string> value,string ignored){await new LayoutSignal();return value.Value;}
}
EOF
NUGET_PACKAGES="$consumer/packages" dotnet build "$consumer/Consumer.csproj" -c Release --no-restore -v:normal > "$consumer/release.log" 2>&1 || { tail -60 "$consumer/release.log"; exit 1; }
for expected in 'PackageLayoutCallback<T1, T2>(global::System.Action<global::System.Func<T1, global::LayoutTask<T1>>> sink, T2 ignored) where T1 : unmanaged' 'PackageLayoutCapture<T1, T2>(T1 value, T2 ignored) where T1 : unmanaged' 'PackageLayoutPhantom<T1, T2, T3>(global::LayoutPayload<T1, T2> value, T3 ignored) where T1 : unmanaged' 'PackageLayoutLocalCall<T1, T2>(T1? value, T2 ignored) where T1 : unmanaged'; do
  if ! grep -Fq "$expected" "$consumer/release.log"; then tail -60 "$consumer/release.log"; exit 1; fi
done
if grep -Eq 'CS8032|AD0001' "$consumer/release.log"; then tail -60 "$consumer/release.log"; exit 1; fi
cp "$consumer/release.log" "$evidence/package-layout-release.txt"
legacy_layout=$(mktemp -d /tmp/hm-package-legacy-layout.XXXXXX)
cp global.json "$legacy_layout/global.json"
cp "$consumer/.editorconfig" "$legacy_layout/.editorconfig"
cp "$consumer/BuilderLayoutSupport.cs" "$legacy_layout/"
sed 's/static async value=>/async value=>/g' "$consumer/BuilderLayoutProbe.cs" > "$legacy_layout/BuilderLayoutProbe.cs"
cat > "$legacy_layout/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>8.0</LangVersion><Optimize>true</Optimize><RestoreSources>$packages</RestoreSources><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
  <ItemGroup><PackageReference Include="SharpProof.HmInference" Version="$version" PrivateAssets="all" /></ItemGroup>
</Project>
EOF
for language in 8.0 9.0; do
  NUGET_PACKAGES="$legacy_layout/packages" dotnet build "$legacy_layout/Consumer.csproj" -c Release -p:LangVersion="$language" -v:normal > "$legacy_layout/$language.log" 2>&1 || { tail -60 "$legacy_layout/$language.log"; exit 1; }
  for expected in 'PackageLayoutCallback<T1, T2>(global::System.Action<global::System.Func<T1, global::LayoutTask<T1>>> sink, T2 ignored) where T1 : unmanaged' 'PackageLayoutLocalCall<T1, T2>(T1? value, T2 ignored) where T1 : unmanaged'; do
    if ! grep -Fq "$expected" "$legacy_layout/$language.log"; then tail -60 "$legacy_layout/$language.log"; exit 1; fi
  done
  if grep -Eq 'CS8032|AD0001' "$legacy_layout/$language.log"; then tail -60 "$legacy_layout/$language.log"; exit 1; fi
  cp "$legacy_layout/$language.log" "$evidence/package-layout-csharp-$language.txt"
done

# A pattern remains valid when the source's disposal interface has no contract member.
legacy=$(mktemp -d /tmp/hm-pattern-package-consumer.XXXXXX)
cp "$consumer/Consumer.csproj" "$consumer/.editorconfig" "$consumer/global.json" "$legacy/"
sed -i 's#</PropertyGroup>#<WarningsAsErrors>CS8019</WarningsAsErrors></PropertyGroup>#' "$legacy/Consumer.csproj"
printf '%s\n' 'dotnet_diagnostic.CS8019.severity = error' >> "$legacy/.editorconfig"
cat > "$legacy/Input.cs" <<'EOF'
using System;using System.Threading.Tasks;using System.Runtime.CompilerServices;
using System.Text;using System.Collections.Generic;
namespace System {public interface IAsyncDisposable {}}
public readonly struct LegacyAwaiter:INotifyCompletion {public bool IsCompleted=>true;public void OnCompleted(Action action)=>action();public void GetResult(){}}
public readonly struct LegacyAwaitable {public LegacyAwaiter GetAwaiter()=>new();}
public sealed class LegacyResource<T>(T value){public T Value=>value;public LegacyAwaitable DisposeAsync()=>default;}
public static class LegacyProbe {public static Task Pending=Task.CompletedTask;public static async Task<int> LegacyDisposal(LegacyResource<int> resource){await using var copy=resource;return copy.Value;}}
public static class StyleProbe {public static int StyleIdentity(int value)=>value;public static List<int> StyleList(List<int> value)=>value;}
EOF
NUGET_PACKAGES="$legacy/packages" dotnet build "$legacy/Consumer.csproj" -v:normal > "$legacy/build.log" 2>&1 || { tail -60 "$legacy/build.log"; exit 1; }
if ! grep -Fq 'LegacyDisposal<T1>(global::LegacyResource<T1> resource)' "$legacy/build.log" || grep -Eq 'CS8032|AD0001' "$legacy/build.log"; then tail -60 "$legacy/build.log"; exit 1; fi
if ! grep -Fq 'StyleIdentity<T1>(T1 value)' "$legacy/build.log"; then tail -60 "$legacy/build.log"; exit 1; fi
# Command-line compiler builds do not expose editor-only import diagnostics like a
# workspace compilation. Verify rejection through the project's editor snapshot.
cli_output=$(find artifacts/bin/SharpProof.HmInference -name SharpProof.HmInference.dll -path '*/net10.0/*' | head -1)
NUGET_PACKAGES="$legacy/packages" dotnet "$cli_output" --project "$legacy/Consumer.csproj" --no-restore > "$legacy/editor.log" 2>&1 || { tail -40 "$legacy/editor.log"; exit 1; }
if ! grep -Fq 'StyleIdentity<T1>(T1 value)' "$legacy/editor.log" || ! grep -Fq 'LegacyDisposal<T1>' "$legacy/editor.log" || grep -Fq 'StyleList<T1>' "$legacy/editor.log"; then tail -40 "$legacy/editor.log"; exit 1; fi
cp "$legacy/editor.log" "$evidence/package-editor-consumer.txt"
cp "$legacy/build.log" "$evidence/package-pattern-consumer.txt"
mkdir -p "$evidence/packages"
cp "$package" "$evidence/packages/"
cp "$consumer/build.log" "$evidence/package-consumer.txt"
printf '%s\n' 'PASS PortablePackageContents' 'PASS NuGetConsumerAnalyzerLoad' 'PASS NuGetNullableAsyncDiagnostics' 'PASS NuGetDelegateAsyncDiagnostics' 'PASS PackageSourceCommit' 'PASS NuGetEnumerationDiagnostics' 'PASS NuGetControlFlowDiagnostics' 'PASS NuGetDeconstructionDiagnostics' 'PASS NuGetCustomAwaitAndNullableOperations' 'PASS NuGetUnconstrainedNullableSchemes' 'PASS NuGetCustomAsyncBuilders' 'PASS NuGetArgumentConversions' 'PASS NuGetPatternInference' 'PASS NuGetExtensionAndImplicitAwaitEnumeration' 'PASS NuGetNestedAndRefLikeTypeShapes' 'PASS NuGetRefLikeTargetCompatibility' 'PASS NuGetCustomBuilderAwaiterConstraints' 'PASS NuGetLocalFunctionCaptureInference' 'PASS NuGetReferenceCallsAndDelegates' 'PASS NuGetExpressionTreeInference' 'PASS NuGetAnonymousAndWithValues' 'PASS NuGetCollectionExpressionsAndParams' 'PASS NuGetUserOperatorsNullableAndAsyncConversions' 'PASS NuGetFormattableStringsAndHandlers' 'PASS NuGetHandlerConstructorCollections' 'PASS NuGetHandlerAppendCollectionsAndConversions' 'PASS NuGetCollectionAddCallShapes' 'PASS NuGetNullableAndAsyncResourceInference' 'PASS NuGetContractlessAsyncDisposalPattern' 'PASS NuGetConsumerEditorWorkspaceValidation' 'PASS NuGetPointersAndStackAllocation' 'PASS NuGetFunctionPointerSignaturesAndCalls' 'PASS NuGetPinningPatternsAndFixedBuffers' 'PASS NuGetConvertingVariantProjectionSelection' 'PASS NuGetVariantConstraintDirections' 'PASS NuGetVariantArrayReceiverDomains' 'PASS NuGetConstraintNormalization' 'PASS NuGetNullableConstraintImplication' 'PASS NuGetDependentNullableAsyncAndEnumBounds' 'PASS NuGetShapeSelectedInterfaceProjection' 'PASS NuGetSubstitutedParameterEnumerators' 'PASS NuGetRigidParameterEnumeratorTags' 'PASS NuGetBuilderStateMachineResultConstraint' 'PASS NuGetUnmanagedStateMachinePhysicalLayout' 'PASS NuGetNestedUnmanagedStateMachineLayouts' 'PASS NuGetGenericLocalNullableLayouts' 'PASS NuGetCSharp8NestedAsyncLayouts' 'PASS NuGetCSharp9NestedAsyncLayouts' | tee "$evidence/package-checks.txt"
