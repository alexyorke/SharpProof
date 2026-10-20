using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class NullableConstraintRuntimeChecks
{
    private static readonly string[] Runners = ["OriginalNullableBoundRunner", "NewNullableBoundRunner"];

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            public static class NullableBoundTrace{public static string Value="";}
            public class RuntimeNullableRoot{}
            public class RuntimeNullableMiddle:RuntimeNullableRoot{}
            public class RuntimeNullableLeaf:RuntimeNullableMiddle{}
            public interface RuntimeNullableContract{}
            public interface RuntimeNullableChild:RuntimeNullableContract{}
            public class RuntimeNullableItem:RuntimeNullableChild{}
            public class RuntimeNullableBox<T>{public T Value=default!;}
            public class RuntimeNullableBoxLeaf<T>:RuntimeNullableBox<T>{}
            public static class NullableBoundProbe{
                public static RuntimeNullableLeaf? Input(RuntimeNullableLeaf? value)=>Root(value);
                public static RuntimeNullableLeaf Control(RuntimeNullableLeaf value)=>Root(value);
                public static RuntimeNullableLeaf? Both(RuntimeNullableLeaf? value)=>Root(Middle(value));
                public static RuntimeNullableLeaf Mixed(RuntimeNullableLeaf value)=>StrictRoot(Middle(value));
                public static RuntimeNullableLeaf NotNull(RuntimeNullableLeaf value)=>Root(Required(value));
                public static RuntimeNullableItem? InterfaceInput(RuntimeNullableItem? value)=>Contract(value);
                public static RuntimeNullableItem InterfaceBoth(RuntimeNullableItem value)=>StrictContract(Child(value));
                public static RuntimeNullableItem InterfaceDuplicate(RuntimeNullableItem value)=>Contract(StrictContract(value));
                public static string? Cycle(string first,string? second){Keep(first,second);Keep(second,first);return second;}
                public static RuntimeNullableBoxLeaf<int>? Projection(RuntimeNullableBoxLeaf<int>? value,int other){Box(value,other);return value;}
                private static T Root<T>(T value) where T:RuntimeNullableRoot?{NullableBoundTrace.Value+="root;";return value;}
                private static T Middle<T>(T value) where T:RuntimeNullableMiddle?{NullableBoundTrace.Value+="middle;";return value;}
                private static T StrictRoot<T>(T value) where T:RuntimeNullableRoot{NullableBoundTrace.Value+="strict;";return value;}
                private static T Required<T>(T value) where T:notnull{NullableBoundTrace.Value+="required;";return value;}
                private static T Contract<T>(T value) where T:RuntimeNullableContract?{NullableBoundTrace.Value+="contract;";return value;}
                private static T Child<T>(T value) where T:RuntimeNullableChild?{NullableBoundTrace.Value+="child;";return value;}
                private static T StrictContract<T>(T value) where T:RuntimeNullableContract{NullableBoundTrace.Value+="strict-contract;";return value;}
                private static T Keep<T,U>(T value,U other) where T:U?{NullableBoundTrace.Value+="keep;";return value;}
                private static T Box<T,U>(T value,U other) where T:RuntimeNullableBox<U>?{NullableBoundTrace.Value+="box;";return value;}
            }
            public static class OriginalNullableBoundRunner{
                public static bool Run(){
                    NullableBoundTrace.Value="";var leaf=new RuntimeNullableLeaf();RuntimeNullableLeaf? absent=null;
                    if(!object.ReferenceEquals(NullableBoundProbe.Input(leaf),leaf)||NullableBoundProbe.Input(absent)is not null)return false;
                    if(!object.ReferenceEquals(NullableBoundProbe.Control(leaf),leaf))return false;
                    if(!object.ReferenceEquals(NullableBoundProbe.Both(leaf),leaf)||NullableBoundProbe.Both(absent)is not null)return false;
                    if(!object.ReferenceEquals(NullableBoundProbe.Mixed(leaf),leaf)||!object.ReferenceEquals(NullableBoundProbe.NotNull(leaf),leaf))return false;
                    var item=new RuntimeNullableItem();RuntimeNullableItem? missing=null;
                    if(!object.ReferenceEquals(NullableBoundProbe.InterfaceInput(item),item)||NullableBoundProbe.InterfaceInput(missing)is not null)return false;
                    if(!object.ReferenceEquals(NullableBoundProbe.InterfaceBoth(item),item)||!object.ReferenceEquals(NullableBoundProbe.InterfaceDuplicate(item),item))return false;
                    string? second=null;if(NullableBoundProbe.Cycle("first",second)is not null)return false;
                    RuntimeNullableBoxLeaf<int>? box=new(){Value=11};if(!object.ReferenceEquals(NullableBoundProbe.Projection(box,13),box)||NullableBoundProbe.Projection(null,17)is not null)return false;
                    return box.Value==11;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "NullableBoundRuntime", "NullableBoundRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("NullableBoundRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalNullableBoundRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original nullable constraint behavior failed."); }
        var expectedTrace = (string)before.GetType("NullableBoundTrace")!.GetField("Value")!.GetValue(null)!;
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 13; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Nullable constraint analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "NullableBoundProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 10)
        { throw new InvalidOperationException("Expected ten nullable constraint fixes; got " + fixes); }
        document = document.Project.AddDocument("NewNullableBoundDomains.cs", """
            #nullable enable
            public class RuntimeNullableSibling:RuntimeNullableMiddle{}
            public class RuntimeNullableRootSibling:RuntimeNullableRoot{}
            public class RuntimeNullableOtherItem:RuntimeNullableChild{}
            public struct RuntimeNullableStruct:RuntimeNullableChild{public int Value;}
            public static class NewNullableBoundRunner{
                public static bool Run(){
                    NullableBoundTrace.Value="";var leaf=new RuntimeNullableSibling();RuntimeNullableSibling? absent=null;
                    if(!object.ReferenceEquals(NullableBoundProbe.Input(leaf),leaf)||NullableBoundProbe.Input(absent)is not null)return false;
                    if(NullableBoundProbe.Control<RuntimeNullableSibling?>(null)is not null)return false;
                    if(!object.ReferenceEquals(NullableBoundProbe.Both(leaf),leaf)||NullableBoundProbe.Both(absent)is not null)return false;
                    if(!object.ReferenceEquals(NullableBoundProbe.Mixed(leaf),leaf))return false;
                    var root=new RuntimeNullableRootSibling();if(!object.ReferenceEquals(NullableBoundProbe.NotNull(root),root))return false;
                    var item=new RuntimeNullableOtherItem();RuntimeNullableOtherItem? missing=null;
                    if(!object.ReferenceEquals(NullableBoundProbe.InterfaceInput(item),item)||NullableBoundProbe.InterfaceInput(missing)is not null)return false;
                    var payload=new RuntimeNullableStruct{Value=19};if(NullableBoundProbe.InterfaceBoth(payload).Value!=19||NullableBoundProbe.InterfaceDuplicate(payload).Value!=19)return false;
                    if(NullableBoundProbe.Cycle(leaf,absent)is not null)return false;
                    var text=new string('x',3);RuntimeNullableBoxLeaf<string>? box=new(){Value=text};
                    if(!object.ReferenceEquals(NullableBoundProbe.Projection(box,"other"),box)||NullableBoundProbe.Projection<string>(null,"last")is not null)return false;
                    return object.ReferenceEquals(box.Value,text);
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var runner in Runners)
        {
            if (after.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)after.GetType("NullableBoundTrace")!.GetField("Value")!.GetValue(null)! != expectedTrace)
            { throw new InvalidOperationException("Nullable fixes changed null handling, identity or helper effects: " + runner); }
        }
        Program.Log.Info("PASS NullableConstraintCodeFixNullIdentityEffectsStructsAndNewDomains");
        var invalid = document.Project.AddDocument("InvalidNullableBoundDomain.cs", "#nullable enable\npublic static class InvalidNullableBoundDomain{public static object? Base(object value)=>NullableBoundProbe.Input<object>(value);public static int Value(int value)=>NullableBoundProbe.InterfaceInput<int>(value);}");
        var rejected = (await invalid.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (!rejected.GetDiagnostics(cancellationToken).Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS0311"; })
            || !rejected.GetDiagnostics(cancellationToken).Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS0452"; }))
        { throw new InvalidOperationException("Nullable constraints must retain required base and reference domains."); }
        var warningDocument = document.Project.AddDocument("NullableBoundWarning.cs", "#nullable enable\npublic static class NullableBoundWarning{public static RuntimeNullableSibling? Run(RuntimeNullableSibling? value)=>NullableBoundProbe.Mixed<RuntimeNullableSibling?>(value);}");
        var warned = (await warningDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (!warned.GetDiagnostics(cancellationToken).Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning && diagnostic.Id is "CS8631" or "CS8634"; }))
        { throw new InvalidOperationException("Non-null base requirements must retain nullable argument diagnostics."); }
        Program.Log.Info("PASS NullableConstraintCodeFixPreservesDomainErrorsAndNullabilityWarnings");
        return fixes + 2;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Nullable constraint runtime emission failed or added warnings: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
