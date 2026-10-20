using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class NullableAsyncChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("ConditionalField", "static int? M(ConditionalBox<int>? x)=>x?.Value;class ConditionalBox<T>{public T Value=default!;}", "Suggestion", "T1? M<T1>(global::Cases.ConditionalBox<T1>? x) where T1 : struct"),
            ("ConditionalReferenceProperty", "static string? M(ConditionalBox<string>? x)=>x?.Value;class ConditionalBox<T>{public T Value=>default!;}", "Suggestion", "T1? M<T1>(global::Cases.ConditionalBox<T1>? x) where T1 : class"),
            ("ConditionalMethod", "static int? M(ConditionalBox<int>? x)=>x?.Read();class ConditionalBox<T>{public T Read()=>default!;}", "Suggestion", "T1? M<T1>(global::Cases.ConditionalBox<T1>? x) where T1 : struct"),
            ("ConditionalIndexer", "static int? M(ConditionalBox<int>? x,int i)=>x?[i];class ConditionalBox<T>{public T this[int i]=>default!;}", "Suggestion", "T1? M<T1>(global::Cases.ConditionalBox<T1>? x, int i) where T1 : struct"),
            ("ConditionalDelegate", "static string? M(System.Func<int,string>? map,int value)=>map?.Invoke(value);", "Suggestion", "T2? M<T1, T2>(global::System.Func<T1, T2>? map, T1 value) where T2 : class"),
            ("ConditionalAction", "static void M(System.Action<int>? sink,int value){sink?.Invoke(value);}", "Suggestion", "void M<T1>(global::System.Action<T1>? sink, T1 value)"),
            ("ConditionalChain", "static int? M(ConditionalBox<int>? x)=>x?.Next?.Value;class ConditionalBox<T>{public T Value=default!;public ConditionalBox<T>? Next;}", "Suggestion", "T1? M<T1>(global::Cases.ConditionalBox<T1>? x) where T1 : struct"),
            ("ConditionalAsyncFallback", "static async Task<int> M(ConditionalBox<int>? x,int fallback)=>await(x?.ReadAsync()??Task.FromResult(fallback));class ConditionalBox<T>{public Task<T> ReadAsync()=>Task.FromResult(default(T)!);}", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::Cases.ConditionalBox<T1>? x, T1 fallback)"),
            ("ConditionalNullableStruct", "static int? M(ConditionalBox<int>? x)=>x?.Value;struct ConditionalBox<T>{public T Value;}", "Suggestion", "T1? M<T1>(global::Cases.ConditionalBox<T1>? x) where T1 : struct"),
            ("ConditionalNullableTuple", "static int? M((int Value,string Tag)? x)=>x?.Value;", "Suggestion", "T1? M<T1, T2>((T1 Value, T2 Tag)? x) where T1 : struct"),
            ("ConditionalAnnotatedResult", "static string? M(ConditionalBox<string?>? x)=>x?.Value;class ConditionalBox<T>{public T Value=default!;}", "Suggestion", "T1? M<T1>(global::Cases.ConditionalBox<T1?>? x) where T1 : class"),
            ("ConditionalArrayIndex", "static int? M(int[]? x,System.Index index)=>x?[index];", "Suggestion", "T1? M<T1>(T1[]? x, global::System.Index index) where T1 : struct"),
            ("ConditionalArrayRange", "static int[]? M(int[]? x,System.Range range)=>x?[range];", "Suggestion", "T1[]? M<T1>(T1[]? x, global::System.Range range)"),
            ("ConditionalEvent", "class ConditionalBox<T>{public event System.Action<T>? Changed;public static System.Action<int>? M(ConditionalBox<int>? x)=>x?.Changed;}", "Suggestion", "global::System.Action<T1>? M<T1>(global::Cases.ConditionalBox<T1>? x)"),
            ("ConditionalExtension", "static int? M(ConditionalBox<int>? x)=>x?.Read();} class ConditionalBox<T>{public T Value=default!;}static class ConditionalExt{public static T Read<T>(this ConditionalBox<T> x)=>x.Value;}class Tail{", "Suggestion", "T1? M<T1>(global::ConditionalBox<T1>? x) where T1 : struct"),
            ("ConditionalUntypedNullCaller", "static int? M(ConditionalBox<int>? x)=>x?.Value;static int? Use()=>M(null);class ConditionalBox<T>{public T Value=default!;}", "Rejected", null),
            ("ConditionalUntypedDelegateCaller", "static string? M(System.Func<int,string>? map,int value)=>map?.Invoke(value);static string? Use()=>M(null,0);", "Suggestion", "string? M<T1>(global::System.Func<T1, string>? map, T1 value)"),
            ("ConditionalBoxedExtension", "static int? M(ConditionalStruct<int>? x,int ignored)=>x?.Read();}interface IConditionalValue<T>{T Value{get;}}struct ConditionalStruct<T>:IConditionalValue<T>{public T Value=>default!;}static class ConditionalExtensions{public static T Read<T>(this IConditionalValue<T> x)=>x.Value;}class Tail{", "Suggestion", "T1? M<T1, T2>(global::ConditionalStruct<T1>? x, T2 ignored) where T1 : struct"),
            ("ConditionalBoxedLiftedCaller", "static int? M(ConditionalStruct<int>? x,int ignored)=>x?.Read();static int? Use()=>M(new ConditionalStruct<int>(),0);}interface IConditionalValue<T>{T Value{get;}}struct ConditionalStruct<T>:IConditionalValue<T>{public T Value=>default!;}static class ConditionalExtensions{public static T Read<T>(this IConditionalValue<T> x)=>x.Value;}class Tail{", "Suggestion", "int? M<T1>(global::ConditionalStruct<int>? x, T1 ignored)"),
            ("ConditionalReferenceFallback", "static string M(ConditionalBox<string>? x,string fallback)=>x?.Read()??fallback;class ConditionalBox<T>{public T Read()=>default!;}", "Suggestion", "T1 M<T1>(global::Cases.ConditionalBox<T1>? x, T1 fallback) where T1 : class"),
            ("ConditionalAnnotatedDeclaration", "static string? M(ConditionalBox<string>? x)=>x?.Value;class ConditionalBox<T>{public T? Value;}", "Suggestion", "T1? M<T1>(global::Cases.ConditionalBox<T1>? x) where T1 : class"),
            ("NullableReferenceIdentity", "static string? M(string? x)=>x;", "Suggestion", "T1? M<T1>(T1? x) where T1 : class"),
            ("NullableValueIdentity", "static int? M(int? x)=>x;", "Suggestion", "T1? M<T1>(T1? x) where T1 : struct"),
            ("NullableArray", "static int[]? M(int[]? x)=>x;", "Suggestion", "T1[]? M<T1>(T1[]? x)"),
            ("NullableArrayElements", "static string?[] M(string?[] x)=>x;", "Suggestion", "T1?[] M<T1>(T1?[] x) where T1 : class"),
            ("NullableNestedArray", "static string?[]? M(string?[]? x)=>x;", "Suggestion", "T1?[]? M<T1>(T1?[]? x) where T1 : class"),
            ("NullableContainer", "static System.Collections.Generic.List<string?>? M(System.Collections.Generic.List<string?>? x)=>x;", "Suggestion", "global::System.Collections.Generic.List<T1?>? M<T1>(global::System.Collections.Generic.List<T1?>? x) where T1 : class"),
            ("LiftNullableValueReturn", "static int? M(int x)=>x;", "Suggestion", "T1? M<T1>(T1 x) where T1 : struct"),
            ("LiftNullableReferenceReturn", "static string? M(string x)=>x;", "Suggestion", "T1? M<T1>(T1 x) where T1 : class"),
            ("NullableValueRead", "static int M(int? x)=>x.Value;", "Suggestion", "T1 M<T1>(T1? x) where T1 : struct"),
            ("NullableHasValue", "static bool M(int? x)=>x.HasValue;", "Suggestion", "bool M<T1>(T1? x) where T1 : struct"),
            ("NullableValueCoalesce", "static int M(int? x,int fallback)=>x??fallback;", "Suggestion", "T1 M<T1>(T1? x, T1 fallback) where T1 : struct"),
            ("NullableReferenceCoalesce", "static string M(string? x,string fallback)=>x??fallback;", "Suggestion", "T1 M<T1>(T1? x, T1 fallback) where T1 : class"),
            ("NullableThrowFallback", "static string M(string? x)=>x??throw new System.InvalidOperationException();", "Suggestion", "T1 M<T1>(T1? x) where T1 : class"),
            ("NullableSuppression", "static string M(string? x)=>x!;", "Suggestion", "T1 M<T1>(T1? x) where T1 : class"),
            ("NullableLiteral", "static string? M(bool ignored)=>null;", "Suggestion", "string? M<T1>(T1 ignored)"),
            ("NullableDefault", "static int? M(bool ignored)=>default;", "Suggestion", "int? M<T1>(T1 ignored)"),
            ("NullableNullEquality", "static bool M(string? x,bool ignored)=>x==null;", "Suggestion", "bool M<T1>(string? x, T1 ignored)"),
            ("NullableNullPattern", "static bool M(string? x,bool ignored)=>x is null;", "Suggestion", "bool M<T1, T2>(T1? x, T2 ignored) where T1 : class"),
            ("NullableNotNullPattern", "static bool M(string? x,bool ignored)=>x is not null;", "Suggestion", "bool M<T1, T2>(T1? x, T2 ignored) where T1 : class"),
            ("NullableConditionalMember", "static int? M(string? x,bool ignored)=>x?.Length;", "Suggestion", "int? M<T1>(string? x, T1 ignored)"),
            ("NullableConditionalIndexer", "static int? M(int[]? x,int i,bool ignored)=>x?[i];", "Suggestion", "T1? M<T1, T2>(T1[]? x, int i, T2 ignored) where T1 : struct"),
            ("NullableConditionalCall", "static string? M(string? x,bool ignored)=>x?.Trim();", "Suggestion", "string? M<T1>(string? x, T1 ignored)"),
            ("NullableCoalesceAssignment", "static string M(string? x,string y,bool ignored) { x??=y; return x; }", "Suggestion", "T1 M<T1, T2>(T1? x, T1 y, T2 ignored) where T1 : class"),
            ("NullableCoalesceAssignmentResult", "static string M(string? x,string y)=>x??=y;", "Suggestion", "T1 M<T1>(T1? x, T1 y) where T1 : class"),
            ("NullableCoalesceAssignmentValue", "static int M(int? x,int y)=>x??=y;", "Suggestion", "T1 M<T1>(T1? x, T1 y) where T1 : struct"),
            ("NullableCoalesceAssignmentBoth", "static int? M(int? x,int? y)=>x??=y;", "Suggestion", "T1? M<T1>(T1? x, T1? y) where T1 : struct"),
            ("NullableCoalesceAssignmentConversion", "static long M(long? x,int y,bool ignored)=>x??=y;", "Suggestion", "long M<T1>(long? x, int y, T1 ignored)"),
            ("NullableWideningCoalesce", "static long M(int? x,long y,bool ignored)=>x??y;", "Suggestion", "long M<T1>(int? x, long y, T1 ignored)"),
            ("NullableValueNullPattern", "static bool M(int? x)=>x is null;", "Suggestion", "bool M<T1>(T1? x) where T1 : struct"),
            ("NullableValueNotNullPattern", "static bool M(int? x)=>x is not null;", "Suggestion", "bool M<T1>(T1? x) where T1 : struct"),
            ("NullableArrayNullPattern", "static bool M(int[]? x)=>x is null;", "Suggestion", "bool M<T1>(T1[]? x)"),
            ("NullableMemberNullPattern", "static bool M(ConditionalBox<string>? x)=>x?.Value is null;class ConditionalBox<T>{public T Value=default!;}", "Suggestion", "bool M<T1>(global::Cases.ConditionalBox<T1>? x) where T1 : class"),
            ("AsyncNullableNullPattern", "static async Task<bool> M(Task<int?> x)=>(await x) is null;", "Suggestion", "global::System.Threading.Tasks.Task<bool> M<T1>(global::System.Threading.Tasks.Task<T1?> x) where T1 : struct"),
            ("AsyncNullableNotNullPattern", "static async Task<bool> M(ValueTask<string?> x)=>(await x) is not null;", "Suggestion", "global::System.Threading.Tasks.Task<bool> M<T1>(global::System.Threading.Tasks.ValueTask<T1?> x) where T1 : class"),

            ("NullableCombinedNullPattern", "static bool M(string? x)=>x is null or not null;", "Suggestion", "bool M<T1>(T1? x) where T1 : class"),
            ("NullableSwitchNullPattern", "static bool M(string? x)=>x switch {null=>true,_=>false};", "Suggestion", "bool M<T1>(T1? x) where T1 : class"),
            ("NullablePropertyNullPattern", "static bool M(ConditionalBox<string> x)=>x is {Value: null};class ConditionalBox<T>{public T? Value;}", "Suggestion", "bool M<T1>(global::Cases.ConditionalBox<T1> x)"),

            ("NullableExistingGeneric", "static T? M<T>(T? x)=>x;", "Concrete", null),
            ("NullableExistingConstraints", "static T? M<T>(T? x,int? ignored) where T:class =>x;", "Suggestion", "T? M<T, T1>(T? x, T1? ignored) where T : class where T1 : struct"),
            ("AsyncTaskResult", "static async Task<int> M(Task<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("AsyncValueTaskResult", "static async ValueTask<int> M(ValueTask<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.ValueTask<T1> M<T1>(global::System.Threading.Tasks.ValueTask<T1> task)"),
            ("AsyncTaskBlock", "static async Task<int> M(Task<int> task) { var x=await task; return x; }", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("AsyncReturnWithoutAwait", "static async Task<int> M(int x)=>x;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(T1 x)"),
            ("AsyncNonGenericTask", "static async Task M(Task<int> task) { await task; }", "Suggestion", "global::System.Threading.Tasks.Task M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("AsyncNonGenericValueTask", "static async ValueTask M(ValueTask<int> task) { await task; return; }", "Suggestion", "global::System.Threading.Tasks.ValueTask M<T1>(global::System.Threading.Tasks.ValueTask<T1> task)"),
            ("AsyncVoidAwait", "static async Task<int> M(Task task,int x) { await task; return x; }", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Threading.Tasks.Task task, T1 x)"),
            ("AsyncConfiguredTask", "static async Task<int> M(Task<int> task,bool ignored)=>await task.ConfigureAwait(false);", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1, T2>(global::System.Threading.Tasks.Task<T1> task, T2 ignored)"),
            ("AsyncNullableValue", "static async Task<int?> M(Task<int?> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1?> M<T1>(global::System.Threading.Tasks.Task<T1?> task) where T1 : struct"),
            ("AsyncNullableReference", "static async Task<string?> M(Task<string?> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1?> M<T1>(global::System.Threading.Tasks.Task<T1?> task) where T1 : class"),
            ("AsyncBoxingReturn", "static async Task<object> M(int x,bool ignored) { await Task.Yield(); return x; }", "Suggestion", "global::System.Threading.Tasks.Task<object> M<T1>(int x, T1 ignored)"),
            ("AsyncTaskFactory", "static async Task<int> M(int x)=>await Task.FromResult(x);", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(T1 x)"),
            ("AsyncRecursive", "static async Task<int> M(bool again,int x) { if(again) return await M(false,x); return x; }", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(bool again, T1 x)"),
            ("NullableTuple", "static (int? value,string? label) M(int? value,string? label)=>(value,label);", "Suggestion", "(T1? value, T2? label) M<T1, T2>(T1? value, T2? label) where T1 : struct where T2 : class"),
            ("NullableTupleLift", "static (string? text,int? value) M(string text,int value)=>(text,value);", "Suggestion", "(T1? text, T2? value) M<T1, T2>(T1 text, T2 value) where T1 : class where T2 : struct"),
            ("NullableNotNullConstraintWarning", "static string M(string x)=>Id(x); static T Id<T>(T x) where T:notnull=>x;", "Suggestion", "T1 M<T1>(T1 x) where T1 : notnull"),
            ("AsyncNotNullConstraintWarning", "static async Task<string> M(string x)=>await Task.FromResult(Id(x)); static T Id<T>(T x) where T:notnull=>x;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(T1 x) where T1 : notnull"),
            ("NullableExistingStruct", "static T? M<T>(T? x,bool ignored) where T:struct=>x;", "Suggestion", "T? M<T, T1>(T? x, T1 ignored) where T : struct"),
            ("AsyncNullableTaskOperand", "static async Task<int> M(Task<int>? task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Threading.Tasks.Task<T1>? task)"),
            ("AsyncNullableReferenceLift", "static async Task<string?> M(string x) { await Task.Yield(); return x; }", "Suggestion", "global::System.Threading.Tasks.Task<T1?> M<T1>(T1 x) where T1 : class"),
            ("AsyncNullableValueLift", "static async ValueTask<int?> M(int x)=>x;", "Suggestion", "global::System.Threading.Tasks.ValueTask<T1?> M<T1>(T1 x) where T1 : struct"),
            ("NullableLambdaResult", "static System.Func<int,string?> M(string x)=>ignored=>x;", "Suggestion", "global::System.Func<int, T1?> M<T1>(T1 x) where T1 : class"),
            ("AsyncCustomAwaiter", "static async Task<int> M(Box task,bool ignored)=>await task; class Box { public System.Runtime.CompilerServices.TaskAwaiter<int> GetAwaiter()=>Task.FromResult(1).GetAwaiter(); }", "Suggestion", "global::System.Threading.Tasks.Task<int> M<T1>(global::Cases.Box task, T1 ignored)"),
            ("NullableConditionalReferenceElement", "static string? M(string[]? x,int i)=>x?[i];", "Suggestion", "T1? M<T1>(T1[]? x, int i) where T1 : class"),
            ("NullableConditionalAnnotatedElement", "static string? M(string?[]? x,int i)=>x?[i];", "Suggestion", "T1? M<T1>(T1?[]? x, int i) where T1 : class"),
            ("NullableConditionalNullableElement", "static int? M(int?[]? x,int i)=>x?[i];", "Suggestion", "T1? M<T1>(T1?[]? x, int i) where T1 : struct"),
            ("NullableConditionalNestedArray", "static int[]? M(int[][]? x,int i)=>x?[i];", "Suggestion", "T1[]? M<T1>(T1[][]? x, int i)"),
            ("NullableConditionalMultidimensional", "static int? M(int[,]? x,int i)=>x?[i,i];", "Suggestion", "T1? M<T1>(T1[,]? x, int i) where T1 : struct"),
            ("NullableConditionalCoalesce", "static int M(int[]? x,int i,int fallback)=>x?[i]??fallback;", "Suggestion", "T1 M<T1>(T1[]? x, int i, T1 fallback) where T1 : struct"),
            ("NullableConditionalReferenceCoalesce", "static string M(string[]? x,int i,string fallback)=>x?[i]??fallback;", "Suggestion", "T1 M<T1>(T1[]? x, int i, T1 fallback) where T1 : class"),
            ("AsyncGenericCustomAwaiter", "static async Task<int> M(AwaitBox<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::AwaitBox<T1> task)"),
            ("AsyncInheritedCustomAwaiter", "static async Task<int> M(DerivedAwaitBox<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::DerivedAwaitBox<T1> task)"),
            ("AsyncNullableCustomAwaiter", "static async Task<int?> M(AwaitBox<int?> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1?> M<T1>(global::AwaitBox<T1?> task) where T1 : struct"),
            ("AsyncReferenceCustomAwaiter", "static async Task<string?> M(AwaitBox<string?> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1?> M<T1>(global::AwaitBox<T1?> task) where T1 : class"),
            ("AsyncExtensionAwaiter", "static async Task<int> M(ExtensionAwaitBox<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::ExtensionAwaitBox<T1> task)"),
            ("AsyncConstrainedExtensionAwaiter", "static async Task<int> M(StructAwaitBox<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::StructAwaitBox<T1> task) where T1 : struct"),
            ("AsyncExtensionOverloadRetained", "static async Task<int> M(OverloadAwaitBox<int> task)=>await task;", "Concrete", null),
            ("AsyncNestedAwaiterInferred", "static async Task<int> M(NestedAwaitBox<int> task,bool ignored)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1, T2>(global::NestedAwaitBox<T1> task, T2 ignored)"),
            ("AsyncOwnAwaiter", "static async Task<int> M(OwnAwaitBox<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::OwnAwaitBox<T1> task)"),
            ("AsyncAwaiterShadowingRejected", "static async Task<int> M(ExplicitAwaitSource task)=>await task;", "Rejected", null),
            ("ImportedNullableValue", "static int M(int x)=>NullableImports.Echo(x);", "Suggestion", "T1? M<T1>(T1 x)"),
            ("ImportedNullableReference", "static string? M(string x)=>NullableImports.Echo(x);", "Suggestion", "T1? M<T1>(T1 x)"),
            ("ImportedNullableAnnotatedReference", "static string? M(string? x)=>NullableImports.Echo(x);", "Suggestion", "T1? M<T1>(T1? x) where T1 : class"),
            ("ImportedNullableValueWrapper", "static int? M(int? x)=>NullableImports.Echo(x);", "Suggestion", "T1? M<T1>(T1? x) where T1 : struct"),
            ("ImportedNullableSuppression", "static string M(string x)=>NullableImports.Maybe(x)!;", "Suggestion", "T1 M<T1>(T1 x)"),
            ("ImportedNullableMaybeValue", "static int M(int x)=>NullableImports.Maybe(x);", "Suggestion", "T1? M<T1>(T1 x)"),
            ("ImportedNullableMaybeReference", "static string? M(string x)=>NullableImports.Maybe(x);", "Suggestion", "T1? M<T1>(T1 x)"),
            ("ImportedNullableExistingParameter", "static T? M<T>(T? x,int ignored)=>NullableImports.Echo(x);", "Suggestion", "T? M<T, T1>(T? x, T1 ignored)"),
            ("ImportedNullableExistingReturn", "static T? M<T>(T x,int ignored)=>NullableImports.Maybe(x);", "Suggestion", "T? M<T, T1>(T x, T1 ignored)"),
            ("ImportedNullableArithmetic", "static int M(int x,bool ignored)=>NullableImports.Echo(x)+1;", "Suggestion", "int M<T1>(int x, T1 ignored)"),
            ("ImportedNullableMember", "static int M(string x,int ignored)=>NullableImports.Maybe(x).Length;", "Suggestion", "int M<T1>(string x, T1 ignored)"),
            ("ImportedNullableReferenceFallback", "static string M(string x,string fallback)=>NullableImports.Maybe(x)??fallback;", "Suggestion", "T1 M<T1>(T1 x, T1 fallback)"),
            ("ImportedNullableValueFallback", "static int M(int? x,int fallback)=>NullableImports.Echo(x)??fallback;", "Suggestion", "T1 M<T1>(T1? x, T1 fallback) where T1 : struct"),
            ("ImportedNullableTuple", "static (int first,int second) M(int x)=>NullableImports.Pair(x);", "Suggestion", "(T1? first, T1 second) M<T1>(T1 x)"),
            ("ImportedNullableArray", "static int[] M(int x)=>NullableImports.Array(x);", "Suggestion", "T1?[] M<T1>(T1 x)"),
            ("ImportedNullableContainer", "static System.Collections.Generic.List<int> M(int x)=>NullableImports.Items(x);", "Suggestion", "global::System.Collections.Generic.List<T1?> M<T1>(T1 x)"),
            ("ImportedNullableAsyncValue", "static async Task<int> M(int x)=>await NullableImports.Async(x);", "Suggestion", "global::System.Threading.Tasks.Task<T1?> M<T1>(T1 x)"),
            ("ImportedNullableAsyncReference", "static async Task<string?> M(string x)=>await NullableImports.Async(x);", "Suggestion", "global::System.Threading.Tasks.Task<T1?> M<T1>(T1 x)"),
            ("ImportedNullableExplicitValue", "static int M(int x,bool ignored)=>NullableImports.Echo<int>(x);", "Suggestion", "int M<T1>(int x, T1 ignored)"),
            ("ImportedNullableExplicitReference", "static string? M(string x,bool ignored)=>NullableImports.Echo<string>(x);", "Suggestion", "string? M<T1>(string x, T1 ignored)"),
            ("ImportedNullableNotNull", "static int M(int x)=>NullableImports.NotNull(x);", "Suggestion", "T1? M<T1>(T1 x) where T1 : notnull"),
            ("ImportedNullableCustomAwait", "static async Task<int> M(OptionalAwaitBox<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1?> M<T1>(global::OptionalAwaitBox<T1> task)"),
            ("ImportedNullableRepeated", "static int M(int x)=>NullableImports.Echo(NullableImports.Echo(x));", "Suggestion", "T1? M<T1>(T1 x)"),
            ("ImportedNullableSameBranch", "static int M(int x,bool flag)=>flag?NullableImports.Echo(x):x;", "Suggestion", "T1? M<T1>(T1 x, bool flag)"),
            ("ImportedNullableValueConstructor", "static System.Collections.Generic.KeyValuePair<int,bool> M(System.Collections.Generic.KeyValuePair<int,bool> x)=>NullableImports.Echo(x);", "Suggestion", "global::System.Collections.Generic.KeyValuePair<T1, T2> M<T1, T2>(global::System.Collections.Generic.KeyValuePair<T1, T2> x)"),
            ("ImportedNullableExplicitStruct", "static System.DateTime M(System.DateTime x,bool ignored)=>NullableImports.Echo<System.DateTime>(x);", "Suggestion", "global::System.DateTime M<T1>(global::System.DateTime x, T1 ignored)"),
            ("ImportedNullableExistingStruct", "static T? M<T>(T? x,int ignored) where T:struct=>NullableImports.Echo(x);", "Suggestion", "T? M<T, T1>(T? x, T1 ignored) where T : struct"),
            ("ImportedNullableExistingReference", "static T? M<T>(T? x,int ignored) where T:class=>NullableImports.Echo(x);", "Suggestion", "T? M<T, T1>(T? x, T1 ignored) where T : class"),
            ("ImportedNullableThenStruct", "static int? M(int x)=>NullableImports.Lift(NullableImports.Maybe(x));", "Suggestion", "T1? M<T1>(T1 x) where T1 : struct"),
            ("ImportedNullableThenNotNull", "static int M(int x)=>NullableImports.Required(NullableImports.Maybe(x));", "Suggestion", "T1 M<T1>(T1 x) where T1 : struct"),
            ("ImportedNullableExplicitNull", "static string? M(bool ignored)=>NullableImports.Echo<string>(null);", "Suggestion", "string? M<T1>(T1 ignored)"),
            ("ImportedNullableExplicitAnnotated", "static string? M(string x,bool ignored)=>NullableImports.Echo<string?>(x);", "Suggestion", "string? M<T1>(string x, T1 ignored)"),
            ("ImportedNullableCapturedLambda", "static System.Func<int,int> M(int x)=>ignored=>NullableImports.Maybe(x);", "Suggestion", "global::System.Func<int, T1?> M<T1>(T1 x)"),
            ("ImportedNullableRepeatedBranch", "static int M(int x,bool flag)=>flag?NullableImports.Echo(NullableImports.Echo(x)):x;", "Suggestion", "T1? M<T1>(T1 x, bool flag)"),
            ("ImportedNullableDisabledContext", "\n#nullable disable\nstatic int M(int x)=>NullableImports.Echo(x);\n", "Suggestion", "T1 M<T1>(T1 x)"),
            ("ImportedNullableWarningsOnly", "\n#nullable enable warnings\n#nullable disable annotations\nstatic int M(int x)=>NullableImports.Maybe(x);\n", "Suggestion", "T1 M<T1>(T1 x)"),
            ("ImportedNullableDisabledConstraint", "\n#nullable disable\nstatic int M(int x,IntOptional source)=>NullableImports.Typed(x,source);\n", "Suggestion", "T1 M<T1, T2>(T1 x, T2 source) where T2 : global::IOptional<T1>"),
            ("ImportedNullableMixedContext", "static int M(int x,\n#nullable disable\nint y,\n#nullable enable\nbool flag)=>flag?NullableImports.Maybe(x):y;", "Suggestion", "T1? M<T1>(T1 x, T1 y, bool flag)"),
            ("AsyncTypedLocal", "static async Task<int> M(Task<int> task) { int x=await task; return x; }", "Concrete", null),
            ("AsyncGenericRoot", "static async Task<T> M<T>(Task<T> task)=>await task;", "Concrete", null),
            ("AsyncVoidRoot", "static async void M(int x) { await Task.Yield(); }", "Suggestion", "void M<T1>(T1 x)"),
            ("AsyncForeach", "static async Task<int> M(System.Collections.Generic.IAsyncEnumerable<int> xs,int seed) { await foreach(var x in xs) seed=x; return seed; }", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Collections.Generic.IAsyncEnumerable<T1> xs, T1 seed)"),
            ("NullableReferenceAssignment", "static string? M(string? x,string y) { x=y; return x; }", "Suggestion", "T1? M<T1>(T1? x, T1 y) where T1 : class"),
            ("NullableToNonnullableAssignment", "static string M(string x,string? y) { x=y; return x; }", "Suggestion", "T1 M<T1>(T1 x, T1? y) where T1 : class"),
            ("NullableBothAssignment", "static string? M(string? x,string? y) { x=y; return x; }", "Suggestion", "T1? M<T1>(T1? x, T1? y) where T1 : class"),
            ("NullableTypedCaller", "static int? M(int? x)=>x; static int? Use()=>M((int?)null);", "Suggestion", "T1? M<T1>(T1? x) where T1 : struct"),
            ("NullableNullCallerRejected", "static int? M(int? x)=>x; static int? Use()=>M(null);", "Rejected", null),
            ("NullableReferenceCaller", "static string? M(string? x)=>x; static string? Use(string? x)=>M(x);", "Suggestion", "T1? M<T1>(T1? x) where T1 : class"),
            ("AsyncExistingCaller", "static async Task<int> M(Task<int> task)=>await task; static Task<int> Use()=>M(Task.FromResult(1));", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("AsyncIterator", "static async System.Collections.Generic.IAsyncEnumerable<int> M(int x) { await Task.Yield(); yield return x; }", "Suggestion", "global::System.Collections.Generic.IAsyncEnumerable<T1> M<T1>(T1 x)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("#nullable enable\nusing System.Threading.Tasks; class Cases { " + test.Source + " }\n#nullable enable\n" + SupportFixtures, cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException($"Invalid nullable/async fixture {test.Name}: {errors[0]}"); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != test.Status || result.Signature != test.Signature)
            {
                failures.Add($"{test.Name}: expected {test.Status}: {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}");
                continue;
            }
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Nullable/async analyzer failed for " + test.Name); }
            Program.Log.Info("PASS " + test.Name);
        }
        var awaitDeclaration = CSharpSyntaxTree.ParseText("using System; using System.Runtime.CompilerServices; static class AwaitProvider { public static TaskAwaiter<int> GetAwaiter(this Func<TaskAwaiter<int>> factory)=>factory(); }", path: "AwaitProvider.cs", cancellationToken: cancellationToken);
        var awaitCaller = CSharpSyntaxTree.ParseText("using System; using System.Runtime.CompilerServices; using System.Threading.Tasks; static class AwaitCaller { public static async Task<int> Use(Func<TaskAwaiter<int>> factory)=>await factory; }", path: "AwaitCaller.cs", cancellationToken: cancellationToken);
        var awaitCompilation = Program.Compile([awaitDeclaration, awaitCaller]);
        var awaitResult = Program.Results(awaitCompilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "GetAwaiter"; });
        if (awaitResult.Status != "Suggestion")
        { failures.Add("Cross-file implicit await did not preserve the generalized helper signature: " + awaitResult.Message); }
        else
        { Program.Log.Info("PASS CrossFileAwaitHelperGeneralization"); }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return tests.Length + 1 + await CodeFixRuntime(cancellationToken).ConfigureAwait(false)
            + await ConditionalAccessRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }

    private const string SupportFixtures = """
        interface IOptional<T> {}
        class IntOptional:IOptional<int> {}
        static class NullableImports {
            public static T? Typed<T,U>(T x,U source) where U:IOptional<T?> => x;
            public static T? Lift<T>(T x) where T:struct=>x;
            public static T Required<T>(T x) where T:notnull=>x;
            public static T? Echo<T>(T? x)=>x;
            public static T? Maybe<T>(T x)=>x;
            public static T? NotNull<T>(T x) where T:notnull=>x;
            public static (T? first,T second) Pair<T>(T x)=>(x,x);
            public static T?[] Array<T>(T x)=>new T?[]{x};
            public static System.Collections.Generic.List<T?> Items<T>(T x)=>new(){x};
            public static System.Threading.Tasks.Task<T?> Async<T>(T x)=>System.Threading.Tasks.Task.FromResult<T?>(x);
        }
        class OptionalAwaitBox<T> { public OwnAwaiter<T?> GetAwaiter()=>new(); }
        class AwaitBox<T> {
            public System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter()=>System.Threading.Tasks.Task.FromResult(default(T)!).GetAwaiter();
        }
        class DerivedAwaitBox<T>:AwaitBox<T> {}
        class ExtensionAwaitBox<T> {}
        class StructAwaitBox<T> {}
        class OverloadAwaitBox<T> {}
        class NestedAwaitBox<T> {
            public Awaiter GetAwaiter()=>new();
            public struct Awaiter:System.Runtime.CompilerServices.INotifyCompletion {
                public bool IsCompleted=>true;
                public T GetResult()=>default!;
                public void OnCompleted(System.Action action)=>action();
            }
        }
        class OwnAwaitBox<T> { public OwnAwaiter<T> GetAwaiter()=>new(); }
        struct OwnAwaiter<T>:System.Runtime.CompilerServices.INotifyCompletion {
            public bool IsCompleted=>true;
            public T GetResult()=>default!;
            public void OnCompleted(System.Action action)=>action();
        }
        interface IAwaitSource { System.Runtime.CompilerServices.TaskAwaiter<int> GetAwaiter(); }
        class ExplicitAwaitSource:IAwaitSource {
            System.Runtime.CompilerServices.TaskAwaiter<int> IAwaitSource.GetAwaiter()=>System.Threading.Tasks.Task.FromResult(1).GetAwaiter();
        }
        static class AwaitExtensions {
            public static System.Runtime.CompilerServices.TaskAwaiter<int> GetAwaiter<T>(this T task) where T:IAwaitSource=>task.GetAwaiter();
            public static System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter<T>(this ExtensionAwaitBox<T> task)=>System.Threading.Tasks.Task.FromResult(default(T)!).GetAwaiter();
            public static System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter<T>(this StructAwaitBox<T> task) where T:struct=>System.Threading.Tasks.Task.FromResult(default(T)).GetAwaiter();
            public static System.Runtime.CompilerServices.TaskAwaiter<int> GetAwaiter(this OverloadAwaitBox<int> task)=>System.Threading.Tasks.Task.FromResult(1).GetAwaiter();
            public static System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter<T>(this OverloadAwaitBox<T> task)=>System.Threading.Tasks.Task.FromResult(default(T)!).GetAwaiter();
        }
        """;
    private static async Task<int> CodeFixRuntime(CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "NullableAsyncFixes", "NullableAsyncFixes", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Probe.cs", """
            #nullable enable
            using System.Threading.Tasks;
            public sealed class LifetimeResource : System.IDisposable, System.IAsyncDisposable {
                public int Trace;
                public void Dispose() { Trace=1; }
                public ValueTask DisposeAsync() { Trace=2; return default; }
                ValueTask System.IAsyncDisposable.DisposeAsync() { Trace=3; return default; }
            }
            public sealed class RuntimeAwaitBox<T>(Task<T> task) {
                public static int Awaits;
                public RuntimeAwaiter<T> GetAwaiter() { Awaits++; return new(task.GetAwaiter()); }
            }
            public readonly struct RuntimeAwaiter<T>(System.Runtime.CompilerServices.TaskAwaiter<T> awaiter):System.Runtime.CompilerServices.INotifyCompletion {
                public bool IsCompleted=>awaiter.IsCompleted;
                public T GetResult()=>awaiter.GetResult();
                public void OnCompleted(System.Action action)=>awaiter.OnCompleted(action);
            }            public sealed class RuntimeExtensionBox<T>(Task<T> task) { public Task<T> Task=task; }
            public static class RuntimeAwaitExtensions {
                public static System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter<T>(this RuntimeExtensionBox<T> box)=>box.Task.GetAwaiter();
            }
            public static class RuntimeNullableImports {
                public static T? Echo<T>(T? x)=>x;
                public static T? Missing<T>(T x)=>default;
                public static T?[] Array<T>(T x)=>new T?[]{x};
                public static Task<T?> Async<T>(Task<T?> task)=>task;
            }            public static class NullableAsyncProbe {
                public static bool NullReference(string? x)=>x is null;
                public static bool NullValue(int? x)=>x is not null;
                public static async Task<bool> NullAwait(Task<int?> x)=>(await x) is null;

                public static int Finalizers;
                public static int Guarded(int x,bool fail) {
                    try { if(fail) throw new System.InvalidOperationException(); return x; }
                    catch(System.InvalidOperationException) when(fail) { return x; }
                    finally { Finalizers++; }
                }
                public static int Resource(LifetimeResource resource,int x) { using(resource) { return x; } }
                public static async Task<int> AsyncResource(LifetimeResource resource,int x) { await using(resource) { return x; } }
                public static string Constrained(string x)=>Required(x);
                private static T Required<T>(T x) where T:notnull=>x;
                public static int? Value(int? x) => x;
                public static string? Reference(string? x) => x;
                public static int Fallback(int? x,int fallback) => x??fallback;
                public static int MaybeEcho(int x)=>RuntimeNullableImports.Echo(x);
                public static int MaybeMissing(int x)=>RuntimeNullableImports.Missing(x);
                public static int[] MaybeArray(int x)=>RuntimeNullableImports.Array(x);
                public static async Task<int> MaybeTask(Task<int> task)=>await RuntimeNullableImports.Async(task);
                public static int SetValue(int? x,int fallback)=>x??=fallback;
                public static string SetReference(string? x,string fallback)=>x??=fallback;
                public static string SetReferenceFactory(string? x,System.Func<string> fallback)=>x??=fallback();
                public static int? ArrayValue(int[]? xs,int i)=>xs?[i];
                public static string? ArrayReference(string[]? xs,int i)=>xs?[i];
                public static async Task<int> CustomEcho(RuntimeAwaitBox<int> task)=>await task;
                public static async Task<int> CustomExtensionEcho(RuntimeExtensionBox<int> task)=>await task;
                public static async Task<int> Echo(Task<int> task) => await task;
                public static async ValueTask<int> ValueEcho(ValueTask<int> task) => await task;
                public static async Task<int?> NullableEcho(Task<int?> task) => await task;
                public static async Task Ignore(Task<int> task) { await task; return; }
                public static T? Existing<T>(T? x,int? ignored) where T : class /* retained */ => x;
            }
            """);
        var provider = new HmSignatureCodeFixProvider();
        var applied = 0;
        for (var round = 0; round < 32; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001"; });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            applied++;
        }
        if (applied != 26)
        { throw new InvalidOperationException($"Expected twenty-six nullable/async/lifetime code fixes; applied {applied}."); }
        var finalCompilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var problems = finalCompilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (problems.Length != 0)
        { throw new InvalidOperationException(string.Join("\n", problems.Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        var text = (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        if (!text.Contains("/* retained */", StringComparison.Ordinal) || !text.Contains("where T1 : struct", StringComparison.Ordinal) || !text.Contains("where T1 : class", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Code fix lost generated constraints or existing constraint trivia.");
        }
        using var image = new MemoryStream();
        var emitted = finalCompilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        var probe = System.Reflection.Assembly.Load(image.ToArray()).GetType("NullableAsyncProbe")!;
        var guid = Guid.NewGuid();
        var nullReference = probe.GetMethod("NullReference")!.MakeGenericMethod(typeof(Uri));
        var nullPatternSentinel = new Uri("https://example.com");
        if (!Equals(nullReference.Invoke(null, new object?[] { null }), true)
            || !Equals(nullReference.Invoke(null, new object?[] { nullPatternSentinel }), false))
        { throw new InvalidOperationException("Generic reference null patterns changed their result."); }
        var nullValue = probe.GetMethod("NullValue")!.MakeGenericMethod(typeof(Guid));
        if (!Equals(nullValue.Invoke(null, new object?[] { null }), false)
            || !Equals(nullValue.Invoke(null, new object?[] { guid }), true))
        { throw new InvalidOperationException("Generic nullable value patterns changed their result."); }
        var nullAwait = probe.GetMethod("NullAwait")!.MakeGenericMethod(typeof(Guid));
        var pendingNull = new TaskCompletionSource<Guid?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nullResult = (Task<bool>)nullAwait.Invoke(null, new object[] { pendingNull.Task })!;
        if (nullResult.IsCompleted)
        { throw new InvalidOperationException("Async null pattern completed before its operand."); }
        pendingNull.SetResult(null);
        if (!await nullResult.WaitAsync(cancellationToken).ConfigureAwait(false)
            || await ((Task<bool>)nullAwait.Invoke(null, new object[] { Task.FromResult<Guid?>(guid) })!).ConfigureAwait(false))
        { throw new InvalidOperationException("Async null patterns changed their result after resumption."); }
        Program.Log.Info("PASS NullableAsyncNullPatternRuntime");

        var valueMethod = probe.GetMethod("Value")!.MakeGenericMethod(typeof(Guid));
        if (!Equals(valueMethod.Invoke(null, new object?[] { guid }), guid) || valueMethod.Invoke(null, new object?[] { null }) is not null
            || !Equals(probe.GetMethod("Fallback")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object?[] { null, guid }), guid))
        {
            throw new InvalidOperationException("Nullable value runtime behavior changed.");
        }
        var marker = new InvalidOperationException("marker");
        var referenceMethod = probe.GetMethod("Reference")!.MakeGenericMethod(typeof(InvalidOperationException));
        if (!ReferenceEquals(referenceMethod.Invoke(null, new object?[] { marker }), marker) || referenceMethod.Invoke(null, new object?[] { null }) is not null)
        {
            throw new InvalidOperationException("Nullable reference runtime behavior changed.");
        }
        var echo = probe.GetMethod("Echo")!.MakeGenericMethod(typeof(string));
        if (await ((Task<string>)echo.Invoke(null, new object[] { Task.FromResult("ok") })!).ConfigureAwait(false) != "ok"
            || await ((ValueTask<string>)probe.GetMethod("ValueEcho")!.MakeGenericMethod(typeof(string)).Invoke(null, new object[] { new ValueTask<string>("ok") })!).ConfigureAwait(false) != "ok"
            || await ((Task<Guid?>)probe.GetMethod("NullableEcho")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { Task.FromResult<Guid?>(guid) })!).ConfigureAwait(false) != guid)
        {
            throw new InvalidOperationException("Generic async result runtime behavior changed.");
        }
        await ((Task)probe.GetMethod("Ignore")!.MakeGenericMethod(typeof(string)).Invoke(null, new object[] { Task.FromResult("ok") })!).ConfigureAwait(false);
        var guarded = probe.GetMethod("Guarded")!.MakeGenericMethod(typeof(Guid));
        if (!Equals(guarded.Invoke(null, new object[] { guid, false }), guid)
            || !Equals(guarded.Invoke(null, new object[] { guid, true }), guid)
            || !Equals(probe.GetField("Finalizers")!.GetValue(null), 2))
        { throw new InvalidOperationException("Exception filter or finalizer behavior changed."); }
        var resourceType = probe.Assembly.GetType("LifetimeResource")!;
        var resource = Activator.CreateInstance(resourceType)!;
        if (!Equals(probe.GetMethod("Resource")!.MakeGenericMethod(resourceType, typeof(Guid)).Invoke(null, new object[] { resource, guid }), guid)
            || !Equals(resourceType.GetField("Trace")!.GetValue(resource), 1))
        { throw new InvalidOperationException("Synchronous disposal behavior changed."); }
        if (await ((Task<Guid>)probe.GetMethod("AsyncResource")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { resource, guid })!).ConfigureAwait(false) != guid
            || !Equals(resourceType.GetField("Trace")!.GetValue(resource), 2))
        { throw new InvalidOperationException("Async disposal pattern changed to explicit interface dispatch."); }
        if (!Equals(probe.GetMethod("Constrained")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { guid }), guid))
        { throw new InvalidOperationException("Imported generic constraint code fix failed."); }
        Program.Log.Info("PASS ExceptionDisposalConstraintCodeFixRuntime");
        var arrayValue = probe.GetMethod("ArrayValue")!.MakeGenericMethod(typeof(Guid));
        var arrayReference = probe.GetMethod("ArrayReference")!.MakeGenericMethod(typeof(object));
        var sentinel = new object();
        if (!Equals(arrayValue.Invoke(null, new object?[] { new[] { guid }, 0 }), guid)
            || arrayValue.Invoke(null, new object?[] { null, 100 }) is not null
            || !ReferenceEquals(arrayReference.Invoke(null, new object?[] { new[] { sentinel }, 0 }), sentinel)
            || arrayReference.Invoke(null, new object?[] { null, 100 }) is not null)
        { throw new InvalidOperationException("Conditional array access lost nullable lifting or short circuiting."); }
        var setValue = probe.GetMethod("SetValue")!.MakeGenericMethod(typeof(Guid));
        var setReference = probe.GetMethod("SetReference")!.MakeGenericMethod(typeof(object));
        if (!Equals(setValue.Invoke(null, new object?[] { null, guid }), guid)
            || !Equals(setValue.Invoke(null, new object?[] { guid, Guid.Empty }), guid)
            || !ReferenceEquals(setReference.Invoke(null, new object?[] { null, sentinel }), sentinel)
            || !ReferenceEquals(setReference.Invoke(null, new object?[] { sentinel, new object() }), sentinel))
        { throw new InvalidOperationException("Coalescing assignment lost its assigned or existing value."); }
        var lazyFallback = probe.GetMethod("SetReferenceFactory")!.MakeGenericMethod(typeof(object));
        var fallbackCalls = 0;
        Func<object> createFallback = () => { fallbackCalls++; return sentinel; };
        if (!ReferenceEquals(lazyFallback.Invoke(null, new object?[] { sentinel, createFallback }), sentinel) || fallbackCalls != 0
            || !ReferenceEquals(lazyFallback.Invoke(null, new object?[] { null, createFallback }), sentinel) || fallbackCalls != 1)
        { throw new InvalidOperationException("Coalescing assignment changed fallback evaluation."); }
        foreach (var methodName in new[] { "CustomEcho", "CustomExtensionEcho" })
        {
            var wrapperName = methodName == "CustomEcho" ? "RuntimeAwaitBox`1" : "RuntimeExtensionBox`1";
            var wrapper = probe.Assembly.GetType(wrapperName)!.MakeGenericType(typeof(string));
            var method = probe.GetMethod(methodName)!.MakeGenericMethod(typeof(string));
            var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var box = Activator.CreateInstance(wrapper, pending.Task)!;
            var result = (Task<string>)method.Invoke(null, new[] { box })!;
            if (result.IsCompleted)
            { throw new InvalidOperationException("Custom awaiter did not suspend."); }
            pending.SetResult("custom resumed");
            if (await result.WaitAsync(cancellationToken).ConfigureAwait(false) != "custom resumed")
            { throw new InvalidOperationException("Custom awaiter lost its result."); }
            if (methodName == "CustomEcho" && !Equals(wrapper.GetField("Awaits")!.GetValue(null), 1))
            { throw new InvalidOperationException("Custom GetAwaiter was not invoked exactly once."); }
            var fault = new InvalidOperationException("custom await failure");
            try
            {
                var failed = Activator.CreateInstance(wrapper, Task.FromException<string>(fault))!;
                await ((Task<string>)method.Invoke(null, new[] { failed })!).ConfigureAwait(false);
                throw new InvalidOperationException("Custom awaiter swallowed a fault.");
            }
            catch (InvalidOperationException caught) when (ReferenceEquals(caught, fault)) { }
            using var customCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await customCancellation.CancelAsync().ConfigureAwait(false);
            var cancelledTask = Task.FromCanceled<string>(customCancellation.Token);
            var cancelledBox = Activator.CreateInstance(wrapper, cancelledTask)!;
            try
            {
                await ((Task<string>)method.Invoke(null, new[] { cancelledBox })!).ConfigureAwait(false);
                throw new InvalidOperationException("Custom awaiter swallowed cancellation.");
            }
            catch (OperationCanceledException) { }
        }
        foreach (var name in new[] { "MaybeEcho", "MaybeMissing", "MaybeArray", "MaybeTask" })
        {
            var parameter = probe.GetMethod(name)!.GetGenericArguments().Single();
            if (parameter.GetGenericParameterConstraints().Length != 0
                || (parameter.GenericParameterAttributes & System.Reflection.GenericParameterAttributes.SpecialConstraintMask) != 0)
            { throw new InvalidOperationException("Unconstrained nullable inference invented a domain constraint."); }
        }
        var maybeEcho = probe.GetMethod("MaybeEcho")!;
        var maybeMissing = probe.GetMethod("MaybeMissing")!;
        if (!Equals(maybeEcho.MakeGenericMethod(typeof(Guid)).Invoke(null, new object?[] { guid }), guid)
            || !Equals(maybeEcho.MakeGenericMethod(typeof(Guid?)).Invoke(null, new object?[] { guid }), guid)
            || maybeEcho.MakeGenericMethod(typeof(Guid?)).Invoke(null, new object?[] { null }) is not null
            || !Equals(maybeEcho.MakeGenericMethod(typeof(string)).Invoke(null, new object?[] { "nullable" }), "nullable")
            || maybeEcho.MakeGenericMethod(typeof(string)).Invoke(null, new object?[] { null }) is not null
            || !Equals(maybeMissing.MakeGenericMethod(typeof(Guid)).Invoke(null, new object?[] { guid }), Guid.Empty)
            || maybeMissing.MakeGenericMethod(typeof(Guid?)).Invoke(null, new object?[] { guid }) is not null
            || maybeMissing.MakeGenericMethod(typeof(string)).Invoke(null, new object?[] { "nullable" }) is not null)
        { throw new InvalidOperationException("Unconstrained T? was confused with Nullable<T> or lost its reference annotation."); }
        var maybeArray = (Guid[])probe.GetMethod("MaybeArray")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object?[] { guid })!;
        var maybeReferenceArray = (string?[])probe.GetMethod("MaybeArray")!.MakeGenericMethod(typeof(string)).Invoke(null, new object?[] { null })!;
        if (maybeArray.Length != 1 || maybeArray[0] != guid || maybeReferenceArray.Length != 1 || maybeReferenceArray[0] is not null)
        { throw new InvalidOperationException("Unconstrained nullable element annotations changed array element types."); }
        var maybeTask = probe.GetMethod("MaybeTask")!.MakeGenericMethod(typeof(Guid));
        var pendingMaybe = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var maybeResult = (Task<Guid>)maybeTask.Invoke(null, new object[] { pendingMaybe.Task })!;
        if (maybeResult.IsCompleted)
        { throw new InvalidOperationException("Annotated generic await completed before its operand."); }
        pendingMaybe.SetResult(guid);
        if (await maybeResult.WaitAsync(cancellationToken).ConfigureAwait(false) != guid
            || await ((Task<string?>)probe.GetMethod("MaybeTask")!.MakeGenericMethod(typeof(string)).Invoke(null, new object[] { Task.FromResult<string?>(null) })!).ConfigureAwait(false) is not null)
        { throw new InvalidOperationException("Annotated generic await changed its result type or value."); }
        Program.Log.Info("PASS NullableAsyncCodeFixRuntime");
        var taskSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingTask = (Task<string>)echo.Invoke(null, new object[] { taskSource.Task })!;
        if (pendingTask.IsCompleted)
        { throw new InvalidOperationException("Async task completed before its operand."); }
        taskSource.SetResult("resumed");
        var valueSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingValue = (ValueTask<string>)probe.GetMethod("ValueEcho")!.MakeGenericMethod(typeof(string)).Invoke(null, new object[] { new ValueTask<string>(valueSource.Task) })!;
        if (pendingValue.IsCompleted)
        { throw new InvalidOperationException("Async value task completed before its operand."); }
        valueSource.SetResult("resumed");
        if (await pendingTask.WaitAsync(cancellationToken).ConfigureAwait(false) != "resumed" || await pendingValue.ConfigureAwait(false) != "resumed")
        {
            throw new InvalidOperationException("Async continuation lost its result.");
        }
        Program.Log.Info("PASS NullableAsyncSuspension");
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cancelled.CancelAsync().ConfigureAwait(false);
        try
        {
            await ((Task<bool>)probe.GetMethod("Echo")!.MakeGenericMethod(typeof(bool)).Invoke(null, new object[] { Task.FromCanceled<bool>(cancelled.Token) })!).ConfigureAwait(false);
            throw new InvalidOperationException("Generic async method lost cancellation.");
        }
        catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
        Program.Log.Info("PASS NullableAsyncCancellation");
        try
        {
            await ((Task<string>)echo.Invoke(null, new object[] { Task.FromException<string>(marker) })!).ConfigureAwait(false);
            throw new InvalidOperationException("Generic async method lost its fault.");
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, marker)) { }
        Program.Log.Info("PASS NullableAsyncFaultPropagation");
        return 6;
    }
}
