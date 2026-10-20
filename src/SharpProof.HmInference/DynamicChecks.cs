using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class DynamicChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("DynamicSignature", "static int M(dynamic receiver,int value)=>value;", "Suggestion", "T1 M<T1>(dynamic receiver, T1 value)"),
            ("DynamicIdentity", "static dynamic M(dynamic value)=>value;", "Concrete", null),
            ("DynamicResult", "static dynamic M(dynamic value,int tag)=>value;", "Suggestion", "dynamic M<T1>(dynamic value, T1 tag)"),
            ("DynamicTuple", "static (dynamic Item,int Tag) M(dynamic[] values,int tag)=>(values[0],tag);", "Suggestion", "(dynamic Item, T1 Tag) M<T1>(dynamic[] values, T1 tag)"),
            ("DynamicMember", "static dynamic M(dynamic value,int tag)=>value.Name;", "Suggestion", "dynamic M<T1>(dynamic value, T1 tag)"),
            ("DynamicInvocation", "static dynamic M(dynamic value,int count,string tag)=>value.Read(count);", "Suggestion", "dynamic M<T1>(dynamic value, int count, T1 tag)"),
            ("DynamicStaticInvocation", "static dynamic M(dynamic value,int tag)=>Pick(value);static int Pick(int value)=>value;static string Pick(string value)=>value;", "Suggestion", "dynamic M<T1>(dynamic value, T1 tag)"),
            ("DynamicMemberStore", "static void M(dynamic value,int count,string tag){value.Count=count;}", "Suggestion", "void M<T1>(dynamic value, int count, T1 tag)"),
            ("DynamicIndexer", "static dynamic M(dynamic value,int index,string tag)=>value[index];", "Suggestion", "dynamic M<T1>(dynamic value, int index, T1 tag)"),
            ("DynamicIndexerStore", "static void M(dynamic value,int index,string item,int tag){value[index]=item;}", "Suggestion", "void M<T1>(dynamic value, int index, string item, T1 tag)"),
            ("DynamicBinary", "static dynamic M(dynamic value,int count,string tag)=>value+count;", "Suggestion", "dynamic M<T1>(dynamic value, int count, T1 tag)"),
            ("DynamicUnary", "static dynamic M(dynamic value,int tag)=>-value;", "Suggestion", "dynamic M<T1>(dynamic value, T1 tag)"),
            ("DynamicIncrement", "static dynamic M(dynamic value,int tag)=>value++;", "Suggestion", "dynamic M<T1>(dynamic value, T1 tag)"),
            ("DynamicCompound", "static int M(int value,dynamic delta,string tag){value+=delta;return value;}", "Suggestion", "int M<T1>(int value, dynamic delta, T1 tag)"),
            ("DynamicCast", "static int M(dynamic value,int tag)=>(int)value;", "Suggestion", "int M<T1>(dynamic value, T1 tag)"),
            ("DynamicReturnConversion", "static int M(dynamic value,int tag)=>value;", "Suggestion", "int M<T1>(dynamic value, T1 tag)"),
            ("DynamicObjectReturn", "static object M(dynamic value,int tag)=>value;", "Suggestion", "object M<T1>(dynamic value, T1 tag)"),
            ("DynamicCondition", "static int M(dynamic flag,int value){if(flag)return value;return value;}", "Suggestion", "T1 M<T1>(dynamic flag, T1 value)"),
            ("DynamicAwait", "static async System.Threading.Tasks.Task<dynamic> M(dynamic task,int tag)=>await task;", "Suggestion", "global::System.Threading.Tasks.Task<dynamic> M<T1>(dynamic task, T1 tag)"),
            ("DynamicOutDeclaration", "static int M(dynamic target,int tag){target.Read(out int value);return value;}", "Suggestion", "int M<T1>(dynamic target, T1 tag)"),
            ("DynamicConstruction", "static Box M(dynamic value,int tag)=>new Box(value);class Box{public Box(int value){}}", "Suggestion", "global::Cases.Box M<T1>(dynamic value, T1 tag)"),
            ("DynamicDelegateInvocation", "static dynamic M(dynamic function,int value,string tag)=>function(value);", "Suggestion", "dynamic M<T1>(dynamic function, int value, T1 tag)"),
            ("DynamicConditionalMember", "static dynamic? M(dynamic? value,int tag)=>value?.Name;", "Suggestion", "dynamic? M<T1>(dynamic? value, T1 tag)"),
            ("DynamicCoalescing", "static dynamic M(dynamic value,int fallback,string tag)=>value??fallback;", "Suggestion", "dynamic M<T1>(dynamic value, int fallback, T1 tag)"),
            ("DynamicCoalescingStore", "static dynamic M(dynamic value,int fallback,string tag)=>value??=fallback;", "Suggestion", "dynamic M<T1>(dynamic value, int fallback, T1 tag)"),
            ("DynamicStaticRootControl", "static int M(int value,int tag)=>value;static dynamic Use(dynamic value)=>M(value,0);", "Rejected", null),
            ("DynamicObjectArgument", "static dynamic M(object value,int tag)=>Imported(value);static dynamic Imported(dynamic value)=>value;", "Suggestion", "dynamic M<T1>(object value, T1 tag)"),

            ("DynamicTaskIdentityBoundary", "static System.Threading.Tasks.Task<object> M(System.Threading.Tasks.Task<dynamic> value,int tag)=>value;", "Suggestion", "global::System.Threading.Tasks.Task<object> M<T1>(global::System.Threading.Tasks.Task<dynamic> value, T1 tag)"),
            ("DynamicNestedArrayBoundary", "static System.Threading.Tasks.Task<object[]> M(System.Threading.Tasks.Task<dynamic[]> value,int tag)=>value;", "Suggestion", "global::System.Threading.Tasks.Task<object[]> M<T1>(global::System.Threading.Tasks.Task<dynamic[]> value, T1 tag)"),
            ("DynamicTupleIdentityBoundary", "static (object Item,int Count) M((dynamic Item,int Count) value,int tag)=>value;", "Suggestion", "(object Item, T1 Count) M<T1, T2>((dynamic Item, T1 Count) value, T2 tag)"),
            ("DynamicNamedIdentityBoundary", "static Pair<object,int> M(Pair<dynamic,int> value,int tag)=>value;class Pair<A,B>{}", "Suggestion", "global::Cases.Pair<object, T1> M<T1, T2>(global::Cases.Pair<dynamic, T1> value, T2 tag)"),
            ("DynamicNamedArgumentBoundary", "static int M(Pair<object,int> value,int tag)=>Imported(value);static T Imported<T>(Pair<dynamic,T> value)=>default!;class Pair<A,B>{}", "Suggestion", "T1 M<T1, T2>(global::Cases.Pair<object, T1> value, T2 tag)"),
            ("DynamicTupleArgumentBoundary", "static int M((object Item,int Count) value,int tag)=>Imported(value);static T Imported<T>((dynamic Item,T Count) value)=>value.Count;", "Suggestion", "T1 M<T1, T2>((object Item, T1 Count) value, T2 tag)"),
            ("DynamicExplicitLocal", "static int M(object value,int tag){dynamic local=value;return local.Count;}", "Suggestion", "int M<T1>(object value, T1 tag)"),
            ("DynamicObjectLocal", "static object M(dynamic value,int tag){object local;local=value;return local;}", "Suggestion", "object M<T1>(dynamic value, T1 tag)"),
            ("DynamicNamedLocalBoundary", "static Pair<object,int> M(Pair<dynamic,int> value,int tag){var local=value;return local;}class Pair<A,B>{}", "Suggestion", "global::Cases.Pair<object, T1> M<T1, T2>(global::Cases.Pair<dynamic, T1> value, T2 tag)"),

            ("DynamicNamedReverseBoundary", "static Pair<dynamic,int> M(Pair<object,int> value,int tag)=>value;class Pair<A,B>{}", "Suggestion", "global::Cases.Pair<dynamic, T1> M<T1, T2>(global::Cases.Pair<object, T1> value, T2 tag)"),
            ("DynamicNamedReverseArgument", "static int M(Pair<dynamic,int> value,int tag)=>Imported(value);static T Imported<T>(Pair<object,T> value)=>default!;class Pair<A,B>{}", "Suggestion", "T1 M<T1, T2>(global::Cases.Pair<dynamic, T1> value, T2 tag)"),
            ("DynamicNestedNullableBoundary", "static Pair<object?,int>? M(Pair<dynamic?,int>? value,int tag)=>value;class Pair<A,B>{}", "Suggestion", "global::Cases.Pair<object?, T1>? M<T1, T2>(global::Cases.Pair<dynamic?, T1>? value, T2 tag)"),
            ("DynamicNestedOwnerBoundary", "static Owner<object>.Pair<int> M(Owner<dynamic>.Pair<int> value,int tag)=>value;class Owner<A>{public class Pair<B>{}}", "Suggestion", "global::Cases.Owner<object>.Pair<T1> M<T1, T2>(global::Cases.Owner<dynamic>.Pair<T1> value, T2 tag)"),

            ("DynamicNamedCallerBoundary", "static Pair<object,int> M(Pair<dynamic,int> value,int tag)=>value;static string Use(Pair<dynamic,int> value)=>M(value,0).Item.ToString()!;class Pair<A,B>{public A Item=default!;}", "Suggestion", "global::Cases.Pair<object, T1> M<T1, T2>(global::Cases.Pair<dynamic, T1> value, T2 tag)"),
            ("DynamicRectangularArrayBoundary", "static object[,] M(dynamic[,] value,int tag)=>value;", "Suggestion", "object[,] M<T1>(dynamic[,] value, T1 tag)"),

            ("DynamicNullComparison", "static bool M(dynamic value,int tag)=>value==null;", "Suggestion", "bool M<T1>(dynamic value, T1 tag)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("#nullable enable\nusing System;class Cases{" + test.Source + "}", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(result =>
            { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            if (result.Status != test.Status || result.Signature != test.Signature)
            { failures.Add(test.Name + ": expected " + test.Status + ": " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS " + test.Name); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return tests.Length + await Runtime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            public class DynamicTarget {
                public static string Trace="";
                private int count;
                public int Count { get { Trace+="get;";return count; } set { Trace+="set;";count=value; } }
                public string Name { get { Trace+="name;";return "target"; } }
                public string Read(int value){Trace+="int;";return "int:"+value;}
                public string Read(object value){Trace+="object;";return "object";}
                public void Read(out int value){Trace+="out;";value=7;}
                public string this[int index] {get{Trace+="index;";return "item:"+index;}set{Trace+="index-set;";}}
                public static DynamicTarget operator +(DynamicTarget left,int right){Trace+="add;";return new DynamicTarget{count=left.count+right};}
                public static implicit operator int(DynamicTarget value){Trace+="convert;";return value.count;}
            }
            public class DynamicCreated {
                public string Kind;
                public DynamicCreated(int value){Kind="int:"+value;}
                public DynamicCreated(object value){Kind="object";}
            }
            public class DynamicPair<A,B>{public A Item=default!;public B Count=default!;}
            public static class DynamicProbe {
                public static DynamicPair<object,int> BoundaryReturn(DynamicPair<dynamic,int> value,int tag)=>value;
                public static int BoundaryArgument(DynamicPair<object,int> value,int tag)=>DynamicItem(value);
                private static T DynamicItem<T>(DynamicPair<dynamic,T> value)=>value.Count;
                public static (object Item,int Count) TupleBoundary((dynamic Item,int Count) value,int tag)=>value;
                public static object[] ArrayBoundary(dynamic[] value,int tag)=>value;
                public static Task<object> TaskBoundary(Task<dynamic> value,int tag)=>value;
                public static DynamicPair<dynamic,int> ReverseBoundary(DynamicPair<object,int> value,int tag)=>value;

                public static dynamic Call(dynamic target,int value,int tag)=>target.Read(value);
                public static dynamic Member(dynamic target,int tag)=>target.Name;
                public static void Store(dynamic target,int value,int tag){target.Count=value;}
                public static dynamic Index(dynamic target,int index,int tag)=>target[index];
                public static void IndexStore(dynamic target,int index,string item,int tag){target[index]=item;}
                public static dynamic Add(dynamic target,int value,int tag)=>target+value;
                public static int Cast(dynamic target,int tag)=>(int)target;
                public static int Convert(dynamic target,int tag)=>target;
                public static int ReadOut(dynamic target,int tag){target.Read(out int value);return value;}
                public static dynamic? Conditional(dynamic? target,int tag)=>target?.Name;
                public static DynamicCreated Create(dynamic value,int tag)=>new DynamicCreated(value);
                public static async Task<dynamic> Await(dynamic task,int tag)=>await task;
                public static int Dispatch(dynamic value,int tag)=>Overload(value);
                private static int Overload(int value)=>value+1;
                private static int Overload(string value)=>value.Length;
            }
            public static class DynamicRunner {
                public static string Run(){
                    DynamicTarget.Trace="";
                    var target=new DynamicTarget();
                    DynamicProbe.Store(target,5,0);
                    string call=DynamicProbe.Call(target,3,0);
                    string member=DynamicProbe.Member(target,0);
                    string index=DynamicProbe.Index(target,2,0);
                    DynamicProbe.IndexStore(target,2,"changed",0);
                    var added=(DynamicTarget)DynamicProbe.Add(target,4,0);
                    int cast=DynamicProbe.Cast(added,0);
                    int convert=DynamicProbe.Convert(added,0);
                    int output=DynamicProbe.ReadOut(target,0);
                    string? missing=DynamicProbe.Conditional(null,0);
                    string? present=DynamicProbe.Conditional(target,0);
                    string created=DynamicProbe.Create(3,0).Kind;
                    object awaited=DynamicProbe.Await(Task.FromResult("awaited"),0).GetAwaiter().GetResult();
                    int first=DynamicProbe.Dispatch(3,0);
                    int second=DynamicProbe.Dispatch("word",0);
                    return DynamicTarget.Trace+"|"+call+"|"+member+"|"+index+"|"+cast+"|"+convert+"|"+output+"|"+missing+"|"+present+"|"+created+"|"+awaited+"|"+first+"|"+second;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "DynamicRuntime", "DynamicRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("DynamicRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false))
                .GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            var diagnostic = diagnostics.FirstOrDefault(diagnostic =>
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
                return diagnostic.Id == "HM0001" && root.FindNode(diagnostic.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "DynamicProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) =>
            { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 19)
        { throw new InvalidOperationException("Expected nineteen dynamic code fixes; got " + fixes); }
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var expected = before.GetType("DynamicRunner")!.GetMethod("Run")!.Invoke(null, null);
        var actual = after.GetType("DynamicRunner")!.GetMethod("Run")!.Invoke(null, null);
        if (!Equals(expected, actual))
        { throw new InvalidOperationException("Dynamic fixes changed binding or effects: " + expected + " -> " + actual); }
        var probe = after.GetType("DynamicProbe")!;
        var target = Activator.CreateInstance(after.GetType("DynamicTarget")!)!;
        var marker = new Uri("https://example.invalid/dynamic");
        if (!Equals(probe.GetMethod("Call")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [target, 8, marker]), "int:8")
            || !Equals(probe.GetMethod("Dispatch")!.MakeGenericMethod(typeof(Guid)).Invoke(null, ["five!", Guid.Empty]), 5)
            || probe.GetMethod("Conditional")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [null, marker]) is not null)
        { throw new InvalidOperationException("Dynamic binding changed with a new generic tag type."); }
        foreach (var assembly in new[] { before, after })
        {
            var call = assembly.GetType("DynamicProbe")!.GetMethod("Call")!;
            if (call.IsGenericMethodDefinition)
            { call = call.MakeGenericMethod(typeof(int)); }
            try
            {
                call.Invoke(null, [new object(), 1, 0]);
                throw new InvalidOperationException("Dynamic call unexpectedly accepted a missing member.");
            }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException?.GetType().FullName == "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException") { }
        }
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var awaiting = probe.GetMethod("Await")!.MakeGenericMethod(typeof(Uri));
        var result = (Task<object>)awaiting.Invoke(null, [pending.Task, marker])!;
        if (result.IsCompleted)
        { throw new InvalidOperationException("Dynamic await completed before its operand."); }
        pending.SetResult("resumed");
        if (!Equals(await result.WaitAsync(cancellationToken).ConfigureAwait(false), "resumed"))
        { throw new InvalidOperationException("Dynamic await lost its result."); }
        var fault = new InvalidOperationException("dynamic fault");
        try
        {
            await ((Task<object>)awaiting.Invoke(null, [Task.FromException<string>(fault), marker])!).ConfigureAwait(false);
            throw new InvalidOperationException("Dynamic await lost its fault.");
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, fault)) { }
        foreach (var name in new[] { "BoundaryReturn", "BoundaryArgument", "TupleBoundary", "TaskBoundary", "ReverseBoundary", "ArrayBoundary" })
        {
            var originalMethod = before.GetType("DynamicProbe")!.GetMethod(name)!;
            var changedMethod = probe.GetMethod(name)!;
            if (DynamicFlags(originalMethod.ReturnParameter) != DynamicFlags(changedMethod.ReturnParameter))
            { throw new InvalidOperationException(name + " changed return dynamic metadata."); }
            var originalParameters = originalMethod.GetParameters();
            var changedParameters = changedMethod.GetParameters();
            for (var i = 0; i < originalParameters.Length; i++)
            {
                if (DynamicFlags(originalParameters[i]) != DynamicFlags(changedParameters[i]))
                { throw new InvalidOperationException(name + " changed parameter dynamic metadata."); }
            }
        }
        var pairType = after.GetType("DynamicPair" + (char)96 + "2")!.MakeGenericType(typeof(object), typeof(decimal));
        var pair = Activator.CreateInstance(pairType)!;
        var payload = new object();
        pairType.GetField("Item")!.SetValue(pair, payload);
        pairType.GetField("Count")!.SetValue(pair, 123.25m);
        if (!ReferenceEquals(probe.GetMethod("BoundaryReturn")!.MakeGenericMethod(typeof(decimal), typeof(Uri)).Invoke(null, [pair, marker]), pair)
            || !ReferenceEquals(probe.GetMethod("ReverseBoundary")!.MakeGenericMethod(typeof(decimal), typeof(Uri)).Invoke(null, [pair, marker]), pair)
            || !Equals(probe.GetMethod("BoundaryArgument")!.MakeGenericMethod(typeof(decimal), typeof(Uri)).Invoke(null, [pair, marker]), 123.25m))
        { throw new InvalidOperationException("Dynamic identity boundaries changed a reference or pinned an unrelated field."); }
        var tupleResult = ((object Item, decimal Count))probe.GetMethod("TupleBoundary")!.MakeGenericMethod(typeof(decimal), typeof(Uri))
            .Invoke(null, [(payload, 123.25m), marker])!;
        if (!ReferenceEquals(tupleResult.Item, payload) || tupleResult.Count != 123.25m)
        { throw new InvalidOperationException("Dynamic tuple metadata conversion changed a value."); }
        var taskPayload = Task.FromResult(payload);
        if (!ReferenceEquals(probe.GetMethod("TaskBoundary")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [taskPayload, marker]), taskPayload))
        { throw new InvalidOperationException("Dynamic task metadata conversion changed task identity."); }
        var arrayPayload = new[] { payload };
        if (!ReferenceEquals(probe.GetMethod("ArrayBoundary")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [arrayPayload, marker]), arrayPayload))
        { throw new InvalidOperationException("Dynamic array metadata conversion changed array identity."); }

        Program.Log.Info("PASS DynamicIdentityBoundaryMetadataAndGenericSiblings");

        Program.Log.Info("PASS DynamicCodeFixOriginalAndGeneralizedRuntime");
        Program.Log.Info("PASS DynamicAwaitSuspensionAndFault");
        return 3;
    }

    private static string DynamicFlags(System.Reflection.ParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var attribute = parameter.GetCustomAttributes(typeof(System.Runtime.CompilerServices.DynamicAttribute), false)
            .OfType<System.Runtime.CompilerServices.DynamicAttribute>().SingleOrDefault();
        return attribute is null ? "none" : string.Join(",", attribute.TransformFlags);
    }


    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var problems = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
        { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (problems.Length != 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems.Select(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.ToString(); })));
        }
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }

}
