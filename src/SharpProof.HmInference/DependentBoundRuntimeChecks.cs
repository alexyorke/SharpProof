using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class DependentBoundRuntimeChecks
{
    private static readonly string[] Runners = ["OriginalDependentBoundRunner", "NewDependentBoundRunner"];

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            public static class DependentBoundTrace{public static string Value="";}
            public class Root{}
            public class Middle:Root{}
            public class Leaf:Middle{}
            public static class DependentBoundProbe{
                public static string Closed(string value,int ignored){string other="fixed";return Keep(value,other);}
                public static int Scalar(int value,int ignored){int other=0;return Keep(value,other);}
                public static string[] Array(string[] value,int ignored){object[] other=[];return Keep(value,other);}
                public static Leaf? Nullable(Leaf? value,int ignored){Root other=new();return Optional(value,other);}
                public static async System.Threading.Tasks.Task<Leaf?> Async(System.Threading.Tasks.Task<Leaf?> value,int ignored){Root other=new();return Optional(await value,other);}
                public static System.Enum? Enum(System.Enum? value,int ignored)=>EnumBound(value);
                public static Leaf Transitive(Leaf left,Middle right){Keep(left,right);RootBound(left);MiddleBound(right);return left;}
                private static T Keep<T,U>(T value,U other) where T:U{DependentBoundTrace.Value+="keep;";return value;}
                private static T Optional<T,U>(T value,U other) where T:U?{DependentBoundTrace.Value+="optional;";return value;}
                private static T EnumBound<T>(T value) where T:System.Enum?{DependentBoundTrace.Value+="enum;";return value;}
                private static T RootBound<T>(T value) where T:Root{DependentBoundTrace.Value+="root;";return value;}
                private static T MiddleBound<T>(T value) where T:Middle{DependentBoundTrace.Value+="middle;";return value;}
            }
            public static class OriginalDependentBoundRunner{
                public static bool Run(){
                    DependentBoundTrace.Value="";var text=new string('x',3);var array=new[]{text};var leaf=new Leaf();Leaf? absent=null;
                    if(!object.ReferenceEquals(DependentBoundProbe.Closed(text,0),text)||DependentBoundProbe.Scalar(23,0)!=23)return false;
                    if(!object.ReferenceEquals(DependentBoundProbe.Array(array,0),array))return false;
                    if(!object.ReferenceEquals(DependentBoundProbe.Nullable(leaf,0),leaf)||DependentBoundProbe.Nullable(absent,0)is not null)return false;
                    if(!object.ReferenceEquals(DependentBoundProbe.Async(System.Threading.Tasks.Task.FromResult<Leaf?>(leaf),0).GetAwaiter().GetResult(),leaf))return false;
                    if(DependentBoundProbe.Async(System.Threading.Tasks.Task.FromResult(absent),0).GetAwaiter().GetResult()is not null)return false;
                    System.Enum boxed=System.DayOfWeek.Friday;if(!object.ReferenceEquals(DependentBoundProbe.Enum(boxed,0),boxed)||DependentBoundProbe.Enum(null,0)is not null)return false;
                    return object.ReferenceEquals(DependentBoundProbe.Transitive(leaf,new Middle()),leaf);
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "DependentBoundRuntime", "DependentBoundRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("DependentBoundRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalDependentBoundRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original dependent bound behavior failed."); }
        var expectedTrace = (string)before.GetType("DependentBoundTrace")!.GetField("Value")!.GetValue(null)!;
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 10; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Dependent bound analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "DependentBoundProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 7)
        { throw new InvalidOperationException("Expected seven dependent bound fixes; got " + fixes); }
        document = document.Project.AddDocument("NewDependentBoundDomains.cs", """
            #nullable enable
            public class Sibling:Middle{}
            public static class NewDependentBoundRunner{
                public static bool Run(){
                    DependentBoundTrace.Value="";var text=new string('x',3);var array=new[]{new object()};var leaf=new Sibling();Sibling? absent=null;
                    if(!object.ReferenceEquals(DependentBoundProbe.Closed(text,"unused"),text)||DependentBoundProbe.Scalar(23,"unused")!=23)return false;
                    if(!object.ReferenceEquals(DependentBoundProbe.Array(array,"unused"),array))return false;
                    if(!object.ReferenceEquals(DependentBoundProbe.Nullable(leaf,"unused"),leaf)||DependentBoundProbe.Nullable(absent,"unused")is not null)return false;
                    if(!object.ReferenceEquals(DependentBoundProbe.Async(System.Threading.Tasks.Task.FromResult<Sibling?>(leaf),"unused").GetAwaiter().GetResult(),leaf))return false;
                    if(DependentBoundProbe.Async(System.Threading.Tasks.Task.FromResult(absent),"unused").GetAwaiter().GetResult()is not null)return false;
                    System.Enum boxed=System.DayOfWeek.Friday;if(!object.ReferenceEquals(DependentBoundProbe.Enum(boxed,"unused"),boxed)||DependentBoundProbe.Enum(null,"unused")is not null)return false;
                    return object.ReferenceEquals(DependentBoundProbe.Transitive(leaf,new Middle()),leaf);
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var runner in Runners)
        {
            if (after.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)after.GetType("DependentBoundTrace")!.GetField("Value")!.GetValue(null)! != expectedTrace)
            { throw new InvalidOperationException("Nullable fixes changed null handling, identity or helper effects: " + runner); }
        }
        Program.Log.Info("PASS DependentBoundCodeFixNullableAsyncConcreteEnumIdentityEffectsAndNewDomains");
        return fixes + 1;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Dependent bound runtime emission failed or added warnings: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
