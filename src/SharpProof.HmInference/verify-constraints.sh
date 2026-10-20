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
public interface CliCovariant<out T>{}
public interface CliContravariant<in T>{}
public class CliProducer<T>:CliCovariant<T>{}
public class CliConsumer<T>:CliContravariant<T>{}
public class CliSplitProducer<T>:CliCovariant<T[]>,CliCovariant<int>{}
public class CliSplitConsumer<T>:CliContravariant<System.Collections.Generic.IEnumerable<T>>,CliContravariant<int>{}
public class CliExactSplit<T>:CliCovariant<System.Collections.Generic.List<T>>,CliCovariant<System.Collections.Generic.IEnumerable<object>>{}
public sealed class CliConcreteDispatch:CliSplitProducer<string>{}
public class CliBoundRoot{}
public class CliBoundMiddle:CliBoundRoot{}
public class CliBoundLeaf:CliBoundMiddle{}
public interface CliProjectionContract<T>{}
public class CliProjectionDual<T>:CliProjectionContract<T[]>,CliProjectionContract<System.Collections.Generic.List<T>>{}
public class CliParameterCursor<T>{public bool MoveNext()=>false;public T Current=>default!;}
public class CliParameterSequence<T,E> where E:CliParameterCursor<T>{public E GetEnumerator()=>default!;}
public class CliAsyncParameterCursor<T>{public System.Threading.Tasks.ValueTask<bool> MoveNextAsync()=>new(false);public T Current=>default!;}
public class CliAsyncParameterSequence<T,E> where E:CliAsyncParameterCursor<T>{public E GetAsyncEnumerator()=>default!;}
public class CliTaggedParameterSequence<Tag,E> where E:CliParameterCursor<int>{public E GetEnumerator()=>default!;}
public class CliTaggedAsyncParameterSequence<Tag,E> where E:CliAsyncParameterCursor<int>{public E GetAsyncEnumerator()=>default!;}
[System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(CliStateBuilder<>))]
public readonly struct CliStateTask<T>(System.Threading.Tasks.Task<T> task){public System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter()=>task.GetAwaiter();}
public struct CliStateBuilder<T>{
    private System.Runtime.CompilerServices.AsyncTaskMethodBuilder<T> inner;
    public static CliStateBuilder<T> Create()=>new(){inner=System.Runtime.CompilerServices.AsyncTaskMethodBuilder<T>.Create()};
    public CliStateTask<T> Task=>new(inner.Task);
    public void SetResult(T value)=>inner.SetResult(value);
    public void SetException(System.Exception exception)=>inner.SetException(exception);
    public void SetStateMachine(System.Runtime.CompilerServices.IAsyncStateMachine state)=>inner.SetStateMachine(state);
    public void Start<S>(ref S state) where S:T,System.Runtime.CompilerServices.IAsyncStateMachine=>inner.Start(ref state);
    public void AwaitOnCompleted<A,S>(ref A awaiter,ref S state) where A:System.Runtime.CompilerServices.INotifyCompletion where S:System.Runtime.CompilerServices.IAsyncStateMachine=>inner.AwaitOnCompleted(ref awaiter,ref state);
    public void AwaitUnsafeOnCompleted<A,S>(ref A awaiter,ref S state) where A:System.Runtime.CompilerServices.ICriticalNotifyCompletion where S:System.Runtime.CompilerServices.IAsyncStateMachine=>inner.AwaitUnsafeOnCompleted(ref awaiter,ref state);
}
public interface CliConditionalValue<T>{T Value{get;}}
public struct CliConditionalStruct<T>:CliConditionalValue<T>{public T Value=>default!;}
public static class CliConditionalExtensions{public static T Read<T>(this CliConditionalValue<T> value)=>value.Value;}
public static class CliBounds{
    public static string ConditionalArrayCoalesce(string[]? values,int index,string fallback)=>values?[index]??fallback;
    public static int? ConditionalBoxed(CliConditionalStruct<int>? value,int ignored)=>value?.Read();
    public class ConditionalContainer<T>{public T Value=default!;public T Read()=>Value;public System.Threading.Tasks.Task<T> ReadAsync()=>System.Threading.Tasks.Task.FromResult(Value);}
    public static int? ConditionalRead(ConditionalContainer<int>? value)=>value?.Read();
    public static async System.Threading.Tasks.Task<int> ConditionalAwait(ConditionalContainer<int>? value,int fallback)=>await(value?.ReadAsync()??System.Threading.Tasks.Task.FromResult(fallback));
    public static string? ConditionalInvoke(System.Func<int,string>? map,int value)=>map?.Invoke(value);
    public static int[]? ConditionalSlice(int[]? values,System.Range range)=>values?[range];
    public static bool NullPatternReference(string? value)=>value is null;
    public static bool NullPatternValue(int? value)=>value is not null;
    public static async System.Threading.Tasks.Task<bool> NullPatternAwait(System.Threading.Tasks.Task<int?> value)=>(await value) is null;

