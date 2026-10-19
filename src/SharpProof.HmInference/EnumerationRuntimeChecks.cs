using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class EnumerationRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "EnumerationFixes", "EnumerationFixes", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Probe.cs", """
            #nullable enable
            using System.Collections.Generic;
            using System.IO;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;
            public sealed class PatternSequence<T> {
                private readonly IEnumerable<T> values;
                public PatternSequence(IEnumerable<T> values) { this.values=values; }
                public IEnumerator<T> GetEnumerator()=>values.GetEnumerator();
            }
            public sealed class AsyncPatternSequence<T> {
                private readonly IAsyncEnumerable<T> values;
                public AsyncPatternSequence(IAsyncEnumerable<T> values) { this.values=values; }
                public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken token=default)=>values.GetAsyncEnumerator(token);
            }
            public sealed class Move<T> {}
            public static class Awaiters {
                public static TaskAwaiter<bool> GetAwaiter(this Move<int> value)=>Task.FromResult(false).GetAwaiter();
                public static TaskAwaiter<bool> GetAwaiter<T>(this Move<T> value)=>Task.FromResult(true).GetAwaiter();
            }
            public sealed class ImplicitSequence<T> {
                public Enumerator GetAsyncEnumerator()=>new();
                public sealed class Enumerator {
                    public Move<T> MoveNextAsync()=>new();
                    public T Current=>default!;
                }
            }
            public static class EnumerationProbe {
                public static async Task<int> PatternAsync(AsyncPatternSequence<int> source,int seed) {
                    await foreach(var value in source) seed=value;
                    return seed;
                }
                public static async Task<int> ImplicitAwaiter(ImplicitSequence<int> source,int seed,bool ignored) {
                    await foreach(var value in source) { seed=value; break; }
                    return seed;
                }
                public static int Cleanup;
                public static IEnumerable<int> Repeat(int value) {
                    try { yield return value; yield return value; }
                    finally { Cleanup++; }
                }
                public static IEnumerator<int> Single(int value) { yield return value; }
                public static IEnumerable<int> Copy(IEnumerable<int> source) { foreach(var value in source) yield return value; }
                public static IEnumerable<int> Pattern(PatternSequence<int> source) { foreach(var value in source) yield return value; }
                public static async IAsyncEnumerable<int> AsyncRepeat(int value,[EnumeratorCancellation] CancellationToken token=default) {
                    try {
                        await Task.Yield(); token.ThrowIfCancellationRequested(); yield return value;
                        await Task.Yield(); token.ThrowIfCancellationRequested(); yield return value;
                    } finally { Cleanup++; }
                }
                public static async IAsyncEnumerator<int> AsyncSingle(int value) { await Task.Yield(); yield return value; }
                public static async IAsyncEnumerable<int> Awaited(Task<int> task) { yield return await task; }
                public static async Task<int> Last(IAsyncEnumerable<int> source,int seed,CancellationToken token) {
                    await foreach(var value in source.WithCancellation(token).ConfigureAwait(false)) seed=value;
                    return seed;
                }
                public static void Append(List<int> sink,int value)=>sink.Add(value);
                public static async Task<int> Configured(Task<int> task)=>await task.ConfigureAwait(false);
                public static IEnumerable<int> Resource(MemoryStream resource,int value) { using(resource) { yield return value; } }
                public static IEnumerable<long> Widen(IEnumerable<int> source,int ignored) { foreach(long value in source) yield return value; }
                public static IEnumerable<long> WidenTuple(IEnumerable<(int,bool)> source,int ignored) { foreach((long value,bool flag) in source) yield return value; }
            }
            """);
        var provider = new HmSignatureCodeFixProvider();
        var applied = 0;
        for (var round = 0; round < 19; round++)
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
        if (applied != 15)
        { throw new InvalidOperationException($"Expected fifteen enumeration code fixes; applied {applied}."); }
        var finalCompilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var problems = finalCompilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (problems.Length != 0)
        { throw new InvalidOperationException(string.Join("\n", problems.Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        using var image = new MemoryStream();
        var emitted = finalCompilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        var probe = System.Reflection.Assembly.Load(image.ToArray()).GetType("EnumerationProbe")!;
        var marker = Guid.NewGuid();
        var repeated = (IEnumerable<Guid>)probe.GetMethod("Repeat")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!;
        if (!Equals(probe.GetField("Cleanup")!.GetValue(null), 0))
        { throw new InvalidOperationException("Iterator executed before enumeration."); }
        using (var iterator = repeated.GetEnumerator())
        {
            if (!iterator.MoveNext() || iterator.Current != marker || !Equals(probe.GetField("Cleanup")!.GetValue(null), 0))
            { throw new InvalidOperationException("Iterator initial yield changed."); }
        }
        if (!Equals(probe.GetField("Cleanup")!.GetValue(null), 1) || !repeated.SequenceEqual(new[] { marker, marker })
            || !Equals(probe.GetField("Cleanup")!.GetValue(null), 2))
        { throw new InvalidOperationException("Iterator finalizer did not run exactly once per enumeration."); }
        using (var single = (IEnumerator<Guid>)probe.GetMethod("Single")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!)
        {
            if (!single.MoveNext() || single.Current != marker || single.MoveNext())
            { throw new InvalidOperationException("IEnumerator yield changed."); }
        }
        Program.Log.Info("PASS IteratorLazinessFinalizationRuntime");
        var values = new[] { marker, Guid.Empty };
        var copied = (IEnumerable<Guid>)probe.GetMethod("Copy")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { values })!;
        var sequenceType = probe.Assembly.GetType("PatternSequence`1")!.MakeGenericType(typeof(Guid));
        var source = Activator.CreateInstance(sequenceType, new object[] { values })!;
        var patterned = (IEnumerable<Guid>)probe.GetMethod("Pattern")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { source })!;
        if (!copied.SequenceEqual(values) || !patterned.SequenceEqual(values))
        { throw new InvalidOperationException("Generic interface or pattern enumeration changed."); }
        Program.Log.Info("PASS PatternInterfaceEnumerationRuntime");
        using var resource = new MemoryStream();
        var resources = (IEnumerable<Guid>)probe.GetMethod("Resource")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { resource, marker })!;
        if (!resource.CanRead)
        { throw new InvalidOperationException("Iterator resource disposed before enumeration."); }
        using (var iterator = resources.GetEnumerator())
        {
            if (!iterator.MoveNext() || iterator.Current != marker)
            { throw new InvalidOperationException("Resource iterator lost value."); }
        }
        var widened = (IEnumerable<long>)probe.GetMethod("Widen")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { new[] { int.MinValue, int.MaxValue }, marker })!;
        var widenedTuple = (IEnumerable<long>)probe.GetMethod("WidenTuple")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { new[] { (int.MinValue, false), (int.MaxValue, true) }, marker })!;
        if (resource.CanRead || !widened.SequenceEqual(new long[] { int.MinValue, int.MaxValue }) || !widenedTuple.SequenceEqual(new long[] { int.MinValue, int.MaxValue }))
        { throw new InvalidOperationException("Resource disposal or iteration widening changed."); }
        Program.Log.Info("PASS IteratorResourceAndConversionRuntime");
        var asyncRepeated = (IAsyncEnumerable<Guid>)probe.GetMethod("AsyncRepeat")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker, cancellationToken })!;
        var early = asyncRepeated.GetAsyncEnumerator(cancellationToken);
        try
        {
            if (!await early.MoveNextAsync().ConfigureAwait(false) || early.Current != marker)
            { throw new InvalidOperationException("Async iterator initial yield changed."); }
        }
        finally { await early.DisposeAsync().ConfigureAwait(false); }
        if (!Equals(probe.GetField("Cleanup")!.GetValue(null), 3))
        { throw new InvalidOperationException("Async iterator early disposal lost finalizer."); }
        Program.Log.Info("PASS AsyncIteratorEarlyDisposalRuntime");
        var asyncSingle = (IAsyncEnumerator<Guid>)probe.GetMethod("AsyncSingle")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { marker })!;
        try
        {
            if (!await asyncSingle.MoveNextAsync().ConfigureAwait(false) || asyncSingle.Current != marker || await asyncSingle.MoveNextAsync().ConfigureAwait(false))
            { throw new InvalidOperationException("IAsyncEnumerator yield changed."); }
        }
        finally { await asyncSingle.DisposeAsync().ConfigureAwait(false); }
        Program.Log.Info("PASS AsyncEnumeratorRuntime");
        var patternType = probe.Assembly.GetType("AsyncPatternSequence`1")!.MakeGenericType(typeof(Guid));
        var asyncPattern = Activator.CreateInstance(patternType, new object[] { asyncRepeated })!;
        if (await ((Task<Guid>)probe.GetMethod("PatternAsync")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { asyncPattern, Guid.Empty })!).ConfigureAwait(false) != marker)
        { throw new InvalidOperationException("Custom async enumeration pattern changed."); }
        var implicitType = probe.Assembly.GetType("ImplicitSequence`1")!.MakeGenericType(typeof(int));
        var implicitSource = Activator.CreateInstance(implicitType)!;
        if (await ((Task<int>)probe.GetMethod("ImplicitAwaiter")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { implicitSource, 23, marker })!).ConfigureAwait(false) != 23)
        { throw new InvalidOperationException("Implicit enumeration awaiter overload changed."); }
        Program.Log.Info("PASS CustomAsyncPatternAndAwaiterBindingRuntime");
        var last = probe.GetMethod("Last")!.MakeGenericMethod(typeof(Guid));
        if (await ((Task<Guid>)last.Invoke(null, new object[] { asyncRepeated, Guid.Empty, cancellationToken })!).WaitAsync(cancellationToken).ConfigureAwait(false) != marker)
        { throw new InvalidOperationException("Configured async enumeration completion changed."); }
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cancelled.CancelAsync().ConfigureAwait(false);
        try
        {
            await ((Task<Guid>)last.Invoke(null, new object[] { asyncRepeated, Guid.Empty, cancelled.Token })!).ConfigureAwait(false);
            throw new InvalidOperationException("Async enumeration lost cancellation.");
        }
        catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
        var fault = new InvalidOperationException("marker");
        try
        {
            await ((Task<Guid>)last.Invoke(null, new object[] { Failing(fault, cancellationToken), Guid.Empty, cancellationToken })!).ConfigureAwait(false);
            throw new InvalidOperationException("Async enumeration lost fault.");
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, fault)) { }
        Program.Log.Info("PASS AsyncEnumerationCompletionCancellationFault");
        var sink = new List<Guid>();
        probe.GetMethod("Append")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { sink, marker });
        if (!sink.SequenceEqual(new[] { marker }) || await ((Task<Guid>)probe.GetMethod("Configured")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { Task.FromResult(marker) })!).ConfigureAwait(false) != marker)
        { throw new InvalidOperationException("Generic instance method or configured await changed."); }
        Program.Log.Info("PASS GenericInstanceConfiguredAwaitRuntime");
        var pendingSource = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var awaited = (IAsyncEnumerable<Guid>)probe.GetMethod("Awaited")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { pendingSource.Task })!;
        var pendingIterator = awaited.GetAsyncEnumerator(cancellationToken);
        try
        {
            var pending = pendingIterator.MoveNextAsync();
            if (pending.IsCompleted)
            { throw new InvalidOperationException("Async iterator failed to suspend."); }
            pendingSource.SetResult(marker);
            if (!await pending.ConfigureAwait(false) || pendingIterator.Current != marker)
            { throw new InvalidOperationException("Async iterator lost suspended value."); }
        }
        finally { await pendingIterator.DisposeAsync().ConfigureAwait(false); }
        Program.Log.Info("PASS AsyncIteratorSuspensionRuntime");
        return 9;
    }

    private static async IAsyncEnumerable<Guid> Failing(Exception fault, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fault);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return Guid.Empty;
        throw fault;
    }
}
