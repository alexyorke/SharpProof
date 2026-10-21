using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class FixedLambdaRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq.Expressions;
            using System.Threading.Tasks;
            #pragma warning disable HM0001
            public static class LambdaApi{
                public static object? Stored;
                public static Delegate? Last;
                public static Func<object> Escaped=null!;
                public static Expression<Func<object>> Tree=null!;
                public static readonly List<string> Events=new();
                public static void Run(Func<object> callback){Events.Add("api");Last=callback;Stored=callback();}
            }
            public class Holder{public Holder(Func<object> callback){LambdaApi.Events.Add("new");LambdaApi.Last=callback;LambdaApi.Stored=callback();}}
            #pragma warning restore HM0001
            public static class LambdaProbe{
                public delegate object Callback();
                public delegate ref object RefCallback();
                public class Box{public object Value=null!;}
                static T Read<T>(T value){LambdaApi.Events.Add("body");return value;}
                public static object Local(object value,int tag){Func<object> callback=()=>Read(value);LambdaApi.Last=callback;LambdaApi.Stored=callback();return value;}
                public static object Cast(object value,int tag){var callback=(Func<object>)(()=>Read(value));LambdaApi.Last=callback;LambdaApi.Stored=callback();return value;}
                public static object Explicit(object value,int tag){var callback=object()=>Read(value);LambdaApi.Last=callback;LambdaApi.Stored=callback();return value;}
                public static object Custom(object value,int tag){Callback callback=()=>Read(value);LambdaApi.Last=callback;LambdaApi.Stored=callback();return value;}
                public static object Argument(object value,int tag){LambdaApi.Run(()=>Read(value));return value;}
                public static object Constructor(object value,int tag){_=new Holder(()=>Read(value));return value;}
                public static object Nested(object value,int tag){Func<Func<object>> callback=()=>()=>Read(value);LambdaApi.Last=callback();LambdaApi.Stored=((Func<object>)LambdaApi.Last)();return value;}
                public static object Escape(object value,int tag){LambdaApi.Escaped=()=>Read(value);return value;}
                public static object? Nullable(object? value,int tag){Func<object?> callback=()=>value;LambdaApi.Last=callback;LambdaApi.Stored=callback();return value;}
                public static object CallerTarget(object value,int tag){Func<object> callback=()=>value;LambdaApi.Stored=callback();return value;}
                public static object BoxedCaller(int value)=>CallerTarget(value,0);
                public static object Tree(object value,int tag){LambdaApi.Tree=()=>value;return value;}
                public static object Natural(object value,int tag){object callback=()=>value;LambdaApi.Last=(Delegate)callback;return value;}
                public static bool RefControl(object value,int tag){var box=new Box{Value=value};RefCallback callback=()=>ref box.Value;LambdaApi.Last=callback;ref object alias=ref callback();alias="changed";return Equals(box.Value,"changed");}
                public static Task<object> Async(Task<object> value,int tag){Func<Task<object>> callback=async()=>{LambdaApi.Events.Add("enter");return await value;};LambdaApi.Last=callback;return callback();}
                public static ValueTask<object> ValueAsync(ValueTask<object> value,int tag){Func<ValueTask<object>> callback=async()=>{LambdaApi.Events.Add("enter");return await value;};LambdaApi.Last=callback;return callback();}
                public static Task<object?> NullableAsync(Task<object?> value,int tag){Func<Task<object?>> callback=async()=>{LambdaApi.Events.Add("enter");return await value;};LambdaApi.Last=callback;return callback();}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "FixedLambdas", "FixedLambdas", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Lambdas.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var symbol = changed.GetTypeByMetadataName("LambdaProbe")!;
        foreach (var name in new[] { "Local", "Cast", "Explicit", "Custom", "Argument", "Constructor", "Nested", "Escape", "Nullable", "Async", "ValueAsync", "NullableAsync" })
        {
            if (symbol.GetMembers(name).OfType<IMethodSymbol>().Single().TypeParameters.Length != 2)
            { throw new InvalidOperationException("Fixed lambda Fix All missed " + name); }
        }
        foreach (var name in new[] { "CallerTarget", "Tree", "Natural" })
        {
            if (symbol.GetMembers(name).OfType<IMethodSymbol>().Single().Parameters[0].Type.SpecialType != SpecialType.System_Object)
            { throw new InvalidOperationException("Fixed lambda lost caller/tree/natural contract: " + name); }
        }
        Program.Log.Info("PASS FixedLambdaFixAllRetainsCallerTreeAndNaturalDelegateContracts");
        var beforeContext = new System.Runtime.Loader.AssemblyLoadContext("BeforeFixedLambdas", isCollectible: true);
        var afterContext = new System.Runtime.Loader.AssemblyLoadContext("AfterFixedLambdas", isCollectible: true);
        try
        {
            Type Load(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Fixed lambda emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                return loadContext.LoadFromStream(image).GetType("LambdaProbe")!;
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
            var marker = new Uri("https://example.invalid/fixed-lambdas");
            foreach (var probe in new[] { before, after })
            {
                var api = probe.Assembly.GetType("LambdaApi")!;
                var events = (List<string>)api.GetField("Events")!.GetValue(null)!;
                object tag = probe == before ? 0 : marker;
                foreach (var name in new[] { "Local", "Cast", "Explicit", "Custom", "Argument", "Constructor", "Nested" })
                {
                    events.Clear();
                    var returned = Invoke(probe, name, [typeof(object), typeof(Uri)], [marker, tag]);
                    var callback = (Delegate)api.GetField("Last")!.GetValue(null)!;
                    var expectedType = name == "Custom" ? probe.GetNestedType("Callback")! : typeof(Func<object>);
                    var expectedEvents = name == "Argument" ? new[] { "api", "body" } : name == "Constructor" ? ["new", "body"] : ["body"];
                    if (!ReferenceEquals(returned, marker) || !ReferenceEquals(api.GetField("Stored")!.GetValue(null), marker)
                        || callback.GetType() != expectedType || callback.Method.ReturnType != typeof(object) || !events.SequenceEqual(expectedEvents))
                    { throw new InvalidOperationException("Fixed lambda delegate type, identity or effects changed: " + name); }
                }
                events.Clear();
                if (!ReferenceEquals(Invoke(probe, "Escape", [typeof(object), typeof(Uri)], [marker, tag]), marker) || events.Count != 0)
                { throw new InvalidOperationException("Escaping fixed lambda evaluated eagerly."); }
                var escaped = (Func<object>)api.GetField("Escaped")!.GetValue(null)!;
                if (!ReferenceEquals(escaped(), marker) || !events.SequenceEqual((string[])["body"]))
                { throw new InvalidOperationException("Escaping fixed lambda lost capture identity."); }
                foreach (var value in new object?[] { null, marker })
                {
                    if (!ReferenceEquals(Invoke(probe, "Nullable", [typeof(object), typeof(Uri)], [value, tag]), value)
                        || !ReferenceEquals(api.GetField("Stored")!.GetValue(null), value))
                    { throw new InvalidOperationException("Nullable fixed lambda changed its result."); }
                }
                var boxed = Invoke(probe, "BoxedCaller", [typeof(int)], [42]);
                if (!ReferenceEquals(boxed, api.GetField("Stored")!.GetValue(null)) || !Equals(Invoke(probe, "RefControl", [typeof(object), typeof(Uri)], [marker, tag]), true))
                { throw new InvalidOperationException("Fixed lambda changed boxing or ref alias identity."); }
                _ = Invoke(probe, "Tree", [typeof(Uri)], [marker, tag]);
                var tree = (System.Linq.Expressions.Expression<Func<object>>)api.GetField("Tree")!.GetValue(null)!;
                if (tree.Body.NodeType != System.Linq.Expressions.ExpressionType.MemberAccess || tree.Body.Type != typeof(object) || !ReferenceEquals(tree.Compile()(), marker))
                { throw new InvalidOperationException("Fixed expression tree gained a conversion node."); }
                _ = Invoke(probe, "Natural", [typeof(Uri)], [marker, tag]);
                if (api.GetField("Last")!.GetValue(null)!.GetType() != typeof(Func<object>))
                { throw new InvalidOperationException("Natural delegate stored as object changed its runtime type."); }
            }
            Program.Log.Info("PASS FixedLambdaOriginalDelegateTypesIdentityEffectsLazyCapturesTreeAndRefAliases");
            var afterApi = after.Assembly.GetType("LambdaApi")!;
            foreach (var name in new[] { "Local", "Cast", "Explicit", "Custom", "Argument", "Constructor", "Nested", "Escape" })
            {
                if (!Equals(Invoke(after, name, [typeof(decimal), typeof(Uri)], [23m, marker]), 23m))
                { throw new InvalidOperationException("Fixed lambda excluded a new value capture: " + name); }
                var result = name == "Escape" ? ((Func<object>)afterApi.GetField("Escaped")!.GetValue(null)!)() : afterApi.GetField("Stored")!.GetValue(null);
                if (!Equals(result, 23m))
                { throw new InvalidOperationException("Fixed lambda lost boxed value capture."); }
            }
            Program.Log.Info("PASS FixedLambdaNewValueCaptureDomains");
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
                        var api = probe.Assembly.GetType("LambdaApi")!;
                        var events = (List<string>)api.GetField("Events")!.GetValue(null)!;
                        events.Clear();
                        var gate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var failure = new InvalidOperationException("lambda-await");
                        object input = name == "ValueAsync" ? new ValueTask<object?>(gate.Task) : gate.Task;
                        var invocation = Invoke(probe, name, [typeof(object), typeof(Uri)], [input, probe == before ? 0 : marker])!;
                        var pending = invocation is Task<object?> task ? task : ((ValueTask<object?>)invocation).AsTask();
                        var callback = (Delegate)api.GetField("Last")!.GetValue(null)!;
                        var delegateType = name == "ValueAsync" ? typeof(Func<ValueTask<object>>) : typeof(Func<Task<object>>);
                        if (pending.IsCompleted || callback.GetType() != delegateType || !events.SequenceEqual((string[])["enter"]))
                        { throw new InvalidOperationException("Fixed async lambda changed delegate type, effects or suspension."); }
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
                            if (outcome is not ("Success" or "Null") || !ReferenceEquals(returned, outcome == "Null" ? null : marker))
                            { throw new InvalidOperationException("Fixed async lambda changed result identity."); }
                        }
                        catch (InvalidOperationException exception) when (outcome == "Fault" && ReferenceEquals(exception, failure)) { }
                        catch (OperationCanceledException exception) when (outcome == "Cancel" && exception.CancellationToken == canceled.Token) { }
                    }
                }
            }
            var newTask = (Task<object>)Invoke(after, "Async", [typeof(string), typeof(Uri)], [Task.FromResult("new"), marker])!;
            var newValueTask = (ValueTask<object>)Invoke(after, "ValueAsync", [typeof(string), typeof(Uri)], [new ValueTask<string>("new"), marker])!;
            var nullTask = (Task<object?>)Invoke(after, "NullableAsync", [typeof(string), typeof(Uri)], [Task.FromResult<string?>(null), marker])!;
            if (!Equals(await newTask.WaitAsync(cancellationToken).ConfigureAwait(false), "new")
                || !Equals(await newValueTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false), "new")
                || await nullTask.WaitAsync(cancellationToken).ConfigureAwait(false) is not null)
            { throw new InvalidOperationException("Fixed async lambda lost new/nullable payload domains."); }
            Program.Log.Info("PASS FixedLambdaTaskValueTaskDelegateTypesNullSuspensionFaultCancellationAndNewDomains");
        }
        finally { beforeContext.Unload(); afterContext.Unload(); }
        return 4;
    }
}