    public static bool LegacyTypeTest(object value)=>value is int;
    public static string? SafeReferenceCast(object value)=>value as string;
    public static int? SafeNullableCast(object value)=>value as int?;
    public static async System.Threading.Tasks.Task<int?> AwaitSafeNullableCast(System.Threading.Tasks.Task<object> value)=>(await value) as int?;

    public static bool DefaultReferenceEquality(object value)=>value==default;
    public static bool TypedNullEquality(object value)=>value==(object?)null;
    public static bool ReferenceNullEquality(object? value)=>value==null;
    public static bool ValueNullInequality(int? value)=>null!=value;
    public static bool TupleNullEquality((int Value,string Tag)? value)=>value==null;
    public static bool ContextualDefaultEquality(object? value)=>value==default;
    public static async System.Threading.Tasks.Task<bool> AwaitNullEquality(System.Threading.Tasks.Task<int?> value)=>(await value)==null;

    public static int DynamicSignature(dynamic value,int item)=>item;
    public static dynamic DynamicMember(dynamic value,int tag)=>value.Name;
    public static dynamic DynamicCall(dynamic value,int count,string tag)=>value.Read(count);
    public static void DynamicStore(dynamic value,int count,string tag){value.Count=count;}
    public static int DynamicCast(dynamic value,int tag)=>(int)value;
    public static async System.Threading.Tasks.Task<dynamic> DynamicAwait(dynamic value,int tag)=>await value;

    public class DynamicBoundaryPair<A,B>{public A Item=default!;public B Count=default!;}
    public static DynamicBoundaryPair<object,int> DynamicBoundaryReturn(DynamicBoundaryPair<dynamic,int> value,int tag)=>value;
    public static int DynamicBoundaryArgument(DynamicBoundaryPair<object,int> value,int tag)=>BoundaryItem(value);
    private static T BoundaryItem<T>(DynamicBoundaryPair<dynamic,T> value)=>value.Count;
    public static System.Threading.Tasks.Task<object> DynamicBoundaryTask(System.Threading.Tasks.Task<dynamic> value,int tag)=>value;
    public static (object Item,int Count) DynamicBoundaryTuple((dynamic Item,int Count) value,int tag)=>value;
    public static object[] DynamicBoundaryArray(dynamic[] value,int tag)=>value;

