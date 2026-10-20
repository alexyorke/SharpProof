using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class UntypedCallerRuntimeChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            public static class ContextualProbe{
                public static async Task<object?> Nullable(object? value,int tag){await Task.Yield();return value;}
                public static Func<int,Task<string?>> Callback(Func<int,Task<string?>> map,int tag)=>map;
                public static int Default(int value,int tag)=>value;
                public static int Collection(System.Collections.Generic.List<int> value,int tag)=>value.Count;
            }
            public static class OriginalContextualRunner{
                public static bool Run(){
                    if(ContextualProbe.Nullable(null,0).GetAwaiter().GetResult()!=null)return false;
                    if(ContextualProbe.Callback(async value=>{await Task.Yield();return value==3?"ok":null;},0)(3).GetAwaiter().GetResult()!="ok")return false;
                    if(ContextualProbe.Callback(async value=>{await Task.Yield();return (string?)null;},0)(0).GetAwaiter().GetResult()!=null)return false;
                    return ContextualProbe.Default(default,0)==0&&ContextualProbe.Collection([],0)==0&&ContextualProbe.Collection(new(),0)==0;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ContextualRuntime", "ContextualRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("ContextualRuntime.cs", source);
        Verify((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, false, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 5; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Contextual caller analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "ContextualProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 4)
        { throw new InvalidOperationException("Expected four contextual caller fixes; got " + fixes); }
        document = document.Project.AddDocument("NewContextualDomains.cs", """
            public static class NewContextualRunner{
                public static bool Run(){
                    object value=new object();
                    if(!object.ReferenceEquals(ContextualProbe.Nullable(value,"tag").GetAwaiter().GetResult(),value))return false;
                    if(ContextualProbe.Nullable(null,3m).GetAwaiter().GetResult()!=null)return false;
                    var map=ContextualProbe.Callback(async (int value)=>{await System.Threading.Tasks.Task.Yield();return value==7?new[]{"new"}:null;},new object());
                    return map(7).GetAwaiter().GetResult()?[0]=="new"&&ContextualProbe.Default(4,"tag")==4&&ContextualProbe.Collection(new System.Collections.Generic.List<int>{1},3m)==1;
                }
            }
            """);
        Verify((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, true, cancellationToken);
        Program.Log.Info("PASS ContextualCallerCodeFixesPreserveNullableAsyncBehaviorAndFreeDomains");
        return fixes + 2;
    }

    private static void Verify(Compilation compilation, bool includeNew, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Contextual caller emission failed: " + string.Join("\n", emitted.Diagnostics)); }
        var assembly = System.Reflection.Assembly.Load(image.ToArray());
        if (assembly.GetType("OriginalContextualRunner")!.GetMethod("Run")!.Invoke(null, null) is not true
            || includeNew && assembly.GetType("NewContextualRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Contextual caller runtime behavior changed."); }
    }
}
