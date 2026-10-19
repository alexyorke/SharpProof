using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class CustomBuilderChecks
{
    private const string Imports = "#nullable enable\nusing System; using System.Threading.Tasks; using System.Runtime.CompilerServices;\n";
    private const string Support = """
        public static class BuilderTrace { public static string Value=""; }
        [AsyncMethodBuilder(typeof(ProbeBuilder<>))]
        public readonly struct ProbeTask<T>(Task<T> task) { public TaskAwaiter<T> GetAwaiter()=>task.GetAwaiter(); public Task<T> AsTask()=>task; }
        [AsyncMethodBuilder(typeof(ProbeBuilder))]
        public readonly struct ProbeTask(Task task) { public TaskAwaiter GetAwaiter()=>task.GetAwaiter(); public Task AsTask()=>task; }
        public struct ProbeBuilder<T> {
            private AsyncTaskMethodBuilder<T> inner;
            public static ProbeBuilder<T> Create() { BuilderTrace.Value+="create;"; return new(){inner=AsyncTaskMethodBuilder<T>.Create()}; }
            public ProbeTask<T> Task { get { BuilderTrace.Value+="task;"; return new(inner.Task); } }
            public void SetResult(T value) { BuilderTrace.Value+="result;";inner.SetResult(value); }
            public void SetException(Exception exception) { BuilderTrace.Value+="error;";inner.SetException(exception); }
            public void SetStateMachine(IAsyncStateMachine state) { BuilderTrace.Value+="state;";inner.SetStateMachine(state); }
            public void Start<S>(ref S state) where S:IAsyncStateMachine { BuilderTrace.Value+="start;";inner.Start(ref state); }
            public void AwaitOnCompleted<A,S>(ref A awaiter,ref S state) where A:INotifyCompletion where S:IAsyncStateMachine { BuilderTrace.Value+="safe;";inner.AwaitOnCompleted(ref awaiter,ref state); }
            public void AwaitUnsafeOnCompleted<A,S>(ref A awaiter,ref S state) where A:ICriticalNotifyCompletion where S:IAsyncStateMachine { BuilderTrace.Value+="unsafe;";inner.AwaitUnsafeOnCompleted(ref awaiter,ref state); }
        }
        public struct ProbeBuilder {
            private AsyncTaskMethodBuilder inner;
            public static ProbeBuilder Create() { BuilderTrace.Value+="create;"; return new(){inner=AsyncTaskMethodBuilder.Create()}; }
            public ProbeTask Task { get { BuilderTrace.Value+="task;";return new(inner.Task); } }
            public void SetResult() { BuilderTrace.Value+="result;";inner.SetResult(); }
            public void SetException(Exception exception) { BuilderTrace.Value+="error;";inner.SetException(exception); }
            public void SetStateMachine(IAsyncStateMachine state) { BuilderTrace.Value+="state;";inner.SetStateMachine(state); }
            public void Start<S>(ref S state) where S:IAsyncStateMachine { BuilderTrace.Value+="start;";inner.Start(ref state); }
            public void AwaitOnCompleted<A,S>(ref A awaiter,ref S state) where A:INotifyCompletion where S:IAsyncStateMachine { BuilderTrace.Value+="safe;";inner.AwaitOnCompleted(ref awaiter,ref state); }
            public void AwaitUnsafeOnCompleted<A,S>(ref A awaiter,ref S state) where A:ICriticalNotifyCompletion where S:IAsyncStateMachine { BuilderTrace.Value+="unsafe;";inner.AwaitUnsafeOnCompleted(ref awaiter,ref state); }
        }
        public readonly struct SafeTask<T>(Task<T> task) { public SafeAwaiter<T> GetAwaiter()=>new(task.GetAwaiter()); }
        public readonly struct SafeAwaiter<T>(TaskAwaiter<T> awaiter):INotifyCompletion {
            public bool IsCompleted=>awaiter.IsCompleted;
            public T GetResult()=>awaiter.GetResult();
            public void OnCompleted(Action action)=>awaiter.OnCompleted(action);
        }
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        var constrained = Support.Replace("struct ProbeBuilder<T> {", "struct ProbeBuilder<T> where T:struct {", StringComparison.Ordinal);
        var fixedResult = Support.Replace("public ProbeTask<T> Task", "public ProbeTask<int> Task", StringComparison.Ordinal)
            .Replace("return new(inner.Task);", "return default;", StringComparison.Ordinal);
        var nested = Support.Replace("typeof(ProbeBuilder", "typeof(BuilderOwner.ProbeBuilder", StringComparison.Ordinal)
            .Replace("public struct ProbeBuilder<T>", "public static class BuilderOwner { public struct ProbeBuilder<T>", StringComparison.Ordinal)
            .Replace("public readonly struct SafeTask<T>", "} public readonly struct SafeTask<T>", StringComparison.Ordinal);
        var standardOverride = Support.Replace("public ProbeTask<T> Task", "public Task<T> Task", StringComparison.Ordinal)
            .Replace("return new(inner.Task);", "return inner.Task;", StringComparison.Ordinal)
            .Replace("public ProbeTask Task", "public Task Task", StringComparison.Ordinal);
        var invalidResult = Support.Replace("SetResult(T value)", "SetResult(object value)", StringComparison.Ordinal)
            .Replace("inner.SetResult(value);", "inner.SetResult(default!);", StringComparison.Ordinal);
        var overloaded = Support.Replace("public void SetResult(T value)", "public void SetResult(int value) { BuilderTrace.Value+=\"wrong;\"; } public void SetResult(T value)", StringComparison.Ordinal);
        var extraAwaiter = "public interface IExtra<T> {}" + Support.Replace("SafeAwaiter<T>(TaskAwaiter<T> awaiter):INotifyCompletion", "SafeAwaiter<T>(TaskAwaiter<T> awaiter):INotifyCompletion,IExtra<T>", StringComparison.Ordinal)
            .Replace("where A:INotifyCompletion where S", "where A:INotifyCompletion,IExtra<int> where S", StringComparison.Ordinal)
            .Replace("where A:ICriticalNotifyCompletion where S", "where A:ICriticalNotifyCompletion,IExtra<int> where S", StringComparison.Ordinal);
        var builderEnd = extraAwaiter.IndexOf("public struct ProbeBuilder {", StringComparison.Ordinal);
        var tiedAwaiter = extraAwaiter[..builderEnd].Replace("IExtra<int>", "IExtra<T>", StringComparison.Ordinal) + extraAwaiter[builderEnd..];
        var unsafeAwaiter = extraAwaiter.Replace(":INotifyCompletion,IExtra<T>", ":ICriticalNotifyCompletion,IExtra<T>", StringComparison.Ordinal)
            .Replace("public void OnCompleted(Action action)=>awaiter.OnCompleted(action);", "public void OnCompleted(Action action)=>awaiter.OnCompleted(action);public void UnsafeOnCompleted(Action action)=>awaiter.UnsafeOnCompleted(action);", StringComparison.Ordinal);
        const string stream = """
            public class BuilderStream<T> {
                public Enumerator GetAsyncEnumerator()=>new();
                public class Enumerator {
                    public T Current=>default!;
                    public MoveAwaitable<T> MoveNextAsync()=>new();
                }
            }
            public readonly struct MoveAwaitable<T> {public MoveAwaiter<T> GetAwaiter()=>new();}
            public readonly struct MoveAwaiter<T>:INotifyCompletion,IExtra<T> {
                public bool IsCompleted=>true;public bool GetResult()=>false;public void OnCompleted(Action action)=>action();
            }
            """;
        (string Name, string Source, string Support, string Status, string? Signature)[] tests =
        [
            ("DiscardAssignmentValue", "static int M(int x)=>(_=x);", Support, "Suggestion", "T1 M<T1>(T1 x)"),
            ("DiscardAssignmentStatement", "static void M(int x) { _=x; }", Support, "Suggestion", "void M<T1>(T1 x)"),
            ("UnderscoreLocalIsNotDiscard", "static int M(int x) { int _=0;_=x;return _; }", Support, "Concrete", null),
            ("CustomBuilderValue", "static async ProbeTask<int> M(int x)=>x;", Support, "Suggestion", "global::ProbeTask<T1> M<T1>(T1 x)"),
            ("CustomBuilderTaskAwait", "static async ProbeTask<int> M(Task<int> task)=>await task;", Support, "Suggestion", "global::ProbeTask<T1> M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("CustomBuilderOwnAwait", "static async ProbeTask<int> M(ProbeTask<int> task)=>await task;", Support, "Suggestion", "global::ProbeTask<T1> M<T1>(global::ProbeTask<T1> task)"),
            ("CustomBuilderNullableValue", "static async ProbeTask<int?> M(int? x)=>x;", Support, "Suggestion", "global::ProbeTask<T1?> M<T1>(T1? x) where T1 : struct"),
            ("CustomBuilderNullableReference", "static async ProbeTask<string?> M(string? x)=>x;", Support, "Suggestion", "global::ProbeTask<T1?> M<T1>(T1? x) where T1 : class"),
            ("CustomBuilderWidening", "static async ProbeTask<long> M(int x,bool ignored)=>x;", Support, "Suggestion", "global::ProbeTask<long> M<T1>(int x, T1 ignored)"),
            ("CustomBuilderNoResult", "static async ProbeTask M(Task<int> task) { await task; return; }", Support, "Suggestion", "global::ProbeTask M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("CustomBuilderStructConstraint", "static async ProbeTask<int> M(int x)=>x;", constrained, "Suggestion", "global::ProbeTask<T1> M<T1>(T1 x) where T1 : struct"),
            ("CustomBuilderFixedProperty", "static async ProbeTask<int> M(int x,bool ignored)=>x;", fixedResult, "Suggestion", "global::ProbeTask<int> M<T1>(int x, T1 ignored)"),
            ("CustomBuilderNested", "static async ProbeTask<int> M(int x)=>x;", nested, "Suggestion", "global::ProbeTask<T1> M<T1>(T1 x)"),
            ("CustomBuilderClass", "static async ProbeTask<int> M(int x)=>x;", Support.Replace("struct ProbeBuilder", "class ProbeBuilder", StringComparison.Ordinal), "Suggestion", "global::ProbeTask<T1> M<T1>(T1 x)"),
            ("CustomBuilderMethodOverride", "[AsyncMethodBuilder(typeof(ProbeBuilder<>))] static async Task<int> M(int x)=>x;", standardOverride, "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(T1 x)"),
            ("CustomBuilderConstrainedOverride", "[AsyncMethodBuilder(typeof(ProbeBuilder<>))] static async Task<int> M(int x)=>x;", standardOverride.Replace("struct ProbeBuilder<T> {", "struct ProbeBuilder<T> where T:struct {", StringComparison.Ordinal), "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(T1 x) where T1 : struct"),
            ("CustomBuilderLambda", "static Func<ProbeTask<int>> M(int x)=>async()=> { await Task.Yield(); return x; };", Support, "Suggestion", "global::System.Func<global::ProbeTask<T1>> M<T1>(T1 x)"),
            ("CustomBuilderVoidLambda", "static Func<ProbeTask> M(Task<int> task)=>async()=> { await task; };", Support, "Suggestion", "global::System.Func<global::ProbeTask> M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("CustomBuilderLambdaState", "static async ProbeTask<int> M(int x) { Func<Task<int>> nested=async()=>x; return await nested(); }", Support, "Concrete", null),
            ("CustomBuilderOverloadedSetResult", "static async ProbeTask<int> M(int x)=>x;", overloaded, "Suggestion", "global::ProbeTask<T1> M<T1>(T1 x)"),
            ("AsyncVoidBuilderAttributeIgnored", "[AsyncMethodBuilder(typeof(ProbeBuilder))] static async void M(int x,Task task) { await task; }", Support, "Suggestion", "void M<T1>(T1 x, global::System.Threading.Tasks.Task task)"),
            ("CustomBuilderInvalidGenericConstraint", "static async ProbeTask<T> M<T>(T x,bool ignored)=>x;", constrained, "Skipped", null),
            ("CustomBuilderAwaiterConstraintGuard", "static async ProbeTask<int> M(SafeTask<int> task) { await task;return 1; }", extraAwaiter, "Concrete", null),
            ("CustomBuilderAwaiterKeepsUnrelated", "static async ProbeTask<int> M(SafeTask<int> task,int x) {await task;return x;}", extraAwaiter, "Suggestion", "global::ProbeTask<T1> M<T1>(global::SafeTask<int> task, T1 x)"),
            ("CustomBuilderVoidAwaiterKeepsUnrelated", "static async ProbeTask M(SafeTask<int> task,int x){await task;_=x;}", extraAwaiter, "Suggestion", "global::ProbeTask M<T1>(global::SafeTask<int> task, T1 x)"),
            ("CustomBuilderAwaiterTiedResult", "static async ProbeTask<int> M(SafeTask<int> task,int x) {await task;return x;}", tiedAwaiter, "Suggestion", "global::ProbeTask<T1> M<T1>(global::SafeTask<T1> task, T1 x)"),
            ("CustomBuilderUnsafeAwaiterKeepsUnrelated", "static async ProbeTask<int> M(SafeTask<int> task,int x) {await task;return x;}", unsafeAwaiter, "Suggestion", "global::ProbeTask<T1> M<T1>(global::SafeTask<int> task, T1 x)"),
            ("CustomBuilderImplicitMoveConstraint", "static async ProbeTask<int> M(BuilderStream<int> stream,int x){await foreach(var item in stream){}return x;}", extraAwaiter + stream, "Suggestion", "global::ProbeTask<T1> M<T1>(global::BuilderStream<int> stream, T1 x)"),
            ("CustomBuilderImplicitMoveTiesResult", "static async ProbeTask<int> M(BuilderStream<int> stream,int x){await foreach(var item in stream){}return x;}", tiedAwaiter + stream, "Suggestion", "global::ProbeTask<T1> M<T1>(global::BuilderStream<T1> stream, T1 x)"),
            ("CustomBuilderLambdaAwaiterConstraint", "static Func<ProbeTask<int>> M(SafeTask<int> task,int x)=>async()=>{await task;return x;};", extraAwaiter, "Suggestion", "global::System.Func<global::ProbeTask<T1>> M<T1>(global::SafeTask<int> task, T1 x)"),
            ("CustomBuilderRestoresStandardOuter", "static async Task<int> M(SafeTask<int> task,int x){Func<ProbeTask<int>> nested=async()=>{await task;return 1;};await nested();return x;}", extraAwaiter, "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::SafeTask<int> task, T1 x)"),
            ("CustomBuilderRestoresCustomOuter", "static async ProbeTask<int> M(SafeTask<int> task,int x){Func<Task<int>> nested=async()=>1;await task;return x;}", extraAwaiter, "Suggestion", "global::ProbeTask<T1> M<T1>(global::SafeTask<int> task, T1 x)"),
            ("CustomBuilderNestedLocalEmission", "static int M(SafeTask<int> task,int x) { async ProbeTask<int> Local(){await task;return 1;} _=Local();return x; }", extraAwaiter, "Suggestion", "T1 M<T1>(global::SafeTask<int> task, T1 x)"),
            ("CustomBuilderInvalidSetResult", "static async ProbeTask<int> M(int x,bool ignored)=>x;", invalidResult, "Skipped", null),
            ("CustomBuilderInvalidConstraint", "static async ProbeTask<string> M(string x,bool ignored)=>x;", constrained, "Skipped", null)
        ];
        foreach (var test in tests)
        {
            var tree = CSharpSyntaxTree.ParseText(Imports + test.Support + "\nclass Cases {" + test.Source + "}", cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != test.Status || result.Signature != test.Signature)
            { throw new InvalidOperationException($"{test.Name}: expected {test.Status}: {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}"); }
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Builder analyzer crashed: " + test.Name); }
            Program.Log.Info("PASS " + test.Name);
        }
        (string Spelling, string Error)[] excluded = [("BuilderOwner<>.ProbeBuilder<>", "CS8940"), ("BuilderOwner<int>.ProbeBuilder<>", "CS7003"), ("BuilderOwner<int>.ProbeBuilder<int>", "CS8940")];
        foreach (var item in excluded)
        {
            var unsupported = nested.Replace("class BuilderOwner {", "class BuilderOwner<OwnerT> {", StringComparison.Ordinal)
                .Replace("typeof(BuilderOwner.ProbeBuilder<>)", "typeof(" + item.Spelling + ")", StringComparison.Ordinal)
                .Replace("typeof(BuilderOwner.ProbeBuilder)", "typeof(BuilderOwner<int>.ProbeBuilder)", StringComparison.Ordinal);
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(Imports + unsupported + "class InvalidNested {static async ProbeTask<int> M(int x)=>x;}", cancellationToken: cancellationToken)]);
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
            if (emitted.Success || !emitted.Diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error && d.Id == item.Error; }))
            { throw new InvalidOperationException("Compiler nested-builder exclusion changed: " + item.Spelling); }
            Program.Log.Info("PASS CompilerExcludesGenericEnclosingBuilder " + item.Spelling);
        }
        var memberTree = CSharpSyntaxTree.ParseText(Imports + Support.Replace("inner.SetException(exception);", "inner.SetException(new Exception());", StringComparison.Ordinal)
            + "class MemberUse {static async ProbeTask<int> M(int x)=>x;}", cancellationToken: cancellationToken);
        var memberCompilation = Program.Compile([memberTree]);
        var member = (await memberTree.GetRootAsync(cancellationToken).ConfigureAwait(false)).DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First(m =>
        {
            ArgumentNullException.ThrowIfNull(m);
            return m.Identifier.ValueText == "SetException";
        });
        var memberResult = Inference.Analyze(member, memberCompilation.GetSemanticModel(memberTree), cancellationToken);
        if (memberResult.Status != "Concrete")
        { throw new InvalidOperationException("A compiler-required builder member was generalized: " + memberResult.Message); }
        Program.Log.Info("PASS CustomBuilderImplicitMemberProtection");
        var unusedTree = CSharpSyntaxTree.ParseText(Imports + Support.Replace("inner.SetException(exception);", "inner.SetException(new Exception());", StringComparison.Ordinal), cancellationToken: cancellationToken);
        var unusedCompilation = Program.Compile([unusedTree]);
        var unusedMember = (await unusedTree.GetRootAsync(cancellationToken).ConfigureAwait(false)).DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First(m => { ArgumentNullException.ThrowIfNull(m); return m.Identifier.ValueText == "SetException"; });
        if (Inference.Analyze(unusedMember, unusedCompilation.GetSemanticModel(unusedTree), cancellationToken).Status != "Concrete")
        { throw new InvalidOperationException("An unused task-like type lost its builder contract."); }
        Program.Log.Info("PASS CustomBuilderUnusedTypeContract");
        return tests.Length + excluded.Length + 2 + await Runtime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        var genericStart = Support.IndexOf("public struct ProbeBuilder<T>", StringComparison.Ordinal);
        var genericEnd = Support.IndexOf("public struct ProbeBuilder {", StringComparison.Ordinal);
        var overrideBuilder = Support[genericStart..genericEnd].Replace("ProbeBuilder", "OverrideBuilder", StringComparison.Ordinal)
            .Replace("public ProbeTask<T> Task", "public Task<T> Task", StringComparison.Ordinal)
            .Replace("return new(inner.Task);", "return inner.Task;", StringComparison.Ordinal);
        var runtimeSupport = "public interface IExtra<T>{}" + Support.Replace("SafeAwaiter<T>(TaskAwaiter<T> awaiter):INotifyCompletion", "SafeAwaiter<T>(TaskAwaiter<T> awaiter):INotifyCompletion,IExtra<T>", StringComparison.Ordinal);
        var fixedBuilder = overrideBuilder.Replace("OverrideBuilder", "FixedAwaitBuilder", StringComparison.Ordinal)
            .Replace("where A:INotifyCompletion where S", "where A:INotifyCompletion,IExtra<int> where S", StringComparison.Ordinal)
            .Replace("where A:ICriticalNotifyCompletion where S", "where A:ICriticalNotifyCompletion,IExtra<int> where S", StringComparison.Ordinal);
        var tiedBuilder = fixedBuilder.Replace("FixedAwaitBuilder", "TiedAwaitBuilder", StringComparison.Ordinal).Replace("IExtra<int>", "IExtra<T>", StringComparison.Ordinal);
        var source = Imports + runtimeSupport + overrideBuilder + fixedBuilder + tiedBuilder + """
            public readonly struct CriticalTask<T>(Task<T> task) {public CriticalAwaiter<T> GetAwaiter()=>new(task.GetAwaiter());}
            public readonly struct CriticalAwaiter<T>(TaskAwaiter<T> awaiter):ICriticalNotifyCompletion,IExtra<T> {
                public bool IsCompleted=>awaiter.IsCompleted;public T GetResult()=>awaiter.GetResult();
                public void OnCompleted(Action action)=>awaiter.OnCompleted(action);
                public void UnsafeOnCompleted(Action action)=>awaiter.UnsafeOnCompleted(action);
            }
            public static class BuilderProbe {
                [AsyncMethodBuilder(typeof(FixedAwaitBuilder<>))] public static async Task<int> FixedUnsafe(CriticalTask<int> task,int value){await task;return value;}
                [AsyncMethodBuilder(typeof(TiedAwaitBuilder<>))] public static async Task<int> TiedUnsafe(CriticalTask<int> task,int value){await task;return value;}
                [AsyncMethodBuilder(typeof(FixedAwaitBuilder<>))] public static async Task<int> Fixed(SafeTask<int> task,int value){await task;return value;}
                [AsyncMethodBuilder(typeof(TiedAwaitBuilder<>))] public static async Task<int> Tied(SafeTask<int> task,int value){await task;return value;}
                [AsyncMethodBuilder(typeof(OverrideBuilder<>))] /* builder retained */ public static async Task<int> Override(Task<int> task)=>await task;
                [AsyncMethodBuilder(typeof(ProbeBuilder))] public static async void Void(int ignored,Task task,TaskCompletionSource signal) { await task;signal.SetResult(); }
                public static async ProbeTask<int> Echo(Task<int> task)=>await task;
                public static async ProbeTask<int> SafeEcho(SafeTask<int> task)=>await task;
                public static async ProbeTask Ignore(Task<int> task) { await task; }
                public static async ProbeTask<int?> Nullable(Task<int?> task)=>await task;
                public static Func<ProbeTask<int>> Capture(int value)=>async()=> { await Task.Yield(); return value; };
                public static async ProbeTask<int> Guarded(Task<int> task) { try { return await task; } finally { BuilderTrace.Value+="finally;"; } }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "BuilderRuntime", "BuilderRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("BuilderRuntime.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var before = Load(original, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 16; round++)
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
            fixes++;
        }
        if (fixes != 12)
        { throw new InvalidOperationException("Expected twelve custom-builder fixes; got " + fixes); }
        var changed = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var after = Load(changed, cancellationToken);
        foreach (var method in new[] { "Echo", "SafeEcho", "Ignore", "Guarded", "Override", "Fixed", "Tied", "FixedUnsafe", "TiedUnsafe" })
        {
            var baseline = await Execute(before, method, false, false, cancellationToken).ConfigureAwait(false);
            var fixedResult = await Execute(after, method, true, false, cancellationToken).ConfigureAwait(false);
            if (baseline != fixedResult)
            { throw new InvalidOperationException("Builder protocol trace changed for " + method + ": " + baseline + " -> " + fixedResult); }
            var pendingBefore = await Execute(before, method, false, true, cancellationToken).ConfigureAwait(false);
            var pendingAfter = await Execute(after, method, true, true, cancellationToken).ConfigureAwait(false);
            if (pendingBefore != pendingAfter)
            { throw new InvalidOperationException("Suspended builder protocol changed for " + method); }
        }
        foreach (var suspend in new[] { false, true })
        {
            var beforeVoid = await ExecuteVoid(before, false, suspend, cancellationToken).ConfigureAwait(false);
            var afterVoid = await ExecuteVoid(after, true, suspend, cancellationToken).ConfigureAwait(false);
            if (beforeVoid != afterVoid)
            { throw new InvalidOperationException("Async void builder override changed its protocol."); }
        }
        foreach (var method in new[] { "Echo", "SafeEcho", "Ignore", "Guarded", "Override", "Fixed", "Tied", "FixedUnsafe", "TiedUnsafe" })
        {
            foreach (var failure in new[] { 1, 2 })
            {
                foreach (var suspend in new[] { false, true })
                {
                    var baseline = await Execute(before, method, false, suspend, cancellationToken, failure).ConfigureAwait(false);
                    var fixedTrace = await Execute(after, method, true, suspend, cancellationToken, failure).ConfigureAwait(false);
                    if (baseline != fixedTrace)
                    { throw new InvalidOperationException("Fault/cancellation builder protocol changed for " + method); }
                }
            }
        }
        var probe = after.GetType("BuilderProbe")!;
        var echo = probe.GetMethod("Echo")!.MakeGenericMethod(typeof(string));
        var work = echo.Invoke(null, new object[] { Task.FromResult("generic") })!;
        if (await Result<string>(work, cancellationToken).ConfigureAwait(false) != "generic")
        { throw new InvalidOperationException("Generic custom task lost its result."); }
        var sentinel = new Uri("https://example.invalid/builder-constraints");
        foreach (var method in new[] { "Fixed", "Tied", "FixedUnsafe", "TiedUnsafe" })
        {
            var element = method.StartsWith("Fixed", StringComparison.Ordinal) ? typeof(int) : typeof(Uri);
            var wrapper = after.GetType(method.EndsWith("Unsafe", StringComparison.Ordinal) ? "CriticalTask`1" : "SafeTask`1")!.MakeGenericType(element);
            object task = element == typeof(int) ? Task.FromResult(42) : Task.FromResult(sentinel);
            var input = Activator.CreateInstance(wrapper, task)!;
            var generic = probe.GetMethod(method)!.MakeGenericMethod(typeof(Uri));
            var value = await Result<Uri>(generic.Invoke(null, new[] { input, sentinel })!, cancellationToken).ConfigureAwait(false);
            if (!ReferenceEquals(value, sentinel))
            { throw new InvalidOperationException("Custom-builder awaiter constraints lost the result domain: " + method); }
        }
        Program.Log.Info("PASS CustomBuilderAwaiterConstraintNewDomains");
        var guid = Guid.NewGuid();
        var nullableWork = probe.GetMethod("Nullable")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { Task.FromResult<Guid?>(guid) })!;
        if (await Result<Guid?>(nullableWork, cancellationToken).ConfigureAwait(false) != guid)
        { throw new InvalidOperationException("Custom builder lost nullable result."); }
        var factory = (Delegate)probe.GetMethod("Capture")!.MakeGenericMethod(typeof(string)).Invoke(null, new object[] { "capture" })!;
        if (await Result<string>(factory.DynamicInvoke()!, cancellationToken).ConfigureAwait(false) != "capture")
        { throw new InvalidOperationException("Custom async lambda lost its capture."); }
        var marker = new InvalidOperationException("builder fault");
        try
        { await Result<string>(echo.Invoke(null, new object[] { Task.FromException<string>(marker) })!, cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Builder swallowed fault."); }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, marker)) { }
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cancelled.CancelAsync().ConfigureAwait(false);
        try
        { await Result<string>(echo.Invoke(null, new object[] { Task.FromCanceled<string>(cancelled.Token) })!, cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Builder swallowed cancellation."); }
        catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
        Program.Log.Info("PASS CustomBuilderCodeFixRuntime");
        Program.Log.Info("PASS CustomBuilderOriginalAndFixedProtocolTraces");
        Program.Log.Info("PASS CustomBuilderSuspensionFaultsAndCancellation");
        Program.Log.Info("PASS CustomBuilderFailureProtocolTraces");
        return 5;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var problems = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (problems.Length != 0)
        { throw new InvalidOperationException(string.Join("\n", problems.Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }

    private static async Task<string> Execute(System.Reflection.Assembly assembly, string method, bool generic, bool suspend, CancellationToken cancellationToken, int failure = 0)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(method);
        var trace = assembly.GetType("BuilderTrace")!.GetField("Value")!;
        trace.SetValue(null, "");
        if (failure is < 0 or > 2)
        { throw new ArgumentOutOfRangeException(nameof(failure)); }
        using var failedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (failure == 2)
        { await failedCancellation.CancelAsync().ConfigureAwait(false); }
        var marker = new InvalidOperationException("protocol failure");
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Complete()
        {
            if (failure == 1)
            { completion.SetException(marker); }
            else if (failure == 2)
            { completion.SetCanceled(failedCancellation.Token); }
            else
            { completion.SetResult(42); }
        }
        if (!suspend)
        { Complete(); }
        object input = completion.Task;
        if (method is "SafeEcho" or "Fixed" or "Tied")
        { input = Activator.CreateInstance(assembly.GetType("SafeTask`1")!.MakeGenericType(typeof(int)), input)!; }
        if (method is "FixedUnsafe" or "TiedUnsafe")
        { input = Activator.CreateInstance(assembly.GetType("CriticalTask`1")!.MakeGenericType(typeof(int)), input)!; }
        var target = assembly.GetType("BuilderProbe")!.GetMethod(method)!;
        if (generic)
        { target = target.MakeGenericMethod(typeof(int)); }
        var work = target.Invoke(null, method is "Fixed" or "Tied" or "FixedUnsafe" or "TiedUnsafe" ? new object[] { input, 42 } : new[] { input })!;
        var pending = method == "Ignore" ? ResultVoid(work, cancellationToken) : Result<int>(work, cancellationToken);
        if (suspend)
        {
            if (pending.IsCompleted)
            { throw new InvalidOperationException("Custom builder completed before its input."); }
            Complete();
        }
        var observed = 0;
        try
        { await pending.ConfigureAwait(false); }
        catch (InvalidOperationException exception) when (failure == 1 && ReferenceEquals(exception, marker)) { observed = 1; }
        catch (OperationCanceledException exception) when (failure == 2 && exception.CancellationToken == failedCancellation.Token) { observed = 2; }
        cancellationToken.ThrowIfCancellationRequested();
        if (observed != failure)
        { throw new InvalidOperationException("Custom builder changed the failure outcome."); }
        return (string)trace.GetValue(null)!;
    }

    private static async Task<string> ExecuteVoid(System.Reflection.Assembly assembly, bool generic, bool suspend, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var trace = assembly.GetType("BuilderTrace")!;
        trace.GetField("Value")!.SetValue(null, "");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!suspend)
        { gate.SetResult(); }
        var method = assembly.GetType("BuilderProbe")!.GetMethod("Void")!;
        if (generic)
        { method = method.MakeGenericMethod(typeof(string)); }
        method.Invoke(null, generic ? new object[] { "ignored", gate.Task, signal } : new object[] { 1, gate.Task, signal });
        var completion = signal.Task;
        if (suspend)
        {
            if (completion.IsCompleted)
            { throw new InvalidOperationException("Async void builder did not suspend."); }
            gate.SetResult();
        }
        await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (((string)trace.GetField("Value")!.GetValue(null)!).Length != 0)
        { throw new InvalidOperationException("An async void method used a custom builder attribute."); }
        return (string)trace.GetField("Value")!.GetValue(null)!;
    }
    private static async Task<T> Result<T>(object work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        var task = work is Task<T> direct ? direct : (Task<T>)work.GetType().GetMethod("AsTask")!.Invoke(work, null)!;
        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ResultVoid(object work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        var task = work is Task direct ? direct : (Task)work.GetType().GetMethod("AsTask")!.Invoke(work, null)!;
        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
