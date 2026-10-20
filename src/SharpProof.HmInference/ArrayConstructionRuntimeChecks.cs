using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class ArrayConstructionRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public sealed class ArrayTarget<T>{public T Value=default!;}
            public sealed class ArraySource<T>{public T Value=default!;public static implicit operator ArrayTarget<T>(ArraySource<T> value){ArrayProbe.Events.Add("convert");return new ArrayTarget<T>{Value=value.Value};}}
            public static class ArrayProbe {
                public static readonly List<string> Events=new();
                static T Mark<T>(T value){Events.Add("item");return value;}
                public static ArrayTarget<int>[] Convert(ArraySource<int> first,ArrayTarget<int> second)=>new[]{Mark(first),Mark(second)};
                public static object[] Box(int first,int second)=>new object[]{Mark(first),Mark(second)};
                public static int[] Widen(short value,bool tag)=>new int[]{value};
                public static long[] Numeric(short first,long second,bool tag)=>new[]{first,second};
                public static object[,] Rectangle(int value)=>new object[,]{{Mark(value),Mark(value)}};
                public static int[,] ImplicitRectangle(int first,int second)=>new[,]{{first,second},{second,first}};
                public static object[] Bare(int value){object[] result={Mark(value)};return result;}
                public static async Task<object[]> AwaitBox(Task<int> value)=>new object[]{Mark(await value)};
                public static async Task<int?[]> AwaitLift(Task<int> value,int? other)=>new[]{await value,other};
                public static int[] Sized(long size,bool tag)=>new int[size];
                public static string?[] Nulls(string value)=>new[]{value,null};
                public static int[] Defaults(int value)=>new[]{value,default};
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ArrayRuntime", "ArrayRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var original = project.AddDocument("Arrays.cs", source);
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(original, provider, FixAllScope.Document, "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = solution.GetDocument(original.Id)!;
        var fixedCompilation = (await changed.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var type = fixedCompilation.GetTypeByMetadataName("ArrayProbe")!;
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(method =>
        { ArgumentNullException.ThrowIfNull(method); return method.MethodKind == MethodKind.Ordinary && method.Name != "Mark"; }))
        {
            if (!method.IsGenericMethod)
            { throw new InvalidOperationException("Array Fix All missed " + method.Name); }
        }
        var warnings = fixedCompilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error; }).ToArray();
        if (warnings.Length != 0)
        {
            throw new InvalidOperationException(string.Join("\n", warnings.Select(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.ToString(); })));
        }
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("HmArrayRuntime", isCollectible: true);
        try
        {
            using var bytes = new MemoryStream();
            var emitted = fixedCompilation.Emit(bytes, cancellationToken: cancellationToken);
            if (!emitted.Success)
            { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
            bytes.Position = 0;
            var probe = loadContext.LoadFromStream(bytes).GetType("ArrayProbe")!;
            var events = (List<string>)probe.GetField("Events")!.GetValue(null)!;
            var marker = new Uri("https://example.invalid/array");
            object? Invoke(string name, Type[] arguments, params object?[] values)
            {
                ArgumentNullException.ThrowIfNull(name);
                ArgumentNullException.ThrowIfNull(arguments);
                ArgumentNullException.ThrowIfNull(values);
                return probe.GetMethod(name)!.MakeGenericMethod(arguments).Invoke(null, values);
            }
            var boxed = (object[])Invoke("Box", [typeof(Uri), typeof(decimal)], marker, 7.5m)!;
            if (boxed.GetType() != typeof(object[]) || !ReferenceEquals(boxed[0], marker) || !Equals(boxed[1], 7.5m) || events.Count != 2)
            { throw new InvalidOperationException("Boxed array changed order, element type or identity."); }
            events.Clear();
            var rectangle = (object[,])Invoke("Rectangle", [typeof(Uri)], marker)!;
            if (rectangle.Rank != 2 || rectangle.GetLength(0) != 1 || rectangle.GetLength(1) != 2
                || !ReferenceEquals(rectangle[0, 0], marker) || !ReferenceEquals(rectangle[0, 1], marker) || events.Count != 2)
            { throw new InvalidOperationException("Rectangular array lost shape, effects or identity."); }
            var implicitRectangle = (decimal[,])Invoke("ImplicitRectangle", [typeof(decimal)], 2m, 3m)!;
            var bare = (object[])Invoke("Bare", [typeof(Uri)], marker)!;
            var nulls = (Uri?[])Invoke("Nulls", [typeof(Uri)], marker)!;
            var defaults = (decimal[])Invoke("Defaults", [typeof(decimal)], 9m)!;
            if (implicitRectangle[1, 0] != 3m || !ReferenceEquals(bare[0], marker) || !ReferenceEquals(nulls[0], marker) || nulls[1] is not null
                || !defaults.SequenceEqual([9m, 0m]))
            { throw new InvalidOperationException("Implicit/bare/null/default array changed values."); }
            if (!((int[])Invoke("Widen", [typeof(Uri)], (short)9, marker)!).SequenceEqual([9])
                || !((long[])Invoke("Numeric", [typeof(Uri)], (short)2, 3L, marker)!).SequenceEqual([2L, 3L])
                || ((int[])Invoke("Sized", [typeof(Uri)], 3L, marker)!).Length != 3)
            { throw new InvalidOperationException("Array numeric conversion or dimension changed."); }
            events.Clear();
            var sourceType = probe.Assembly.GetType("ArraySource" + (char)96 + "1")!.MakeGenericType(typeof(Uri));
            var targetType = probe.Assembly.GetType("ArrayTarget" + (char)96 + "1")!.MakeGenericType(typeof(Uri));
            var convertedSource = Activator.CreateInstance(sourceType)!;
            var convertedTarget = Activator.CreateInstance(targetType)!;
            sourceType.GetField("Value")!.SetValue(convertedSource, marker);
            targetType.GetField("Value")!.SetValue(convertedTarget, marker);
            var converted = (Array)Invoke("Convert", [typeof(Uri)], convertedSource, convertedTarget)!;
            if (converted.GetType().GetElementType() != targetType || !ReferenceEquals(converted.GetValue(1), convertedTarget)
                || !ReferenceEquals(targetType.GetField("Value")!.GetValue(converted.GetValue(0)), marker)
                || !events.SequenceEqual(["item", "convert", "item"]))
            { throw new InvalidOperationException("Array user conversion changed order, target type or payload identity."); }
            var referenceDefaults = (Uri?[])Invoke("Defaults", [typeof(Uri)], marker)!;
            if (!ReferenceEquals(referenceDefaults[0], marker) || referenceDefaults[1] is not null)
            { throw new InvalidOperationException("Generic default array lost reference null semantics."); }
            try
            { Invoke("Sized", [typeof(Uri)], -1L, marker); throw new InvalidOperationException("Missing negative dimension failure."); }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is OverflowException) { }
            Program.Log.Info("PASS ArrayUserConversionOrderAndDefaultDomains");

            Program.Log.Info("PASS ArrayFixAllConversionsShapesAndNewDomains");

            events.Clear();
            var completion = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = (Task<object[]>)Invoke("AwaitBox", [typeof(Uri)], completion.Task)!;
            if (pending.IsCompleted || events.Count != 0)
            { throw new InvalidOperationException("Array await ran its initializer before completion."); }
            completion.SetResult(marker);
            var result = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!ReferenceEquals(result[0], marker) || events.Count != 1)
            { throw new InvalidOperationException("Awaited array lost identity or effects."); }
            var lifted = new TaskCompletionSource<decimal>(TaskCreationOptions.RunContinuationsAsynchronously);
            var liftedPending = (Task<decimal?[]>)Invoke("AwaitLift", [typeof(decimal)], lifted.Task, null)!;
            if (liftedPending.IsCompleted)
            { throw new InvalidOperationException("Nullable array await failed to suspend."); }
            lifted.SetResult(4.5m);
            var liftedResult = await liftedPending.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!liftedResult.SequenceEqual([4.5m, null]))
            { throw new InvalidOperationException("Awaited nullable array changed lifting."); }
            events.Clear();
            var failure = new InvalidOperationException("array-await");
            var fault = (Task<object[]>)Invoke("AwaitBox", [typeof(Uri)], Task.FromException<Uri>(failure))!;
            try
            { await fault.WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Missing array await fault."); }
            catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure)) { }
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(false);
#pragma warning disable SPINT003 // Deliberately canceled fixture token, independent of test cancellation.
            var cancellation = (Task<object[]>)Invoke("AwaitBox", [typeof(Uri)], Task.FromCanceled<Uri>(canceled.Token))!;
#pragma warning restore SPINT003
            try
            { await cancellation.WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Missing array await cancellation."); }
            catch (OperationCanceledException exception) when (exception.CancellationToken == canceled.Token) { }
            if (events.Count != 0)
            { throw new InvalidOperationException("Faulted/canceled array await ran later initializer effects."); }
            Program.Log.Info("PASS ArrayNullableAwaitSuspensionFaultAndCancellation");
            return 3;
        }
        finally { loadContext.Unload(); }
    }
}
