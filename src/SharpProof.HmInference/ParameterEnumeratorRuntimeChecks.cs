using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class ParameterEnumeratorRuntimeChecks
{
    private static readonly string[] Runners = ["OriginalParameterEnumerationRunner", "NewParameterEnumerationRunner"];

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            public static class ParameterEnumerationTrace{public static string Value="";}
            public class Cursor<T>:System.Collections.Generic.IEnumerator<T>{
                private readonly T[] items;private int index=-1;public Cursor(T[] values)=>items=values;
                public bool MoveNext(){ParameterEnumerationTrace.Value+="move;";return ++index<items.Length;}
                public T Current{get{ParameterEnumerationTrace.Value+="current;";return items[index];}}
                object System.Collections.IEnumerator.Current=>Current!;public void Reset()=>throw new System.NotSupportedException();
                public void Dispose(){ParameterEnumerationTrace.Value+="dispose;";}
            }
            public struct ValueCursor<T>:System.Collections.Generic.IEnumerator<T>{
                private readonly T[] items;private int index;public ValueCursor(T[] values){items=values;index=-1;}
                public bool MoveNext(){ParameterEnumerationTrace.Value+="value-move;";return ++index<items.Length;}
                public T Current{get{ParameterEnumerationTrace.Value+="value-current;";return items[index];}}
                object System.Collections.IEnumerator.Current=>Current!;public void Reset()=>throw new System.NotSupportedException();
                public void Dispose(){ParameterEnumerationTrace.Value+="value-dispose;";}
            }
            public class AsyncCursor<T>:System.Collections.Generic.IAsyncEnumerator<T>{
                private readonly T[] items;private int index=-1;public AsyncCursor(T[] values)=>items=values;
                public async System.Threading.Tasks.ValueTask<bool> MoveNextAsync(){await System.Threading.Tasks.Task.Yield();ParameterEnumerationTrace.Value+="async-move;";return ++index<items.Length;}
                public T Current{get{ParameterEnumerationTrace.Value+="async-current;";return items[index];}}
                public async System.Threading.Tasks.ValueTask DisposeAsync(){await System.Threading.Tasks.Task.Yield();ParameterEnumerationTrace.Value+="async-dispose;";}
            }
            public class Sequence<T,E> where E:System.Collections.Generic.IEnumerator<T>{private readonly E enumerator;public Sequence(E value)=>enumerator=value;public E GetEnumerator()=>enumerator;}
            public class AsyncSequence<T,E> where E:System.Collections.Generic.IAsyncEnumerator<T>{private readonly E enumerator;public AsyncSequence(E value)=>enumerator=value;public E GetAsyncEnumerator()=>enumerator;}
            public static class ParameterEnumerationProbe{
                public static int Sync(Sequence<int,Cursor<int>> values,int seed){foreach(var item in values)seed=item;return seed;}
                public static int Struct(Sequence<int,ValueCursor<int>> values,int seed){foreach(var item in values)seed=item;return seed;}
                public static async System.Threading.Tasks.Task<int> Async(AsyncSequence<int,AsyncCursor<int>> values,int seed){await foreach(var item in values)seed=item;return seed;}
            }
            public static class OriginalParameterEnumerationRunner{
                public static bool Run(){
                    ParameterEnumerationTrace.Value="";var items=new[]{11,17,23};
                    if(ParameterEnumerationProbe.Sync(new Sequence<int,Cursor<int>>(new(items)),0)!=23)return false;
                    if(ParameterEnumerationProbe.Struct(new Sequence<int,ValueCursor<int>>(new(items)),0)!=23)return false;
                    return ParameterEnumerationProbe.Async(new AsyncSequence<int,AsyncCursor<int>>(new(items)),0).GetAwaiter().GetResult()==23;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ParameterEnumerationRuntime", "ParameterEnumerationRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("ParameterEnumerationRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalParameterEnumerationRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original parameter enumeration behavior failed."); }
        var expectedTrace = (string)before.GetType("ParameterEnumerationTrace")!.GetField("Value")!.GetValue(null)!;
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 6; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Parameter enumeration analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "ParameterEnumerationProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 3)
        { throw new InvalidOperationException("Expected three parameter enumeration fixes; got " + fixes); }
        document = document.Project.AddDocument("NewParameterEnumerationDomains.cs", """
            #nullable enable
            public static class NewParameterEnumerationRunner{
                public static bool Run(){
                    ParameterEnumerationTrace.Value="";var payload=new string('x',3);var items=new string?[]{"first",null,payload};
                    if(!object.ReferenceEquals(ParameterEnumerationProbe.Sync(new Sequence<string?,Cursor<string?>>(new(items)),null),payload))return false;
                    if(!object.ReferenceEquals(ParameterEnumerationProbe.Struct(new Sequence<string?,ValueCursor<string?>>(new(items)),null),payload))return false;
                    return object.ReferenceEquals(ParameterEnumerationProbe.Async(new AsyncSequence<string?,AsyncCursor<string?>>(new(items)),null).GetAwaiter().GetResult(),payload);
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var runner in Runners)
        {
            if (after.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)after.GetType("ParameterEnumerationTrace")!.GetField("Value")!.GetValue(null)! != expectedTrace)
            { throw new InvalidOperationException("ParameterEnumeration fixes changed dispatch, identity or helper effects: " + runner); }
        }
        Program.Log.Info("PASS ParameterEnumeratorCodeFixIterationDisposalSuspensionIdentityAndNullableDomains");
        return fixes + 1;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Parameter enumeration runtime emission failed or added warnings: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
