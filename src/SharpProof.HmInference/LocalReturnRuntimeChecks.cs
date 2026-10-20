using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class LocalReturnRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public static class LocalReturnProbe{
                public static object? Stored;
                public static readonly List<string> Events=new();
                public static TaskCompletionSource<bool> Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public static object Scalar(object value,int tag){object Local()=>value;Stored=Local();return value;}
                public static IComparable Iface(IComparable value,int tag){IComparable Local()=>value;Stored=Local();return value;}
                public static object[] Array(object[] value,int tag){object[] Local()=>value;Stored=Local();return value;}
                public static (object left,int right) Pair((object left,int right) value,int tag){(object left,int right) Local()=>value;Stored=Local();return value;}
                public static Func<object> Factory(Func<object> value,int tag){Func<object> Local()=>value;Stored=Local();return value;}
                public static object Iterator(object value,int tag){IEnumerable<object> Local(){yield return value;}foreach(var item in Local()){Events.Add("yield");Stored=item;}return value;}
                public static object Boxed(object value,int tag){object Local()=>value;Stored=Local();return value;}
                public static object UseBoxed(int value)=>Boxed(value,0);
                public static Task<object> Async(object value,int tag){async Task<object> Local(){Events.Add("enter");await Gate.Task;Events.Add("resume");return value;}return Local();}
                public static ValueTask<object> ValueAsync(object value,int tag){async ValueTask<object> Local(){Events.Add("enter");await Gate.Task;Events.Add("resume");return value;}return Local();}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "LocalReturns", "LocalReturns", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Returns.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        async Task<string[]> Schemes(Compilation compilation)
        {
            ArgumentNullException.ThrowIfNull(compilation);
            var schemes = new List<string>();
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var local in (await tree.GetRootAsync(cancellationToken).ConfigureAwait(false)).DescendantNodes().OfType<LocalFunctionStatementSyntax>())
                {
                    var symbol = model.GetDeclaredSymbol(local, cancellationToken)!;
                    var owner = local.Ancestors().OfType<MethodDeclarationSyntax>().First().Identifier.ValueText;
                    schemes.Add(owner + ":" + symbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + symbol.RefKind + ":" + symbol.IsAsync);
                }
            }
            return schemes.Order(StringComparer.Ordinal).ToArray();
        }
        if (!(await Schemes(original).ConfigureAwait(false)).SequenceEqual(await Schemes(changed).ConfigureAwait(false)))
        { throw new InvalidOperationException("Local return fixes changed a declared local scheme."); }
        var probeSymbol = changed.GetTypeByMetadataName("LocalReturnProbe")!;
        foreach (var name in new[] { "Scalar", "Iface", "Array", "Pair", "Factory", "Iterator", "Async", "ValueAsync" })
        {
            if (probeSymbol.GetMembers(name).OfType<IMethodSymbol>().Single().TypeParameters.Length != 2)
            { throw new InvalidOperationException("Local return Fix All missed " + name); }
        }
        if (probeSymbol.GetMembers("Boxed").OfType<IMethodSymbol>().Single().Parameters[0].Type.SpecialType != SpecialType.System_Object)
        { throw new InvalidOperationException("Supplied boxing caller lost its original argument contract."); }
        Program.Log.Info("PASS LocalReturnFixAllRetainsDeclaredSchemesAndCallerContracts");
        var beforeContext = new System.Runtime.Loader.AssemblyLoadContext("BeforeLocalReturns", isCollectible: true);
        var afterContext = new System.Runtime.Loader.AssemblyLoadContext("AfterLocalReturns", isCollectible: true);
        try
        {
            Type Load(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Local return emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                return loadContext.LoadFromStream(image).GetType("LocalReturnProbe")!;
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
            var marker = new Uri("https://example.invalid/local-returns");
            foreach (var probe in new[] { before, after })
            {
                object tag = probe == before ? 0 : marker;
                foreach (var name in new[] { "Scalar", "Iterator" })
                {
                    var result = Invoke(probe, name, [typeof(object), typeof(Uri)], [marker, tag]);
                    if (!ReferenceEquals(result, marker) || !ReferenceEquals(probe.GetField("Stored")!.GetValue(null), marker))
                    { throw new InvalidOperationException("Local return reference identity changed: " + name); }
                }
                object number = 42;
                var comparable = Invoke(probe, "Iface", [typeof(IComparable), typeof(Uri)], [number, tag]);
                if (!ReferenceEquals(comparable, number) || !ReferenceEquals(probe.GetField("Stored")!.GetValue(null), number))
                { throw new InvalidOperationException("Local interface return reboxed an existing value."); }
                var boxed = Invoke(probe, "UseBoxed", [typeof(int)], [42]);
                if (!ReferenceEquals(boxed, probe.GetField("Stored")!.GetValue(null)))
                { throw new InvalidOperationException("Supplied local-return caller changed boxing identity."); }
            }
            Program.Log.Info("PASS LocalReturnOriginalReferenceAndBoxingIdentity");
            foreach (var name in new[] { "Scalar", "Iface", "Iterator" })
            {
                var result = Invoke(after, name, [typeof(decimal), typeof(Uri)], [23m, marker]);
                if (!Equals(result, 23m) || !Equals(after.GetField("Stored")!.GetValue(null), 23m))
                { throw new InvalidOperationException("Local return lost a new value domain: " + name); }
            }
            string[] array = ["payload"];
            if (!ReferenceEquals(Invoke(after, "Array", [typeof(string), typeof(Uri)], [array, marker]), array)
                || !ReferenceEquals(after.GetField("Stored")!.GetValue(null), array))
            { throw new InvalidOperationException("Local array return copied a covariant array."); }
            var pair = ((decimal, int))Invoke(after, "Pair", [typeof(decimal), typeof(Uri)], [((decimal)7, 9), marker])!;
            var storedPair = ((object, int))after.GetField("Stored")!.GetValue(null)!;
            if (pair != (7m, 9) || !Equals(storedPair.Item1, 7m) || storedPair.Item2 != 9)
            { throw new InvalidOperationException("Local tuple return lost its element conversion."); }
            Func<string> factory = () => "payload";
            if (!ReferenceEquals(Invoke(after, "Factory", [typeof(string), typeof(Uri)], [factory, marker]), factory)
                || !ReferenceEquals(after.GetField("Stored")!.GetValue(null), factory))
            { throw new InvalidOperationException("Local delegate return changed identity."); }
            Program.Log.Info("PASS LocalReturnNewDomainsArrayTupleAndDelegateConversions");
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(false);
            foreach (var name in new[] { "Async", "ValueAsync" })
            {
                foreach (var outcome in new[] { "Success", "Fault", "Cancel" })
                {
                    string[]? expectedEvents = null;
                    var failure = new InvalidOperationException("local-return");
                    foreach (var probe in new[] { before, after })
                    {
                        var events = (List<string>)probe.GetField("Events")!.GetValue(null)!;
                        events.Clear();
                        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        probe.GetField("Gate")!.SetValue(null, gate);
                        var invocation = Invoke(probe, name, [typeof(object), typeof(Uri)], [marker, probe == before ? 0 : marker])!;
                        var pending = invocation is Task<object> task ? task : ((ValueTask<object>)invocation).AsTask();
                        if (pending.IsCompleted)
                        { throw new InvalidOperationException("Local async return failed to suspend."); }
                        if (outcome == "Success")
                        { gate.SetResult(true); }
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
                            if (outcome != "Success" || !ReferenceEquals(returned, marker))
                            { throw new InvalidOperationException("Local async return changed its result."); }
                        }
                        catch (InvalidOperationException exception) when (outcome == "Fault" && ReferenceEquals(exception, failure)) { }
                        catch (OperationCanceledException exception) when (outcome == "Cancel" && exception.CancellationToken == canceled.Token) { }
                        expectedEvents ??= events.ToArray();
                        if (!expectedEvents.SequenceEqual(events))
                        { throw new InvalidOperationException("Local async return changed effects."); }
                    }
                }
            }
            Program.Log.Info("PASS LocalReturnTaskValueTaskSuspensionFaultCancellationAndEffects");
        }
        finally { beforeContext.Unload(); afterContext.Unload(); }
        var bad = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText("using System;class InvalidLocalReturns{void Check(){_=LocalReturnProbe.Array(new[]{1},0);_=LocalReturnProbe.Factory((Func<int>)(()=>1),0);}}", cancellationToken: cancellationToken));
        if (bad.GetDiagnostics(cancellationToken).Count(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS0452"; }) != 2)
        { throw new InvalidOperationException("Local covariant conversions must reject value-element domains."); }
        Program.Log.Info("PASS LocalReturnCovarianceRejectsValueDomains");
        return 5;
    }
}
