using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class ResourceChecks
{
    private const string Support = """
        #nullable enable
        using System;using System.Threading.Tasks;
        public static class ResourceTrace {public static string Log="";public static TaskCompletionSource<bool> Gate=new();}
        public sealed class PlainResource:IDisposable {public void Dispose(){ResourceTrace.Log+="plain;";}}
        public sealed class GenericResource<T>(T value):IDisposable,IAsyncDisposable {
            public T Value=>value;
            public void Dispose(){ResourceTrace.Log+="sync;";}
            public async ValueTask DisposeAsync(){ResourceTrace.Log+="async-start;";await ResourceTrace.Gate.Task;ResourceTrace.Log+="async-end;";}
        }
        public readonly struct StructResource<T>(T value):IDisposable {public T Value=>value;public void Dispose(){ResourceTrace.Log+="struct;";}}
        public sealed class SplitResource:IDisposable,IAsyncDisposable {
            public void Dispose(){ResourceTrace.Log+="public-sync;";}void IDisposable.Dispose(){ResourceTrace.Log+="interface-sync;";}
            public Task DisposeAsync(){ResourceTrace.Log+="public-async;";return Task.CompletedTask;}ValueTask IAsyncDisposable.DisposeAsync(){ResourceTrace.Log+="interface-async;";return default;}
        }
        public sealed class FaultResource(bool fail):IDisposable,IAsyncDisposable {public void Dispose(){ResourceTrace.Log+="dispose;";if(fail)throw new ApplicationException("dispose");}public async ValueTask DisposeAsync(){await Task.Yield();Dispose();}}
        public ref struct RefResource<T>(T value):IDisposable {public T Value=>value;public void Dispose(){ResourceTrace.Log+="pattern;";}void IDisposable.Dispose(){ResourceTrace.Log+="interface-pattern;";}}
        public readonly struct DisposalAwaiter<T>(System.Runtime.CompilerServices.TaskAwaiter<bool> pending):System.Runtime.CompilerServices.INotifyCompletion {
            public bool IsCompleted=>pending.IsCompleted;public void OnCompleted(Action action)=>pending.OnCompleted(action);public void GetResult(){pending.GetResult();ResourceTrace.Log+="custom-end;";}
        }
        public readonly struct DisposalAwaitable<T> {public DisposalAwaiter<T> GetAwaiter()=>new(ResourceTrace.Gate.Task.GetAwaiter());}
        public sealed class CustomResource<T>(T value){public T Value=>value;public DisposalAwaitable<T> DisposeAsync(){ResourceTrace.Log+="custom-start;";return default;}}
        public readonly struct ExtensionDisposalAwaitable<T> {}
        public static class DisposalExtensions {public static DisposalAwaiter<T> GetAwaiter<T>(this ExtensionDisposalAwaitable<T> pending)=>new(ResourceTrace.Gate.Task.GetAwaiter());}
        public sealed class ExtensionResource<T>(T value){public T Value=>value;public ExtensionDisposalAwaitable<T> DisposeAsync()=>default;}
        public readonly struct SpecializedDisposalAwaitable<T> {}
        public static class SpecializedDisposalExtensions {public static DisposalAwaiter<int> GetAwaiter(this SpecializedDisposalAwaitable<int> pending)=>new(ResourceTrace.Gate.Task.GetAwaiter());}
        public sealed class SpecializedResource<T>(T value){public T Value=>value;public SpecializedDisposalAwaitable<T> DisposeAsync()=>default;}
        public sealed class PatternResource<T>(T value){public T Value=>value;public ValueTask DisposeAsync(){ResourceTrace.Log+="pattern-async;";return default;}}
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("ResourceInterfaceConstraint", "static int M(PlainResource resource,int value){using(resource){return value;}}", "T2 M<T1, T2>(T1 resource, T2 value) where T1 : global::System.IDisposable"),
            ("ResourceVarConstraint", "static int M(PlainResource resource,int value){using var copy=resource;return value;}", "T2 M<T1, T2>(T1 resource, T2 value) where T1 : global::System.IDisposable"),
            ("ResourceGenericPayload", "static int M(GenericResource<int> resource){using(var copy=resource){return copy.Value;}}", "T1 M<T1>(global::GenericResource<T1> resource)"),
            ("ResourceGenericStructPayload", "static int M(StructResource<int> resource){using var copy=resource;return copy.Value;}", "T1 M<T1>(global::StructResource<T1> resource)"),
            ("ResourceExplicitStorage", "static int M(GenericResource<int> resource,bool ignored){using GenericResource<int> copy=resource;return copy.Value;}", "int M<T1>(global::GenericResource<int> resource, T1 ignored)"),
            ("ResourceInterfaceStorage", "static int M(PlainResource resource,int value){using IDisposable copy=resource;return value;}", "T2 M<T1, T2>(T1 resource, T2 value) where T1 : global::System.IDisposable"),
            ("ResourceNullableClass", "static int M(PlainResource? resource,int value){using(resource){return value;}}", "T2 M<T1, T2>(T1? resource, T2 value) where T1 : class, global::System.IDisposable"),
            ("ResourceNullableStruct", "static int M(StructResource<int>? resource,int value){using(resource){return value;}}", "T2 M<T1, T2>(global::StructResource<T1>? resource, T2 value)"),
            ("ResourceSyncSplit", "static int M(SplitResource resource,int value){using(resource){return value;}}", "T2 M<T1, T2>(T1 resource, T2 value) where T1 : global::System.IDisposable"),
            ("ResourceRefPattern", "static int M(RefResource<int> resource){using var copy=resource;return copy.Value;}", "T1 M<T1>(global::RefResource<T1> resource)"),
            ("ResourceAsyncPayload", "static async Task<int> M(GenericResource<int> resource){await using var copy=resource;return copy.Value;}", "global::System.Threading.Tasks.Task<T1> M<T1>(global::GenericResource<T1> resource)"),
            ("ResourceAsyncInterface", "static async Task<int> M(IAsyncDisposable resource,int value){await using(resource){return value;}}", "global::System.Threading.Tasks.Task<T2> M<T1, T2>(T1 resource, T2 value) where T1 : global::System.IAsyncDisposable"),
            ("ResourceNullableAsyncInterface", "static async Task<int> M(IAsyncDisposable? resource,int value){await using(resource){return value;}}", "global::System.Threading.Tasks.Task<T2> M<T1, T2>(T1? resource, T2 value) where T1 : class, global::System.IAsyncDisposable"),
            ("ResourceAsyncPatternPayload", "static async Task<int> M(PatternResource<int> resource){await using var copy=resource;return copy.Value;}", "global::System.Threading.Tasks.Task<T1> M<T1>(global::PatternResource<T1> resource)"),
            ("ResourceAsyncSplit", "static async Task<int> M(SplitResource resource,int value){await using(resource){return value;}}", "global::System.Threading.Tasks.Task<T1> M<T1>(global::SplitResource resource, T1 value)"),
            ("ResourceCustomDisposalAwaiter", "static async Task<int> M(CustomResource<int> resource){await using var copy=resource;return copy.Value;}", "global::System.Threading.Tasks.Task<T1> M<T1>(global::CustomResource<T1> resource)"),
            ("ResourceExtensionDisposalAwaiter", "static async Task<int> M(ExtensionResource<int> resource){await using var copy=resource;return copy.Value;}", "global::System.Threading.Tasks.Task<T1> M<T1>(global::ExtensionResource<T1> resource)"),
            ("ResourceSpecializedDisposalAwaiter", "static async Task<int> M(SpecializedResource<int> resource,int value){await using(resource){return value;}}", "global::System.Threading.Tasks.Task<T1> M<T1>(global::SpecializedResource<int> resource, T1 value)"),
            ("ResourceMultiple", "static int M(GenericResource<int> first,GenericResource<int> second){using(var left=first)using(var right=second){return right.Value;}}", "T2 M<T1, T2>(global::GenericResource<T1> first, global::GenericResource<T2> second)")
        ];
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(Support + "class Cases{" + test.Source + "}", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        return tests.Length + ContractlessPattern(cancellationToken) + await Runtime(cancellationToken).ConfigureAwait(false);
    }
    private static int ContractlessPattern(CancellationToken cancellationToken)
    {
        const string source = """
            using System;using System.Threading.Tasks;using System.Runtime.CompilerServices;
            namespace System {public interface IAsyncDisposable {}}
            public readonly struct LegacyAwaiter:INotifyCompletion {public bool IsCompleted=>true;public void OnCompleted(Action action)=>action();public void GetResult(){}}
            public readonly struct LegacyAwaitable {public LegacyAwaiter GetAwaiter()=>new();}
            public sealed class LegacyResource<T>(T value){public T Value=>value;public LegacyAwaitable DisposeAsync()=>default;}
            public static class LegacyProbe {public static async Task<int> M(LegacyResource<int> resource){await using var copy=resource;return copy.Value;}}
            """;
        var compilation = Program.Compile([CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)]);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Contractless resource fixture: " + string.Join("\n", emitted.Diagnostics)); }
        var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
        if (result.Status != "Suggestion" || result.Signature != "global::System.Threading.Tasks.Task<T1> M<T1>(global::LegacyResource<T1> resource)")
        { throw new InvalidOperationException("Contractless disposal pattern: " + result); }
        Program.Log.Info("PASS ResourcePatternWithoutDisposalContractMember");
        return 1;
    }
    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        const string source = """
            public static class ResourceProbe {
                public static int Fault(FaultResource resource,int value,bool fail){using(resource){ResourceTrace.Log+="body;";if(fail)throw new InvalidOperationException("body");return value;}}
                public static async Task<int> AsyncFault(FaultResource resource,int value,bool fail){await using(resource){ResourceTrace.Log+="body;";if(fail)throw new InvalidOperationException("body");return value;}}
                public static int Whole(PlainResource resource,int value){using(resource){ResourceTrace.Log+="body;";return value;}}
                public static int Payload(GenericResource<int> resource){using var copy=resource;ResourceTrace.Log+="body;";return copy.Value;}
                public static int Struct(StructResource<int> resource){using var copy=resource;return copy.Value;}
                public static int SplitSync(SplitResource resource,int value){using(resource){return value;}}
                public static async Task<int> Awaited(GenericResource<int> resource){await using var copy=resource;ResourceTrace.Log+="body;";return copy.Value;}
                public static async Task<int> Custom(CustomResource<int> resource){await using var copy=resource;return copy.Value;}
                public static async Task<int> Extension(ExtensionResource<int> resource){await using var copy=resource;return copy.Value;}
                public static async Task<int> Pattern(PatternResource<int> resource){await using var copy=resource;return copy.Value;}
                public static async Task<int> SplitAsync(SplitResource resource,int value){await using(resource){return value;}}
            }
            public static class OriginalResourceRunner {
                public static string Run(){
                    ResourceTrace.Log="";try{ResourceProbe.Fault(new FaultResource(false),0,true);return "bad";}catch(InvalidOperationException){}if(ResourceTrace.Log!="body;dispose;")return "bad";
                    ResourceTrace.Log="";try{ResourceProbe.Fault(new FaultResource(true),0,true);return "bad";}catch(ApplicationException){}if(ResourceTrace.Log!="body;dispose;")return "bad";
                    ResourceTrace.Log="";
                    if(ResourceProbe.Whole(new PlainResource(),7)!=7||ResourceProbe.Whole((PlainResource)null!,11)!=11||ResourceProbe.Payload(new GenericResource<int>(13))!=13||ResourceProbe.Struct(new StructResource<int>(17))!=17||ResourceProbe.SplitSync(new SplitResource(),19)!=19)return "bad";
                    return ResourceTrace.Log;
                }
                public static async Task<string> RunAsync(){
                    ResourceTrace.Log="";if(await ResourceProbe.AsyncFault((FaultResource)null!,5,false)!=5||ResourceTrace.Log!="body;")return "bad";
                    ResourceTrace.Log="";try{await ResourceProbe.AsyncFault(new FaultResource(false),0,true);return "bad";}catch(InvalidOperationException){}if(ResourceTrace.Log!="body;dispose;")return "bad";
                    ResourceTrace.Log="";try{await ResourceProbe.AsyncFault(new FaultResource(true),0,true);return "bad";}catch(ApplicationException){}if(ResourceTrace.Log!="body;dispose;")return "bad";
                    ResourceTrace.Log="";ResourceTrace.Gate=new();var pending=ResourceProbe.Awaited(new GenericResource<int>(23));
                    if(pending.IsCompleted||ResourceTrace.Log!="body;async-start;")return "bad";
                    ResourceTrace.Gate.SetResult(true);if(await pending!=23)return "bad";
                    if(await ResourceProbe.Pattern(new PatternResource<int>(29))!=29||await ResourceProbe.SplitAsync(new SplitResource(),31)!=31)return "bad";
                    ResourceTrace.Gate=new();var custom=ResourceProbe.Custom(new CustomResource<int>(37));if(custom.IsCompleted)return "bad";
                    ResourceTrace.Gate.SetResult(true);if(await custom!=37)return "bad";
                    ResourceTrace.Gate=new();var extension=ResourceProbe.Extension(new ExtensionResource<int>(41));if(extension.IsCompleted)return "bad";
                    ResourceTrace.Gate.SetResult(true);if(await extension!=41)return "bad";
                    return ResourceTrace.Log;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ResourceRuntime", "ResourceRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Resources.cs", Support + source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var originalTrace = before.GetType("OriginalResourceRunner")!.GetMethod("Run")!.Invoke(null, null);
        var originalAsync = await ((Task<string>)before.GetType("OriginalResourceRunner")!.GetMethod("RunAsync")!.Invoke(null, null)!).ConfigureAwait(false);
        if (!Equals(originalTrace, "body;plain;body;body;sync;struct;interface-sync;") || originalAsync != "body;async-start;async-end;pattern-async;public-async;custom-start;custom-end;custom-end;")
        { throw new InvalidOperationException("Original resource behavior failed: " + originalTrace + " / " + originalAsync); }
        var provider = new HmSignatureCodeFixProvider();
        for (var round = 0; round < 11; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Resource analyzer crashed."); }
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && root.FindNode(d.Location.SourceSpan).AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().First().Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax { Identifier.ValueText: "ResourceProbe" }; }) ?? throw new InvalidOperationException("Missing resource fix: " + string.Join<MethodResult>("; ", Program.Results((CSharpCompilation)compilation, cancellationToken)));
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        }
        document = document.Project.AddDocument("NewResourceDomains.cs", """
            using System;using System.Threading.Tasks;
            public static class NewResourceRunner {
                public static async Task<bool> Run(){
                    var value=new Uri("https://example.invalid/resource");ResourceTrace.Log="";
                    if(!ReferenceEquals(ResourceProbe.Whole(new StructResource<Uri>(value),value),value)||!ReferenceEquals(ResourceProbe.Payload(new GenericResource<Uri>(value)),value)||!ReferenceEquals(ResourceProbe.Struct(new StructResource<Uri>(value)),value))return false;
                    if(ResourceTrace.Log!="body;struct;body;sync;struct;")return false;
                    ResourceTrace.Log="";ResourceTrace.Gate=new();var pending=ResourceProbe.Awaited(new GenericResource<Uri>(value));
                    if(pending.IsCompleted||ResourceTrace.Log!="body;async-start;")return false;
                    ResourceTrace.Gate.SetResult(true);if(!ReferenceEquals(await pending,value))return false;
                    if(!ReferenceEquals(await ResourceProbe.Pattern(new PatternResource<Uri>(value)),value)||!ReferenceEquals(await ResourceProbe.SplitAsync(new SplitResource(),value),value))return false;
                    ResourceTrace.Gate=new();var custom=ResourceProbe.Custom(new CustomResource<Uri>(value));if(custom.IsCompleted)return false;
                    ResourceTrace.Gate.SetResult(true);if(!ReferenceEquals(await custom,value))return false;
                    ResourceTrace.Gate=new();var extension=ResourceProbe.Extension(new ExtensionResource<Uri>(value));if(extension.IsCompleted)return false;
                    ResourceTrace.Gate.SetResult(true);if(!ReferenceEquals(await extension,value))return false;
                    return ResourceTrace.Log=="body;async-start;async-end;pattern-async;public-async;custom-start;custom-end;custom-end;";
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var updatedTrace = after.GetType("OriginalResourceRunner")!.GetMethod("Run")!.Invoke(null, null);
        var updatedAsync = await ((Task<string>)after.GetType("OriginalResourceRunner")!.GetMethod("RunAsync")!.Invoke(null, null)!).ConfigureAwait(false);
        var newDomains = await ((Task<bool>)after.GetType("NewResourceRunner")!.GetMethod("Run")!.Invoke(null, null)!).ConfigureAwait(false);
        if (!Equals(originalTrace, updatedTrace) || originalAsync != updatedAsync || !newDomains)
        { throw new InvalidOperationException("Resource fixes changed interface/pattern disposal, null handling, copy behavior or async suspension."); }
        Program.Log.Info("PASS ResourceCodeFixInterfacePatternNullIdentityAndAsyncRuntime");
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
