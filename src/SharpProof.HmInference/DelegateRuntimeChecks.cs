using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class DelegateRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "DelegateFixes", "DelegateFixes", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Probe.cs", """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            public delegate R Map<A,R>(A value);
            public delegate Task<R> AsyncMap<A,R>(A value);
            public sealed class CaptureBox<T> {
                public T Value;
                public CaptureBox(T value) { Value=value; }
                public T Get()=>Value;
            }
            public static class DelegateProbe {
                private static T Echo<T>(T x)=>x;
                public static Func<int> Group(CaptureBox<int> box)=>box.Get;
                public static Func<int,int> StaticGroup(int ignored)=>Echo;
                public static object? Last;
                private static void Remember<T>(T x,T y) { Last=x; }
                private static void Send<T>(T x) { Last=x; }
                private static T Pick<T>(T x,T y)=>x;
                public static void Forward(Action<int> sink,int value)=>sink(value);
                public static Action<int> Capture(int seed)=>value=>Remember(seed,value);
                public static Func<Task<int>> AsyncCapture(int x)=>async()=>{ await Task.Yield(); return x; };
                public static Func<ValueTask<int>> ValueCapture(int x)=>async()=>{ await Task.Yield(); return x; };
                public static async Task<Func<int>> Nested(int x) { await Task.Yield(); return ()=>x; }
                public static Map<int,int> Custom(int seed)=>x=>Pick(seed,x);
                public static AsyncMap<int,int> CustomAsync(int seed)=>async x=>{ await Task.Yield(); return Pick(seed,x); };
                public static async void Fire(Task<int> task,Action<int> done) { done(await task); }
                public static Func<Task<int>> FromTask(Task<int> task)=>async()=>await task;
                public static Action Anonymous(int x)=>delegate { Send(x); };
            }
            """);
        var provider = new HmSignatureCodeFixProvider();
        var applied = 0;
        for (var round = 0; round < 16; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001"; });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            applied++;
        }
        if (applied != 12)
        { throw new InvalidOperationException($"Expected twelve delegate code fixes; applied {applied}."); }
        var finalCompilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var problems = finalCompilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (problems.Length != 0)
        { throw new InvalidOperationException(string.Join("\n", problems.Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        using var image = new MemoryStream();
        var emitted = finalCompilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        var probe = System.Reflection.Assembly.Load(image.ToArray()).GetType("DelegateProbe")!;
        var marker = Guid.NewGuid();
        var forwarded = Guid.Empty;
        Action<Guid> receive = (Guid /* SPINT001: all values valid - Every Guid can be forwarded. */ value) => forwarded = value;
        probe.GetMethod("Forward")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { receive, marker });
        if (forwarded != marker)
        { throw new InvalidOperationException("Void delegate forwarding failed."); }
        ((Action<Guid>)probe.GetMethod("Capture")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!)(Guid.Empty);
        if (!Equals(probe.GetField("Last")!.GetValue(null), marker))
        { throw new InvalidOperationException("Action capture failed."); }
        ((Action)probe.GetMethod("Anonymous")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!)();
        if (!Equals(probe.GetField("Last")!.GetValue(null), marker))
        { throw new InvalidOperationException("Anonymous method capture failed."); }
        Program.Log.Info("PASS VoidActionAnonymousCodeFixRuntime");
        var taskFactory = (Func<Task<Guid>>)probe.GetMethod("AsyncCapture")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!;
        var valueFactory = (Func<ValueTask<Guid>>)probe.GetMethod("ValueCapture")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!;
        var nested = (Task<Func<Guid>>)probe.GetMethod("Nested")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!;
        if (await taskFactory().WaitAsync(cancellationToken).ConfigureAwait(false) != marker || await valueFactory().ConfigureAwait(false) != marker
            || (await nested.WaitAsync(cancellationToken).ConfigureAwait(false))() != marker)
        { throw new InvalidOperationException("Async lambda context or capture failed."); }
        Program.Log.Info("PASS AsyncLambdaNestedContextRuntime");
        var custom = (Delegate)probe.GetMethod("Custom")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!;
        var customAsync = (Delegate)probe.GetMethod("CustomAsync")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!;
        if (!Equals(custom.DynamicInvoke(Guid.Empty), marker) || await ((Task<Guid>)customAsync.DynamicInvoke(Guid.Empty)!).WaitAsync(cancellationToken).ConfigureAwait(false) != marker)
        { throw new InvalidOperationException("Custom delegate generic code fix failed."); }
        var boxType = probe.Assembly.GetType("CaptureBox`1")!.MakeGenericType(typeof(Guid));
        var box = Activator.CreateInstance(boxType, new object[] { marker })!;
        if (((Func<Guid>)probe.GetMethod("Group")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { box })!)() != marker
            || ((Func<int, int>)probe.GetMethod("StaticGroup")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!)(23) != 23)
        { throw new InvalidOperationException("Method group receiver or instantiation changed."); }
        Program.Log.Info("PASS MethodGroupCodeFixRuntime");
        Program.Log.Info("PASS CustomDelegateCodeFixRuntime");
        var source = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<Guid> signal = (Guid /* SPINT001: all values valid - Every Guid can complete the callback. */ value) => completed.SetResult(value);
        probe.GetMethod("Fire")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { source.Task, signal });
        if (completed.Task.IsCompleted)
        { throw new InvalidOperationException("Async void callback ran before suspension completed."); }
        source.SetResult(marker);
        if (await completed.Task.WaitAsync(cancellationToken).ConfigureAwait(false) != marker)
        { throw new InvalidOperationException("Async void callback lost result."); }
        Program.Log.Info("PASS AsyncVoidCallbackRuntime");
        var fromTask = probe.GetMethod("FromTask")!.MakeGenericMethod(typeof(Guid));
        var suspended = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = ((Func<Task<Guid>>)fromTask.Invoke(null, new object[] { suspended.Task })!)();
        if (pending.IsCompleted)
        { throw new InvalidOperationException("Async lambda did not suspend."); }
        suspended.SetResult(marker);
        if (await pending.WaitAsync(cancellationToken).ConfigureAwait(false) != marker)
        { throw new InvalidOperationException("Async lambda resumed incorrectly."); }
        var fault = new InvalidOperationException("marker");
        try
        {
            await ((Func<Task<Guid>>)fromTask.Invoke(null, new object[] { Task.FromException<Guid>(fault) })!)().ConfigureAwait(false);
            throw new InvalidOperationException("Async lambda lost fault.");
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, fault)) { }
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cancelled.CancelAsync().ConfigureAwait(false);
        try
        {
            await ((Func<Task<Guid>>)fromTask.Invoke(null, new object[] { Task.FromCanceled<Guid>(cancelled.Token) })!)().ConfigureAwait(false);
            throw new InvalidOperationException("Async lambda lost cancellation.");
        }
        catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
        Program.Log.Info("PASS AsyncLambdaSuspensionFaultCancellation");
        return 6;
    }
}
