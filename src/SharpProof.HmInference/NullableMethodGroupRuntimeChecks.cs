using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class NullableMethodGroupRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public static class NullableMethodGroupProbe{
                public static readonly List<string> Events=new();
                public static TaskCompletionSource<bool> Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
                static void Need<T>(T value)where T:notnull{Events.Add(typeof(T).Name);}
                static void NeedClass<T>(T value)where T:class{Events.Add(typeof(T).Name);}
                static void NeedInterface<T>(T value)where T:IComparable{Events.Add(typeof(T).Name);}
                public static object NotNull(object value,int tag){Need(value);return value;}
                public static object Class(object value,int tag){NeedClass(value);return value;}
                public static IComparable Interface(IComparable value,int tag){NeedInterface(value);return value;}
                public static string Return(string value,int tag)=>value;
                public static string[] Array(string[] value,int tag)=>value;
                public static (string left,int right) Tuple((string left,int right) pair,int tag)=>pair;
                public static async ValueTask<object> ValueAsync(object value,int tag){await Gate.Task;NeedClass(value);return value;}
                public static ValueTask<object> UseValueAsync(string? value)=>((Func<string?,int,ValueTask<object>>)ValueAsync)(value,1);
                public static async Task<object> Async(object value,int tag){await Gate.Task;Need(value);return value;}
                public static object? UseNotNull(string? value)=>((Func<string?,int,object>)NotNull)(value,1);
                public static object? UseValue(int? value)=>((Func<object?,int,object>)NotNull)(value,1);
                public static object? UseClass(string? value)=>((Func<string?,int,object>)Class)(value,1);
                public static IComparable? UseInterface(string? value)=>((Func<string?,int,IComparable>)Interface)(value,1);
                public static string UseReturn(string? value)=>((Func<string?,int,string>)Return)(value,1);
                public static string[] UseArray(string?[] value)=>((Func<string?[],int,string[]>)Array)(value,1);
                public static (string,int) UseTuple((string?,int) pair)=>((Func<(string?,int),int,(string,int)>)Tuple)(pair,1);
                public static Task<object> UseAsync(string? value)=>((Func<string?,int,Task<object>>)Async)(value,1);
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "NullableMethodGroupRuntime", "NullableMethodGroupRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, warningLevel: 9999), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Calls.cs", source);
        var baseline = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var baselineErrors = baseline.GetDiagnostics(cancellationToken).Where(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return Inference.IsCompilationError(diagnostic); }).ToArray();
        if (baselineErrors.Length != 0)
        { throw new InvalidOperationException("Invalid method-group runtime fixture: " + baselineErrors[0]); }
        var suppressed = new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal);
        foreach (var diagnostic in baseline.GetDiagnostics(cancellationToken))
        {
            if (diagnostic.Severity == DiagnosticSeverity.Warning)
            { suppressed[diagnostic.Id] = ReportDiagnostic.Suppress; }
        }
        var options = ((CSharpCompilationOptions)baseline.Options).WithSpecificDiagnosticOptions(suppressed).WithGeneralDiagnosticOption(ReportDiagnostic.Error);
        document = document.Project.WithCompilationOptions(options).GetDocument(document.Id)!;
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        foreach (var name in new[] { "NotNull", "Class", "Interface", "Return", "Array", "Tuple", "Async", "ValueAsync" })
        {
            if (!changed.GetTypeByMetadataName("NullableMethodGroupProbe")!.GetMembers(name).OfType<IMethodSymbol>().Single().IsGenericMethod)
            { throw new InvalidOperationException("Nullable method-group Fix All missed " + name + ": " + string.Join("; ", Program.Results((CSharpCompilation)original, cancellationToken).Where(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == name; }))); }
        }
        var beforeContext = new System.Runtime.Loader.AssemblyLoadContext("BeforeNullableMethodGroups", isCollectible: true);
        var afterContext = new System.Runtime.Loader.AssemblyLoadContext("AfterNullableMethodGroups", isCollectible: true);
        try
        {
            Type Load(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Nullable caller emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                return loadContext.LoadFromStream(image).GetType("NullableMethodGroupProbe")!;
            }
            System.Reflection.MethodInfo Close(Type probe, string name, Type argument)
            {
                ArgumentNullException.ThrowIfNull(probe);
                ArgumentNullException.ThrowIfNull(name);
                ArgumentNullException.ThrowIfNull(argument);
                var method = probe.GetMethod(name)!;
                return method.IsGenericMethodDefinition ? method.MakeGenericMethod(Enumerable.Repeat(argument, method.GetGenericArguments().Length).ToArray()) : method;
            }

            var before = Load(original, beforeContext);
            var after = Load(changed, afterContext);
            var beforeEvents = (List<string>)before.GetField("Events")!.GetValue(null)!;
            var afterEvents = (List<string>)after.GetField("Events")!.GetValue(null)!;
            foreach (var name in new[] { "UseNotNull", "UseClass", "UseInterface", "UseReturn" })
            {
                foreach (var value in new string?[] { null, "payload" })
                {
                    beforeEvents.Clear();
                    afterEvents.Clear();
                    var oldValue = Close(before, name, typeof(string)).Invoke(null, [value]);
                    var newValue = Close(after, name, typeof(string)).Invoke(null, [value]);
                    if (!Equals(oldValue, newValue) || !beforeEvents.SequenceEqual(afterEvents))
                    { throw new InvalidOperationException("Nullable caller changed null/value/helper effects: " + name); }
                }
            }
            foreach (var value in new int?[] { null, 42 })
            {
                if (!Equals(Close(before, "UseValue", typeof(int)).Invoke(null, [value]), Close(after, "UseValue", typeof(int)).Invoke(null, [value])))
                { throw new InvalidOperationException("Nullable value boxing changed."); }
            }
            Program.Log.Info("PASS NullableMethodGroupOriginalNullsBoxingAndEffects");
            var marker = new Uri("https://example.invalid/nullable-method-group");
            if (!ReferenceEquals(after.GetMethod("NotNull")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [marker, marker]), marker))
            { throw new InvalidOperationException("Nullable caller refinement lost an independent generic domain."); }
            string?[] values = [null, "payload"];
            if (!ReferenceEquals(after.GetMethod("Array")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [values, marker]), values))
            { throw new InvalidOperationException("Nullable array refinement changed identity."); }
            var pair = ((string?, decimal))after.GetMethod("Tuple")!.MakeGenericMethod(typeof(decimal), typeof(Uri)).Invoke(null, [((string?)null, 3.5m), marker])!;
            if (pair.Item1 is not null || pair.Item2 != 3.5m)
            { throw new InvalidOperationException("Nullable tuple refinement froze or changed its sibling."); }
            Program.Log.Info("PASS NullableMethodGroupArrayIdentityTupleSiblingsAndNewDomains");
            var gateField = after.GetField("Gate")!;
            var gate = (TaskCompletionSource<bool>)gateField.GetValue(null)!;
            var pending = (Task<object>)Close(after, "UseAsync", typeof(string)).Invoke(null, [null])!;
            if (pending.IsCompleted)
            { throw new InvalidOperationException("Nullable caller async failed to suspend."); }
            gate.SetResult(true);
            if (await pending.WaitAsync(cancellationToken).ConfigureAwait(false) is not null)
            { throw new InvalidOperationException("Nullable async caller lost null."); }
            var method = after.GetMethod("Async")!.MakeGenericMethod(typeof(Uri));
            gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            gateField.SetValue(null, gate);
            var identity = (Task<object>)method.Invoke(null, [marker, marker])!;
            gate.SetResult(true);
            if (!ReferenceEquals(await identity.WaitAsync(cancellationToken).ConfigureAwait(false), marker))
            { throw new InvalidOperationException("Nullable async caller lost identity."); }
            gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            gateField.SetValue(null, gate);
            var failure = new InvalidOperationException("nullable-method-group");
            var fault = (Task<object>)method.Invoke(null, [marker, marker])!;
            gate.SetException(failure);
            try
            { await fault.WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Missing nullable async fault."); }
            catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure)) { }
            gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            gateField.SetValue(null, gate);
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(false);
            var cancellation = (Task<object>)method.Invoke(null, [marker, marker])!;
