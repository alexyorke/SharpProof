using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class BuilderLayoutRuntimeChecks
{
    private static readonly string[] Runners = ["OriginalBuilderLayoutRunner", "NewBuilderLayoutRunner"];

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        var support = BuilderLayoutChecks.Support;
        var first = support.IndexOf("public struct LayoutBuilder<T>", StringComparison.Ordinal);
        var last = support.IndexOf("public readonly struct LayoutSignal", StringComparison.Ordinal);
        var unused = support[first..last].Replace("LayoutBuilder", "UnusedLayoutBuilder", StringComparison.Ordinal)
            .Replace("where S:unmanaged,IAsyncStateMachine", "where S:IAsyncStateMachine", StringComparison.Ordinal)
            .Replace("where A:INotifyCompletion where S:IAsyncStateMachine", "where A:INotifyCompletion where S:unmanaged,IAsyncStateMachine", StringComparison.Ordinal);
        var source = support + unused + """
            public static class BuilderLayoutProbe{
                public static async LayoutTask<int> Value(int value,string ignored){await new LayoutSignal();return value;}
                public static async LayoutTask<int> Local(int value,string ignored){var saved=value;await new LayoutSignal();return saved;}
                public static async LayoutTask<int> Phantom(LayoutPayload<int,string> value,string ignored){await new LayoutSignal();return value.Value;}
                public static async LayoutTask<int?> Nullable(int? value,string ignored){await new LayoutSignal();return value;}
                public static async LayoutTask<int> Awaiter(LayoutEmptyAwaitable<int> value,string ignored){await value;return 0;}
                [AsyncMethodBuilder(typeof(UnusedLayoutBuilder<>))] public static async LayoutTask<int> Free(int value,string ignored){await new LayoutCriticalSignal();return value;}
            }
            public static class OriginalBuilderLayoutRunner{
                public static bool Run(){
                    LayoutTrace.Value="";
                    return BuilderLayoutProbe.Value(23,"tag").GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Local(23,"tag").GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Phantom(new LayoutPayload<int,string>{Value=23},"tag").GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Nullable((int?)23,"tag").GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Nullable((int?)null,"tag").GetAwaiter().GetResult()is null
                        &&BuilderLayoutProbe.Awaiter(default(LayoutEmptyAwaitable<int>),"tag").GetAwaiter().GetResult()==0
                        &&BuilderLayoutProbe.Free(23,"tag").GetAwaiter().GetResult()==23;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "BuilderLayoutRuntime", "BuilderLayoutRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, optimizationLevel: OptimizationLevel.Release), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("BuilderLayoutRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalBuilderLayoutRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original builder layout behavior failed."); }
        var expectedTrace = (string)before.GetType("LayoutTrace")!.GetField("Value")!.GetValue(null)!;
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 9; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Builder layout analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "BuilderLayoutProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 6)
        { throw new InvalidOperationException("Expected six builder layout fixes; got " + fixes); }
        document = document.Project.AddDocument("NewBuilderLayoutDomains.cs", """
            #nullable enable
            public static class NewBuilderLayoutRunner{
                public static bool Run(){
                    LayoutTrace.Value="";var tag=new object();var payload=new string('x',3);
                    return BuilderLayoutProbe.Value((byte)23,tag).GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Local((byte)23,tag).GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Phantom(new LayoutPayload<byte,object[]>{Value=23},tag).GetAwaiter().GetResult()==23
                        &&BuilderLayoutProbe.Nullable((decimal?)23m,tag).GetAwaiter().GetResult()==23m
                        &&BuilderLayoutProbe.Nullable((decimal?)null,tag).GetAwaiter().GetResult()is null
                        &&BuilderLayoutProbe.Awaiter(default(LayoutEmptyAwaitable<byte>),tag).GetAwaiter().GetResult()==0
                        &&object.ReferenceEquals(BuilderLayoutProbe.Free(payload,tag).GetAwaiter().GetResult(),payload);
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var runner in Runners)
        {
            if (after.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)after.GetType("LayoutTrace")!.GetField("Value")!.GetValue(null)! != expectedTrace)
            { throw new InvalidOperationException("BuilderLayout fixes changed dispatch, identity or helper effects: " + runner); }
        }
        Program.Log.Info("PASS BuilderLayoutCodeFixValuesNullablePhysicalPhantomAndUnusedDomains");
        (string Name, Type[] Arguments, bool Managed)[] states =
        [
            ("Value", [typeof(byte), typeof(object)], false),
            ("Phantom", [typeof(byte), typeof(object[]), typeof(object)], false),
            ("Free", [typeof(string), typeof(object)], true)
        ];
        foreach (var entry in states)
        {
            var method = after.GetType("BuilderLayoutProbe")!.GetMethod(entry.Name)!;
            var attribute = method.GetCustomAttributesData().Single(item =>
            { ArgumentNullException.ThrowIfNull(item); return item.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute"; });
            var state = ((Type)attribute.ConstructorArguments[0].Value!).MakeGenericType(entry.Arguments);
            var check = typeof(System.Runtime.CompilerServices.RuntimeHelpers).GetMethod("IsReferenceOrContainsReferences")!.MakeGenericMethod(state);
            if (check.Invoke(null, null) is not bool managed || managed != entry.Managed)
            { throw new InvalidOperationException("State-machine physical layout mismatch: " + entry.Name); }
        }
        Program.Log.Info("PASS BuilderLayoutRuntimeFieldContracts");
        var invalid = document.Project.AddDocument("InvalidLayoutDomain.cs", "#nullable enable\npublic struct ManagedLayoutPayload{public string? Value;}public static class InvalidLayoutDomain{public static void Bad(){_=BuilderLayoutProbe.Value(\"managed\",0);_=BuilderLayoutProbe.Value(new ManagedLayoutPayload(),0);}}");
        var rejected = (await invalid.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (rejected.GetDiagnostics(cancellationToken).Count(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS8377"; }) != 2)
        { throw new InvalidOperationException("Layout constraints must reject managed reference and struct payloads."); }
        Program.Log.Info("PASS BuilderLayoutRejectsManagedPayloadDomains");
        return fixes + 3;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Builder layout runtime emission failed or added warnings: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
