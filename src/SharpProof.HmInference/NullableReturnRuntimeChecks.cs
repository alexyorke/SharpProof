using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class NullableReturnRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq.Expressions;
            using System.Threading.Tasks;
            public static class NullableProbe{
                public static object? Stored;
                public static Delegate? Last;
                public static Expression<Func<object?>> TreeStored=null!;
                public static readonly List<string> Events=new();
                public delegate ref object? RefCallback();
                public class Box<T>{public T Value=default!;}
                static T Read<T>(T value){Events.Add("value");return value;}
                public static object Local(bool flag,object value,int tag){object? Get(){Events.Add("enter");if(flag)return Read(value);return null;}Stored=Get();return value;}
                public static object Lambda(bool flag,object value,int tag){Func<object?> callback=()=>{Events.Add("enter");if(flag)return Read(value);return null;};Last=callback;Stored=callback();return value;}
                public static object Anonymous(bool flag,object value,int tag){Func<object?> callback=delegate{Events.Add("enter");if(flag)return Read(value);return null;};Last=callback;Stored=callback();return value;}
                public static object Tuple(bool flag,object value,int tag){Func<(object?,int)> callback=()=>{Events.Add("enter");if(flag)return(Read(value),1);return(null,2);};Last=callback;Stored=callback();return value;}
                public static (object,object) Independent(bool flag,object left,object right,int tag){Func<object?> callback=()=>{Events.Add("enter");if(flag)return Read(left);return Read(right);};Stored=callback();return(left,right);}
                public static object Iterator(object value,int tag){IEnumerable<object?> Get(){Events.Add("yield");yield return Read(value);yield return null;}var items=new List<object?>();foreach(var item in Get())items.Add(item);Stored=items;return value;}
                public static async Task<object> AsyncIterator(object value,int tag){async IAsyncEnumerable<object?> Get(){Events.Add("yield");await Task.Yield();yield return Read(value);yield return null;}var items=new List<object?>();await foreach(var item in Get())items.Add(item);Stored=items;return value;}
                public static object? Narrow(object? value,int tag){Func<object> callback=()=>{Events.Add("enter");if(value is null)return new object();return value;};Last=callback;Stored=callback();return value;}
                public static object CallerTarget(bool flag,object value,int tag){Func<object?> callback=()=>{if(flag)return value;return null;};Stored=callback();return value;}
                public static object BoxedCaller(int value)=>CallerTarget(true,value,0);
                public static object Tree(object value,int tag){TreeStored=()=>value;return value;}
                public static bool RefControl(Box<object?> box,int tag){RefCallback callback=()=>ref box.Value;Last=callback;ref object? alias=ref callback();alias=null;return box.Value is null;}
                public static Task<object?> Async(bool flag,Task<object> value,int tag){Func<Task<object?>> callback=async()=>{Events.Add("enter");if(flag){var item=await value;Events.Add("resume");return item;}return null;};Last=callback;return callback();}
                public static ValueTask<object?> ValueAsync(bool flag,ValueTask<object> value,int tag){Func<ValueTask<object?>> callback=async()=>{Events.Add("enter");if(flag){var item=await value;Events.Add("resume");return item;}return null;};Last=callback;return callback();}
                public static Task<object?> LocalAsync(bool flag,Task<object> value,int tag){async Task<object?> Get(){Events.Add("enter");if(flag){var item=await value;Events.Add("resume");return item;}return null;}return Get();}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "NullableReturns", "NullableReturns", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Returns.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var symbol = changed.GetTypeByMetadataName("NullableProbe")!;
        foreach (var name in new[] { "Local", "Lambda", "Anonymous", "Tuple", "Iterator", "AsyncIterator", "Narrow", "Async", "ValueAsync", "LocalAsync" })
        {
            if (symbol.GetMembers(name).OfType<IMethodSymbol>().Single().TypeParameters.Length != 2)
            { throw new InvalidOperationException("Nullable return Fix All missed " + name); }
        }
        foreach (var name in new[] { "Local", "Lambda", "Anonymous", "Tuple", "Iterator", "AsyncIterator", "Async", "ValueAsync", "LocalAsync" })
        {
            if (symbol.GetMembers(name).OfType<IMethodSymbol>().Single().TypeParameters[0].HasReferenceTypeConstraint)
            { throw new InvalidOperationException("Nullable object result unnecessarily restricted value captures."); }
        }
        if (symbol.GetMembers("CallerTarget").OfType<IMethodSymbol>().Single().Parameters[1].Type.SpecialType != SpecialType.System_Object
            || symbol.GetMembers("Tree").OfType<IMethodSymbol>().Single().Parameters[0].Type.SpecialType != SpecialType.System_Object
            || symbol.GetMembers("RefControl").OfType<IMethodSymbol>().Single().TypeParameters.Length != 1)
        { throw new InvalidOperationException("Nullable return lost caller/tree/ref contracts."); }
        Program.Log.Info("PASS NullableReturnFixAllAdmitsValuesAndRetainsCallerTreeRefContracts");
        var beforeContext = new System.Runtime.Loader.AssemblyLoadContext("BeforeNullableReturns", isCollectible: true);
        var afterContext = new System.Runtime.Loader.AssemblyLoadContext("AfterNullableReturns", isCollectible: true);
        try
        {
            Type Load(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Nullable return emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                return loadContext.LoadFromStream(image).GetType("NullableProbe")!;
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
            var marker = new Uri("https://example.invalid/nullable-returns");
            foreach (var probe in new[] { before, after })
            {
                object tag = probe == before ? 0 : marker;
                var events = (List<string>)probe.GetField("Events")!.GetValue(null)!;
                foreach (var chosen in new[] { true, false })
                {
                    foreach (var name in new[] { "Local", "Lambda", "Anonymous", "Tuple" })
                    {
                        events.Clear();
                        var returned = Invoke(probe, name, [typeof(object), typeof(Uri)], [chosen, marker, tag]);
                        var stored = probe.GetField("Stored")!.GetValue(null);
                        var payload = name == "Tuple" ? ((ValueTuple<object?, int>)stored!).Item1 : stored;
                        if (!ReferenceEquals(returned, marker) || !ReferenceEquals(payload, chosen ? marker : null)
                            || name == "Tuple" && ((ValueTuple<object?, int>)stored!).Item2 != (chosen ? 1 : 2)
                            || !events.SequenceEqual(chosen ? (string[])["enter", "value"] : ["enter"]))
                        { throw new InvalidOperationException("Nullable return changed branch, identity or effects: " + name); }
                    }
                }
                foreach (var value in new object?[] { marker, null })
                {
                    var returned = Invoke(probe, "Narrow", [typeof(object), typeof(Uri)], [value, tag]);
                    var stored = probe.GetField("Stored")!.GetValue(null);
                    if (!ReferenceEquals(returned, value) || stored is null || value is not null && !ReferenceEquals(stored, value))
                    { throw new InvalidOperationException("Nullable narrowing changed null/reference semantics."); }
                }
                foreach (var name in new[] { "Iterator", "AsyncIterator" })
                {
                    events.Clear();
                    var invocation = Invoke(probe, name, [typeof(object), typeof(Uri)], [marker, tag]);
                    var returned = invocation is Task<object> pending ? await pending.WaitAsync(cancellationToken).ConfigureAwait(false) : invocation;
                    var items = (List<object?>)probe.GetField("Stored")!.GetValue(null)!;
                    if (!ReferenceEquals(returned, marker) || items.Count != 2 || !ReferenceEquals(items[0], marker) || items[1] is not null
                        || !events.SequenceEqual((string[])["yield", "value"]))
                    { throw new InvalidOperationException("Nullable iterator changed yield identity, nulls or effects."); }
                }
                var boxed = Invoke(probe, "BoxedCaller", [typeof(int)], [42]);
                if (!ReferenceEquals(boxed, probe.GetField("Stored")!.GetValue(null)))
                { throw new InvalidOperationException("Nullable return reboxed a supplied caller's value."); }
                _ = Invoke(probe, "Tree", [typeof(Uri)], [marker, tag]);
                var tree = (System.Linq.Expressions.Expression<Func<object?>>)probe.GetField("TreeStored")!.GetValue(null)!;
                if (tree.Body.NodeType != System.Linq.Expressions.ExpressionType.MemberAccess || !ReferenceEquals(tree.Compile()(), marker))
                { throw new InvalidOperationException("Nullable expression tree changed its conversion structure."); }
                var box = Activator.CreateInstance(probe.GetNestedType("Box`1")!.MakeGenericType(typeof(object)))!;
                box.GetType().GetField("Value")!.SetValue(box, marker);
                if (!Equals(Invoke(probe, "RefControl", [typeof(Uri)], [box, tag]), true) || box.GetType().GetField("Value")!.GetValue(box) is not null)
                { throw new InvalidOperationException("Nullable ref-return alias stopped updating its storage."); }
            }
            Program.Log.Info("PASS NullableReturnOriginalBranchesEffectsIdentityBoxingNarrowingTreeAndRefAliases");
            foreach (var name in new[] { "Local", "Lambda", "Anonymous", "Tuple" })
            {
                foreach (var (type, value) in new (Type, object?)[] { (typeof(decimal), 23m), (typeof(int?), (int?)7), (typeof(int?), null) })
                {
                    var returned = Invoke(after, name, [type, typeof(Uri)], [true, value, marker]);
                    var stored = after.GetField("Stored")!.GetValue(null);
                    var payload = name == "Tuple" ? ((ValueTuple<object?, int>)stored!).Item1 : stored;
                    if (!Equals(returned, value) || !Equals(payload, value))
                    { throw new InvalidOperationException("Nullable return lost value/nullable-value capture: " + name); }
                }
            }
            foreach (var chosen in new[] { true, false })
            {
                var pair = (System.Runtime.CompilerServices.ITuple)Invoke(after, "Independent", [typeof(decimal), typeof(string), typeof(Uri)], [chosen, 23m, "right", marker])!;
                if (!Equals(pair[0], 23m) || !Equals(pair[1], "right") || !Equals(after.GetField("Stored")!.GetValue(null), chosen ? 23m : "right"))
                { throw new InvalidOperationException("Nullable returns coupled independent capture domains."); }
            }
            foreach (var name in new[] { "Iterator", "AsyncIterator" })
            {
                var invocation = Invoke(after, name, [typeof(int?), typeof(Uri)], [null, marker]);
                var returned = invocation is Task<int?> pending ? await pending.WaitAsync(cancellationToken).ConfigureAwait(false) : invocation;
                var items = (List<object?>)after.GetField("Stored")!.GetValue(null)!;
                if (returned is not null || items.Count != 2 || items.Any(item => item is not null))
                { throw new InvalidOperationException("Nullable iterator excluded nullable value captures."); }
            }
            Program.Log.Info("PASS NullableReturnNewValueNullableValueAndIndependentCaptureDomains");
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(false);
            foreach (var name in new[] { "Async", "ValueAsync", "LocalAsync" })
            {
                foreach (var outcome in new[] { "Success", "Fault", "Cancel", "Unselected" })
                {
                    foreach (var probe in new[] { before, after })
                    {
                        var events = (List<string>)probe.GetField("Events")!.GetValue(null)!;
                        events.Clear();
                        var gate = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var failure = new InvalidOperationException("nullable-await");
                        object input = name == "ValueAsync" ? new ValueTask<object>(gate.Task) : gate.Task;
                        var invocation = Invoke(probe, name, [typeof(object), typeof(Uri)], [outcome != "Unselected", input, probe == before ? 0 : marker])!;
                        var pending = invocation is Task<object?> task ? task : ((ValueTask<object?>)invocation).AsTask();
                        if (pending.IsCompleted != (outcome == "Unselected"))
                        { throw new InvalidOperationException("Nullable return changed conditional await suspension."); }
                        if (outcome == "Success")
                        { gate.SetResult(marker); }
                        else if (outcome == "Fault")
                        { gate.SetException(failure); }
                        else if (outcome == "Cancel")
                        {
#pragma warning disable SPINT003 // Deliberately canceled fixture token, independent of test cancellation.
                            gate.SetCanceled(canceled.Token);
#pragma warning restore SPINT003
                        }
                        try
                        {
                            var returned = await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
                            if (outcome == "Success" ? !ReferenceEquals(returned, marker) : outcome != "Unselected" || returned is not null)
                            { throw new InvalidOperationException("Nullable return changed async reference/null result."); }
                        }
                        catch (InvalidOperationException exception) when (outcome == "Fault" && ReferenceEquals(exception, failure)) { }
                        catch (OperationCanceledException exception) when (outcome == "Cancel" && exception.CancellationToken == canceled.Token) { }
                        if (!events.SequenceEqual(outcome == "Success" ? (string[])["enter", "resume"] : ["enter"]))
                        { throw new InvalidOperationException("Nullable return changed async effects."); }
                    }
                }
            }
            foreach (var name in new[] { "Async", "ValueAsync", "LocalAsync" })
            {
                object input = name == "ValueAsync" ? new ValueTask<int?>((int?)null) : Task.FromResult<int?>(null);
                var invocation = Invoke(after, name, [typeof(int?), typeof(Uri)], [true, input, marker])!;
                var pending = invocation is Task<object?> task ? task : ((ValueTask<object?>)invocation).AsTask();
                if (await pending.WaitAsync(cancellationToken).ConfigureAwait(false) is not null)
                { throw new InvalidOperationException("Nullable return lost new nullable-value async payload."); }
            }
            Program.Log.Info("PASS NullableReturnTaskValueTaskSelectedAwaitNullFaultCancellationAndNewDomains");
        }
        finally { beforeContext.Unload(); afterContext.Unload(); }
        return 4;
    }
}
