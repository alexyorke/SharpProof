using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class ProtocolEnumerationChecks
{
    private static readonly int[] RuntimeValues = [42, 7];
    private const string Support = """
        #nullable enable
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        public static class ProtocolTrace {public static string Value="";}
        public class ExtensionSource<T> {public T[] Values=[];}
        public class DerivedExtensionSource<T>:ExtensionSource<T> {}
        public class StructExtensionSource<T>:ExtensionSource<T> {}
        public class SpecialExtensionSource<T>:ExtensionSource<T> {}
        public interface IExtensionItems<T> {T[] Values {get;}}
        public class InterfaceExtensionSource<T>:IExtensionItems<T> {public T[] Values=>[];}
        public class AsyncExtensionSource<T> {
            public T[] Values=[];public bool Suspend;public bool FailMove;public bool FailDispose;public CancellationToken Token;
        }
        public class ExtensionEnumerator<T> {
            private readonly AsyncExtensionSource<T> source;private int index=-1;
            public ExtensionEnumerator(AsyncExtensionSource<T> source){this.source=source;}
            public T Current {get {ProtocolTrace.Value+="current;";return source.Values[index];}}
            public MoveStep<T> MoveNextAsync()=>new(Move());
            private async Task<bool> Move(){ProtocolTrace.Value+="move;";if(source.Suspend)await Task.Yield();source.Token.ThrowIfCancellationRequested();if(source.FailMove)throw new InvalidOperationException("move");return ++index<source.Values.Length;}
            public DisposeStep<T> DisposeAsync()=>new(Dispose());
            private async Task Dispose(){ProtocolTrace.Value+="dispose;";if(source.Suspend)await Task.Yield();if(source.FailDispose)throw new InvalidOperationException("dispose");}
        }
        public readonly struct MoveStep<T>(Task<bool> task) {public Task<bool> Task=>task;}
        public readonly struct DisposeStep<T>(Task task) {public Task Task=>task;}
        public class InstanceStep<T> {public TaskAwaiter<bool> GetAwaiter()=>Task.FromResult(false).GetAwaiter();}
        public class InstanceStepChild<T>:InstanceStep<T> {}
        public class InstanceAsyncSource<T> {public InstanceEnumerator<T> GetAsyncEnumerator()=>new();}
        public class InstanceEnumerator<T> {public T Current=>default!;public InstanceStepChild<T> MoveNextAsync()=>new();public ValueTask DisposeAsync()=>default;}
        public class InheritedAsyncSource<T> {public InheritedEnumerator<T> GetAsyncEnumerator()=>new();}
        public class InheritedEnumerator<T>:InheritedEnumeratorBase<T> {public T Current=>default!;}
        public class InheritedEnumeratorBase<T> {public ClassStep<T> MoveNextAsync()=>new();public ClassDispose<T> DisposeAsync()=>new();}
        public class ClassAsyncSource<T> {public ClassEnumerator<T> GetAsyncEnumerator()=>new();}
        public class ClassEnumerator<T> {public T Current=>default!;public ClassStep<T> MoveNextAsync()=>new();public ClassDispose<T> DisposeAsync()=>new();}
        public struct ClassStep<T>{}
        public struct ClassDispose<T>{}
        public class BoolResultSource<T> {public BoolResultEnumerator<T> GetAsyncEnumerator()=>new();}
        public class BoolResultEnumerator<T> {public T Current=>default!;public BoolResultStep<T> MoveNextAsync()=>new();}
        public class BoolResultStep<T> {public TaskAwaiter<T> GetAwaiter()=>Task.FromResult(default(T)!).GetAwaiter();}
        public class CompletionSource<T> {public CompletionEnumerator<T> GetAsyncEnumerator()=>new();}
        public class CompletionEnumerator<T> {public T Current=>default!;public CompletionStep<T> MoveNextAsync()=>new();}
        public class CompletionStep<T> {public CompletionAwaiter<T> GetAwaiter()=>new();}
        public class CompletionAwaiter<T>:INotifyCompletion {public T IsCompleted=>default!;public bool GetResult()=>false;public void OnCompleted(Action continuation)=>continuation();}
        public static class ProtocolExtensions {
            public static IEnumerator<T> GetEnumerator<T>(this ExtensionSource<T> source) {ProtocolTrace.Value+="get;";return ((IEnumerable<T>)source.Values).GetEnumerator();}
            public static IEnumerator<T> GetEnumerator<T>(this IExtensionItems<T> source)=>((IEnumerable<T>)source.Values).GetEnumerator();
            public static IEnumerator<T> GetEnumerator<T>(this StructExtensionSource<T> source) where T:struct=>((IEnumerable<T>)source.Values).GetEnumerator();
            public static IEnumerator<int> GetEnumerator(this SpecialExtensionSource<int> source)=>((IEnumerable<int>)source.Values).GetEnumerator();
            public static IEnumerator<T> GetEnumerator<T>(this SpecialExtensionSource<T> source)=>((IEnumerable<T>)source.Values).GetEnumerator();
            public static ExtensionEnumerator<T> GetAsyncEnumerator<T>(this AsyncExtensionSource<T> source,CancellationToken token=default) {ProtocolTrace.Value+="async-get:"+token.CanBeCanceled+";";return new(source);}
            public static TaskAwaiter<bool> GetAwaiter<T>(this MoveStep<T> step)=>step.Task.GetAwaiter();
            public static TaskAwaiter GetAwaiter<T>(this DisposeStep<T> step)=>step.Task.GetAwaiter();
            public static TaskAwaiter<bool> GetAwaiter<T>(this ClassStep<T> step) where T:class=>Task.FromResult(false).GetAwaiter();
            public static TaskAwaiter GetAwaiter<T>(this ClassDispose<T> step) where T:class=>Task.CompletedTask.GetAwaiter();
        }
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("ExtensionForeach","static int M(ExtensionSource<int> source,int seed){foreach(var item in source)seed=item;return seed;}","Suggestion","T1 M<T1>(global::ExtensionSource<T1> source, T1 seed)"),
            ("DerivedExtensionForeach","static int M(DerivedExtensionSource<int> source,int seed){foreach(var item in source)seed=item;return seed;}","Suggestion","T1 M<T1>(global::DerivedExtensionSource<T1> source, T1 seed)"),
            ("InterfaceExtensionForeach","static int M(InterfaceExtensionSource<int> source,int seed){foreach(var item in source)seed=item;return seed;}","Suggestion","T1 M<T1>(global::InterfaceExtensionSource<T1> source, T1 seed)"),
            ("ConstrainedExtensionForeach","static int M(StructExtensionSource<int> source,int seed){foreach(var item in source)seed=item;return seed;}","Suggestion","T1 M<T1>(global::StructExtensionSource<T1> source, T1 seed) where T1 : struct"),
            ("SpecializedExtensionForeach","static int M(SpecialExtensionSource<int> source,int seed,bool ignored){foreach(var item in source)seed=item;return seed;}","Suggestion","int M<T1>(global::SpecialExtensionSource<int> source, int seed, T1 ignored)"),
            ("AsyncExtensionForeach","static async Task<int> M(AsyncExtensionSource<int> source,int seed){await foreach(var item in source)seed=item;return seed;}","Suggestion","global::System.Threading.Tasks.Task<T1> M<T1>(global::AsyncExtensionSource<T1> source, T1 seed)"),
            ("ImplicitInheritedAwaiter","static async Task<int> M(InstanceAsyncSource<int> source,int seed){await foreach(var item in source)seed=item;return seed;}","Suggestion","global::System.Threading.Tasks.Task<T1> M<T1>(global::InstanceAsyncSource<T1> source, T1 seed)"),
            ("InheritedImplicitProtocolOwner", "static async Task<string> M(InheritedAsyncSource<string> source,string seed){await foreach(var item in source)seed=item;return seed;}", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::InheritedAsyncSource<T1> source, T1 seed) where T1 : class"),
            ("ImplicitAwaiterConstraints","static async Task<string> M(ClassAsyncSource<string> source,string seed){await foreach(var item in source)seed=item;return seed;}","Suggestion","global::System.Threading.Tasks.Task<T1> M<T1>(global::ClassAsyncSource<T1> source, T1 seed) where T1 : class"),
            ("ImplicitMoveResultMustBeBoolean","static async Task<bool> M(BoolResultSource<bool> source,bool seed,bool ignored){await foreach(var item in source)seed=item;return seed;}","Suggestion","global::System.Threading.Tasks.Task<bool> M<T1>(global::BoolResultSource<bool> source, bool seed, T1 ignored)"),
            ("ImplicitCompletionMustBeBoolean","static async Task<bool> M(CompletionSource<bool> source,bool seed,bool ignored){await foreach(var item in source)seed=item;return seed;}","Suggestion","global::System.Threading.Tasks.Task<bool> M<T1>(global::CompletionSource<bool> source, bool seed, T1 ignored)"),
            ("ExplicitCompletionMustBeBoolean","static async Task<bool> M(CompletionStep<bool> source,bool ignored)=>await source;","Suggestion","global::System.Threading.Tasks.Task<bool> M<T1>(global::CompletionStep<bool> source, T1 ignored)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var tree = CSharpSyntaxTree.ParseText(Support + "\nclass Cases {" + test.Source + "}", cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != test.Status || result.Signature != test.Signature)
            { failures.Add(test.Name + ": expected " + test.Status + ": " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS " + test.Name); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        (string Name, string Definition, string Consumer, string Method, string Signature)[] implicitMethods =
        [
            ("CrossFileExtensionEnumerator", "public class ExternalSequence<T>{} public static class ExternalExtensions {public static System.Collections.Generic.IEnumerator<int> GetEnumerator(this ExternalSequence<int> source)=>throw new System.Exception();}", "class Reader {static int Read(ExternalSequence<int> source,int seed){foreach(var item in source)seed=item;return seed;}}", "GetEnumerator", "global::System.Collections.Generic.IEnumerator<int> GetEnumerator<T1>(this global::ExternalSequence<T1> source)"),
            ("CrossFileImplicitGetAwaiter", "public struct ExternalStep<T>{} public static class ExternalExtensions {public static System.Runtime.CompilerServices.TaskAwaiter<bool> GetAwaiter(this ExternalStep<int> step)=>System.Threading.Tasks.Task.FromResult(false).GetAwaiter();}", "class ExternalSource {public ExternalEnumerator GetAsyncEnumerator()=>new();} class ExternalEnumerator {public int Current=>0;public ExternalStep<int> MoveNextAsync()=>new();} class Reader {static async System.Threading.Tasks.Task<int> Read(ExternalSource source,int seed){await foreach(var item in source)seed=item;return seed;}}", "GetAwaiter", "global::System.Runtime.CompilerServices.TaskAwaiter<bool> GetAwaiter<T1>(this global::ExternalStep<T1> step)")
        ];
        foreach (var test in implicitMethods)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(test.Definition, cancellationToken: cancellationToken), CSharpSyntaxTree.ParseText(test.Consumer, cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == test.Method; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        return tests.Length + implicitMethods.Length + await Runtime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        var source = Support + """
            public static class ProtocolProbe {
                public static async Task<string> Inherited(InheritedAsyncSource<string> source,string seed){await foreach(var item in source)seed=item;return seed;}
                public static int Sync(ExtensionSource<int> source,int seed){foreach(var item in source)seed=item;return seed;}
                public static async Task<int> Async(AsyncExtensionSource<int> source,int seed){await foreach(var item in source)seed=item;return seed;}
                public static async Task<int> BreakAsync(AsyncExtensionSource<int> source,int seed){await foreach(var item in source){seed=item;break;}return seed;}
                public static async Task<int> FaultBody(AsyncExtensionSource<int> source,int seed){try{await foreach(var item in source){seed=item;throw new ApplicationException("body");}return seed;}finally{ProtocolTrace.Value+="body-finally;";}}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ProtocolRuntime", "ProtocolRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("ProtocolRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 10; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Protocol analyzer crashed."); }
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && root.FindNode(d.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "ProtocolProbe" }; });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 5)
        { throw new InvalidOperationException("Expected five protocol fixes; got " + fixes); }
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var name in new[] { "Sync", "Async", "BreakAsync", "FaultBody" })
        {
            for (var scenario = 0; scenario < 8; scenario++)
            {
                var expected = await Execute(before, name, false, scenario, cancellationToken).ConfigureAwait(false);
                var actual = await Execute(after, name, true, scenario, cancellationToken).ConfigureAwait(false);
                if (expected != actual)
                { throw new InvalidOperationException(name + " protocol behavior changed: " + expected + " -> " + actual); }
            }
        }
        var guid = Guid.NewGuid();
        var typedSource = Activator.CreateInstance(after.GetType("AsyncExtensionSource`1")!.MakeGenericType(typeof(Guid)))!;
        typedSource.GetType().GetField("Values")!.SetValue(typedSource, new[] { guid });
        typedSource.GetType().GetField("Suspend")!.SetValue(typedSource, true);
        var task = (Task<Guid>)after.GetType("ProtocolProbe")!.GetMethod("Async")!.MakeGenericMethod(typeof(Guid)).Invoke(null, [typedSource, Guid.Empty])!;
        if (await task.ConfigureAwait(false) != guid)
        { throw new InvalidOperationException("Generalized implicit await lost values."); }
        var sentinel = new Uri("https://example.invalid/enumeration");
        var constrainedSource = Activator.CreateInstance(after.GetType("InheritedAsyncSource`1")!.MakeGenericType(typeof(Uri)))!;
        var constrainedTask = (Task<Uri>)after.GetType("ProtocolProbe")!.GetMethod("Inherited")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [constrainedSource, sentinel])!;
        if (!ReferenceEquals(await constrainedTask.ConfigureAwait(false), sentinel))
        { throw new InvalidOperationException("Inherited implicit protocol constraints lost reference identity."); }
        Program.Log.Info("PASS ExtensionEnumerationCodeFixRuntime");
        Program.Log.Info("PASS ImplicitAwaitSuspensionFaultCancellationAndDisposal");
        Program.Log.Info("PASS EnumerationOriginalAndFixedProtocolTraces");
        return 3;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var problems = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error; }).ToArray();
        if (problems.Length != 0)
        { throw new InvalidOperationException(string.Join("\n", problems.Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }

    private static async Task<string> Execute(System.Reflection.Assembly assembly, string name, bool generic, int scenario, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();
        var trace = assembly.GetType("ProtocolTrace")!.GetField("Value")!;
        trace.SetValue(null, "");
        var source = Activator.CreateInstance(assembly.GetType(name == "Sync" ? "ExtensionSource`1" : "AsyncExtensionSource`1")!.MakeGenericType(typeof(int)))!;
        source.GetType().GetField("Values")!.SetValue(source, scenario == 0 ? Array.Empty<int>() : RuntimeValues);
        if (name != "Sync")
        {
            source.GetType().GetField("Suspend")!.SetValue(source, scenario >= 2);
            source.GetType().GetField("FailMove")!.SetValue(source, scenario is 3 or 7);
            source.GetType().GetField("FailDispose")!.SetValue(source, scenario is 4 or 6 or 7);
            source.GetType().GetField("Token")!.SetValue(source, new CancellationToken(scenario is 5 or 6));
        }
        var method = assembly.GetType("ProtocolProbe")!.GetMethod(name)!;
        if (generic)
        { method = method.MakeGenericMethod(typeof(int)); }
        string result;
        try
        {
            var value = method.Invoke(null, [source, -1]);
            result = name == "Sync" ? value!.ToString()! : (await ((Task<int>)value!).ConfigureAwait(false)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (OperationCanceledException) { result = "cancel"; }
        catch (InvalidOperationException exception) { result = "invalid:" + exception.Message; }
        catch (ApplicationException exception) { result = "application:" + exception.Message; }
        return result + ":" + trace.GetValue(null);
    }
}
