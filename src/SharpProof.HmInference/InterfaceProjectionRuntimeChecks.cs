using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class InterfaceProjectionRuntimeChecks
{
    private static readonly string[] Runners = ["OriginalProjectionRunner", "NewProjectionRunner"];

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            public static class ProjectionTrace{public static string Value="";}
            public interface Contract<T>{int Read(T value);}
            public class Dual<T>:Contract<T[]>,Contract<System.Collections.Generic.List<T>>{
                int Contract<T[]>.Read(T[] value){ProjectionTrace.Value+="array;";return value.Length;}
                int Contract<System.Collections.Generic.List<T>>.Read(System.Collections.Generic.List<T> value){ProjectionTrace.Value+="list;";return value.Count;}
            }
            public struct ValueDual<T>:Contract<T[]>,Contract<System.Collections.Generic.List<T>>{
                int Contract<T[]>.Read(T[] value){ProjectionTrace.Value+="struct-array;";return value.Length;}
                int Contract<System.Collections.Generic.List<T>>.Read(System.Collections.Generic.List<T> value){ProjectionTrace.Value+="struct-list;";return value.Count;}
            }
            public static class ProjectionProbe{
                public static Dual<int> Array(Dual<int> value,int[] other)=>Need(value,other);
                public static Dual<int> List(Dual<int> value,System.Collections.Generic.List<int> other)=>Need(value,other);
                public static ValueDual<int> StructArray(ValueDual<int> value,int[] other)=>Need(value,other);
                public static ValueDual<int> StructList(ValueDual<int> value,System.Collections.Generic.List<int> other)=>Need(value,other);
                private static T Need<T,U>(T value,U other) where T:Contract<U>{ProjectionTrace.Value+=value.Read(other)+";";return value;}
            }
            public static class OriginalProjectionRunner{
                public static bool Run(){
                    ProjectionTrace.Value="";var value=new Dual<int>();var array=new[]{11,13};var list=new System.Collections.Generic.List<int>{17,19,23};
                    if(!object.ReferenceEquals(ProjectionProbe.Array(value,array),value)||!object.ReferenceEquals(ProjectionProbe.List(value,list),value))return false;
                    ProjectionProbe.StructArray(new ValueDual<int>(),array);ProjectionProbe.StructList(new ValueDual<int>(),list);return true;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ProjectionRuntime", "ProjectionRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("ProjectionRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalProjectionRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original interface projection behavior failed."); }
        var expectedTrace = (string)before.GetType("ProjectionTrace")!.GetField("Value")!.GetValue(null)!;
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 7; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Interface projection analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "ProjectionProbe" };
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
        { throw new InvalidOperationException("Expected four interface projection fixes; got " + fixes); }
        document = document.Project.AddDocument("NewProjectionDomains.cs", """
            #nullable enable
            public static class NewProjectionRunner{
                public static bool Run(){
                    ProjectionTrace.Value="";var value=new Dual<string?>();var array=new string?[]{null,"payload"};var list=new System.Collections.Generic.List<string?>{null,"other","last"};
                    if(!object.ReferenceEquals(ProjectionProbe.Array(value,array),value)||!object.ReferenceEquals(ProjectionProbe.List(value,list),value))return false;
                    ProjectionProbe.StructArray(new ValueDual<string?>(),array);ProjectionProbe.StructList(new ValueDual<string?>(),list);return true;
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var runner in Runners)
        {
            if (after.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)after.GetType("ProjectionTrace")!.GetField("Value")!.GetValue(null)! != expectedTrace)
            { throw new InvalidOperationException("Projection fixes changed dispatch, identity or helper effects: " + runner); }
        }
        Program.Log.Info("PASS InterfaceProjectionCodeFixDispatchIdentityEffectsAndNullableDomains");
        return fixes + 1;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Interface projection runtime emission failed or added warnings: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
