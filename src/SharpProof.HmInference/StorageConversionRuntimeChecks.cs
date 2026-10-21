using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class StorageConversionRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public static class StorageProbe{
                public static readonly List<string> Events=new();
                public static object Stored=null!;
                public static object? NullableStored;
                public static object[] ArrayStored=null!;
                public static (object,int) TupleStored;
                public static Func<object> FactoryStored=null!;
                public sealed class Box{
                    object data=null!;
                    public Box(){Events.Add("new");}
                    public object Value{get=>data;set{Events.Add("set");data=value;}}
                    public object this[int index]{get=>data;set{Events.Add("index-set:"+index);data=value;}}
                }
                public sealed class RefBox{public object Value=null!;}
                static Box Receiver(Box value){Events.Add("receiver");return value;}
                static int Index(){Events.Add("index");return 0;}
                static T Read<T>(T value){Events.Add("value");return value;}
                public static object Field(object value,int tag){Stored=Read(value);return value;}
                public static object? Nullable(object? value,int tag){NullableStored=value;return value;}
                public static object Property(Box box,object value,int tag){Receiver(box).Value=Read(value);return value;}
                public static object Indexer(Box box,object value,int tag){Receiver(box)[Index()]=Read(value);return value;}
                public static object Deconstruct(object value,int tag){(object copy,int number)=(Read(value),0);Stored=copy;_=number;return value;}
                public static object Local(object value,int tag){object copy=Read(value);Stored=copy;return value;}
                public static (object,object) Conditional(bool flag,object left,object right,int tag){Stored=flag?Read(left):Read(right);return(left,right);}
                public static Box Initializer(object value,int tag)=>new Box{Value=Read(value)};
                public static object[] Array(object[] value,int tag){ArrayStored=value;return value;}
                public static (object,int) Pair((object,int) value,int tag){TupleStored=value;return value;}
                public static Func<object> Factory(Func<object> value,int tag){FactoryStored=value;return value;}
                public static object CallerTarget(object value,int tag){Stored=value;return value;}
                public static object BoxedCaller(int value)=>CallerTarget(value,0);
                public static async Task<object> Async(Task<object> value,int tag){Events.Add("enter");Stored=await value;Events.Add("store");return await value;}
                public static async ValueTask<object> ValueAsync(ValueTask<object> value,int tag){Events.Add("enter");Stored=await value;Events.Add("store");return await value;}
                public static async Task<object?> NullableAsync(Task<object?> value,int tag){Events.Add("enter");NullableStored=await value;Events.Add("store");return await value;}
                public static ref object Ref(RefBox left,RefBox right,int tag){ref object alias=ref left.Value;alias=ref right.Value;return ref alias;}
                public static bool RefControl(){object marker=new object();var left=new RefBox{Value=marker};var right=new RefBox{Value=new object()};ref object alias=ref Ref(left,right,0);alias="changed";return ReferenceEquals(left.Value,marker)&&Equals(right.Value,"changed");}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "StorageConversions", "StorageConversions", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Storage.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var symbol = changed.GetTypeByMetadataName("StorageProbe")!;
        foreach (var name in new[] { "Field", "Nullable", "Property", "Indexer", "Local", "Deconstruct", "Initializer", "Array", "Pair", "Factory", "Async", "ValueAsync", "NullableAsync" })
        {
            if (symbol.GetMembers(name).OfType<IMethodSymbol>().Single().TypeParameters.Length != 2)
            { throw new InvalidOperationException("Storage Fix All missed " + name); }
        }
        if (symbol.GetMembers("CallerTarget").OfType<IMethodSymbol>().Single().Parameters[0].Type.SpecialType != SpecialType.System_Object)
        { throw new InvalidOperationException("Storage inference changed supplied boxing caller contract."); }
        async Task<string[]> Locals(Compilation compilation)
        {
            ArgumentNullException.ThrowIfNull(compilation);
            var locals = new List<string>();
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var declaration in (await tree.GetRootAsync(cancellationToken).ConfigureAwait(false)).DescendantNodes().OfType<VariableDeclarationSyntax>())
                {
                    if (declaration.Type.IsVar || declaration.Parent is not LocalDeclarationStatementSyntax)
                    { continue; }
                    foreach (var variable in declaration.Variables)
                    {
                        var local = (ILocalSymbol)model.GetDeclaredSymbol(variable, cancellationToken)!;
                        locals.Add(local.Name + ":" + local.RefKind + ":" + local.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                }
            }
            return locals.ToArray();
        }
        if (!(await Locals(original).ConfigureAwait(false)).SequenceEqual(await Locals(changed).ConfigureAwait(false)))
        { throw new InvalidOperationException("Storage Fix All changed explicit local storage types."); }
        Program.Log.Info("PASS StorageFixAllPreservesFixedLocalsAndCallerBoxingContracts");
        var beforeContext = new System.Runtime.Loader.AssemblyLoadContext("BeforeStorage", isCollectible: true);
        var afterContext = new System.Runtime.Loader.AssemblyLoadContext("AfterStorage", isCollectible: true);
        try
        {
            Type Load(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Storage emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                return loadContext.LoadFromStream(image).GetType("StorageProbe")!;
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
            var marker = new Uri("https://example.invalid/storage");
            foreach (var probe in new[] { before, after })
            {
                object tag = probe == before ? 0 : marker;
                var events = (List<string>)probe.GetField("Events")!.GetValue(null)!;
                foreach (var name in new[] { "Field", "Local", "Deconstruct" })
                {
                    events.Clear();
                    var result = Invoke(probe, name, [typeof(object), typeof(Uri)], [marker, tag]);
                    if (!ReferenceEquals(result, marker) || !ReferenceEquals(probe.GetField("Stored")!.GetValue(null), marker) || !events.SequenceEqual((string[])["value"]))
                    { throw new InvalidOperationException("Storage reference identity/effects changed: " + name); }
                }
                foreach (var name in new[] { "Property", "Indexer" })
                {
                    var box = Activator.CreateInstance(probe.GetNestedType("Box")!)!;
                    events.Clear();
                    var result = Invoke(probe, name, [typeof(object), typeof(Uri)], [box, marker, tag]);
                    var stored = box.GetType().GetProperty("Value")!.GetValue(box);
                    var expected = name == "Property" ? new[] { "receiver", "value", "set" } : ["receiver", "index", "value", "index-set:0"];
                    if (!ReferenceEquals(result, marker) || !ReferenceEquals(stored, marker) || !events.SequenceEqual(expected))
                    { throw new InvalidOperationException("Storage setter/index evaluation changed: " + name); }
                }
                events.Clear();
                var initialized = Invoke(probe, "Initializer", [typeof(object), typeof(Uri)], [marker, tag])!;
                if (!ReferenceEquals(initialized.GetType().GetProperty("Value")!.GetValue(initialized), marker) || !events.SequenceEqual((string[])["new", "value", "set"]))
                { throw new InvalidOperationException("Storage initializer evaluation changed."); }
                foreach (var value in new object?[] { marker, null })
                {
                    var result = Invoke(probe, "Nullable", [typeof(object), typeof(Uri)], [value, tag]);
                    if (!ReferenceEquals(result, value) || !ReferenceEquals(probe.GetField("NullableStored")!.GetValue(null), value))
                    { throw new InvalidOperationException("Storage null/reference semantics changed."); }
                }
                var boxed = Invoke(probe, "BoxedCaller", [typeof(int)], [42]);
                if (!ReferenceEquals(boxed, probe.GetField("Stored")!.GetValue(null)) || !Equals(Invoke(probe, "RefControl", [], []), true))
                { throw new InvalidOperationException("Storage boxing or ref-reassignment identity changed."); }
            }
            Program.Log.Info("PASS StorageOriginalIdentityBoxingSettersInitializerOrderNullAndRefAliases");
            foreach (var name in new[] { "Field", "Local", "Deconstruct" })
            {
                if (!Equals(Invoke(after, name, [typeof(decimal), typeof(Uri)], [23m, marker]), 23m)
                    || !Equals(after.GetField("Stored")!.GetValue(null), 23m))
                { throw new InvalidOperationException("Storage lost a new value domain."); }
            }
            string[] array = ["new"];
            Func<string> factory = () => "new";
            var pair = ((decimal, int))Invoke(after, "Pair", [typeof(decimal), typeof(Uri)], [((decimal)7, 9), marker])!;
            var storedPair = ((object, int))after.GetField("TupleStored")!.GetValue(null)!;
            if (!ReferenceEquals(Invoke(after, "Array", [typeof(string), typeof(Uri)], [array, marker]), array)
                || !ReferenceEquals(after.GetField("ArrayStored")!.GetValue(null), array)
                || !ReferenceEquals(Invoke(after, "Factory", [typeof(string), typeof(Uri)], [factory, marker]), factory)
                || !ReferenceEquals(after.GetField("FactoryStored")!.GetValue(null), factory)
                || pair != (7m, 9) || !Equals(storedPair.Item1, 7m) || storedPair.Item2 != 9)
            { throw new InvalidOperationException("Storage covariance/tuple conversion changed new domains."); }
            var conditional = (System.Runtime.CompilerServices.ITuple)Invoke(after, "Conditional", [typeof(decimal), typeof(string), typeof(Uri)], [true, 23m, "right", marker])!;
            if (!Equals(conditional[0], 23m) || !Equals(conditional[1], "right") || !Equals(after.GetField("Stored")!.GetValue(null), 23m))
            { throw new InvalidOperationException("Fixed storage coupled independent conditional captures."); }
            Program.Log.Info("PASS StorageNewDomainsArrayDelegateTupleAndIndependentJoinConversions");
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
                        var events = (List<string>)probe.GetField("Events")!.GetValue(null)!;
                        events.Clear();
                        var gate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var failure = new InvalidOperationException("store-await");
                        object input = name == "ValueAsync" ? new ValueTask<object?>(gate.Task) : gate.Task;
                        var invocation = Invoke(probe, name, [typeof(object), typeof(Uri)], [input, probe == before ? 0 : marker])!;
                        var pending = invocation is Task<object?> task ? task : ((ValueTask<object?>)invocation).AsTask();
                        if (pending.IsCompleted)
                        { throw new InvalidOperationException("Storage await failed to suspend."); }
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
                            if (outcome is not ("Success" or "Null") || !ReferenceEquals(returned, expected)
                                || !ReferenceEquals(probe.GetField(name == "NullableAsync" ? "NullableStored" : "Stored")!.GetValue(null), expected))
                            { throw new InvalidOperationException("Storage await changed result identity."); }
                        }
                        catch (InvalidOperationException exception) when (outcome == "Fault" && ReferenceEquals(exception, failure)) { }
                        catch (OperationCanceledException exception) when (outcome == "Cancel" && exception.CancellationToken == canceled.Token) { }
                        if (!events.SequenceEqual(outcome is "Success" or "Null" ? (string[])["enter", "store"] : ["enter"]))
                        { throw new InvalidOperationException("Storage await changed effects."); }
                    }
                }
            }
            var newTask = (Task<string>)Invoke(after, "Async", [typeof(string), typeof(Uri)], [Task.FromResult("new"), marker])!;
            var newValueTask = (ValueTask<string>)Invoke(after, "ValueAsync", [typeof(string), typeof(Uri)], [new ValueTask<string>("new"), marker])!;
            var nullTask = (Task<string?>)Invoke(after, "NullableAsync", [typeof(string), typeof(Uri)], [Task.FromResult<string?>(null), marker])!;
            if (await newTask.WaitAsync(cancellationToken).ConfigureAwait(false) != "new"
                || await newValueTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false) != "new"
                || await nullTask.WaitAsync(cancellationToken).ConfigureAwait(false) is not null)
            { throw new InvalidOperationException("Storage lost new async payload domains."); }
            Program.Log.Info("PASS StorageTaskValueTaskSuspensionNullFaultCancellationEffectsAndNewDomains");
            var invalid = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText("using System;class InvalidStorage{void Check(){_=StorageProbe.Array(new[]{1},0);_=StorageProbe.Factory((Func<int>)(()=>1),0);}}", cancellationToken: cancellationToken));
            if (invalid.GetDiagnostics(cancellationToken).Count(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS0452"; }) != 2)
            { throw new InvalidOperationException("Storage covariance admitted value-element domains."); }
            Program.Log.Info("PASS StorageCovarianceRejectsValueElementDomains");
        }
        finally { beforeContext.Unload(); afterContext.Unload(); }
        return 5;
    }
}
