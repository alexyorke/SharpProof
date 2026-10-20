using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class ConstraintNormalizationRuntimeChecks
{
    private static readonly string[] Runners = ["OriginalBoundRunner", "NewBoundRunner"];
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            public static class BoundTrace{public static string Value="";}
            public class BoundRoot{}
            public class BoundMiddle:BoundRoot{}
            public class BoundLeaf:BoundMiddle{}
            public interface BoundInvariant<T>{}
            public class BoundBox<T>{public T Value=default!;}
            public class BoundBoxLeaf<T>:BoundBox<T>,BoundInvariant<T>{}
            public class BoundIntBox:BoundBoxLeaf<int>{}
            public static class BoundProbe{
                public static object Self(object value)=>Keep(value,value);
                public static object Cycle(object left,object right){Keep(left,right);Keep(right,left);return left;}
                public static object CycleThree(object first,object second,object third){Keep(first,second);Keep(second,third);Keep(third,first);return first;}
                public static int StructCycle(int left,int right){Keep(left,right);Keep(right,left);return Value(left);}
                public static BoundLeaf Strongest(BoundLeaf value)=>Root(Middle(value));
                public static BoundLeaf CycleBase(BoundLeaf left,BoundLeaf right){Keep(left,right);Keep(right,left);Root(left);Middle(right);return left;}
                public static BoundIntBox GenericBounds(BoundIntBox value,int first,int second){Box(value,first);Leaf(value,second);return value;}
                public static BoundBoxLeaf<int> GenericProjection(BoundBoxLeaf<int> value,int first,int second){Box(value,first);Leaf(value,second);return value;}
                public static BoundBoxLeaf<int> InvariantProjection(BoundBoxLeaf<int> value,int other)=>Interface(value,other);
                private static T Keep<T,U>(T value,U other) where T:U{BoundTrace.Value+="keep;";return value;}
                private static T Value<T>(T value) where T:struct{BoundTrace.Value+="value;";return value;}
                private static T Root<T>(T value) where T:BoundRoot{BoundTrace.Value+="root;";return value;}
                private static T Middle<T>(T value) where T:BoundMiddle{BoundTrace.Value+="middle;";return value;}
                private static T Box<T,U>(T value,U other) where T:BoundBox<U>{BoundTrace.Value+="box;";return value;}
                private static T Leaf<T,U>(T value,U other) where T:BoundBoxLeaf<U>{BoundTrace.Value+="leaf;";return value;}
                private static T Interface<T,U>(T value,U other) where T:BoundInvariant<U>{BoundTrace.Value+="interface;";return value;}
            }
            public static class OriginalBoundRunner{
                public static bool Run(){
                    BoundTrace.Value="";var first=new object();var second=new object();var third=new object();
                    if(!object.ReferenceEquals(BoundProbe.Self(first),first)||!object.ReferenceEquals(BoundProbe.Cycle(first,second),first)||!object.ReferenceEquals(BoundProbe.CycleThree(first,second,third),first))return false;
                    if(BoundProbe.StructCycle(11,13)!=11)return false;
                    var leaf=new BoundLeaf();if(!object.ReferenceEquals(BoundProbe.Strongest(leaf),leaf)||!object.ReferenceEquals(BoundProbe.CycleBase(leaf,new BoundLeaf()),leaf))return false;
                    var bound=new BoundIntBox{Value=17};if(!object.ReferenceEquals(BoundProbe.GenericBounds(bound,19,23),bound)||bound.Value!=17)return false;
                    var box=new BoundBoxLeaf<int>{Value=29};if(!object.ReferenceEquals(BoundProbe.GenericProjection(box,31,37),box)||box.Value!=29)return false;
                    return object.ReferenceEquals(BoundProbe.InvariantProjection(box,41),box)&&box.Value==29;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "BoundRuntime", "BoundRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("BoundRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalBoundRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original constraint runtime behavior failed."); }
        var expectedTrace = (string)before.GetType("BoundTrace")!.GetField("Value")!.GetValue(null)!;
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 12; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Constraint analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "BoundProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 9)
        { throw new InvalidOperationException("Expected nine constraint fixes; got " + fixes); }
        document = document.Project.AddDocument("NewBoundDomains.cs", """
            public class BoundSibling:BoundMiddle{}
            public class BoundStringBox:BoundBoxLeaf<string>{}
            public static class NewBoundRunner{
                public static bool Run(){
                    BoundTrace.Value="";var first=new string('x',3);var second=new string('y',4);var third=new string('z',5);
                    if(!object.ReferenceEquals(BoundProbe.Self(first),first)||!object.ReferenceEquals(BoundProbe.Cycle(first,second),first)||!object.ReferenceEquals(BoundProbe.CycleThree(first,second,third),first))return false;
                    if(BoundProbe.StructCycle(43m,47m)!=43m)return false;
                    var leaf=new BoundSibling();if(!object.ReferenceEquals(BoundProbe.Strongest(leaf),leaf)||!object.ReferenceEquals(BoundProbe.CycleBase(leaf,new BoundSibling()),leaf))return false;
                    var bound=new BoundStringBox{Value=first};if(!object.ReferenceEquals(BoundProbe.GenericBounds(bound,second,third),bound)||!object.ReferenceEquals(bound.Value,first))return false;
                    var box=new BoundBoxLeaf<string>{Value=second};if(!object.ReferenceEquals(BoundProbe.GenericProjection(box,first,third),box)||!object.ReferenceEquals(box.Value,second))return false;
                    return object.ReferenceEquals(BoundProbe.InvariantProjection(box,first),box)&&object.ReferenceEquals(box.Value,second);
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var runner in Runners)
        {
            if (after.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)after.GetType("BoundTrace")!.GetField("Value")!.GetValue(null)! != expectedTrace)
            { throw new InvalidOperationException("Constraint fixes changed values, identity or helper effects: " + runner); }
        }
        Program.Log.Info("PASS ConstraintCodeFixCyclesBasesProjectionsIdentityEffectsAndNewDomains");
        var invalid = document.Project.AddDocument("InvalidBoundDomains.cs", "public static class InvalidBoundDomains{public static BoundRoot Base(BoundRoot value)=>BoundProbe.Strongest(value);public static string Value(string value)=>BoundProbe.StructCycle(value,value);}");
        var rejected = (await invalid.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (rejected.GetDiagnostics(cancellationToken).Count(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Id is "CS0311" or "CS0453"; }) != 2)
        { throw new InvalidOperationException("Normalized constraints must reject invalid base and value domains."); }
        Program.Log.Info("PASS ConstraintCodeFixRejectsInvalidBaseAndValueDomains");
        return fixes + 2;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Constraint runtime emission failed: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
