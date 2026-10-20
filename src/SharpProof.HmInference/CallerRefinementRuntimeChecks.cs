using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class CallerRefinementRuntimeChecks
{
    private static readonly string[] Runners = ["OriginalRefinementRunner", "NewRefinementRunner"];

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        var source = CallerRefinementChecks.Support + """
            public static class RefinementProbe{
                public static object Scalar(ConcreteDispatchProducer value,object other)=>RefinementApi.Read(value,other);
                public static object Tuple(ConcreteDispatchProducer value,(object Tag,int Count) other)=>RefinementApi.Read(value,other.Tag);
                public static object Array(ConcreteDispatchProducer value,object[] other)=>RefinementApi.Read(value,other[0]);
                public static object Delegate(ConcreteDispatchProducer value,System.Func<object> other)=>RefinementApi.Read(value,other());
                public static object Group(ConcreteDispatchProducer value,object other)=>RefinementApi.Read(value,other);
                public static object Multiple(ConcreteDispatchProducer first,object left,ConcreteDispatchProducer second,object right,int tag){_=RefinementApi.Read(first,left);return RefinementApi.Read(second,right);}
            }
            public static class RefinementExtensions{
                public static object Refine(this ConcreteDispatchProducer value,object other)=>RefinementApi.Read(value,other);
            }
            public static class OriginalRefinementRunner{
                public static bool Run(){
                    BoundTrace.Value="";var values=new[]{"payload"};var value=new ConcreteDispatchProducer{Values=values};
                    if(!object.ReferenceEquals(RefinementProbe.Scalar(value,23),values)||RefinementApi.ArgumentType!=typeof(object)||RefinementApi.ReceiverType!=typeof(ConcreteDispatchProducer))return false;
                    if(!object.ReferenceEquals(RefinementProbe.Scalar(value,values),values)
                        ||!object.ReferenceEquals(RefinementProbe.Tuple(value,(23,0)),values)
                        ||!object.ReferenceEquals(RefinementProbe.Array(value,new[]{values}),values)
                        ||!object.ReferenceEquals(RefinementProbe.Delegate(value,()=>values),values))return false;
                    System.Func<ConcreteDispatchProducer,string[],object> group=RefinementProbe.Group;
                    if(group.Method.GetParameters()[1].ParameterType!=typeof(object)||group.Method.ReturnType!=typeof(object)||!object.ReferenceEquals(group(value,values),values))return false;
                    if(!object.ReferenceEquals(RefinementProbe.Multiple(value,23,value,new object(),0),values)
                        ||!object.ReferenceEquals(RefinementProbe.Multiple(value,new object(),value,23,0),values)
                        ||!object.ReferenceEquals(value.Refine(23),values))return false;
                    return RefinementApi.ArgumentType==typeof(object);
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "CallerRefinementRuntime", "CallerRefinementRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("CallerRefinementRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalRefinementRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original caller refinement behavior failed."); }
        var trace = (string)before.GetType("BoundTrace")!.GetField("Value")!.GetValue(null)!;
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 10; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Caller refinement analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "RefinementProbe" or "RefinementExtensions" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
            Program.Log.Info("PASS CallerRefinementFix" + root.FindNode(diagnostic.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().First().Identifier.ValueText);
        }
        if (fixes != 7)
        { throw new InvalidOperationException("Expected seven caller refinement fixes; got " + fixes); }
        document = document.Project.AddDocument("NewRefinementDomains.cs", """
            public static class NewRefinementRunner{
                public static bool Run(){
                    BoundTrace.Value="";var values=new[]{23m};var value=new SplitProducer<decimal>{Values=values};
                    if(!object.ReferenceEquals(RefinementProbe.Scalar(value,23),values)||RefinementApi.ArgumentType!=typeof(object)||RefinementApi.ReceiverType!=typeof(SplitProducer<decimal>))return false;
                    if(!object.ReferenceEquals(RefinementProbe.Scalar(value,values),values)
                        ||!object.ReferenceEquals(RefinementProbe.Tuple(value,(23,"new component")),values)
                        ||!object.ReferenceEquals(RefinementProbe.Array(value,new object[]{values}),values)
                        ||!object.ReferenceEquals(RefinementProbe.Delegate(value,()=>values),values))return false;
                    System.Func<SplitProducer<decimal>,string[],object> group=RefinementProbe.Group;
                    if(group.Method.GetParameters()[1].ParameterType!=typeof(object)||group.Method.ReturnType!=typeof(object)||!object.ReferenceEquals(group(value,new string[0]),values))return false;
                    var optional=new decimal?[]{23m,null};var first=new ValueSplitProducer<decimal?>{Values=optional};
                    if(!object.ReferenceEquals(RefinementProbe.Multiple(first,23,value,new object(),"new tag"),values)
                        ||!object.ReferenceEquals(RefinementProbe.Multiple(first,new object(),value,23,"new tag"),values)
                        ||!object.ReferenceEquals(first.Refine(23),optional))return false;
                    return RefinementApi.ArgumentType==typeof(object);
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var runner in Runners)
        {
            if (after.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)after.GetType("BoundTrace")!.GetField("Value")!.GetValue(null)! != trace)
            { throw new InvalidOperationException("Caller refinement changed dispatch, instantiated type arguments or identity: " + runner); }
        }
        Program.Log.Info("PASS CallerRefinementCodeFixPreservesBoxingGroupsExtensionsAndFreeComponents");
        return fixes + 1;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Caller refinement emission failed or added warnings: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
