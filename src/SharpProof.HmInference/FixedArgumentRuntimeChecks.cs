using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class FixedArgumentRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            #pragma warning disable HM0001
            public static class FixedApi{
                public static object? Stored;
                public static readonly List<string> Events=new();
                public static readonly Action<object> Callback=Capture;
                public static void Capture(object value){Events.Add("call");Stored=value;}
                public static void Nullable(object? value){Events.Add("nullable");Stored=value;}
                public static void Params(params object[] values){Events.Add("params");if(!ReferenceEquals(values[0],values[1]))throw new Exception("identity");Stored=values[0];}
                public static void Array(object[] values){Events.Add("array");Stored=values;}
                public static void Factory(Func<object> value){Events.Add("factory");Stored=value;}
                public static void Pair((object,int) value){Events.Add("pair");Stored=value;}
                public static void Echo<T>(T value){Events.Add("generic");Stored=value;}
                public static ref readonly object Identity(in object value)=>ref value;
            }
            public sealed class FixedBox{public FixedBox(object value){FixedApi.Events.Add("new");FixedApi.Stored=value;}}
            #pragma warning restore HM0001
            public static class ArgumentProbe{
                static T Read<T>(T value){FixedApi.Events.Add("value");return value;}
                public static object Call(object value,int tag){FixedApi.Capture(Read(value));return value;}
                public static object Constructor(object value,int tag){_=new FixedBox(Read(value));return value;}
                public static object Delegate(object value,int tag){FixedApi.Callback(Read(value));return value;}
                public static object Generic(object value,int tag){FixedApi.Echo<object>(Read(value));return value;}
                public static object Params(object value,int tag){FixedApi.Params(Read(value),Read(value));return value;}
                public static object? Nullable(object? value,int tag){FixedApi.Nullable(value);return value;}
                public static object[] Array(object[] value,int tag){FixedApi.Array(value);return value;}
                public static Func<object> Factory(Func<object> value,int tag){FixedApi.Factory(value);return value;}
                public static (object,int) Pair((object,int) value,int tag){FixedApi.Pair(value);return value;}
                public static (object,object) Conditional(bool flag,object left,object right,int tag){FixedApi.Capture(flag?Read(left):Read(right));return(left,right);}
                public static object CallerTarget(object value,int tag){FixedApi.Capture(value);return value;}
                public static object BoxedCaller(int value)=>CallerTarget(value,0);
                public static bool InAlias(object value,int tag){ref readonly object alias=ref FixedApi.Identity(value);return Unsafe.AreSame(ref value,ref Unsafe.AsRef(in alias));}
                public static async Task<object> Async(Task<object> value,int tag){FixedApi.Capture(await value);return await value;}
                public static async ValueTask<object> ValueAsync(ValueTask<object> value,int tag){FixedApi.Capture(await value);return await value;}
                public static async Task<object?> NullableAsync(Task<object?> value,int tag){FixedApi.Nullable(await value);return await value;}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "FixedArguments", "FixedArguments", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Arguments.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var symbol = changed.GetTypeByMetadataName("ArgumentProbe")!;
        foreach (var name in new[] { "Call", "Constructor", "Delegate", "Generic", "Params", "Nullable", "Array", "Factory", "Pair", "Async", "ValueAsync", "NullableAsync" })
        {
            if (symbol.GetMembers(name).OfType<IMethodSymbol>().Single().TypeParameters.Length != 2)
            { throw new InvalidOperationException("Fixed argument Fix All missed " + name); }
        }
        if (symbol.GetMembers("CallerTarget").OfType<IMethodSymbol>().Single().Parameters[0].Type.SpecialType != SpecialType.System_Object
            || symbol.GetMembers("InAlias").OfType<IMethodSymbol>().Single().Parameters[0].Type.SpecialType != SpecialType.System_Object
            || changed.GetTypeByMetadataName("FixedApi")!.GetMembers("Capture").OfType<IMethodSymbol>().Single().IsGenericMethod)
        { throw new InvalidOperationException("Fixed argument caller, readonly storage or fixed API contracts changed."); }
        Program.Log.Info("PASS FixedArgumentFixAllPreservesApiBoxingCallerAndReadonlyContracts");
        var beforeContext = new System.Runtime.Loader.AssemblyLoadContext("BeforeFixedArguments", isCollectible: true);
        var afterContext = new System.Runtime.Loader.AssemblyLoadContext("AfterFixedArguments", isCollectible: true);
        try
        {
            Type Load(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Fixed argument emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                return loadContext.LoadFromStream(image).GetType("ArgumentProbe")!;
            }
            object? Invoke(Type probe, string name, Type[] types, object?[] arguments)
            {
                ArgumentNullException.ThrowIfNull(probe);
                ArgumentNullException.ThrowIfNull(name);
                ArgumentNullException.ThrowIfNull(types);
                ArgumentNullException.ThrowIfNull(arguments);
                var method = probe.GetMethod(name)!;
                if (method.IsGenericMethodDefinition)
                { method = method.MakeGenericMethod(types); }
                return method.Invoke(null, arguments);
            }
            var before = Load(original, beforeContext);
            var after = Load(changed, afterContext);
            var marker = new Uri("https://example.invalid/fixed-arguments");
            foreach (var probe in new[] { before, after })
            {
                var api = probe.Assembly.GetType("FixedApi")!;
                var events = (List<string>)api.GetField("Events")!.GetValue(null)!;
                object tag = probe == before ? 0 : marker;
                foreach (var name in new[] { "Call", "Constructor", "Delegate", "Generic", "Params" })
                {
                    events.Clear();
                    var returned = Invoke(probe, name, [typeof(object), typeof(Uri)], [marker, tag]);
                    var last = name switch { "Constructor" => "new", "Generic" => "generic", "Params" => "params", _ => "call" };
                    if (!ReferenceEquals(returned, marker) || !ReferenceEquals(api.GetField("Stored")!.GetValue(null), marker)
                        || !events.SequenceEqual(name == "Params" ? new[] { "value", "value", last } : ["value", last]))
                    { throw new InvalidOperationException("Fixed argument identity/order changed: " + name); }
                }
                foreach (var value in new object?[] { marker, null })
                {
                    if (!ReferenceEquals(Invoke(probe, "Nullable", [typeof(object), typeof(Uri)], [value, tag]), value)
                        || !ReferenceEquals(api.GetField("Stored")!.GetValue(null), value))
                    { throw new InvalidOperationException("Fixed nullable argument changed identity."); }
                }
                if (!Equals(Invoke(probe, "InAlias", [typeof(Uri)], [marker, tag]), true))
                { throw new InvalidOperationException("Omitted in argument lost its original storage alias."); }
                var boxed = Invoke(probe, "BoxedCaller", [typeof(int)], [42]);
                if (!ReferenceEquals(boxed, api.GetField("Stored")!.GetValue(null)))
                { throw new InvalidOperationException("Fixed argument supplied caller changed boxing identity."); }
            }
            Program.Log.Info("PASS FixedArgumentOriginalCallOrderIdentityNullBoxingAndReadonlyAlias");
            var afterApi = after.Assembly.GetType("FixedApi")!;
            foreach (var name in new[] { "Call", "Constructor", "Delegate", "Generic" })
            {
                if (!Equals(Invoke(after, name, [typeof(decimal), typeof(Uri)], [23m, marker]), 23m)
                    || !Equals(afterApi.GetField("Stored")!.GetValue(null), 23m))
                { throw new InvalidOperationException("Fixed argument lost a new value domain: " + name); }
            }
            string[] array = ["new"];
            Func<string> factory = () => "new";
            if (!ReferenceEquals(Invoke(after, "Array", [typeof(string), typeof(Uri)], [array, marker]), array)
                || !ReferenceEquals(afterApi.GetField("Stored")!.GetValue(null), array)
                || !ReferenceEquals(Invoke(after, "Factory", [typeof(string), typeof(Uri)], [factory, marker]), factory)
                || !ReferenceEquals(afterApi.GetField("Stored")!.GetValue(null), factory))
            { throw new InvalidOperationException("Fixed argument changed covariant payload identity."); }
            var pair = ((decimal, int))Invoke(after, "Pair", [typeof(decimal), typeof(Uri)], [((decimal)7, 9), marker])!;
            var storedPair = ((object, int))afterApi.GetField("Stored")!.GetValue(null)!;
            if (pair != (7m, 9) || !Equals(storedPair.Item1, 7m) || storedPair.Item2 != 9)
            { throw new InvalidOperationException("Fixed tuple argument lost element conversions."); }
            foreach (var chosen in new[] { true, false })
            {
                var pairValue = (System.Runtime.CompilerServices.ITuple)Invoke(after, "Conditional", [typeof(decimal), typeof(string), typeof(Uri)], [chosen, 23m, "right", marker])!;
                if (!Equals(pairValue[0], 23m) || !Equals(pairValue[1], "right") || !Equals(afterApi.GetField("Stored")!.GetValue(null), chosen ? 23m : "right"))
                { throw new InvalidOperationException("Fixed conditional argument coupled independent capture types."); }
            }
            Program.Log.Info("PASS FixedArgumentNewDomainsCovarianceTupleAndIndependentCaptures");
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(false);
            foreach (var name in new[] { "Async", "ValueAsync", "NullableAsync" })
            {
                foreach (var outcome in new[] { "Success", "Null", "Fault", "Cancel" })
                {
                    if (outcome == "Null" && name != "NullableAsync")
                    { continue; }
                    foreach (var probe in new[] { before, after })
                    {
                        var api = probe.Assembly.GetType("FixedApi")!;
                        var events = (List<string>)api.GetField("Events")!.GetValue(null)!;
                        events.Clear();
                        var gate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var failure = new InvalidOperationException("argument-await");
                        object input = name == "ValueAsync" ? new ValueTask<object?>(gate.Task) : gate.Task;
                        var invocation = Invoke(probe, name, [typeof(object), typeof(Uri)], [input, probe == before ? 0 : marker])!;
                        var pending = invocation is Task<object?> task ? task : ((ValueTask<object?>)invocation).AsTask();
                        if (pending.IsCompleted || events.Count != 0)
                        { throw new InvalidOperationException("Fixed argument call did not wait for its awaited input."); }
                        if (outcome is "Success" or "Null")
                        { gate.SetResult(outcome == "Null" ? null : marker); }
                        else if (outcome == "Fault")
                        { gate.SetException(failure); }
                        else
                        {
#pragma warning disable SPINT003 // Deliberately canceled fixture token, independent of test cancellation.
                            gate.SetCanceled(canceled.Token);
#pragma warning restore SPINT003
                        }
                        try
                        {
                            var returned = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
                            var expected = outcome == "Null" ? null : marker;
                            if (outcome is not ("Success" or "Null") || !ReferenceEquals(returned, expected) || !ReferenceEquals(api.GetField("Stored")!.GetValue(null), expected))
                            { throw new InvalidOperationException("Fixed argument async result identity changed."); }
                        }
                        catch (InvalidOperationException exception) when (outcome == "Fault" && ReferenceEquals(exception, failure)) { }
                        catch (OperationCanceledException exception) when (outcome == "Cancel" && exception.CancellationToken == canceled.Token) { }
                        var expectedEvents = outcome is "Success" or "Null" ? new[] { name == "NullableAsync" ? "nullable" : "call" } : [];
                        if (!events.SequenceEqual(expectedEvents))
                        { throw new InvalidOperationException("Fixed argument async call effects changed."); }
                    }
                }
            }
            var newTask = (Task<string>)Invoke(after, "Async", [typeof(string), typeof(Uri)], [Task.FromResult("new"), marker])!;
            var newValueTask = (ValueTask<string>)Invoke(after, "ValueAsync", [typeof(string), typeof(Uri)], [new ValueTask<string>("new"), marker])!;
            var nullTask = (Task<string?>)Invoke(after, "NullableAsync", [typeof(string), typeof(Uri)], [Task.FromResult<string?>(null), marker])!;
            if (await newTask.WaitAsync(cancellationToken).ConfigureAwait(false) != "new"
                || await newValueTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false) != "new"
                || await nullTask.WaitAsync(cancellationToken).ConfigureAwait(false) is not null)
            { throw new InvalidOperationException("Fixed argument lost new async payload domains."); }
            Program.Log.Info("PASS FixedArgumentTaskValueTaskNullSuspensionFaultCancellationAndEffects");
            var invalid = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText("using System;class Bad{void Check(){_=ArgumentProbe.Array(new[]{1},0);_=ArgumentProbe.Factory((Func<int>)(()=>1),0);}}", cancellationToken: cancellationToken));
            if (invalid.GetDiagnostics(cancellationToken).Count(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS0452"; }) != 2)
            { throw new InvalidOperationException("Fixed argument covariance admitted value element types."); }
            Program.Log.Info("PASS FixedArgumentCovarianceRejectsValueElementDomains");
        }
        finally { beforeContext.Unload(); afterContext.Unload(); }
        return 5;
    }
}
