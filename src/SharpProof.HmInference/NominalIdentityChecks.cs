using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class NominalIdentityChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string librarySource = """
            #nullable enable
            namespace Collision {
                public interface IContract {}
                public class Item:IContract {}
                public class Box<T> {public T Value=default!;public class Inner<U>{}}
                public class Constrained<T> where T:IContract {}
            }
            """;
        var images = new List<byte[]>();
        MetadataReference Library(string name, string alias)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(alias);
            using var image = new MemoryStream();
            var library = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(librarySource, cancellationToken: cancellationToken)], Program.Compile([]).References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var emitted = library.Emit(image, cancellationToken: cancellationToken);
            if (!emitted.Success)
            { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
            var bytes = image.ToArray();
            images.Add(bytes);
            return MetadataReference.CreateFromImage(bytes).WithAliases([alias]);
        }
        var references = Program.Compile([]).References.Concat([Library("HmLeftIdentity", "Left"), Library("HmRightIdentity", "global")]).ToArray();
        const string source = """
            #nullable enable
            extern alias Left;
            using System.Threading.Tasks;
            using Closed=Left::Collision.Box<int>;
            public static class NominalProbe {
                public static Left::Collision.Box<int> Identity(Left::Collision.Box<int> value)=>value;
                public static Closed ClosedAlias(Closed value)=>value;
                public static object Narrow(Left::Collision.Box<int> value)=>value;
                public static async Task<object?> AwaitNarrow(Task<Left::Collision.Box<int>?> value)=>await value;
                public static (Left::Collision.Box<int>,Collision.Box<string>) Pair(Left::Collision.Box<int> left,Collision.Box<string> right)=>(left,right);
                public static Left::Collision.Box<int>? Nullable(Left::Collision.Box<int>? value)=>value;
                public static async Task<Left::Collision.Box<int>?> Await(Task<Left::Collision.Box<int>?> value)=>await value;
                public static Left::Collision.Box<int>.Inner<string> Nested(Left::Collision.Box<int>.Inner<string> value)=>value;
                public static Left::Collision.Constrained<Left::Collision.Item> Constraint(Left::Collision.Constrained<Left::Collision.Item> value)=>value;
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "NominalIdentity", "NominalIdentity", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: references));
        var document = project.AddDocument("NominalIdentity.cs", source);
        var provider = new HmSignatureCodeFixProvider();
        for (var round = 0; round < 7; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var results = Program.Results((CSharpCompilation)compilation, cancellationToken).ToArray();
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Nominal analyzer crashed."); }
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001"; });
            if (diagnostic is null)
            { throw new InvalidOperationException("Missing nominal fix at round " + round + ": " + string.Join("; ", results.Select(r => { ArgumentNullException.ThrowIfNull(r); return r.ToString(); }))); }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        }
        var changed = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var errors = changed.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (errors.Length != 0)
        { throw new InvalidOperationException(string.Join("\n", errors.Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        var probe = changed.GetTypeByMetadataName("NominalProbe")!;
        foreach (var method in probe.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.Name is "Narrow" or "AwaitNarrow")
            {
                var retained = method.Name == "Narrow" ? method.ReturnType : ((INamedTypeSymbol)method.ReturnType).TypeArguments[0];
                if (method.TypeParameters.Length != 0 || retained.SpecialType != SpecialType.System_Object)
                { throw new InvalidOperationException("Return widening was lost: " + method.Name); }
                Program.Log.Info("PASS NominalPreservedConversion" + method.Name);
                continue;
            }
            if (method.TypeParameters.Length == 0)
            { throw new InvalidOperationException("Missing inferred parameters for " + method.Name); }
            var returnType = method.ReturnType;
            if (method.Name == "Await")
            { returnType = ((INamedTypeSymbol)returnType).TypeArguments[0]; }
            if (method.Name == "Pair")
            {
                var pair = (INamedTypeSymbol)returnType;
                if (pair.TupleElements[0].Type.ContainingAssembly.Name != "HmLeftIdentity" || pair.TupleElements[1].Type.ContainingAssembly.Name != "HmRightIdentity")
                { throw new InvalidOperationException("Aliased pair constructor identity changed."); }
            }
            else if (returnType.ContainingAssembly.Name != "HmLeftIdentity")
            { throw new InvalidOperationException("Aliased return constructor identity changed: " + method.Name); }
            if (method.Name == "Constraint" && method.TypeParameters[0].ConstraintTypes.Single().ContainingAssembly.Name != "HmLeftIdentity")
            { throw new InvalidOperationException("Aliased constraint identity changed."); }
            Program.Log.Info("PASS NominalAliasFix" + method.Name);
        }
        // New domains must compile against the applied fixes, retaining both assemblies.
        document = document.Project.AddDocument("NominalRunner.cs", """
            #nullable enable
            extern alias Left;
            using System;
            using System.Threading.Tasks;
            public static class NominalRunner {
                public static async Task<bool> Run(){
                    var sentinel=new Uri("https://example.invalid/nominal");
                    var box=new Left::Collision.Box<Uri>{Value=sentinel};
                    var other=new Collision.Box<Guid>{Value=Guid.NewGuid()};
                    if(!ReferenceEquals(NominalProbe.Identity(box),box)||!ReferenceEquals(NominalProbe.ClosedAlias(box),box)||NominalProbe.Nullable<Uri>(null)!=null)return false;
                    var pair=NominalProbe.Pair(box,other);if(!ReferenceEquals(pair.Item1,box)||!ReferenceEquals(pair.Item2,other))return false;
                    if(!ReferenceEquals(await NominalProbe.Await(Task.FromResult<Left::Collision.Box<Uri>?>(box)),box))return false;
                    var completion=new TaskCompletionSource<Left::Collision.Box<Uri>?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var pending=NominalProbe.Await(completion.Task);if(pending.IsCompleted)return false;
                    completion.SetResult(box);if(!ReferenceEquals(await pending,box))return false;
                    var wideBox=new Left::Collision.Box<int>{Value=42};if(!ReferenceEquals(NominalProbe.Narrow(wideBox),wideBox))return false;
                    var wideCompletion=new TaskCompletionSource<Left::Collision.Box<int>?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var widePending=NominalProbe.AwaitNarrow(wideCompletion.Task);if(widePending.IsCompleted)return false;
                    wideCompletion.SetResult(wideBox);if(!ReferenceEquals(await widePending,wideBox))return false;
                    return await NominalProbe.Await(Task.FromResult<Left::Collision.Box<Uri>?>(null))==null;
                }
            }
            """);
        var runtime = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        using var runtimeImage = new MemoryStream();
        var runtimeEmit = runtime.Emit(runtimeImage, cancellationToken: cancellationToken);
        if (!runtimeEmit.Success)
        { throw new InvalidOperationException(string.Join("\n", runtimeEmit.Diagnostics)); }
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("HmNominalRuntime", isCollectible: true);
        try
        {
            foreach (var bytes in images)
            {
                using var dependency = new MemoryStream(bytes);
                loadContext.LoadFromStream(dependency);
            }
            runtimeImage.Position = 0;
            var loaded = loadContext.LoadFromStream(runtimeImage);
            var runtimeCheck = (Task<bool>)loaded.GetType("NominalRunner")!.GetMethod("Run")!.Invoke(null, null)!;
            if (!await runtimeCheck.ConfigureAwait(false))
            { throw new InvalidOperationException("Aliased nullable/async runtime behavior changed."); }
        }
        finally { loadContext.Unload(); }
        Program.Log.Info("PASS NominalAliasNullableAsyncRuntime");
        const string shadow = "class Outer<T>{public class Inner<T>{public static Inner<T> M(Inner<T> value,bool ignored)=>value;}}";
        var shadowCompilation = Program.Compile([CSharpSyntaxTree.ParseText(shadow, cancellationToken: cancellationToken)]);
        var shadowResult = Program.Results(shadowCompilation, cancellationToken).Single();
        if (shadowResult.Signature != "Inner<T> M<T1>(Inner<T> value, T1 ignored)")
        { throw new InvalidOperationException("Shadowed owner identity: " + shadowResult); }
        Program.Log.Info("PASS ShadowedEnclosingGenericNames");
        return 11;
    }
}
