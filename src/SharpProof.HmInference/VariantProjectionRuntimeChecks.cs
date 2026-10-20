using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class VariantProjectionRuntimeChecks
{
    private static readonly string[] Runners = ["OriginalProjectionRunner", "NewProjectionRunner"];

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        var source = "using System.Linq;\n" + VariantProjectionChecks.Support + """
            public static class ProjectionApi{
                public static U Read<T,U>(T value,U other) where T:BoundProducer<U> =>value.Read();
                public static void Write<T,U>(T value,U other) where T:BoundConsumer<U> =>value.Write(other);
            }
            public static class MultiVariantProbe{
                public static System.Func<object> Nested(NestedSplitProducer<string> value,System.Func<object> other)=>ProjectionApi.Read(value,other);
                public static System.Collections.Generic.IEnumerable<string> ExactInterface(InterfaceSplitProducer<string> value,System.Collections.Generic.IEnumerable<string> other)=>ProjectionApi.Read(value,other);
                public static System.Collections.Generic.IEnumerable<object> Identity(ExactSplitProducer<string> value,System.Collections.Generic.IEnumerable<object> other)=>ProjectionApi.Read(value,other);
                public static object Array(SplitProducer<string> value,object other)=>ProjectionApi.Read(value,other);
                public static object Reverse(ReverseSplitProducer<string> value,object other)=>ProjectionApi.Read(value,other);
                public static object Inherited(InheritedSplitProducer<string> value,object other)=>ProjectionApi.Read(value,other);
                public static object Struct(ValueSplitProducer<string> value,object other)=>ProjectionApi.Read(value,other);
                public static int Fixed(SplitProducer<string> value,int other)=>ProjectionApi.Read(value,other);
                public static SplitConsumer<string> Sequence(SplitConsumer<string> value,System.Collections.Generic.List<string> items){ProjectionApi.Write(value,items);return value;}
                public static SplitConsumer<object> Variance(SplitConsumer<object> value,System.Collections.Generic.List<string> items){ProjectionApi.Write(value,items);return value;}
                public static SplitConsumer<string> FixedConsumer(SplitConsumer<string> value,int item){ProjectionApi.Write(value,item);return value;}
                public static object Multiple(SplitProducer<string> first,SplitProducer<System.DateTime> second,object other){_=ProjectionApi.Read(second,other);return ProjectionApi.Read(first,other);}
                public static async System.Threading.Tasks.Task<object> Async(SplitProducer<string> value,object other){await System.Threading.Tasks.Task.Yield();return ProjectionApi.Read(value,other);}
            }
            public static class OriginalProjectionRunner{
                public static bool Run(){
                    BoundTrace.Value="";var payload=new string('x',3);var values=new[]{payload};var marker=new object();
                    if(!object.ReferenceEquals(MultiVariantProbe.Array(new SplitProducer<string>{Values=values},23),values)
                        ||!object.ReferenceEquals(MultiVariantProbe.Reverse(new ReverseSplitProducer<string>{Values=values},marker),values)
                        ||!object.ReferenceEquals(MultiVariantProbe.Inherited(new InheritedSplitProducer<string>{Values=values},marker),values)
                        ||!object.ReferenceEquals(MultiVariantProbe.Struct(new ValueSplitProducer<string>{Values=values},marker),values)
                        ||MultiVariantProbe.Fixed(new SplitProducer<string>{Values=values},0)!=23)return false;
                    System.Func<string[]> callback=()=>values;var nested=MultiVariantProbe.Nested(new NestedSplitProducer<string>{Callback=callback},()=>marker);if(!object.ReferenceEquals(nested,callback)||!object.ReferenceEquals(nested(),values))return false;
                    var exact=new InterfaceSplitProducer<string>{Values=values,Sequence=new System.Collections.Generic.List<string>{payload}};var fixedItems=new object[]{payload};
                    var identity=new ExactSplitProducer<string>{Values=new System.Collections.Generic.List<string>{payload},Fixed=fixedItems};
                    if(!object.ReferenceEquals(MultiVariantProbe.ExactInterface(exact,System.Array.Empty<string>()),exact.Sequence)
                        ||!object.ReferenceEquals(MultiVariantProbe.Identity(identity,System.Array.Empty<object>()),fixedItems))return false;
                    var items=new System.Collections.Generic.List<string>{payload};var sequence=new SplitConsumer<string>();var variance=new SplitConsumer<object>();var fixedConsumer=new SplitConsumer<string>();
                    if(!object.ReferenceEquals(MultiVariantProbe.Sequence(sequence,items),sequence)||!object.ReferenceEquals(sequence.Values,items)||!object.ReferenceEquals(sequence.Values.First(),payload)
                        ||!object.ReferenceEquals(MultiVariantProbe.Variance(variance,items),variance)||!object.ReferenceEquals(variance.Values,items)||!object.ReferenceEquals(variance.Values.First(),payload)
                        ||!object.ReferenceEquals(MultiVariantProbe.FixedConsumer(fixedConsumer,23),fixedConsumer)||fixedConsumer.Fixed!=23)return false;
                    if(!object.ReferenceEquals(MultiVariantProbe.Multiple(new SplitProducer<string>{Values=values},new SplitProducer<System.DateTime>{Values=new[]{new System.DateTime(2020,1,1)}},marker),values))return false;
                    return object.ReferenceEquals(MultiVariantProbe.Async(new SplitProducer<string>{Values=values},marker).GetAwaiter().GetResult(),values);
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "VariantProjectionRuntime", "VariantProjectionRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("VariantProjectionRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalProjectionRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original multi-projection behavior failed."); }
        var trace = (string)before.GetType("BoundTrace")!.GetField("Value")!.GetValue(null)!;
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 16; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Multi-projection analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "MultiVariantProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
            Program.Log.Info("PASS MultiProjectionFix" + root.FindNode(diagnostic.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().First().Identifier.ValueText);
        }
        if (fixes != 13)
        { throw new InvalidOperationException("Expected thirteen multi-projection fixes; got " + fixes); }
        document = document.Project.AddDocument("NewProjectionDomains.cs", """
            using System.Linq;
            public class ProjectionBase{}
            public sealed class ProjectionDerived:ProjectionBase{}
            public static class NewProjectionRunner{
                public static bool Run(){
                    BoundTrace.Value="";var payload=new ProjectionDerived();var values=new[]{payload};var marker=new object();
                    var numbers=new[]{23};var decimals=new[]{23m};var optional=new decimal?[]{23m,null};
                    if(!object.ReferenceEquals(MultiVariantProbe.Array(new SplitProducer<ProjectionDerived>{Values=values},23),values)
                        ||!object.ReferenceEquals(MultiVariantProbe.Reverse(new ReverseSplitProducer<int>{Values=numbers},marker),numbers)
                        ||!object.ReferenceEquals(MultiVariantProbe.Inherited(new InheritedSplitProducer<decimal>{Values=decimals},marker),decimals)
                        ||!object.ReferenceEquals(MultiVariantProbe.Struct(new ValueSplitProducer<decimal?>{Values=optional},marker),optional)
                        ||MultiVariantProbe.Fixed(new SplitProducer<ProjectionDerived>{Values=values},0)!=23)return false;
                    System.Func<decimal?[]> callback=()=>optional;var nested=MultiVariantProbe.Nested(new NestedSplitProducer<decimal?>{Callback=callback},()=>marker);if(!object.ReferenceEquals(nested,callback)||!object.ReferenceEquals(nested(),optional))return false;
                    var exact=new InterfaceSplitProducer<decimal>{Values=decimals,Sequence=new System.Collections.Generic.List<decimal>{23m}};var fixedItems=new object[]{payload};
                    var identity=new ExactSplitProducer<decimal?>{Values=new System.Collections.Generic.List<decimal?>{23m,null},Fixed=fixedItems};
                    if(!object.ReferenceEquals(MultiVariantProbe.ExactInterface(exact,System.Array.Empty<decimal>()),exact.Sequence)
                        ||!object.ReferenceEquals(MultiVariantProbe.Identity(identity,System.Array.Empty<object>()),fixedItems))return false;
                    var items=new System.Collections.Generic.List<decimal>{23m};var sequence=new SplitConsumer<decimal>();
                    var references=new System.Collections.Generic.List<ProjectionDerived>{payload};var variance=new SplitConsumer<ProjectionBase>();var fixedConsumer=new SplitConsumer<decimal?>();
                    if(!object.ReferenceEquals(MultiVariantProbe.Sequence(sequence,items),sequence)||!object.ReferenceEquals(sequence.Values,items)||sequence.Values.First()!=23m
                        ||!object.ReferenceEquals(MultiVariantProbe.Variance(variance,references),variance)||!object.ReferenceEquals(variance.Values,references)||!object.ReferenceEquals(variance.Values.First(),payload)
                        ||!object.ReferenceEquals(MultiVariantProbe.FixedConsumer(fixedConsumer,23),fixedConsumer)||fixedConsumer.Fixed!=23)return false;
                    if(!object.ReferenceEquals(MultiVariantProbe.Multiple(new SplitProducer<ProjectionDerived>{Values=values},new SplitProducer<decimal>{Values=decimals},marker),values))return false;
                    return object.ReferenceEquals(MultiVariantProbe.Async(new SplitProducer<ProjectionDerived>{Values=values},marker).GetAwaiter().GetResult(),values);
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var runner in Runners)
        {
            if (after.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)after.GetType("BoundTrace")!.GetField("Value")!.GetValue(null)! != trace)
            { throw new InvalidOperationException("Multi-projection fixes changed interface dispatch, identity or helper effects: " + runner); }
        }
        Program.Log.Info("PASS MultiProjectionCodeFixOriginalAndNewReferenceValueNullableAndAsyncDomains");
        var invalid = document.Project.AddDocument("InvalidProjectionDomains.cs", """
            public static class InvalidProjectionDomains{
                public static void Bad(){
                    _=MultiVariantProbe.Fixed(new SplitProducer<decimal>(),"bad");
                    _=MultiVariantProbe.Variance(new SplitConsumer<ProjectionBase>(),new System.Collections.Generic.List<int>());
                }
            }
            """);
        var errors = (await invalid.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!.GetDiagnostics(cancellationToken);
        if (!errors.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS1503"; })
            || !errors.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS0452"; }))
        { throw new InvalidOperationException("Fixed endpoints and reference variance must retain their required domains."); }
        Program.Log.Info("PASS MultiProjectionRejectsInvalidFixedAndVarianceDomains");
        return fixes + 2;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Projection runtime emission failed or added warnings: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
