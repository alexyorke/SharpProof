using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class VariantConstraintRuntimeChecks
{
    private static readonly string[] Runners = ["OriginalVariantRunner", "NewVariantRunner"];

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        var source = VariantConstraintChecks.Support + """
            public static class BoundApi{
                public static T Sequence<T,U>(T value,U other) where T:System.Collections.Generic.IEnumerable<U> =>value;
                public static U Read<T,U>(T value,U other) where T:BoundProducer<U> =>value.Read();
                public static void Write<T,U>(T target,U value) where T:BoundConsumer<U> =>target.Write(value);
            }
            public static class VariantProbe{
                public static string[] Sequence(string[] value,object other)=>BoundApi.Sequence(value,other);
                public static int?[] NullableSequence(int?[] value,int? other)=>BoundApi.Sequence(value,other);
                public static int[] ValueSequence(int[] value,int other)=>BoundApi.Sequence(value,other);
                public static object Produce(BoundProducerBox<string> value,object other)=>BoundApi.Read(value,other);
                public static BoundConsumerBox<object> Consume(BoundConsumerBox<object> value,string item){BoundApi.Write(value,item);return value;}
                public static int Value(BoundProducerBox<int> value,int other)=>BoundApi.Read(value,other);
                public static object? Nullable(BoundProducerBox<string?> value,object? other)=>BoundApi.Read(value,other);
                public static async System.Threading.Tasks.Task<object> Async(BoundProducerBox<string> value,object other){await System.Threading.Tasks.Task.Yield();return BoundApi.Read(value,other);}
                public static object Multiple(BoundProducerBox<string> first,BoundProducerBox<System.Uri> second,object other,int ignored){var saved=BoundApi.Read(first,other);_=BoundApi.Read(second,other);return saved;}
                public static object[] Array(BoundProducerBox<string[]> value,object[] other)=>BoundApi.Read(value,other);
                public static System.Func<object> Delegate(BoundProducerBox<System.Func<string>> value,System.Func<object> other)=>BoundApi.Read(value,other);
                public static object Struct(BoundProducerValue<string> value,object other)=>BoundApi.Read(value,other);
            }
            public static class OriginalVariantRunner{
                public static bool Run(){
                    BoundTrace.Value="";var payload=new string('x',3);var marker=new object();
                    if(!object.ReferenceEquals(VariantProbe.Produce(new BoundProducerBox<string>{Value=payload},marker),payload))return false;
                    var sink=new BoundConsumerBox<object>();
                    if(!object.ReferenceEquals(VariantProbe.Consume(sink,payload),sink)||!object.ReferenceEquals(sink.Value,payload))return false;
                    if(VariantProbe.Value(new BoundProducerBox<int>{Value=23},0)!=23)return false;
                    if(VariantProbe.Nullable(new BoundProducerBox<string?>{Value=null},(object?)null)is not null
                        ||!object.ReferenceEquals(VariantProbe.Nullable(new BoundProducerBox<string?>{Value=payload},(object?)null),payload))return false;
                    if(!object.ReferenceEquals(VariantProbe.Async(new BoundProducerBox<string>{Value=payload},marker).GetAwaiter().GetResult(),payload))return false;
                    if(!object.ReferenceEquals(VariantProbe.Multiple(new BoundProducerBox<string>{Value=payload},new BoundProducerBox<System.Uri>{Value=new System.Uri("https://example.com")},marker,0),payload))return false;
                    var values=new[]{payload};if(!object.ReferenceEquals(VariantProbe.Array(new BoundProducerBox<string[]>{Value=values},new object[0]),values))return false;
                    if(!object.ReferenceEquals(VariantProbe.Sequence(values,marker),values))return false;var numbers=new[]{23};if(!object.ReferenceEquals(VariantProbe.ValueSequence(numbers,0),numbers))return false;
                    var nullableNumbers=new int?[]{23,null};if(!object.ReferenceEquals(VariantProbe.NullableSequence(nullableNumbers,(int?)null),nullableNumbers))return false;
                    System.Func<string> callback=()=>payload;var result=VariantProbe.Delegate(new BoundProducerBox<System.Func<string>>{Value=callback},()=>marker);
                    if(!object.ReferenceEquals(result,callback)||!object.ReferenceEquals(result(),payload))return false;
                    return object.ReferenceEquals(VariantProbe.Struct(new BoundProducerValue<string>{Value=payload},marker),payload);
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "VariantRuntime", "VariantRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("VariantRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalVariantRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original variant constraint behavior failed."); }
        var trace = (string)before.GetType("BoundTrace")!.GetField("Value")!.GetValue(null)!;
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 15; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Variant constraint analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "VariantProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
            Program.Log.Info("PASS VariantCodeFix" + root.FindNode(diagnostic.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().First().Identifier.ValueText);
        }
        if (fixes != 12)
        { throw new InvalidOperationException("Expected twelve variant constraint fixes; got " + fixes); }
        document = document.Project.AddDocument("NewVariantDomains.cs", """
            #nullable enable
            public class VariantBase{}
            public sealed class VariantDerived:VariantBase{}
            public static class NewVariantRunner{
                public static bool Run(){
                    BoundTrace.Value="";var payload=new VariantDerived();var marker=new VariantBase();
                    if(!object.ReferenceEquals(VariantProbe.Produce(new BoundProducerBox<VariantDerived>{Value=payload},marker),payload))return false;
                    var sink=new BoundConsumerBox<VariantBase>();
                    if(!object.ReferenceEquals(VariantProbe.Consume(sink,payload),sink)||!object.ReferenceEquals(sink.Value,payload))return false;
                    if(VariantProbe.Value(new BoundProducerBox<decimal>{Value=23m},0m)!=23m)return false;
                    if(VariantProbe.Nullable(new BoundProducerBox<VariantDerived?>{Value=null},(VariantBase?)null)is not null
                        ||!object.ReferenceEquals(VariantProbe.Nullable(new BoundProducerBox<VariantDerived?>{Value=payload},(VariantBase?)null),payload))return false;
                    if(!object.ReferenceEquals(VariantProbe.Async(new BoundProducerBox<VariantDerived>{Value=payload},marker).GetAwaiter().GetResult(),payload))return false;
                    if(!object.ReferenceEquals(VariantProbe.Multiple(new BoundProducerBox<VariantDerived>{Value=payload},new BoundProducerBox<VariantDerived>{Value=new VariantDerived()},marker,"new tag"),payload))return false;
                    var values=new[]{payload};if(!object.ReferenceEquals(VariantProbe.Array(new BoundProducerBox<VariantDerived[]>{Value=values},new VariantBase[0]),values))return false;
                    if(!object.ReferenceEquals(VariantProbe.Sequence(values,marker),values))return false;var numbers=new[]{23m};if(!object.ReferenceEquals(VariantProbe.ValueSequence(numbers,0m),numbers))return false;
                    var nullableNumbers=new decimal?[]{23m,null};if(!object.ReferenceEquals(VariantProbe.NullableSequence(nullableNumbers,(decimal?)null),nullableNumbers))return false;
                    System.Func<VariantDerived> callback=()=>payload;var result=VariantProbe.Delegate(new BoundProducerBox<System.Func<VariantDerived>>{Value=callback},()=>marker);
                    if(!object.ReferenceEquals(result,callback)||!object.ReferenceEquals(result(),payload))return false;
                    return object.ReferenceEquals(VariantProbe.Struct(new BoundProducerValue<VariantDerived>{Value=payload},marker),payload);
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var runner in Runners)
        {
            if (after.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)after.GetType("BoundTrace")!.GetField("Value")!.GetValue(null)! != trace)
            { throw new InvalidOperationException("Variant fixes changed dispatch, identity or helper effects: " + runner); }
        }
        Program.Log.Info("PASS VariantConstraintCodeFixDispatchNullableAsyncArrayDelegateAndValueDomains");
        var invalid = document.Project.AddDocument("InvalidVariantDomains.cs", """
            public static class InvalidVariantDomains{
                public static void Bad(){_=VariantProbe.Produce(new BoundProducerBox<int>(),new object());_=VariantProbe.Consume(new BoundConsumerBox<object>(),23);}
            }
            """);
        var errors = (await invalid.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!.GetDiagnostics(cancellationToken);
        if (errors.Count(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS0452"; }) != 2)
        { throw new InvalidOperationException("Reference variance must reject value-type element conversions."); }
        Program.Log.Info("PASS VariantConstraintRejectsBoxingAsReferenceVariance");
        return fixes + 2;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Variant runtime emission failed or added warnings: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
