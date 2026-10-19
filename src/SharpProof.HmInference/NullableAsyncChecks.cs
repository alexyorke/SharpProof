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
            ("NullableNullPattern", "static bool M(string? x,bool ignored)=>x is null;", "Suggestion", "bool M<T1>(string? x, T1 ignored)"),
            ("NullableNotNullPattern", "static bool M(string? x,bool ignored)=>x is not null;", "Suggestion", "bool M<T1>(string? x, T1 ignored)"),
            ("NullableConditionalMember", "static int? M(string? x,bool ignored)=>x?.Length;", "Suggestion", "int? M<T1>(string? x, T1 ignored)"),
            ("NullableConditionalIndexer", "static int? M(int[]? x,int i,bool ignored)=>x?[i];", "Suggestion", "T1? M<T1, T2>(T1[]? x, int i, T2 ignored) where T1 : struct"),
            ("NullableConditionalCall", "static string? M(string? x,bool ignored)=>x?.Trim();", "Suggestion", "string? M<T1>(string? x, T1 ignored)"),
            ("NullableCoalesceAssignment", "static string M(string? x,string y,bool ignored) { x??=y; return x; }", "Suggestion", "T1 M<T1, T2>(T1? x, T1 y, T2 ignored) where T1 : class"),
            ("NullableCoalesceAssignmentResult", "static string M(string? x,string y)=>x??=y;", "Suggestion", "T1 M<T1>(T1? x, T1 y) where T1 : class"),
            ("NullableCoalesceAssignmentValue", "static int M(int? x,int y)=>x??=y;", "Suggestion", "T1 M<T1>(T1? x, T1 y) where T1 : struct"),
            ("NullableCoalesceAssignmentBoth", "static int? M(int? x,int? y)=>x??=y;", "Suggestion", "T1? M<T1>(T1? x, T1? y) where T1 : struct"),
            ("NullableCoalesceAssignmentConversion", "static long M(long? x,int y,bool ignored)=>x??=y;", "Suggestion", "long M<T1>(long? x, int y, T1 ignored)"),            ("NullableWideningCoalesce", "static long M(int? x,long y,bool ignored)=>x??y;", "Suggestion", "long M<T1>(int? x, long y, T1 ignored)"),
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
            ("NullableConditionalReferenceCoalesce", "static string M(string[]? x,int i,string fallback)=>x?[i]??fallback;", "Suggestion", "T1 M<T1>(T1[]? x, int i, T1 fallback) where T1 : class"),            ("AsyncGenericCustomAwaiter", "static async Task<int> M(AwaitBox<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::AwaitBox<T1> task)"),
            ("AsyncInheritedCustomAwaiter", "static async Task<int> M(DerivedAwaitBox<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::DerivedAwaitBox<T1> task)"),
            ("AsyncNullableCustomAwaiter", "static async Task<int?> M(AwaitBox<int?> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1?> M<T1>(global::AwaitBox<T1?> task) where T1 : struct"),
            ("AsyncReferenceCustomAwaiter", "static async Task<string?> M(AwaitBox<string?> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1?> M<T1>(global::AwaitBox<T1?> task) where T1 : class"),
            ("AsyncExtensionAwaiter", "static async Task<int> M(ExtensionAwaitBox<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::ExtensionAwaitBox<T1> task)"),
            ("AsyncConstrainedExtensionAwaiter", "static async Task<int> M(StructAwaitBox<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::StructAwaitBox<T1> task) where T1 : struct"),
            ("AsyncExtensionOverloadRetained", "static async Task<int> M(OverloadAwaitBox<int> task)=>await task;", "Concrete", null),            ("AsyncNestedAwaiterRetained", "static async Task<int> M(NestedAwaitBox<int> task,bool ignored)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<int> M<T1>(global::NestedAwaitBox<int> task, T1 ignored)"),            ("AsyncOwnAwaiter", "static async Task<int> M(OwnAwaitBox<int> task)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::OwnAwaitBox<T1> task)"),
            ("AsyncAwaiterShadowingRejected", "static async Task<int> M(ExplicitAwaitSource task)=>await task;", "Rejected", null),            ("AsyncTypedLocal", "static async Task<int> M(Task<int> task) { int x=await task; return x; }", "Concrete", null),
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
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("#nullable enable\nusing System.Threading.Tasks; class Cases { " + test.Source + " }" + AwaitFixtures, cancellationToken: cancellationToken)]);
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
        return tests.Length + 1 + await CodeFixRuntime(cancellationToken).ConfigureAwait(false);
    }

    private const string AwaitFixtures = """
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
        }        class OwnAwaitBox<T> { public OwnAwaiter<T> GetAwaiter()=>new(); }
        struct OwnAwaiter<T>:System.Runtime.CompilerServices.INotifyCompletion {
            public bool IsCompleted=>true;
            public T GetResult()=>default!;
            public void OnCompleted(System.Action action)=>action();
        }
        interface IAwaitSource { System.Runtime.CompilerServices.TaskAwaiter<int> GetAwaiter(); }
        class ExplicitAwaitSource:IAwaitSource {
            System.Runtime.CompilerServices.TaskAwaiter<int> IAwaitSource.GetAwaiter()=>System.Threading.Tasks.Task.FromResult(1).GetAwaiter();
        }        static class AwaitExtensions {
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
            public static class NullableAsyncProbe {
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
        for (var round = 0; round < 24; round++)
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
        if (applied != 19)
        { throw new InvalidOperationException($"Expected nineteen nullable/async/lifetime code fixes; applied {applied}."); }
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
        if (!Equals(probe.GetMethod("Resource")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { resource, guid }), guid)
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
        return 5;
    }
}