    public delegate string ClosedMapper(int value);
    public delegate System.Threading.Tasks.Task<string?> ClosedAsyncMapper(int value);
    public static class DelegateOwner<T>{public delegate V Mapper<V>(int value);}
    public static ClosedMapper ClosedContext(ClosedMapper map,int tag)=>map;
    public static ClosedAsyncMapper ClosedAsyncContext(ClosedAsyncMapper map,int tag)=>map;
    public static DelegateOwner<int>.Mapper<string> OwnerContext(DelegateOwner<int>.Mapper<string> map,int tag)=>map;
    public static System.Linq.Expressions.Expression<ClosedMapper> ClosedExpressionContext(System.Linq.Expressions.Expression<ClosedMapper> map,int tag)=>map;
    private static string ClosedEcho(int value)=>"ok";
    private static async System.Threading.Tasks.Task<string?> ClosedEchoAsync<T>(T value){await System.Threading.Tasks.Task.Yield();return null;}
    private static ClosedMapper CallClosed()=>ClosedContext(ClosedEcho,0);
    private static ClosedAsyncMapper CallClosedAsync()=>ClosedAsyncContext(ClosedEchoAsync,0);
    private static DelegateOwner<int>.Mapper<string> CallOwner()=>OwnerContext(value=>"ok",0);
    private static System.Linq.Expressions.Expression<ClosedMapper> CallClosedExpression()=>ClosedExpressionContext(value=>"ok",0);
    public static System.Func<int,string> MethodGroupContext(System.Func<int,string> map,int tag)=>map;
    public static System.Func<int,System.Threading.Tasks.Task<string?>> AsyncMethodGroupContext(System.Func<int,System.Threading.Tasks.Task<string?>> map,int tag)=>map;
    public static System.Collections.Generic.List<System.Func<int,string>> CollectionMethodGroupContext(System.Collections.Generic.List<System.Func<int,string>> maps,int tag)=>maps;
    private static string ContextEcho<T>(T value)=>"ok";
    private static async System.Threading.Tasks.Task<string?> ContextEchoAsync<T>(T value){await System.Threading.Tasks.Task.Yield();return null;}
    private static System.Func<int,string> CallMethodGroupContext()=>MethodGroupContext(ContextEcho,0);
    private static System.Func<int,System.Threading.Tasks.Task<string?>> CallAsyncMethodGroupContext()=>AsyncMethodGroupContext(ContextEchoAsync,0);
    private static System.Collections.Generic.List<System.Func<int,string>> CallCollectionMethodGroupContext()=>CollectionMethodGroupContext([ContextEcho],0);
    public static object? SharedContext(object? value,object? hint,object? tag,bool flag)=>flag?value:hint;
    private static object? CallSharedContext()=>SharedContext(null,(object?)new object(),null,true);
    public static async System.Threading.Tasks.Task<object?> SharedAsyncContext(object? value,object? hint,object? tag,bool flag){await System.Threading.Tasks.Task.Yield();return flag?value:hint;}
    private static System.Threading.Tasks.Task<object?> CallSharedAsyncContext()=>SharedAsyncContext(null,(object?)new object(),null,true);
    public static (object? Value,int Count) CompositeTuple((object? Value,int Count) value,int tag)=>value;
    private static (object?,int) CallCompositeTuple()=>CompositeTuple((null,1),0);
    public static System.Func<int,System.Threading.Tasks.Task<(object? Value,int Count)>> CompositeAsync(System.Func<int,System.Threading.Tasks.Task<(object? Value,int Count)>> map,int tag)=>map;
    private static System.Func<int,System.Threading.Tasks.Task<(object? Value,int Count)>> CallCompositeAsync()=>CompositeAsync(async value=>{await System.Threading.Tasks.Task.Yield();return (null,value);},0);
    public static async System.Threading.Tasks.Task<object?> ContextualNullable(object? value,int tag){await System.Threading.Tasks.Task.Yield();return value;}
    public static System.Func<int,System.Threading.Tasks.Task<string?>> ContextualAsync(System.Func<int,System.Threading.Tasks.Task<string?>> map,int tag)=>map;
    private static System.Threading.Tasks.Task<object?> CallNullable()=>ContextualNullable(null,0);
    private static System.Func<int,System.Threading.Tasks.Task<string?>> CallAsync()=>ContextualAsync(async value=>{await System.Threading.Tasks.Task.Yield();return (string?)null;},0);
    public static CliConcreteDispatch CallerRefined(CliConcreteDispatch value,object other)=>Produce(value,other);
    public static CliConcreteDispatch CallerRefinedTuple(CliConcreteDispatch value,(object Tag,int Count) other)=>Produce(value,other.Tag);
    private static CliConcreteDispatch InvokeRefined(CliConcreteDispatch value)=>CallerRefined(value,23);
    private static CliConcreteDispatch InvokeRefinedTuple(CliConcreteDispatch value)=>CallerRefinedTuple(value,(23,0));
    public static CliExactSplit<string> PreferredProjection(CliExactSplit<string> value,System.Collections.Generic.IEnumerable<object> other)=>Produce(value,other);
    public static CliSplitProducer<string> ConvertingProjection(CliSplitProducer<string> value,object other)=>Produce(value,other);
    public static CliSplitConsumer<object> ContravariantProjection(CliSplitConsumer<object> value,System.Collections.Generic.List<string> other)=>Consume(value,other);
    public static CliSplitProducer<string> FixedProjection(CliSplitProducer<string> value,int other)=>Produce(value,other);
    public static int[] VariantArrayIdentity(int[] value,int other)=>PreserveSequence(value,other);
    public static string[] VariantArrayReference(string[] value,object other)=>PreserveSequence(value,other);
    private static T PreserveSequence<T,U>(T value,U other) where T:System.Collections.Generic.IEnumerable<U> =>value;
    public static CliProducer<string> CovariantBound(CliProducer<string> value,object other)=>Produce(value,other);
    public static CliConsumer<object> ContravariantBound(CliConsumer<object> value,string other)=>Consume(value,other);
    private static T Produce<T,U>(T value,U other) where T:CliCovariant<U> =>value;
    private static T Consume<T,U>(T value,U other) where T:CliContravariant<U> =>value;
    public static async CliStateTask<System.Runtime.CompilerServices.IAsyncStateMachine> BuilderStateMachine(System.Runtime.CompilerServices.IAsyncStateMachine value,int ignored){await System.Threading.Tasks.Task.Yield();return value;}
    public static int RigidParameterEnumeration<E>(CliTaggedParameterSequence<int,E> values,int seed) where E:CliParameterCursor<int>{foreach(var item in values)seed=item;return seed;}
    public static async System.Threading.Tasks.Task<int> RigidAsyncParameterEnumeration<E>(CliTaggedAsyncParameterSequence<int,E> values,int seed) where E:CliAsyncParameterCursor<int>{await foreach(var item in values)seed=item;return seed;}
    public static int ParameterEnumeration(CliParameterSequence<int,CliParameterCursor<int>> values,int seed){foreach(var item in values)seed=item;return seed;}
    public static async System.Threading.Tasks.Task<int> AsyncParameterEnumeration(CliAsyncParameterSequence<int,CliAsyncParameterCursor<int>> values,int seed){await foreach(var item in values)seed=item;return seed;}
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
  for expected in 'ConditionalArrayCoalesce<T1>(T1[]? values, int index, T1 fallback) where T1 : class' 'ConditionalBoxed<T1, T2>(global::CliConditionalStruct<T1>? value, T2 ignored) where T1 : struct' 'ConditionalRead<T1>(global::CliBounds.ConditionalContainer<T1>? value) where T1 : struct' 'ConditionalAwait<T1>(global::CliBounds.ConditionalContainer<T1>? value, T1 fallback)' 'ConditionalInvoke<T1, T2>(global::System.Func<T1, T2>? map, T1 value) where T2 : class' 'ConditionalSlice<T1>(T1[]? values, global::System.Range range)' 'NullPatternReference<T1>(T1? value) where T1 : class' 'LegacyTypeTest<T1>(T1 value)' 'ReferenceNullEquality<T1>(T1? value) where T1 : class' 'TypedNullEquality<T1>(T1 value) where T1 : class' 'DynamicSignature<T1>(dynamic value, T1 item)' 'global::CliBounds.DynamicBoundaryPair<object, T1> DynamicBoundaryReturn<T1, T2>(global::CliBounds.DynamicBoundaryPair<dynamic, T1> value, T2 tag)' 'T1 DynamicBoundaryArgument<T1, T2>(global::CliBounds.DynamicBoundaryPair<object, T1> value, T2 tag)' 'global::System.Threading.Tasks.Task<object> DynamicBoundaryTask<T1>(global::System.Threading.Tasks.Task<dynamic> value, T1 tag)' '(object Item, T1 Count) DynamicBoundaryTuple<T1, T2>((dynamic Item, T1 Count) value, T2 tag)' 'object[] DynamicBoundaryArray<T1>(dynamic[] value, T1 tag)' 'DynamicMember<T1>(dynamic value, T1 tag)' 'DynamicCall<T1>(dynamic value, int count, T1 tag)' 'DynamicStore<T1>(dynamic value, int count, T1 tag)' 'DynamicCast<T1>(dynamic value, T1 tag)' 'DynamicAwait<T1>(dynamic value, T1 tag)' 'DefaultReferenceEquality<T1>(T1 value) where T1 : class' 'ValueNullInequality<T1>(T1? value) where T1 : struct' 'TupleNullEquality<T1, T2>((T1 Value, T2 Tag)? value)' 'ContextualDefaultEquality<T1>(T1? value) where T1 : class' 'AwaitNullEquality<T1>(global::System.Threading.Tasks.Task<T1?> value) where T1 : struct' 'SafeReferenceCast<T1>(T1 value)' 'SafeNullableCast<T1>(T1 value)' 'AwaitSafeNullableCast<T1>(global::System.Threading.Tasks.Task<T1> value)' 'NullPatternValue<T1>(T1? value) where T1 : struct' 'NullPatternAwait<T1>(global::System.Threading.Tasks.Task<T1?> value) where T1 : struct' 'ClosedContext<T1>(global::CliBounds.ClosedMapper map, T1 tag)' 'ClosedAsyncContext<T1>(global::CliBounds.ClosedAsyncMapper map, T1 tag)' 'OwnerContext<T1, T2>(global::CliBounds.DelegateOwner<int>.Mapper<T1> map, T2 tag)' 'ClosedExpressionContext<T1>(global::System.Linq.Expressions.Expression<global::CliBounds.ClosedMapper> map, T1 tag)' 'MethodGroupContext<T1, T2>(global::System.Func<int, T1> map, T2 tag)' 'AsyncMethodGroupContext<T1, T2>(global::System.Func<int, global::System.Threading.Tasks.Task<T1?>> map, T2 tag) where T1 : class' 'CollectionMethodGroupContext<T1, T2>(global::System.Collections.Generic.List<global::System.Func<int, T1>> maps, T2 tag)' 'T1? SharedContext<T1>(T1? value, T1? hint, object? tag, bool flag) where T1 : class' 'SharedAsyncContext<T1>(T1? value, T1? hint, object? tag, bool flag) where T1 : class' 'CompositeTuple<T1, T2>((object? Value, T1 Count) value, T2 tag)' 'CompositeAsync<T1>(global::System.Func<int, global::System.Threading.Tasks.Task<(object? Value, int Count)>> map, T1 tag)' 'ContextualNullable<T1>(object? value, T1 tag)' 'ContextualAsync<T1, T2>(global::System.Func<int, global::System.Threading.Tasks.Task<T1?>> map, T2 tag) where T1 : class' 'CallerRefined<T1>(T1 value, object other) where T1 : global::CliCovariant<object>' 'CallerRefinedTuple<T1, T2>(T1 value, (object Tag, T2 Count) other) where T1 : global::CliCovariant<object>' 'PreferredProjection<T1>(global::CliExactSplit<T1> value, global::System.Collections.Generic.IEnumerable<object> other)' 'ConvertingProjection<T1>(global::CliSplitProducer<T1> value, object other)' 'ContravariantProjection<T1, T2>(global::CliSplitConsumer<T1> value, global::System.Collections.Generic.List<T2> other) where T1 : class where T2 : class, T1' 'FixedProjection<T1>(global::CliSplitProducer<T1> value, int other)' 'VariantArrayIdentity<T1>(T1[] value, T1 other)' 'VariantArrayReference<T1, T2>(T1[] value, T2 other) where T1 : class, T2 where T2 : class' 'CovariantBound<T1, T2>(global::CliProducer<T1> value, T2 other) where T1 : class, T2 where T2 : class' 'ContravariantBound<T1, T2>(global::CliConsumer<T1> value, T2 other) where T1 : class where T2 : class, T1' 'BuilderStateMachine<T1>(global::System.Runtime.CompilerServices.IAsyncStateMachine value, T1 ignored)' 'RigidParameterEnumeration<E, T1>(global::CliTaggedParameterSequence<T1, E> values, int seed)' 'RigidAsyncParameterEnumeration<E, T1>(global::CliTaggedAsyncParameterSequence<T1, E> values, int seed)' 'ParameterEnumeration<T1>(global::CliParameterSequence<T1, global::CliParameterCursor<T1>> values, T1 seed)' 'AsyncParameterEnumeration<T1>(global::CliAsyncParameterSequence<T1, global::CliAsyncParameterCursor<T1>> values, T1 seed)' 'ProjectionArray<T1>(global::CliProjectionDual<T1> value, T1[] other)' 'DependentAsync<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 ignored) where T1 : global::CliBoundRoot' 'DependentEnum<T1>(global::System.Enum? value, T1 ignored)' 'NullableBound<T1>(T1? value) where T1 : global::CliBoundRoot' 'NullableBoth<T1>(T1? value) where T1 : global::CliBoundMiddle' 'BoundCycle<T1>(T1 left, T1 right)' 'StrongestBound<T1>(T1 value) where T1 : global::CliBoundMiddle'; do
    if ! grep -Fq "$expected" "$fixture/$log.log"; then tail -35 "$fixture/$log.log"; exit 1; fi
  done
done
cp "$fixture/source.log" /workspace/SharpProof/artifacts/hm-inference/constraint-cli.txt
cp "$fixture/project.log" /workspace/SharpProof/artifacts/hm-inference/constraint-project.txt
printf '%s\n' 'PASS CliSymbolicConditionalAccess' 'PASS CliNullableAsyncNullPatterns' 'PASS CliTypeTestsAndSafeCasts' 'PASS CliBuiltinNullEquality' 'PASS CliDynamicSignaturesAndOperations' 'PASS CliNestedDynamicIdentityBoundaries' 'PASS CliNominalDelegateContexts' 'PASS CliMethodGroupContextInference' 'PASS CliSharedContextGeneralization' 'PASS CliCompositeNullableAsyncCallers' 'PASS CliContextualNullableAsyncCallers' 'PASS CliCallerSignatureRefinement' 'PASS CliConvertingVariantProjectionSelection' 'PASS CliVariantConstraintDirections' 'PASS CliVariantArrayReceiverDomains' 'PASS CliSourceConstraintNormalization' 'PASS CliProjectConstraintNormalization' 'PASS CliNullableConstraintImplication' 'PASS CliDependentNullableAsyncAndEnumBounds' 'PASS CliShapeSelectedInterfaceProjection' 'PASS CliSubstitutedParameterEnumerators' 'PASS CliRigidParameterEnumeratorTags' 'PASS CliBuilderStateMachineResultConstraint'
