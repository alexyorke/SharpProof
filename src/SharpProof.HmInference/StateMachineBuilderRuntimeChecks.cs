using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class StateMachineBuilderRuntimeChecks
{
    private static readonly string[] Runners = ["OriginalStateMachineRunner", "NewStateMachineRunner"];

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        var support = CustomBuilderChecks.Support;
        var first = support.IndexOf("public struct ProbeBuilder<T>", StringComparison.Ordinal);
        var last = support.IndexOf("public struct ProbeBuilder {", StringComparison.Ordinal);
        var builder = support[first..last].Replace("public ProbeTask<T> Task", "public Task<T> Task", StringComparison.Ordinal)
            .Replace("return new(inner.Task);", "return inner.Task;", StringComparison.Ordinal);
        var startBuilder = builder.Replace("ProbeBuilder", "StateStartBuilder", StringComparison.Ordinal).Replace("public void Start<S>(ref S state) where S:IAsyncStateMachine", "public void Start<S>(ref S state) where S:T,IAsyncStateMachine", StringComparison.Ordinal);
        var safeBuilder = builder.Replace("ProbeBuilder", "StateSafeBuilder", StringComparison.Ordinal).Replace("where A:INotifyCompletion where S:IAsyncStateMachine", "where A:INotifyCompletion where S:T,IAsyncStateMachine", StringComparison.Ordinal);
        var unsafeBuilder = builder.Replace("ProbeBuilder", "StateUnsafeBuilder", StringComparison.Ordinal).Replace("where A:ICriticalNotifyCompletion where S:IAsyncStateMachine", "where A:ICriticalNotifyCompletion where S:T,IAsyncStateMachine", StringComparison.Ordinal);
        var source = "#nullable enable\nusing System;using System.Threading;using System.Threading.Tasks;using System.Runtime.CompilerServices;\n" + support + startBuilder + safeBuilder + unsafeBuilder + """
            public struct ValueMarker:IAsyncStateMachine{public void MoveNext(){}public void SetStateMachine(IAsyncStateMachine state){}}
            public class Marker:IAsyncStateMachine{public void MoveNext()=>throw new InvalidOperationException("marker");public void SetStateMachine(IAsyncStateMachine state){}}
            public static class StateMachineProbe{
                [AsyncMethodBuilder(typeof(StateStartBuilder<>))] public static async Task<IAsyncStateMachine> ActiveStart(IAsyncStateMachine value,int tag,Task gate){await gate;return value;}
                [AsyncMethodBuilder(typeof(StateSafeBuilder<>))] public static async Task<IAsyncStateMachine> ActiveSafe(IAsyncStateMachine value,int tag,Task gate){await new SafeTask<int>(Gate(gate));return value;}
                [AsyncMethodBuilder(typeof(StateUnsafeBuilder<>))] public static async Task<IAsyncStateMachine> ActiveUnsafe(IAsyncStateMachine value,int tag,Task gate){await gate;return value;}
                [AsyncMethodBuilder(typeof(StateStartBuilder<>))] public static async Task<IAsyncStateMachine?> NullableStart(IAsyncStateMachine? value,int tag,Task gate){await gate;return value;}
                [AsyncMethodBuilder(typeof(StateSafeBuilder<>))] public static async Task<IAsyncStateMachine> UnusedSafe(IAsyncStateMachine value,int tag,Task gate){await gate;return value;}
                [AsyncMethodBuilder(typeof(StateUnsafeBuilder<>))] public static async Task<IAsyncStateMachine> UnusedUnsafe(IAsyncStateMachine value,int tag,Task gate){await new SafeTask<int>(Gate(gate));return value;}
                private static async Task<int> Gate(Task gate){await gate;return 0;}
            }
            public static class StateMachineRunner{
                public static bool Check<T>(Func<Task,Task<T>> start,T expected){
                    var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var task=start(gate.Task);if(task.IsCompleted)return false;
                    gate.SetResult();return object.ReferenceEquals(task.GetAwaiter().GetResult(),expected);
                }
                public static bool Failure(Func<Task,Task<IAsyncStateMachine>> start){
                    var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var task=start(gate.Task);var expected=new InvalidOperationException("failure");
                    gate.SetException(expected);try{task.GetAwaiter().GetResult();return false;}catch(InvalidOperationException actual){return object.ReferenceEquals(actual,expected);}
                }
                public static bool Cancellation(Func<Task,Task<IAsyncStateMachine>> start){
                    var token=new CancellationToken(true);var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var task=start(gate.Task);
                    gate.SetCanceled(token);try{task.GetAwaiter().GetResult();return false;}catch(OperationCanceledException actual){return actual.CancellationToken==token;}
                }
            }
            public static class OriginalStateMachineRunner{
                public static bool Run(){
                    BuilderTrace.Value="";IAsyncStateMachine value=new Marker();IAsyncStateMachine boxed=new ValueMarker();
                    return StateMachineRunner.Check(gate=>StateMachineProbe.ActiveStart(value,0,gate),value)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.ActiveStart(boxed,0,gate),boxed)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.ActiveSafe(value,0,gate),value)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.ActiveUnsafe(value,0,gate),value)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.NullableStart(null,0,gate),(IAsyncStateMachine?)null)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.UnusedSafe(value,0,gate),value)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.UnusedUnsafe(value,0,gate),value)
                        &&StateMachineRunner.Failure(gate=>StateMachineProbe.ActiveStart(value,0,gate))
                        &&StateMachineRunner.Cancellation(gate=>StateMachineProbe.ActiveStart(value,0,gate))
                        &&StateMachineRunner.Failure(gate=>StateMachineProbe.ActiveSafe(value,0,gate))
                        &&StateMachineRunner.Cancellation(gate=>StateMachineProbe.ActiveUnsafe(value,0,gate));
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "StateMachineRuntime", "StateMachineRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("StateMachineRuntime.cs", source);
        var originalCompilation = (CSharpCompilation)(await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var before = Load(originalCompilation, cancellationToken);
        if (before.GetType("OriginalStateMachineRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original state-machine builder behavior failed."); }
        var expectedTrace = (string)before.GetType("BuilderTrace")!.GetField("Value")!.GetValue(null)!;
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 9; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("State-machine builder analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "StateMachineProbe" };
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
        { throw new InvalidOperationException("Expected six state-machine builder fixes; got " + fixes); }
        document = document.Project.AddDocument("NewStateMachineDomains.cs", """
            #nullable enable
            using System.Runtime.CompilerServices;
            public static class NewStateMachineRunner{
                public static bool Run(){
                    BuilderTrace.Value="";IAsyncStateMachine value=new Marker();IAsyncStateMachine boxed=new ValueMarker();var payload=new string('x',3);
                    return StateMachineRunner.Check(gate=>StateMachineProbe.ActiveStart(value,"tag",gate),value)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.ActiveStart(boxed,"tag",gate),boxed)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.ActiveSafe(value,"tag",gate),value)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.ActiveUnsafe(value,"tag",gate),value)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.NullableStart(null,"tag",gate),(IAsyncStateMachine?)null)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.UnusedSafe(payload,47m,gate),payload)
                        &&StateMachineRunner.Check(gate=>StateMachineProbe.UnusedUnsafe(payload,47m,gate),payload)
                        &&StateMachineRunner.Failure(gate=>StateMachineProbe.ActiveStart(value,"tag",gate))
                        &&StateMachineRunner.Cancellation(gate=>StateMachineProbe.ActiveStart(value,"tag",gate))
                        &&StateMachineRunner.Failure(gate=>StateMachineProbe.ActiveSafe(value,"tag",gate))
                        &&StateMachineRunner.Cancellation(gate=>StateMachineProbe.ActiveUnsafe(value,"tag",gate));
                }
            }
            """);
        var afterCompilation = (CSharpCompilation)(await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var after = Load(afterCompilation, cancellationToken);
        foreach (var runner in Runners)
        {
            if (after.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)after.GetType("BuilderTrace")!.GetField("Value")!.GetValue(null)! != expectedTrace)
            { throw new InvalidOperationException("StateMachine fixes changed dispatch, identity or helper effects: " + runner); }
        }
        Program.Log.Info("PASS StateMachineBuilderCodeFixSuspensionIdentityFaultCancellationAndUnrelatedDomains");
        var releaseBefore = Load(originalCompilation.WithOptions(originalCompilation.Options.WithOptimizationLevel(OptimizationLevel.Release)), cancellationToken);
        if (releaseBefore.GetType("OriginalStateMachineRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original release builder behavior failed."); }
        var expectedReleaseTrace = (string)releaseBefore.GetType("BuilderTrace")!.GetField("Value")!.GetValue(null)!;
        var releaseAfter = Load(afterCompilation.WithOptions(afterCompilation.Options.WithOptimizationLevel(OptimizationLevel.Release)), cancellationToken);
        foreach (var runner in Runners)
        {
            if (releaseAfter.GetType(runner)!.GetMethod("Run")!.Invoke(null, null) is not true
                || (string)releaseAfter.GetType("BuilderTrace")!.GetField("Value")!.GetValue(null)! != expectedReleaseTrace)
            { throw new InvalidOperationException("Release builder fixes changed runtime behavior: " + runner); }
        }
        Program.Log.Info("PASS StateMachineBuilderCodeFixReleaseProtocolAndNewDomains");
        return fixes + 2;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("State-machine builder runtime emission failed or added warnings: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
