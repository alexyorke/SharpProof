using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class DelegateAsyncChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("VoidRoot", "static void M(int x) {}", "Suggestion", "void M<T1>(T1 x)"),
            ("VoidExpression", "static void M(int x)=>Consume(x); static void Consume<T>(T x) {}", "Suggestion", "void M<T1>(T1 x)"),
            ("VoidDiscardGeneric", "static void M(int x)=>Id(x); static T Id<T>(T x)=>x;", "Suggestion", "void M<T1>(T1 x)"),
            ("VoidReturn", "static void M(int x) { return; }", "Suggestion", "void M<T1>(T1 x)"),
            ("VoidRecursive", "static void M(bool again,int x) { if(again) M(false,x); }", "Suggestion", "void M<T1>(bool again, T1 x)"),
            ("ActionInvoke", "static void M(System.Action<int> sink,int x)=>sink(x);", "Suggestion", "void M<T1>(global::System.Action<T1> sink, T1 x)"),
            ("ActionExplicitInvoke", "static void M(System.Action<int> sink,int x)=>sink.Invoke(x);", "Suggestion", "void M<T1>(global::System.Action<T1> sink, T1 x)"),
            ("NonGenericAction", "static void M(System.Action sink,int ignored)=>sink();", "Suggestion", "void M<T1>(global::System.Action sink, T1 ignored)"),
            ("ActionCapture", "static System.Action M(int x)=>()=>Consume(x); static void Consume<T>(T x) {}", "Suggestion", "global::System.Action M<T1>(T1 x)"),
            ("ActionBlockCapture", "static System.Action M(int x)=>()=>{ Consume(x); return; }; static void Consume<T>(T x) {}", "Suggestion", "global::System.Action M<T1>(T1 x)"),
            ("ActionGenericCapture", "static System.Action<int> M(int seed)=>x=>Consume(seed,x); static void Consume<T>(T x,T y) {}", "Suggestion", "global::System.Action<T1> M<T1>(T1 seed)"),
            ("ActionDiscardResult", "static System.Action M(int ignored)=>()=>System.Math.Abs(-1);", "Suggestion", "global::System.Action M<T1>(T1 ignored)"),
            ("AsyncCapturedResult", "static System.Func<Task<int>> M(int x)=>async()=>{ await Task.Yield(); return x; };", "Suggestion", "global::System.Func<global::System.Threading.Tasks.Task<T1>> M<T1>(T1 x)"),
            ("AsyncCapturedValueTask", "static System.Func<ValueTask<int>> M(int x)=>async()=>{ await Task.Yield(); return x; };", "Suggestion", "global::System.Func<global::System.Threading.Tasks.ValueTask<T1>> M<T1>(T1 x)"),
            ("AsyncLambdaInput", "static System.Func<int,Task<int>> M(int seed)=>async x=>{ await Task.Yield(); return Choose(seed,x); }; static T Choose<T>(T x,T y)=>x;", "Suggestion", "global::System.Func<T1, global::System.Threading.Tasks.Task<T1>> M<T1>(T1 seed)"),
            ("AsyncExpressionLambda", "static System.Func<Task<int>> M(Task<int> task)=>async()=>await task;", "Suggestion", "global::System.Func<global::System.Threading.Tasks.Task<T1>> M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("AsyncLambdaNullable", "static System.Func<Task<string?>> M(string? x)=>async()=>{ await Task.Yield(); return x; };", "Suggestion", "global::System.Func<global::System.Threading.Tasks.Task<T1?>> M<T1>(T1? x) where T1 : class"),
            ("AsyncActionLambda", "static System.Action M(Task<int> task)=>async()=>{ await task; };", "Suggestion", "global::System.Action M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("AsyncTaskLambda", "static System.Func<Task> M(Task<int> task)=>async()=>{ await task; };", "Suggestion", "global::System.Func<global::System.Threading.Tasks.Task> M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("SyncLambdaInAsyncRoot", "static async Task<System.Func<int>> M(int x) { await Task.Yield(); return ()=>x; }", "Suggestion", "global::System.Threading.Tasks.Task<global::System.Func<T1>> M<T1>(T1 x)"),
            ("AsyncVoidTaskInput", "static async void M(Task<int> task) { await task; }", "Suggestion", "void M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("TypedAsyncLambda", "static System.Func<int,Task<int>> M(int ignored)=>async (int x)=>{ await Task.Yield(); return x; };", "Suggestion", "global::System.Func<int, global::System.Threading.Tasks.Task<int>> M<T1>(T1 ignored)"),
            ("ExplicitLambdaReturn", "static System.Func<int> M(int x)=>int ()=>x;", "Concrete", null),
            ("CustomDelegateInvoke", "static string M(Mapper<int,string> mapper,int x)=>mapper(x); } delegate R Mapper<T,R>(T x); class Tail {", "Suggestion", "T2 M<T1, T2>(global::Mapper<T1, T2> mapper, T1 x)"),
            ("CustomDelegateLambda", "static Mapper<int,int> M(int seed)=>x=>Choose(seed,x); static T Choose<T>(T x,T y)=>x; } delegate R Mapper<T,R>(T x); class Tail {", "Suggestion", "global::Mapper<T1, T1> M<T1>(T1 seed)"),
            ("CustomNonGenericDelegate", "static IntMap M(int ignored)=>x=>x; } delegate int IntMap(int x); class Tail {", "Suggestion", "global::IntMap M<T1>(T1 ignored)"),
            ("CustomVoidDelegate", "static void M(Sink<int> sink,int x)=>sink(x); } delegate void Sink<T>(T x); class Tail {", "Suggestion", "void M<T1>(global::Sink<T1> sink, T1 x)"),
            ("CustomAsyncDelegate", "static AsyncMap<int,int> M(int seed)=>async x=>{ await Task.Yield(); return Choose(seed,x); }; static T Choose<T>(T x,T y)=>x; } delegate Task<R> AsyncMap<T,R>(T x); class Tail {", "Suggestion", "global::AsyncMap<T1, T1> M<T1>(T1 seed)"),
            ("DelegateConstraint", "static void M(Sink<string> sink,string x)=>sink(x); } delegate void Sink<T>(T x) where T:notnull; class Tail {", "Suggestion", "void M<T1>(global::Sink<T1> sink, T1 x) where T1 : notnull"),
            ("NullableDelegate", "static int M(System.Func<int,int>? f,int x)=>f!(x);", "Suggestion", "T2 M<T1, T2>(global::System.Func<T1, T2>? f, T1 x)"),
            ("AnonymousCapture", "static System.Func<int> M(int x)=>delegate { return x; };", "Suggestion", "global::System.Func<T1> M<T1>(T1 x)"),
            ("AsyncAnonymousCapture", "static System.Func<Task<int>> M(int x)=>async delegate { await Task.Yield(); return x; };", "Suggestion", "global::System.Func<global::System.Threading.Tasks.Task<T1>> M<T1>(T1 x)"),
            ("NaturalLambda", "static int M(int x) { var f=(int y)=>y; return x; }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("MethodGroup", "static System.Func<int,int> M(int ignored)=>Identity; static T Identity<T>(T x)=>x;", "Suggestion", "global::System.Func<int, int> M<T1>(T1 ignored)"),
            ("ExplicitMethodGroup", "static System.Func<int,int> M(int ignored)=>Identity<int>; static T Identity<T>(T x)=>x;", "Suggestion", "global::System.Func<int, int> M<T1>(T1 ignored)"),
            ("InstanceMethodGroup", "static System.Func<int> M(Box<int> box)=>box.Get; } class Box<T> { public T Get()=>default!; } class Tail {", "Suggestion", "global::System.Func<T1> M<T1>(global::Box<T1> box)"),
            ("InheritedMethodGroup", "static System.Func<int[]> M(Derived<int> box)=>box.Get; } class Box<T> { public T Get()=>default!; } class Derived<T>:Box<T[]> {} class Tail {", "Suggestion", "global::System.Func<T1[]> M<T1>(global::Derived<T1> box)"),
            ("FixedInheritedMethodGroup", "static System.Func<int> M(Derived box,int ignored)=>box.Get; } class Box<T> { public T Get()=>default!; } class Derived:Box<int> {} class Tail {", "Suggestion", "global::System.Func<int> M<T1>(global::Derived box, T1 ignored)"),
            ("GenericOwnerStaticGroup", "static System.Func<int> M(int ignored)=>Box<int>.Get; } class Box<T> { public static T Get()=>default!; } class Tail {", "Suggestion", "global::System.Func<int> M<T1>(T1 ignored)"),
            ("VoidMethodGroup", "static System.Action<int> M(int ignored)=>Consume; static void Consume(int x) {}", "Suggestion", "global::System.Action<int> M<T1>(T1 ignored)"),
            ("VarianceMethodGroup", "static System.Func<string,object> M(int ignored)=>Identity; static object Identity(object x)=>x;", "Suggestion", "global::System.Func<string, object> M<T1>(T1 ignored)"),
            ("ExternalMethodGroupCaller", "static int M(int x)=>x; static System.Func<int,int> Use()=>M;", "Suggestion", "T1 M<T1>(T1 x)"),
            ("VoidCaller", "static void M(int x) {} static void Use()=>M(1);", "Suggestion", "void M<T1>(T1 x)"),
            ("ExtensionBoxingCaller", "} static class Extensions { public static void M(this object x) {} } class Consumer { static void Use(){ 1.M(); } } class Tail {", "Rejected", null),
            ("ExtensionObjectCaller", "} static class Extensions { public static void M(this object x) {} } class Consumer { static void Use(object x)=>x.M(); } class Tail {", "Suggestion", "void M<T1>(this T1 x)"),
            ("ExtensionGroupReceiverConversion", "} static class Extensions { public static void M(this object x) {} } class Consumer { static System.Action Use(string x)=>x.M; } class Tail {", "Rejected", null),
            ("RefDelegate", "static int M(RefMap<int> f,int x)=>f(ref x); } delegate T RefMap<T>(ref T x); class Tail {", "Skipped", null)
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("#nullable enable\nusing System.Threading.Tasks; class Cases { " + test.Source + " }", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException($"Invalid fixture {test.Name}: {errors[0]}"); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != test.Status || result.Signature != test.Signature)
            {
                failures.Add($"{test.Name}: expected {test.Status}: {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}");
                continue;
            }
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Analyzer failure: " + test.Name); }
            Program.Log.Info("PASS " + test.Name);
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        var crossFile = Program.Compile([
            CSharpSyntaxTree.ParseText("public static class Api { public static int Identity(int x)=>x; }", path: "Api.cs", cancellationToken: cancellationToken),
            CSharpSyntaxTree.ParseText("class Consumer { static System.Func<int,int> Read()=>Api.Identity; }", path: "Consumer.cs", cancellationToken: cancellationToken)
        ]);
        var crossResult = Program.Results(crossFile, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "Identity"; });
        if (crossResult.Signature != "T1 Identity<T1>(T1 x)")
        { throw new InvalidOperationException("Cross-file method group caller was not preserved."); }
        Program.Log.Info("PASS CrossFileMethodGroupBinding");
        return tests.Length + 1;
    }
}
