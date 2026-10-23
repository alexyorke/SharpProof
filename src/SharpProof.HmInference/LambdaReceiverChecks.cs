using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class LambdaReceiverChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Method, string Signature)[] tests =
        [
            ("LambdaReceiverExpressionReturn", "static int M(Box<int> box,int value)=>box.Run(value,()=>Maybe(value));", "T1? M<T1>(global::Cases.Box<T1?> box, T1 value)"),
            ("LambdaReceiverBlockReturn", "static int M(Box<int> box,int value){return box.Run(value,()=>{return Maybe(value);});}", "T1? M<T1>(global::Cases.Box<T1?> box, T1 value)"),
            ("LambdaReceiverAnonymousReturn", "static int M(Box<int> box,int value)=>box.Run(value,delegate{return Maybe(value);});", "T1? M<T1>(global::Cases.Box<T1?> box, T1 value)"),
            ("LambdaReceiverReturnFirst", "static int M(PairBox<int,string> box,int value,string tag)=>box.Run(value,x=>Maybe(value),tag);", "T1? M<T1, T2>(global::Cases.PairBox<T1?, T2> box, T1 value, T2 tag)"),
            ("LambdaReceiverTaskReturn", "static Task<int> M(AsyncBox<int> box,int value)=>box.Run(value,async()=>{await Task.Yield();return Maybe(value);});", "global::System.Threading.Tasks.Task<T1?> M<T1>(global::Cases.AsyncBox<T1?> box, T1 value)"),
            ("LambdaReceiverValueTaskReturn", "static ValueTask<int> M(ValueAsyncBox<int> box,int value)=>box.Run(value,async()=>{await Task.Yield();return Maybe(value);});", "global::System.Threading.Tasks.ValueTask<T1?> M<T1>(global::Cases.ValueAsyncBox<T1?> box, T1 value)"),
            ("LambdaReceiverCustomTaskReturn", "static Task<int> M(AsyncPairBox<int,string> box,int value,string tag)=>box.Run(value,async x=>{await Task.Yield();return Maybe(value);},tag);", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::Cases.AsyncPairBox<T1?, T2> box, T1 value, T2 tag)"),
            ("LambdaReceiverCustomValueTaskReturn", "static ValueTask<int> M(ValueAsyncPairBox<int,string> box,int value,string tag)=>box.Run(value,async x=>{await Task.Yield();return Maybe(value);},tag);", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(global::Cases.ValueAsyncPairBox<T1?, T2> box, T1 value, T2 tag)"),
            ("LambdaReceiverListReturn", "static List<int> M(ListBox<int> box,int value)=>box.Run(value,()=>Pack(value,Maybe(value)));", "global::System.Collections.Generic.List<T1?> M<T1>(global::Cases.ListBox<T1?> box, T1 value)"),
            ("LambdaReceiverNestedListReturn", "static List<List<int>> M(NestedListBox<int> box,int value)=>box.Run(value,()=>NestedPack(Pack(value,Maybe(value))));", "global::System.Collections.Generic.List<global::System.Collections.Generic.List<T1?>> M<T1>(global::Cases.NestedListBox<T1?> box, T1 value)"),
            ("LambdaReceiverArrayReturn", "static int[] M(ArrayBox<int> box,int value)=>box.Run(value,()=>ArrayPack(value,Maybe(value)));", "T1?[] M<T1>(global::Cases.ArrayBox<T1?> box, T1 value)"),
            ("LambdaReceiverTupleReturn", "static (int value,string tag) M(TupleBox<int,string> box,int value,string tag)=>box.Run(value,()=> (Maybe(value),tag));", "(T1? value, T2 tag) M<T1, T2>(global::Cases.TupleBox<T1?, T2> box, T1 value, T2 tag)"),
            ("LambdaReceiverTupleSibling", "static (int value,string tag) M(TupleBox<int,string> box,int value,string tag)=>box.Run(value,()=> (value,Maybe(tag)));", "(T1 value, T2? tag) M<T1, T2>(global::Cases.TupleBox<T1, T2?> box, T1 value, T2 tag)"),
            ("LambdaReceiverRepeatedTupleFirst", "static (int left,int right) M(RepeatBox<int> box,int value)=>box.Run(value,()=> (Maybe(value),value));", "(T1? left, T1? right) M<T1>(global::Cases.RepeatBox<T1?> box, T1 value)"),
            ("LambdaReceiverRepeatedTupleSecond", "static (int left,int right) M(RepeatBox<int> box,int value)=>box.Run(value,()=> (value,Maybe(value)));", "(T1? left, T1? right) M<T1>(global::Cases.RepeatBox<T1?> box, T1 value)"),
            ("LambdaReceiverMixedTupleSibling", "static (int left,string tag) M(MixedBox<int,string> box,int value,string tag)=>box.Run(value,()=> (value,Maybe(tag)));", "(T1 left, T2? tag) M<T1, T2>(global::Cases.MixedBox<T1, T2?> box, T1 value, T2 tag)"),
            ("LambdaReceiverCustomListReturn", "static List<int> M(CustomListBox<int,string> box,int value,string tag)=>box.Run(value,x=>Pack(value,Maybe(value)),tag);", "global::System.Collections.Generic.List<T1?> M<T1, T2>(global::Cases.CustomListBox<T1?, T2> box, T1 value, T2 tag)"),
            ("LambdaReceiverAsyncListReturn", "static Task<List<int>> M(AsyncListBox<int> box,int value)=>box.Run(value,async()=>{await Task.Yield();return Pack(value,Maybe(value));});", "global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<T1?>> M<T1>(global::Cases.AsyncListBox<T1?> box, T1 value)")
        ];
        foreach (var test in tests)
        {
            var source = "#nullable enable\nusing System;using System.Collections.Generic;using System.Diagnostics.CodeAnalysis;using System.Threading.Tasks;class Cases{"
                + "sealed class Box<T>{public T Run(T seed,Func<T> action)=>action();}"
                + "delegate R ReturnFirst<R,A>(A argument);"
                + "sealed class PairBox<T,U>{public T Run(T seed,ReturnFirst<T,U> action,U tag)=>action(tag);}"
                + "sealed class AsyncBox<T>{public Task<T> Run(T seed,Func<Task<T>> action)=>action();}"
                + "sealed class ValueAsyncBox<T>{public ValueTask<T> Run(T seed,Func<ValueTask<T>> action)=>action();}"
                + "delegate Task<R> AsyncReturn<R,A>(A argument);"
                + "delegate ValueTask<R> ValueAsyncReturn<R,A>(A argument);"
                + "sealed class AsyncPairBox<T,U>{public Task<T> Run(T seed,AsyncReturn<T,U> action,U tag)=>action(tag);}"
                + "sealed class ValueAsyncPairBox<T,U>{public ValueTask<T> Run(T seed,ValueAsyncReturn<T,U> action,U tag)=>action(tag);}"
                + "sealed class ListBox<T>{public List<T> Run(T seed,Func<List<T>> action)=>action();}"
                + "sealed class NestedListBox<T>{public List<List<T>> Run(T seed,Func<List<List<T>>> action)=>action();}"
                + "sealed class ArrayBox<T>{public T[] Run(T seed,Func<T[]> action)=>action();}"
                + "sealed class TupleBox<T,U>{public (T value,U tag) Run(T seed,Func<(T value,U tag)> action)=>action();}"
                + "delegate (R left,R right) RepeatReturn<R>();"
                + "sealed class RepeatBox<T>{public (T left,T right) Run(T seed,RepeatReturn<T> action)=>action();}"
                + "delegate (R left,U tag) MixedReturn<R,U>();"
                + "sealed class MixedBox<T,U>{public (T left,U tag) Run(T seed,MixedReturn<T,U> action)=>action();}"
                + "delegate List<R> ListReturn<R,A>(A argument);"
                + "sealed class CustomListBox<T,U>{public List<T> Run(T seed,ListReturn<T,U> action,U tag)=>action(tag);}"
                + "sealed class AsyncListBox<T>{public Task<List<T>> Run(T seed,Func<Task<List<T>>> action)=>action();}"
                + "[return:MaybeNull]static T Maybe<T>(T value)=>default;"
                + "static List<T> Pack<T>(T first,T second)=>new(){first,second};"
                + "static List<List<T>> NestedPack<T>(List<T> value)=>new(){value};"
                + "static T[] ArrayPack<T>(T first,T second)=>new[]{first,second};"
                + test.Method
                + "static T? Fixed<T>(Box<T?> box,T value)=>box.Run(value,()=>Maybe(value));"
                + "static List<T?> FixedList<T>(ListBox<T?> box,T value)=>box.Run(value,()=>Pack(value,Maybe(value)));"
                + "static (T? left,T? right) GoodRepeat<T>(RepeatBox<T?> box,T value)=>box.Run(value,()=> (Maybe(value),value));"
                + "static (T left,U? tag) GoodMixed<T,U>(MixedBox<T,U?> box,T value,U tag)=>box.Run(value,()=> (value,Maybe(tag)));}";
            var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            if (test.Name.StartsWith("LambdaReceiverRepeated", StringComparison.Ordinal)
                || test.Name == "LambdaReceiverMixedTupleSibling")
            {
                var goodName = test.Name == "LambdaReceiverMixedTupleSibling" ? "GoodMixed" : "GoodRepeat";
                var goodMethod = tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
                { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == goodName; });
                var nullableWarnings = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
                {
                    ArgumentNullException.ThrowIfNull(diagnostic);
                    return diagnostic.Id is "CS8603" or "CS8619"
                        && diagnostic.Location.SourceTree == tree
                        && goodMethod.Span.Contains(diagnostic.Location.SourceSpan);
                }).ToArray();
                if (nullableWarnings.Length != 0)
                { throw new InvalidOperationException(test.Name + " native annotated control: " + nullableWarnings[0]); }
            }
            var result = Program.Results(compilation, cancellationToken).Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        (string Name, string Method)[] boundaries =
        [
            ("LambdaReceiverSuppressedReturn", "\n#pragma warning disable CS8603\nstatic int M(Box<int> box,int value)=>box.Run(value,()=>Maybe(value));\n#pragma warning restore CS8603\n"),
            ("LambdaReceiverSuppressedList", "\n#pragma warning disable CS8619\nstatic System.Collections.Generic.List<int> M(ListBox<int> box,int value)=>box.Run(value,()=>Pack(value,Maybe(value)));\n#pragma warning restore CS8619\n"),
            ("LambdaReceiverExplicitReturn", "static int M(Box<int> box,int value)=>box.Run(value,int()=>Maybe(value));"),
            ("LambdaReceiverNestedReturn", "static int M(Box<int> box,int value)=>box.Run(value,()=>{_=(Func<object>)(()=>Maybe(value));return value;});"),
            ("LambdaReceiverNestedListReturn", "static System.Collections.Generic.List<int> M(ListBox<int> box,int value)=>box.Run(value,()=>{_=(Func<System.Collections.Generic.List<object>>)(()=>Pack<object>(new object(),Maybe<object>(new object())));return new System.Collections.Generic.List<int>{value};});")
        ];
        foreach (var boundary in boundaries)
        {
            var source = "#nullable enable\nusing System;using System.Diagnostics.CodeAnalysis;class Cases{"
                + "sealed class Box<T>{public T Run(T seed,Func<T> action)=>action();}"
                + "sealed class ListBox<T>{public System.Collections.Generic.List<T> Run(T seed,Func<System.Collections.Generic.List<T>> action)=>action();}"
                + "[return:MaybeNull]static T Maybe<T>(T value)=>default;"
                + "static System.Collections.Generic.List<T> Pack<T>(T first,T second)=>new(){first,second};"
                + boundary.Method + "}";
            var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(boundary.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "M"; });
            if (result.Status == "Suggestion" && (result.Signature?.Contains("Box<T1?>", StringComparison.Ordinal) == true
                || result.Signature?.Contains("ListBox<T1?>", StringComparison.Ordinal) == true))
            { throw new InvalidOperationException(boundary.Name + " promoted a receiver without a direct, effective return warning: " + result.Signature); }
            Program.Log.Info("PASS " + boundary.Name);
        }
        return tests.Length + boundaries.Length;
    }

    public static async Task<int> RunApplied(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Diagnostics.CodeAnalysis;
            using System.Threading;
            using System.Threading.Tasks;
            #pragma warning disable HM0001
            public static class ReceiverTrace { public static int Helpers,Packs; public static bool UseDefault; public static Exception? Failure; }
            public sealed class ReceiverBox<T> {
                public int Runs;
                public T? Seed;
                public T Run(T seed, Func<T> action) { Runs++; Seed=seed; return action(); }
            }
            public sealed class AsyncReceiverBox<T> {
                public int Runs;
                public Task<T> Run(T seed, Func<Task<T>> action) { Runs++; return action(); }
            }
            public sealed class ValueAsyncReceiverBox<T> {
                public int Runs;
                public ValueTask<T> Run(T seed, Func<ValueTask<T>> action) { Runs++; return action(); }
            }
            public sealed class ListReceiverBox<T> {
                public int Runs;
                public List<T> Run(T seed, Func<List<T>> action) { Runs++; return action(); }
            }
            public sealed class TupleReceiverBox<T,U> {
                public int Runs;
                public (T value,U tag) Run(T seed, Func<(T value,U tag)> action) { Runs++; return action(); }
            }
            public sealed class AsyncListReceiverBox<T> {
                public int Runs;
                public Task<List<T>> Run(T seed, Func<Task<List<T>>> action) { Runs++; return action(); }
            }
            #pragma warning restore HM0001
            public static class ReceiverProbe {
                [return:MaybeNull]static T Maybe<T>(T value) {
                    ReceiverTrace.Helpers++;
                    if (ReceiverTrace.Failure is { } failure) throw failure;
                    return ReceiverTrace.UseDefault ? default : value;
                }
                static List<T> Pack<T>(T first,T second) { ReceiverTrace.Packs++; return new(){first,second}; }
                public static int M(ReceiverBox<int> box,int value)=>box.Run(value,()=>Maybe(value));
                public static int Block(ReceiverBox<int> box,int value)=>box.Run(value,()=>{return Maybe(value);});
                public static List<int> List(ListReceiverBox<int> box,int value)=>box.Run(value,()=>Pack(value,Maybe(value)));
                public static (int value,string tag) Tuple(TupleReceiverBox<int,string> box,int value,string tag)
                    =>box.Run(value,()=> (Maybe(value),tag));
                public static Task<int> Async(AsyncReceiverBox<int> box,int value,Task gate)
                    =>box.Run(value,async()=>{await gate;return Maybe(value);});
                public static ValueTask<int> ValueAsync(ValueAsyncReceiverBox<int> box,int value,Task gate)
                    =>box.Run(value,async()=>{await gate;return Maybe(value);});
                public static Task<List<int>> AsyncList(AsyncListReceiverBox<int> box,int value,Task gate)
                    =>box.Run(value,async()=>{await gate;return Pack(value,Maybe(value));});
            }
            public static class OriginalReceiverRunner {
                public static void Run() {
                    var box=new ReceiverBox<int>();
                    ReceiverTrace.Helpers=0;ReceiverTrace.UseDefault=false;
                    if(ReceiverProbe.M(box,7)!=7||box.Runs!=1||box.Seed!=7||ReceiverTrace.Helpers!=1)throw new Exception("expression result/effects");
                    ReceiverTrace.UseDefault=true;
                    if(ReceiverProbe.Block(box,9)!=0||box.Runs!=2||box.Seed!=9||ReceiverTrace.Helpers!=2)throw new Exception("block default/effects");
                    var failure=new InvalidOperationException("receiver-fault");ReceiverTrace.Failure=failure;
                    try{ReceiverProbe.M(box,11);throw new Exception("missing fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,failure)){}
                    finally{ReceiverTrace.Failure=null;}
                    if(box.Runs!=3||box.Seed!=11||ReceiverTrace.Helpers!=3)throw new Exception("fault effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var listBox=new ListReceiverBox<int>();var list=ReceiverProbe.List(listBox,31);
                    if(list.Count!=2||list[0]!=31||list[1]!=0||listBox.Runs!=1||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("list default/effects");
                    var tupleBox=new TupleReceiverBox<int,string>();var pair=ReceiverProbe.Tuple(tupleBox,37,"tag");
                    if(pair.value!=0||pair.tag!="tag"||tupleBox.Runs!=1||ReceiverTrace.Helpers!=2||ReceiverTrace.Packs!=1)
                        throw new Exception("tuple default/sibling/effects");
                }
                public static async Task RunAsync() {
                    ReceiverTrace.UseDefault=false;ReceiverTrace.Helpers=0;
                    var taskBox=new AsyncReceiverBox<int>();var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pending=ReceiverProbe.Async(taskBox,17,gate.Task);
                    if(pending.IsCompleted||taskBox.Runs!=1||ReceiverTrace.Helpers!=0)throw new Exception("task suspension");
                    gate.SetResult();
                    if(await pending!=17||ReceiverTrace.Helpers!=1)throw new Exception("task result/effects");
                    var valueBox=new ValueAsyncReceiverBox<int>();var valueGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    ReceiverTrace.UseDefault=true;
                    var valuePending=ReceiverProbe.ValueAsync(valueBox,19,valueGate.Task);
                    if(valuePending.IsCompleted||valueBox.Runs!=1||ReceiverTrace.Helpers!=1)throw new Exception("value task suspension");
                    valueGate.SetResult();
                    if(await valuePending!=0||ReceiverTrace.Helpers!=2)throw new Exception("value task default/effects");
                    var failure=new InvalidOperationException("async-receiver-fault");
                    var faultGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var faulted=ReceiverProbe.Async(taskBox,23,faultGate.Task);faultGate.SetException(failure);
                    try{await faulted;throw new Exception("missing async fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,failure)){}
                    if(taskBox.Runs!=2||ReceiverTrace.Helpers!=2)throw new Exception("async fault effects");
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    var cancelGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var canceledTask=ReceiverProbe.ValueAsync(valueBox,29,cancelGate.Task);
                    cancelGate.SetCanceled(canceled.Token);
                    try{await canceledTask;throw new Exception("missing async cancellation");}
                    catch(OperationCanceledException caught) when(caught.CancellationToken==canceled.Token){}
                    if(valueBox.Runs!=2||ReceiverTrace.Helpers!=2)throw new Exception("async cancel effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var listBox=new AsyncListReceiverBox<int>();var listGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var listPending=ReceiverProbe.AsyncList(listBox,41,listGate.Task);
                    if(listPending.IsCompleted||listBox.Runs!=1||ReceiverTrace.Helpers!=0||ReceiverTrace.Packs!=0)
                        throw new Exception("async list suspension");
                    listGate.SetResult();var list=await listPending;
                    if(list.Count!=2||list[0]!=41||list[1]!=0||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("async list result/effects");
                    var listFailure=new InvalidOperationException("async-list-fault");
                    var listFaultGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var listFaulted=ReceiverProbe.AsyncList(listBox,43,listFaultGate.Task);listFaultGate.SetException(listFailure);
                    try{await listFaulted;throw new Exception("missing async list fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,listFailure)){}
                    if(listBox.Runs!=2||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("async list fault effects");
                    var listCancelGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var listCanceled=ReceiverProbe.AsyncList(listBox,47,listCancelGate.Task);listCancelGate.SetCanceled(canceled.Token);
                    try{await listCanceled;throw new Exception("missing async list cancellation");}
                    catch(OperationCanceledException caught) when(caught.CancellationToken==canceled.Token){}
                    if(listBox.Runs!=3||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("async list cancel effects");
                }
            }
            """;
        const string newSource = """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            public static class NewReceiverRunner {
                public static void Run() {
                    var strings=new ReceiverBox<string?>();
                    ReceiverTrace.Helpers=0;ReceiverTrace.UseDefault=true;
                    if(ReceiverProbe.M(strings,"first") is not null||strings.Runs!=1||strings.Seed!="first"||ReceiverTrace.Helpers!=1)
                        throw new Exception("reference default/effects");
                    ReceiverTrace.UseDefault=false;
                    if(ReceiverProbe.Block(strings,"second")!="second"||strings.Runs!=2||strings.Seed!="second"||ReceiverTrace.Helpers!=2)
                        throw new Exception("reference result/effects");
                    var values=new ReceiverBox<int?>();ReceiverTrace.UseDefault=true;
                    if(ReceiverProbe.M(values,(int?)13) is not null||values.Runs!=1||values.Seed!=13||ReceiverTrace.Helpers!=3)
                        throw new Exception("physical nullable/effects");
                    var failure=new InvalidOperationException("new-receiver-fault");ReceiverTrace.Failure=failure;
                    try{ReceiverProbe.M(strings,"fault");throw new Exception("missing fault");}
                    catch(InvalidOperationException caught) when(ReferenceEquals(caught,failure)){}
                    finally{ReceiverTrace.Failure=null;}
                    if(strings.Runs!=3||ReceiverTrace.Helpers!=4)throw new Exception("reference fault effects");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var listStrings=new ListReceiverBox<string?>();var list=ReceiverProbe.List(listStrings,"item");
                    if(list.Count!=2||list[0]!="item"||list[1] is not null||listStrings.Runs!=1||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("new list reference result/effects");
                    var listValues=new ListReceiverBox<int?>();var valueList=ReceiverProbe.List(listValues,(int?)59);
                    if(valueList.Count!=2||valueList[0]!=59||valueList[1] is not null||listValues.Runs!=1||ReceiverTrace.Helpers!=2||ReceiverTrace.Packs!=2)
                        throw new Exception("new list physical nullable/effects");
                    var tupleBox=new TupleReceiverBox<string?,int>();var pair=ReceiverProbe.Tuple(tupleBox,"tuple",61);
                    if(pair.value is not null||pair.tag!=61||tupleBox.Runs!=1||ReceiverTrace.Helpers!=3||ReceiverTrace.Packs!=2)
                        throw new Exception("new tuple nullable/sibling/effects");
                }
                public static async Task RunAsync() {
                    ReceiverTrace.Helpers=0;ReceiverTrace.UseDefault=true;
                    var taskBox=new AsyncReceiverBox<string?>();var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pending=ReceiverProbe.Async(taskBox,"text",gate.Task);
                    if(pending.IsCompleted||taskBox.Runs!=1||ReceiverTrace.Helpers!=0)throw new Exception("new task suspension");
                    gate.SetResult();
                    if(await pending is not null||ReceiverTrace.Helpers!=1)throw new Exception("new task nullable result");
                    var valueBox=new ValueAsyncReceiverBox<string?>();var valueGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    ReceiverTrace.UseDefault=false;
                    var valuePending=ReceiverProbe.ValueAsync(valueBox,"value",valueGate.Task);
                    if(valuePending.IsCompleted||valueBox.Runs!=1||ReceiverTrace.Helpers!=1)throw new Exception("new value task suspension");
                    valueGate.SetResult();
                    if(await valuePending!="value"||ReceiverTrace.Helpers!=2)throw new Exception("new value task result");
                    ReceiverTrace.Helpers=0;ReceiverTrace.Packs=0;ReceiverTrace.UseDefault=true;
                    var listBox=new AsyncListReceiverBox<string?>();var listGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var listPending=ReceiverProbe.AsyncList(listBox,"async-list",listGate.Task);
                    if(listPending.IsCompleted||listBox.Runs!=1||ReceiverTrace.Helpers!=0||ReceiverTrace.Packs!=0)
                        throw new Exception("new async list suspension");
                    listGate.SetResult();var list=await listPending;
                    if(list.Count!=2||list[0]!="async-list"||list[1] is not null||ReceiverTrace.Helpers!=1||ReceiverTrace.Packs!=1)
                        throw new Exception("new async list nullable/effects");
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "LambdaReceivers", "LambdaReceivers", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Receivers.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var methods = changed.GetTypeByMetadataName("ReceiverProbe")!;
        foreach (var name in new[] { "M", "Block", "List", "Tuple", "Async", "ValueAsync", "AsyncList" })
        {
            var method = methods.GetMembers(name).OfType<IMethodSymbol>().Single();
            var expectedParameters = name == "Tuple" ? 2 : 1;
            if (method.TypeParameters.Length != expectedParameters || method.Parameters[0].Type is not INamedTypeSymbol owner
                || owner.TypeArguments[0].NullableAnnotation != NullableAnnotation.Annotated)
            { throw new InvalidOperationException("Lambda receiver Fix All missed " + name + ": " + method.ToDisplayString()); }
        }
        var contextBefore = new System.Runtime.Loader.AssemblyLoadContext("BeforeLambdaReceivers", isCollectible: true);
        var contextAfter = new System.Runtime.Loader.AssemblyLoadContext("AfterLambdaReceivers", isCollectible: true);
        try
        {
            async Task Execute(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext, string runner)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                ArgumentNullException.ThrowIfNull(runner);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Lambda receiver emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                var assembly = loadContext.LoadFromStream(image);
                var runnerType = assembly.GetType(runner)!;
                try
                {
                    runnerType.GetMethod("Run")!.Invoke(null, null);
                    await ((Task)runnerType.GetMethod("RunAsync")!.Invoke(null, null)!).WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is { } inner)
                { throw new InvalidOperationException(runner + ": " + inner.Message, inner); }
            }
            await Execute(original.WithAssemblyName("BeforeLambdaReceivers"), contextBefore, "OriginalReceiverRunner").ConfigureAwait(false);
            await Execute(changed.WithAssemblyName("AfterLambdaReceivers"), contextAfter, "OriginalReceiverRunner").ConfigureAwait(false);
            await Execute(changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newSource, cancellationToken: cancellationToken))
                .WithAssemblyName("NewLambdaReceivers"), contextAfter, "NewReceiverRunner").ConfigureAwait(false);
        }
        finally { contextBefore.Unload(); contextAfter.Unload(); }
        Program.Log.Info("PASS LambdaReceiverFixAllOriginalAndNewNullableDomainsEffectsAndFaults");
        return 1;
    }
}
