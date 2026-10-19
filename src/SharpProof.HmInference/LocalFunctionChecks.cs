using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class LocalFunctionChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("LocalCapture", "static int M(int value,bool ignored){int Local()=>value;return Local();}", "int M<T1>(int value, T1 ignored)"),
            ("LocalStore", "static int M(int value,bool ignored){void Local(){value=1;}Local();return value;}", "int M<T1>(int value, T1 ignored)"),
            ("LocalCapturedLocal", "static int M(int value,bool ignored){var copy=value;int Local()=>copy;return Local();}", "int M<T1>(int value, T1 ignored)"),
            ("LocalGenericScheme", "static int M(int value,bool ignored){T Id<T>(T v)=>v;return Id(value);}", "T1 M<T1, T2>(T1 value, T2 ignored)"),
            ("LocalRecursiveCapture", "static int M(int value,bool ignored){int Count(int n)=>n==0?value:Count(n-1);return Count(2);}", "int M<T1>(int value, T1 ignored)"),
            ("LocalMutualCapture", "static int M(int value,bool ignored){int Left(int n)=>n==0?value:Right(n-1);int Right(int n)=>Left(n);return Left(2);}", "int M<T1>(int value, T1 ignored)"),
            ("LocalIteratorCapture", "static IEnumerable<int> M(int value,bool ignored){IEnumerable<int> Local(){yield return value;}return Local();}", "global::System.Collections.Generic.IEnumerable<int> M<T1>(int value, T1 ignored)"),
            ("LocalAsyncCapture", "static int M(Task<int> pending,int value){async Task<int> Local()=>await pending;_=Local();return value;}", "T1 M<T1>(global::System.Threading.Tasks.Task<int> pending, T1 value)"),
            ("LocalAsyncVoidCapture", "static int M(Task<int> pending,int value){async void Local(){await pending;}Local();return value;}", "T2 M<T1, T2>(global::System.Threading.Tasks.Task<T1> pending, T2 value)"),
            ("LocalUnsupportedBodyRetainsSuggestion", "static int M(int value){object Local(){var payload=new{Value=value};return payload;}_=Local();return value;}", "T1 M<T1>(T1 value)"),
            ("LocalReturnBoxing", "static int M(int value,bool ignored){object Local()=>value;_=Local();return value;}", "int M<T1>(int value, T1 ignored)"),
            ("LocalInsideIterator", "static IEnumerable<int> M(int value,bool ignored){void Local(){_=value;}Local();yield return value;}", "global::System.Collections.Generic.IEnumerable<T1> M<T1, T2>(T1 value, T2 ignored)"),
            ("LocalIteratorInsideAsync", "static async Task<int> M(Task<int> pending,int value){IEnumerable<int> Local(){yield return value;}_=Local();return await pending;}", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Threading.Tasks.Task<T1> pending, int value)"),
            ("LocalGenericAsync", "static int M(int value,bool ignored){async Task<T> Local<T>(T v){await Task.Yield();return v;}_=Local(value);return value;}", "T1 M<T1, T2>(T1 value, T2 ignored)"),
            ("LocalNestedCapture", "static int M(int value,bool ignored){int Outer(){int Inner()=>value;return Inner();}return Outer();}", "int M<T1>(int value, T1 ignored)"),
            ("LocalVoidExpression", "static int M(int value,bool ignored){void Local()=>LocalApi.Take(value);Local();return value;}", "int M<T1>(int value, T1 ignored)")
        ];
        const string imports = "using System;using System.Collections.Generic;using System.Threading.Tasks;static class LocalApi{public static void Take(int value){}}";
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(imports + "class Cases{" + test.Source + "}", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        return tests.Length + await Runtime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        const string source = """
            using System.Collections.Generic;using System.Threading.Tasks;
            public static class LocalProbe {
                public static string Trace="";
                public static int Capture(int value,bool ignored){int Local()=>value;return Local();}
                public static int Mutate(int value,bool ignored){void Local(){value+=2;}Local();return value;}
                public static int CaptureCopy(int value,bool ignored){var copy=value;int Local()=>copy;return Local();}
                public static int Mutual(int value,bool ignored){int Left(int n)=>n==0?value:Right(n-1);int Right(int n)=>Left(n);return Left(3);}
                public static int Generic(int value){T Local<T>(T x)=>x;return Local(value);}
                public static async Task<int> Async(Task<int> task,int value){async Task<int> Local()=>await task;await Local();return value;}
                public static IEnumerable<int> Iterate(int value,bool ignored){IEnumerable<int> Local(){Trace+="yield;";try{yield return value;}finally{Trace+="dispose;";}}return Local();}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "LocalFunctionRuntime", "LocalFunctionRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("LocalFunctions.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        for (var round = 0; round < 7; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Local-function analyzer crashed."); }
            var diagnostic = diagnostics.First(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001"; });
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        }
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var assembly in new[] { before, after })
        {
            var changed = ReferenceEquals(assembly, after);
            var probe = assembly.GetType("LocalProbe")!;
            foreach (var name in new[] { "Capture", "Mutate", "CaptureCopy", "Mutual" })
            {
                var method = probe.GetMethod(name)!;
                if (changed)
                { method = method.MakeGenericMethod(typeof(string)); }
                var value = method.Invoke(null, changed ? new object[] { 42, "ignored" } : new object[] { 42, false });
                var expected = name == "Mutate" ? 44 : 42;
                if (!Equals(value, expected))
                { throw new InvalidOperationException("Local capture/mutation changed: " + name); }
            }
            var iterate = probe.GetMethod("Iterate")!;
            if (changed)
            { iterate = iterate.MakeGenericMethod(typeof(string)); }
            probe.GetField("Trace")!.SetValue(null, "");
            var sequence = (IEnumerable<int>)iterate.Invoke(null, changed ? new object[] { 42, "ignored" } : new object[] { 42, false })!;
            if (((string)probe.GetField("Trace")!.GetValue(null)!).Length != 0)
            { throw new InvalidOperationException("Local iterator lost laziness."); }
            using (var enumerator = sequence.GetEnumerator())
            { if (!enumerator.MoveNext() || enumerator.Current != 42) { throw new InvalidOperationException("Local iterator lost capture."); } }
            if ((string)probe.GetField("Trace")!.GetValue(null)! != "yield;dispose;")
            { throw new InvalidOperationException("Local iterator lost disposal."); }
        }
        var sentinel = new Uri("https://example.invalid/local-function");
        var generic = after.GetType("LocalProbe")!.GetMethod("Generic")!.MakeGenericMethod(typeof(Uri));
        if (!ReferenceEquals(generic.Invoke(null, new object[] { sentinel }), sentinel))
        { throw new InvalidOperationException("Local scheme lost polymorphism."); }
        var asyncMethod = after.GetType("LocalProbe")!.GetMethod("Async")!.MakeGenericMethod(typeof(Uri));
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = (Task<Uri>)asyncMethod.Invoke(null, new object[] { completion.Task, sentinel })!;
        if (pending.IsCompleted)
        { throw new InvalidOperationException("Local async function lost suspension."); }
        completion.SetResult(42);
        if (!ReferenceEquals(await pending.WaitAsync(cancellationToken).ConfigureAwait(false), sentinel))
        { throw new InvalidOperationException("Local async result lost its domain."); }
        var fault = new InvalidOperationException("local fault");
        try
        { await ((Task<Uri>)asyncMethod.Invoke(null, new object[] { Task.FromException<int>(fault), sentinel })!).WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Local async swallowed fault."); }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, fault)) { }
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cancelled.CancelAsync().ConfigureAwait(false);
        try
        { await ((Task<Uri>)asyncMethod.Invoke(null, new object[] { Task.FromCanceled<int>(cancelled.Token), sentinel })!).WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Local async swallowed cancellation."); }
        catch (OperationCanceledException exception) when (exception.CancellationToken == cancelled.Token) { }
        Program.Log.Info("PASS LocalFunctionCodeFixCaptureMutationIteratorAndAsyncRuntime");
        return 1;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