#pragma warning disable SPINT003 // Deliberately canceled fixture token, independent of test cancellation.
            gate.SetCanceled(canceled.Token);
#pragma warning restore SPINT003
            try
            { await cancellation.WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Missing nullable async cancellation."); }
            catch (OperationCanceledException exception) when (exception.CancellationToken == canceled.Token) { }
            Program.Log.Info("PASS NullableMethodGroupAsyncSuspensionFaultAndCancellation");
            foreach (var probe in new[] { before, after })
            {
                var valueGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                probe.GetField("Gate")!.SetValue(null, valueGate);
                var suspended = (ValueTask<object>)Close(probe, "UseValueAsync", typeof(string)).Invoke(null, [null])!;
                if (suspended.IsCompleted)
                { throw new InvalidOperationException("Nullable ValueTask delegate failed to suspend."); }
                valueGate.SetResult(true);
                if (await suspended.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false) is not null)
                { throw new InvalidOperationException("Nullable ValueTask delegate lost null."); }
            }
            var valueMethod = after.GetMethod("ValueAsync")!.MakeGenericMethod(typeof(Uri));
            var valueIdentity = (ValueTask<object>)valueMethod.Invoke(null, [marker, marker])!;
            if (!ReferenceEquals(await valueIdentity.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false), marker))
            { throw new InvalidOperationException("Nullable ValueTask delegate lost identity or its independent domain."); }
            Program.Log.Info("PASS NullableMethodGroupValueTaskSuspensionNullAndIdentity");
            return 4;
        }
        finally { beforeContext.Unload(); afterContext.Unload(); }
    }
}
