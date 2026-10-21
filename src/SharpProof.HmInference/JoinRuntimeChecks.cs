using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class JoinRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public static class JoinProbe{
                public static readonly List<string> Events=new();
                public static object? Stored;
                static T Read<T>(string label,T value){Events.Add(label);return value;}
                static object Other(){Events.Add("other");return new object();}
                public static object Conditional(bool flag,object value,int tag){object Local()=>Read("condition",flag)?Read("value",value):Other();Stored=Local();return value;}
                public static object Switch(int key,object value,int tag){object Local()=>Read("key",key) switch{0 when Read("guard",true)=>Read("value",value),_=>Other()};Stored=Local();return value;}
                public static object? Nullable(bool flag,object? value,int tag){object? Local()=>Read("condition",flag)?Read("value",value):null;Stored=Local();return value;}
                public static Task<object> Async(bool flag,Task<object> value,int tag){async Task<object> Local()=>Read("condition",flag)?await value:Other();return Local();}
                public static ValueTask<object> ValueAsync(bool flag,ValueTask<object> value,int tag){async ValueTask<object> Local()=>Read("condition",flag)?await value:Other();return Local();}
                public static Task<object?> NullableAsync(bool flag,Task<object?> value,int tag){async Task<object?> Local()=>Read("condition",flag)?await value:null;return Local();}
                public static int? Lift(bool flag,int left,int? right,int tag)=>flag?left:right;
                public static long Numeric(bool flag,int left,long right,int tag)=>Read("condition",flag)?Read("left",left):Read("right",right);
                public static long NumericSwitch(int key,int left,long right,int tag)=>Read("key",key) switch{0 when Read("guard",true)=>Read("left",left),_=>Read("right",right)};
                public static object Boxing(bool flag,int left,string right,int tag)=>Read("condition",flag)?Read("left",left):Read("right",right);
                public static object Throwing(bool flag,object value,Exception failure,int tag){object Local()=>Read("condition",flag)?Read("value",value):throw failure;Stored=Local();return value;}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Joins", "Joins", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Joins.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var symbol = changed.GetTypeByMetadataName("JoinProbe")!;
        foreach (var name in new[] { "Conditional", "Switch", "Nullable", "Async", "ValueAsync", "NullableAsync", "Throwing" })
        {
            if (symbol.GetMembers(name).OfType<IMethodSymbol>().Single().TypeParameters.Length != 2)
            { throw new InvalidOperationException("Join Fix All missed " + name); }
        }
        async Task<string[]> Schemes(Compilation compilation)
        {
            ArgumentNullException.ThrowIfNull(compilation);
            var schemes = new List<string>();
            foreach (var tree in compilation.SyntaxTrees)
            {
                var model = compilation.GetSemanticModel(tree);
                foreach (var local in (await tree.GetRootAsync(cancellationToken).ConfigureAwait(false)).DescendantNodes().OfType<LocalFunctionStatementSyntax>())
                {
                    var localSymbol = model.GetDeclaredSymbol(local, cancellationToken)!;
                    schemes.Add(localSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
            }
            return schemes.ToArray();
        }
        if (!(await Schemes(original).ConfigureAwait(false)).SequenceEqual(await Schemes(changed).ConfigureAwait(false)))
        { throw new InvalidOperationException("Join Fix All changed a fixed local result."); }
        Program.Log.Info("PASS JoinFixAllPreservesLocalReturnSchemes");
        var beforeContext = new System.Runtime.Loader.AssemblyLoadContext("BeforeJoins", isCollectible: true);
        var afterContext = new System.Runtime.Loader.AssemblyLoadContext("AfterJoins", isCollectible: true);
        try
        {
            Type Load(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Join emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                return loadContext.LoadFromStream(image).GetType("JoinProbe")!;
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
            var marker = new Uri("https://example.invalid/joins");
            foreach (var probe in new[] { before, after })
            {
                object tag = probe == before ? 0 : marker;
                var events = (List<string>)probe.GetField("Events")!.GetValue(null)!;
                foreach (var chosen in new[] { true, false })
                {
                    foreach (var name in new[] { "Conditional", "Switch", "Nullable" })
                    {
                        events.Clear();
                        var result = Invoke(probe, name, [typeof(object), typeof(Uri)], [name == "Switch" ? chosen ? 0 : 1 : chosen, marker, tag]);
                        var stored = probe.GetField("Stored")!.GetValue(null);
                        if (!ReferenceEquals(result, marker) || chosen && !ReferenceEquals(stored, marker) || !chosen && name == "Nullable" && stored is not null)
                        { throw new InvalidOperationException("Join reference/null result changed: " + name); }
                        var expected = name == "Switch" ? chosen ? new[] { "key", "guard", "value" } : ["key", "other"]
                            : chosen ? ["condition", "value"] : name == "Nullable" ? ["condition"] : ["condition", "other"];
                        if (!events.SequenceEqual(expected))
                        { throw new InvalidOperationException("Join branch evaluation changed: " + name); }
                    }
                    foreach (var name in new[] { "Numeric", "NumericSwitch", "Boxing" })
                    {
                        events.Clear();
                        var arguments = name == "Boxing" ? new object?[] { chosen, 17, "right", tag }
                            : [name == "NumericSwitch" ? chosen ? 0 : 1 : chosen, 17, 29L, tag];
                        var result = Invoke(probe, name, [typeof(Uri)], arguments);
                        if (name == "Boxing" ? !Equals(result, chosen ? 17 : "right") : !Equals(result, chosen ? 17L : 29L))
                        { throw new InvalidOperationException("Join numeric/boxing conversion changed: " + name); }
                        if (events.Count != (name == "NumericSwitch" && chosen ? 3 : 2) || events[^1] != (chosen ? "left" : "right"))
                        { throw new InvalidOperationException("Join evaluated an unselected conversion arm."); }
                    }
                }
                foreach (var chosen in new[] { true, false })
                {
                    foreach (var optional in new int?[] { null, 29 })
                    {
                        var lifted = Invoke(probe, "Lift", [typeof(int), typeof(Uri)], [chosen, 17, optional, tag]);
                        if (!Equals(lifted, chosen ? 17 : optional))
                        { throw new InvalidOperationException("Join changed nullable lifting."); }
                    }
                }
                var failure = new InvalidOperationException("join");
                try
                { _ = Invoke(probe, "Throwing", [typeof(object), typeof(Uri)], [false, marker, failure, tag]); throw new InvalidOperationException("Join throw arm did not throw."); }
                catch (System.Reflection.TargetInvocationException exception) when (ReferenceEquals(exception.InnerException, failure)) { }
            }
            Program.Log.Info("PASS JoinOriginalConversionsIdentityShortCircuitGuardsAndThrow");
            foreach (var name in new[] { "Conditional", "Switch", "Throwing" })
            {
                var arguments = name == "Throwing" ? new object?[] { true, 23m, new InvalidOperationException(), marker }
                    : [name == "Switch" ? 0 : true, 23m, marker];
                if (!Equals(Invoke(after, name, [typeof(decimal), typeof(Uri)], arguments), 23m))
                { throw new InvalidOperationException("Join lost a new capture domain: " + name); }
            }
            if (Invoke(after, "Nullable", [typeof(string), typeof(Uri)], [true, null, marker]) is not null
                || !Equals(Invoke(after, "Nullable", [typeof(string), typeof(Uri)], [true, "new", marker]), "new"))
            { throw new InvalidOperationException("Join lost a nullable new domain."); }
            if (!Equals(Invoke(after, "Lift", [typeof(decimal), typeof(Uri)], [true, 23m, null, marker]), 23m)
                || Invoke(after, "Lift", [typeof(decimal), typeof(Uri)], [false, 23m, null, marker]) is not null
                || !Equals(Invoke(after, "Lift", [typeof(decimal), typeof(Uri)], [false, 23m, (decimal?)29m, marker]), 29m))
            { throw new InvalidOperationException("Join lost new nullable value domains."); }
            Program.Log.Info("PASS JoinNewValueReferenceAndNullDomains");
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(false);
            foreach (var name in new[] { "Async", "ValueAsync", "NullableAsync" })
            {
                foreach (var outcome in new[] { "Success", "Fault", "Cancel", "Unselected" })
                {
                    foreach (var probe in new[] { before, after })
                    {
                        var events = (List<string>)probe.GetField("Events")!.GetValue(null)!;
                        events.Clear();
                        var gate = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var failure = new InvalidOperationException("join-await");
                        object input = name == "ValueAsync" ? new ValueTask<object>(gate.Task) : gate.Task;
                        var value = Invoke(probe, name, [typeof(object), typeof(Uri)], [outcome != "Unselected", input, probe == before ? 0 : marker])!;
                        var pending = value is Task<object> task ? task : ((ValueTask<object>)value).AsTask();
                        if (pending.IsCompleted != (outcome == "Unselected"))
                        { throw new InvalidOperationException("Join changed selected/unselected await suspension."); }
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
                            if (outcome == "Success" ? !ReferenceEquals(returned, marker) : outcome != "Unselected" || name == "NullableAsync" && returned is not null)
                            { throw new InvalidOperationException("Join changed async result."); }
                        }
                        catch (InvalidOperationException exception) when (outcome == "Fault" && ReferenceEquals(exception, failure)) { }
                        catch (OperationCanceledException exception) when (outcome == "Cancel" && exception.CancellationToken == canceled.Token) { }
                        if (!events.SequenceEqual(outcome == "Unselected" && name != "NullableAsync" ? (string[])["condition", "other"] : ["condition"]))
                        { throw new InvalidOperationException("Join changed async branch effects."); }
                    }
                }
            }
            var newTask = (Task<object>)Invoke(after, "Async", [typeof(string), typeof(Uri)], [true, Task.FromResult("new"), marker])!;
            var newValueTask = (ValueTask<object>)Invoke(after, "ValueAsync", [typeof(string), typeof(Uri)], [true, new ValueTask<string>("new"), marker])!;
            var nullTask = (Task<object?>)Invoke(after, "NullableAsync", [typeof(string), typeof(Uri)], [true, Task.FromResult<string?>(null), marker])!;
            if (!Equals(await newTask.WaitAsync(cancellationToken).ConfigureAwait(false), "new")
                || !Equals(await newValueTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false), "new")
                || await nullTask.WaitAsync(cancellationToken).ConfigureAwait(false) is not null)
            { throw new InvalidOperationException("Join lost new async payload domains."); }
            Program.Log.Info("PASS JoinTaskValueTaskSuspensionFaultCancellationAndNullableDomains");
            const string legacySource = """
                #nullable enable
                public static class JoinProbe{
                    public static object? Stored;
                    public static (object,object) Independent(bool flag,object left,object right,int tag){object Local()=>flag?left:right;Stored=Local();return(left,right);}
                    public static (object[],object[]) Arrays(bool flag,object[] left,object[] right,int tag){object[] Local()=>flag?left:right;Stored=Local();return(left,right);}
                }
                """;
            var legacyProject = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "LegacyJoins", "LegacyJoins", LanguageNames.CSharp,
                parseOptions: new CSharpParseOptions(LanguageVersion.CSharp8),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
                metadataReferences: Program.Compile([]).References));
            var legacyDocument = legacyProject.AddDocument("Legacy.cs", legacySource);
            var legacyOriginal = (await legacyDocument.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var legacyContext = new FixAllContext(legacyDocument, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
            var legacyAction = (await provider.GetFixAllProvider().GetFixAsync(legacyContext).ConfigureAwait(false))!;
            var legacySolution = (await legacyAction.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
            var legacyChanged = (await legacySolution.GetDocument(legacyDocument.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var legacyBefore = Load(legacyOriginal, beforeContext);
            var legacyAfter = Load(legacyChanged, afterContext);
            foreach (var probe in new[] { legacyBefore, legacyAfter })
            {
                foreach (var chosen in new[] { true, false })
                {
                    var result = (System.Runtime.CompilerServices.ITuple)Invoke(probe, "Independent", [typeof(string), typeof(object), typeof(Uri)], [chosen, "new", marker, probe == legacyBefore ? 0 : marker])!;
                    if (!Equals(result[0], "new") || !ReferenceEquals(result[1], marker)
                        || !ReferenceEquals(probe.GetField("Stored")!.GetValue(null), chosen ? result[0] : marker))
                    { throw new InvalidOperationException("Legacy conditional changed subtype capture identity."); }
                    string[] left = ["new"];
                    object[] right = [marker];
                    var pair = (System.Runtime.CompilerServices.ITuple)Invoke(probe, "Arrays", [typeof(string), typeof(object), typeof(Uri)], [chosen, left, right, probe == legacyBefore ? 0 : marker])!;
                    if (!ReferenceEquals(pair[0], left) || !ReferenceEquals(pair[1], right)
                        || !ReferenceEquals(probe.GetField("Stored")!.GetValue(null), chosen ? left : right))
                    { throw new InvalidOperationException("Legacy conditional changed array covariance identity."); }
                }
            }
            var invalid = legacyChanged.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Bad{void Check(){_=JoinProbe.Independent<int,string,int>(true,1,\"x\",0);}}", new CSharpParseOptions(LanguageVersion.CSharp8), cancellationToken: cancellationToken));
            if (!invalid.GetDiagnostics(cancellationToken).Any(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS0315"; }))
            { throw new InvalidOperationException("Legacy conditional admitted unrelated operand types."); }
            Program.Log.Info("PASS LegacyJoinFixAllSubtypeArraysIdentityAndInvalidDomains");
        }
        finally { beforeContext.Unload(); afterContext.Unload(); }
        return 5;
    }
}
