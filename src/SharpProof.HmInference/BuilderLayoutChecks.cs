using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class BuilderLayoutChecks
{
    internal const string Support = """
        #nullable enable
        using System;
        using System.Runtime.CompilerServices;
        [AsyncMethodBuilder(typeof(LayoutBuilder<>))]
        public readonly struct LayoutTask<T>(T value){public LayoutAwaiter<T> GetAwaiter(){LayoutTrace.Value+="awaiter;";return new(value);}}
        public readonly struct LayoutAwaiter<T>(T value):INotifyCompletion{public bool IsCompleted=>true;public T GetResult(){LayoutTrace.Value+="get;";return value;}public void OnCompleted(Action action)=>action();}
        public struct LayoutBuilder<T>{
            private T result;public static LayoutBuilder<T> Create()=>default;public LayoutTask<T> Task=>new(result);
            public void SetResult(T value){LayoutTrace.Value+="result;";result=value;}public void SetException(Exception exception)=>throw exception;public void SetStateMachine(IAsyncStateMachine state){}
            public void Start<S>(ref S state) where S:unmanaged,IAsyncStateMachine{LayoutTrace.Value+="start;";state.MoveNext();}
            public void AwaitOnCompleted<A,S>(ref A awaiter,ref S state) where A:INotifyCompletion where S:IAsyncStateMachine=>awaiter.OnCompleted(state.MoveNext);
            public void AwaitUnsafeOnCompleted<A,S>(ref A awaiter,ref S state) where A:ICriticalNotifyCompletion where S:IAsyncStateMachine=>awaiter.UnsafeOnCompleted(state.MoveNext);
        }
        public readonly struct LayoutSignal{public LayoutSignalAwaiter GetAwaiter()=>default;}
        public readonly struct LayoutSignalAwaiter:INotifyCompletion{public bool IsCompleted=>true;public void GetResult(){}public void OnCompleted(Action action)=>action();}
        public readonly struct LayoutCriticalSignal{public LayoutCriticalAwaiter GetAwaiter()=>default;}
        public readonly struct LayoutCriticalAwaiter:ICriticalNotifyCompletion{public bool IsCompleted=>true;public void GetResult(){}public void OnCompleted(Action action)=>action();public void UnsafeOnCompleted(Action action)=>action();}
        public struct LayoutPayload<T,Tag>{public T Value;}
        public static class LayoutUse{public static void Consume<T,Tag>(LayoutPayload<T,Tag> value){LayoutTrace.Value+="consume;";}}
        public readonly struct LayoutEmptyAwaitable<T>{public LayoutAwaiter<T> GetAwaiter()=>new(default!);}
        public static class LayoutTrace{public static string Value="";}
        """;

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        var normal = Support.Replace("where S:unmanaged,IAsyncStateMachine", "where S:IAsyncStateMachine", StringComparison.Ordinal);
        var safe = normal.Replace("where A:INotifyCompletion where S:IAsyncStateMachine", "where A:INotifyCompletion where S:unmanaged,IAsyncStateMachine", StringComparison.Ordinal);
        var critical = normal.Replace("where A:ICriticalNotifyCompletion where S:IAsyncStateMachine", "where A:ICriticalNotifyCompletion where S:unmanaged,IAsyncStateMachine", StringComparison.Ordinal);
        (string Name, string Body, string Support, string Signature)[] tests =
        [
            ("LayoutKeywordNamespace", "}namespace @event{class Cases{static async LayoutTask<int> M(int value,string ignored){await new LayoutSignal();return value;}}}class Tail{", Support, "global::LayoutTask<T1> M<T1, T2>(T1 value, T2 ignored) where T1 : unmanaged"),
            ("LayoutParameterOnlyStorage", "static async LayoutTask<int> M(LayoutPayload<int,string> value,string ignored){await new LayoutSignal();LayoutUse.Consume(value);return 0;}", Support, "global::LayoutTask<int> M<T1, T2, T3>(global::LayoutPayload<T1, T2> value, T3 ignored) where T1 : unmanaged"),
            ("LayoutAwaiterOnlyStorage", "static async LayoutTask<int> M(LayoutEmptyAwaitable<int> value,string ignored){await value;return 0;}", Support, "global::LayoutTask<int> M<T1, T2>(global::LayoutEmptyAwaitable<T1> value, T2 ignored) where T1 : unmanaged"),
            ("LayoutCapturedValue", "static async LayoutTask<int> M(int value,string ignored){await new LayoutSignal();return value;}", Support, "global::LayoutTask<T1> M<T1, T2>(T1 value, T2 ignored) where T1 : unmanaged"),
            ("LayoutHoistedLocal", "static async LayoutTask<int> M(int value,string ignored){var saved=value;await new LayoutSignal();return saved;}", Support, "global::LayoutTask<T1> M<T1, T2>(T1 value, T2 ignored) where T1 : unmanaged"),
            ("LayoutPhysicalPhantom", "static async LayoutTask<int> M(LayoutPayload<int,string> value,string ignored){await new LayoutSignal();return value.Value;}", Support, "global::LayoutTask<T1> M<T1, T2, T3>(global::LayoutPayload<T1, T2> value, T3 ignored) where T1 : unmanaged"),
            ("LayoutNullableStorage", "static async LayoutTask<int?> M(int? value,string ignored){await new LayoutSignal();return value;}", Support, "global::LayoutTask<T1?> M<T1, T2>(T1? value, T2 ignored) where T1 : unmanaged"),
            ("LayoutAwaiterStorage", "static async LayoutTask<int> M(LayoutTask<int> value,string ignored)=>await value;", Support, "global::LayoutTask<T1> M<T1, T2>(global::LayoutTask<T1> value, T2 ignored) where T1 : unmanaged"),
            ("LayoutSafeConstraint", "static async LayoutTask<int> M(int value,string ignored){await new LayoutSignal();return value;}", safe, "global::LayoutTask<T1> M<T1, T2>(T1 value, T2 ignored) where T1 : unmanaged"),
            ("LayoutUnusedSafeConstraint", "static async LayoutTask<int> M(int value,string ignored){await new LayoutCriticalSignal();return value;}", safe, "global::LayoutTask<T1> M<T1, T2>(T1 value, T2 ignored)"),
            ("LayoutCriticalConstraint", "static async LayoutTask<int> M(int value,string ignored){await new LayoutCriticalSignal();return value;}", critical, "global::LayoutTask<T1> M<T1, T2>(T1 value, T2 ignored) where T1 : unmanaged"),
            ("LayoutUnusedCriticalConstraint", "static async LayoutTask<int> M(int value,string ignored){await new LayoutSignal();return value;}", critical, "global::LayoutTask<T1> M<T1, T2>(T1 value, T2 ignored)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(test.Support + "class Cases{" + test.Body + "}", cancellationToken: cancellationToken)]);
            compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
            using var image = new MemoryStream();
            var original = compilation.Emit(image, cancellationToken: cancellationToken);
            if (!original.Success)
            { throw new InvalidOperationException(test.Name + " fixture emission: " + string.Join("; ", original.Diagnostics)); }
            var result = Program.Results(compilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { failures.Add(test.Name + ": " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS " + test.Name); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        var debug = Program.Compile([CSharpSyntaxTree.ParseText(Support + "class DebugCases{static async LayoutTask<int> M(int value,string ignored){await new LayoutSignal();return value;}}", cancellationToken: cancellationToken)]);
        using var debugImage = new MemoryStream();
        if (debug.Emit(debugImage, cancellationToken: cancellationToken).Success
            || Program.Results(debug, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; }).Status == "Suggestion")
        { throw new InvalidOperationException("An unmanaged builder cannot accept a Debug class state machine."); }
        Program.Log.Info("PASS BuilderLayoutRejectsDebugClassStateMachine");
        var runtimeChecks = await BuilderLayoutRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        return tests.Length + runtimeChecks + 1;
    }
}
