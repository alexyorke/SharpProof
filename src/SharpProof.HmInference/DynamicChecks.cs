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

            ("DynamicForeachSource", "static int M(dynamic values,int seed){foreach(var item in values){}return seed;}", "Suggestion", "T1 M<T1>(dynamic values, T1 seed)"),
            ("DynamicForeachArray", "static int M(dynamic[] values,int seed){foreach(var item in values){}return seed;}", "Suggestion", "T1 M<T1>(dynamic[] values, T1 seed)"),
            ("DynamicForeachSequence", "static int M(System.Collections.Generic.IEnumerable<dynamic> values,int seed){foreach(var item in values){}return seed;}", "Suggestion", "T1 M<T1>(global::System.Collections.Generic.IEnumerable<dynamic> values, T1 seed)"),
            ("DynamicForeachAsyncSequence", "static async System.Threading.Tasks.Task<int> M(System.Collections.Generic.IAsyncEnumerable<dynamic> values,int seed){await foreach(var item in values){}return seed;}", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Collections.Generic.IAsyncEnumerable<dynamic> values, T1 seed)"),
            ("DynamicForeachTypedValue", "static int M(dynamic values,int tag){foreach(int item in values)return item;return 0;}", "Suggestion", "int M<T1>(dynamic values, T1 tag)"),
            ("DynamicForeachTypedReference", "static object M(dynamic values,object fallback,int tag){foreach(object item in values)return item;return fallback;}", "Suggestion", "object M<T1>(dynamic values, object fallback, T1 tag)"),
            ("DynamicForeachExplicitDynamic", "static int M(System.Collections.Generic.IEnumerable<int> values,int seed){foreach(dynamic item in values){}return seed;}", "Suggestion", "T1 M<T1>(global::System.Collections.Generic.IEnumerable<int> values, T1 seed)"),
            ("DynamicForeachTuple", "static int M(System.Collections.Generic.IEnumerable<(dynamic Item,int Count)> values,int seed){foreach(var(item,count) in values)seed=count;return seed;}", "Suggestion", "T1 M<T1>(global::System.Collections.Generic.IEnumerable<(dynamic Item, T1 Count)> values, T1 seed)"),
            ("DynamicUsing", "static int M(dynamic resource,int value){using(resource){return value;}}", "Suggestion", "T1 M<T1>(dynamic resource, T1 value)"),
            ("DynamicUsingNullable", "static int M(dynamic? resource,int value){using(resource){return value;}}", "Suggestion", "T1 M<T1>(dynamic? resource, T1 value)"),
            ("DynamicUsingDeclaration", "static int M(dynamic resource,int value){using var lease=resource;return value;}", "Suggestion", "T1 M<T1>(dynamic resource, T1 value)"),
            ("DynamicUsingTypedContract", "static int M(dynamic resource,int value){using System.IDisposable lease=resource;return value;}", "Suggestion", "T1 M<T1>(dynamic resource, T1 value)"),

            ("DynamicForeachIterator", "static System.Collections.Generic.IEnumerable<dynamic> M(dynamic values,int tag){foreach(var item in values)yield return item;}", "Suggestion", "global::System.Collections.Generic.IEnumerable<dynamic> M<T1>(dynamic values, T1 tag)"),
            ("DynamicForeachAsyncIterator", "static async System.Collections.Generic.IAsyncEnumerable<dynamic> M(System.Collections.Generic.IAsyncEnumerable<dynamic> values,int tag){await foreach(var item in values)yield return item;}", "Suggestion", "global::System.Collections.Generic.IAsyncEnumerable<dynamic> M<T1>(global::System.Collections.Generic.IAsyncEnumerable<dynamic> values, T1 tag)"),

            ("DynamicReceiverRebinding", "public int M(int value)=>value;static dynamic Use(dynamic target)=>target.M(\"changed\");", "Rejected", null),
            ("DynamicReceiverNewArity", "public int M(int value)=>value;static dynamic Use(dynamic target)=>target.M<int>(1);", "Rejected", null),
            ("DynamicReceiverOptional", "public int M(int value,int tag=0)=>value;static dynamic Use(dynamic target)=>target.M(value:\"changed\");", "Rejected", null),
            ("DynamicReceiverParams", "public int M(int value,params object[] rest)=>value;static dynamic Use(dynamic target)=>target.M(\"changed\",1,2);", "Rejected", null),
            ("DynamicReceiverConditional", "public int M(int value)=>value;static dynamic? Use(dynamic? target)=>target?.M(\"changed\");", "Rejected", null),
            ("DynamicReceiverStaticControl", "public static int M(int value)=>value;static dynamic Use(dynamic target)=>target.M(\"changed\");", "Suggestion", "T1 M<T1>(T1 value)"),
            ("DynamicReceiverArityControl", "public int M(int value)=>value;static dynamic Use(dynamic target)=>target.M<int,string>(1);", "Suggestion", "T1 M<T1>(T1 value)"),
            ("DynamicReceiverCountControl", "public int M(int value)=>value;static dynamic Use(dynamic target)=>target.M(1,2);", "Suggestion", "T1 M<T1>(T1 value)"),
            ("DynamicReceiverNameControl", "public int M(int value)=>value;static dynamic Use(dynamic target)=>target.M(other:\"changed\");", "Suggestion", "T1 M<T1>(T1 value)"),
            ("DynamicReceiverPrivateControl", "private int M(int value)=>value;}class Other{static dynamic Use(dynamic target)=>target.M(\"changed\");}class Tail{", "Suggestion", "T1 M<T1>(T1 value)"),

            ("DynamicReceiverKnownSealedControl", "public int M(int value)=>value;static dynamic Use(Other target)=>((dynamic)target).M(\"changed\");sealed class Other{public Func<string,string> M=>value=>value;}", "Suggestion", "T1 M<T1>(T1 value)"),
            ("DynamicReceiverKnownDerived", "public int M(int value)=>value;static dynamic Use(Derived target)=>((dynamic)target).M(\"changed\");sealed class Derived:Cases{}", "Rejected", null),
            ("DynamicReceiverUserConversion", "public int M(int value)=>value;static dynamic Use(Other target)=>((dynamic)(Cases)target).M(\"changed\");sealed class Other{public static explicit operator Cases(Other value)=>new Cases();}", "Rejected", null),
            ("DynamicReceiverRuntimeConversion", "public int M(int value)=>value;static dynamic Use(Other target)=>((dynamic)(Cases)(dynamic)target).M(\"changed\");sealed class Other{public static explicit operator Cases(Other value)=>new Cases();}", "Rejected", null),
            ("DynamicReceiverNullableBox", "struct Box{public int M(int value)=>value;}static dynamic Use(Box? target)=>((dynamic)target!).M(\"changed\");", "Rejected", null),
            ("DynamicReceiverExistingArity", "public int M<T>(int value)=>value;static dynamic Use(dynamic target)=>target.M<int>(1);", "Rejected", null),
            ("DynamicReceiverAddedArity", "public int M<T>(int value)=>value;static dynamic Use(dynamic target)=>target.M<int,string>(\"changed\");", "Rejected", null),
            ("DynamicReceiverRefControl", "public int M(int value)=>value;static dynamic Use(dynamic target,ref int value)=>target.M(ref value);", "Suggestion", "T1 M<T1>(T1 value)"),

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
        return tests.Length + await Runtime(cancellationToken).ConfigureAwait(false)
            + await EnumerationRuntime(cancellationToken).ConfigureAwait(false)
            + await ReceiverRuntime(cancellationToken).ConfigureAwait(false)
            + await RefModifierRuntime(cancellationToken).ConfigureAwait(false);
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

    private static async Task<int> EnumerationRuntime(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            public static class DynamicLoopControl {
                public static string Trace="";
                public static TaskCompletionSource<bool> Move=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public static TaskCompletionSource<bool> Dispose=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public static TaskCompletionSource<bool> EnteredDispose=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public static void Reset(bool move,bool dispose){
                    Trace="";Move=new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Dispose=new(TaskCreationOptions.RunContinuationsAsynchronously);
                    EnteredDispose=new(TaskCreationOptions.RunContinuationsAsynchronously);
                    if(move)Move.SetResult(true);if(dispose)Dispose.SetResult(true);
                }
            }
            public sealed class DynamicLoopSequence:IEnumerable {
                public PatternCursor GetEnumerator(){DynamicLoopControl.Trace+="pattern-get;";return new();}
                IEnumerator IEnumerable.GetEnumerator(){DynamicLoopControl.Trace+="interface-get;";return new Cursor();}
                public sealed class PatternCursor {
                    public object Current=>0;
                    public bool MoveNext()=>false;
                }
                private sealed class Cursor:IEnumerator,IDisposable {
                    private int index;
                    public object Current {get{DynamicLoopControl.Trace+="current;";return index;}}
                    public bool MoveNext(){DynamicLoopControl.Trace+="move;";return ++index<=2;}
                    public void Reset()=>throw new NotSupportedException();
                    public void Dispose(){DynamicLoopControl.Trace+="pattern-dispose;";}
                    void IDisposable.Dispose(){DynamicLoopControl.Trace+="interface-dispose;";}
                }
            }
            public sealed class DynamicPatternOnly {
                public DynamicLoopSequence.PatternCursor GetEnumerator()=>new();
            }
            public sealed class DynamicPatternResource {public void Dispose(){}}
            public sealed class DynamicLoopResource:IDisposable {
                public void Dispose(){DynamicLoopControl.Trace+="resource-pattern;";}
                void IDisposable.Dispose(){DynamicLoopControl.Trace+="resource-interface;";}
            }
            public sealed class DynamicAsyncLoop:IAsyncEnumerable<object> {
                public IAsyncEnumerator<object> GetAsyncEnumerator(CancellationToken token=default)=>new Cursor();
                private sealed class Cursor:IAsyncEnumerator<object> {
                    private int index;
                    public dynamic Current {get{DynamicLoopControl.Trace+="async-current;";return index;}}
                    public async ValueTask<bool> MoveNextAsync(){await DynamicLoopControl.Move.Task;DynamicLoopControl.Trace+="async-move;";return ++index<=2;}
                    public async ValueTask DisposeAsync(){
                        DynamicLoopControl.Trace+="async-dispose-start;";DynamicLoopControl.EnteredDispose.TrySetResult(true);
                        await DynamicLoopControl.Dispose.Task;DynamicLoopControl.Trace+="async-disposed;";
                    }
                }
            }
            public static class DynamicLoopProbe {
                public static IEnumerable<dynamic> Iterator(dynamic values,int tag){foreach(var item in values)yield return item;}
                public static async IAsyncEnumerable<dynamic> AsyncIterator(IAsyncEnumerable<dynamic> values,int tag){await foreach(var item in values)yield return item;}
                public static int Sync(dynamic values,int seed){foreach(var item in values){}return seed;}
                public static int Break(dynamic values,int seed){foreach(var item in values){break;}return seed;}
                public static int Typed(dynamic values,int tag){foreach(int item in values)return item;return 0;}
                public static int Static(IEnumerable<dynamic> values,int seed){foreach(var item in values){}return seed;}
                public static async Task<int> Async(IAsyncEnumerable<dynamic> values,int seed){await foreach(var item in values){}return seed;}
                public static int Use(dynamic resource,int seed){using(resource){return seed;}}
                public static int UseDeclared(dynamic resource,int seed){using var lease=resource;return seed;}
                public static int UseNullable(dynamic? resource,int seed){using(resource){return seed;}}
                public static int Throwing(dynamic values,int seed,bool fail){foreach(var item in values){if(fail)throw new InvalidOperationException("loop");}return seed;}
                public static dynamic First(dynamic values,dynamic fallback,int tag){foreach(var item in values)return item;return fallback;}
            }
            public static class DynamicLoopRunner {
                public static string Run(){
                    DynamicLoopControl.Reset(true,true);
                    var sequence=new DynamicLoopSequence();var resource=new DynamicLoopResource();
                    int sync=DynamicLoopProbe.Sync(sequence,1);int stop=DynamicLoopProbe.Break(sequence,2);
                    int typed=DynamicLoopProbe.Typed(sequence,0);int stat=DynamicLoopProbe.Static(new dynamic[]{1,2},3);
                    int async=DynamicLoopProbe.Async(new DynamicAsyncLoop(),4).GetAwaiter().GetResult();
                    int used=DynamicLoopProbe.Use(resource,5);int declared=DynamicLoopProbe.UseDeclared(resource,6);
                    int missing=DynamicLoopProbe.UseNullable(null,7);int complete=DynamicLoopProbe.Throwing(sequence,8,false);
                    object first=DynamicLoopProbe.First(sequence,0,0);
                    return DynamicLoopControl.Trace+"|"+sync+"|"+stop+"|"+typed+"|"+stat+"|"+async+"|"+used+"|"+declared+"|"+missing+"|"+complete+"|"+first;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "DynamicEnumeration", "DynamicEnumeration", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("DynamicEnumeration.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false))
                .GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            var diagnostic = diagnostics.FirstOrDefault(diagnostic =>
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
                return diagnostic.Id == "HM0001" && root.FindNode(diagnostic.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "DynamicLoopProbe" };
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
        if (fixes != 12)
        { throw new InvalidOperationException("Expected twelve dynamic enumeration/resource fixes; got " + fixes); }
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var expected = before.GetType("DynamicLoopRunner")!.GetMethod("Run")!.Invoke(null, null);
        var actual = after.GetType("DynamicLoopRunner")!.GetMethod("Run")!.Invoke(null, null);
        if (!Equals(expected, actual) || actual is not string trace || !trace.Contains("interface-get;", StringComparison.Ordinal)
            || trace.Contains("pattern-get;", StringComparison.Ordinal) || trace.Contains("resource-pattern;", StringComparison.Ordinal))
        { throw new InvalidOperationException("Dynamic enumeration/disposal changed protocol or effects: " + expected + " -> " + actual); }
        var marker = new Uri("https://example.invalid/dynamic-loop");
        var probe = after.GetType("DynamicLoopProbe")!;
        var control = after.GetType("DynamicLoopControl")!;
        var reset = control.GetMethod("Reset")!;
        var sequence = Activator.CreateInstance(after.GetType("DynamicLoopSequence")!)!;
        var resource = Activator.CreateInstance(after.GetType("DynamicLoopResource")!)!;
        if (!ReferenceEquals(probe.GetMethod("Sync")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [sequence, marker]), marker)
            || !ReferenceEquals(probe.GetMethod("Use")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [resource, marker]), marker))
        { throw new InvalidOperationException("Dynamic enumeration/resource fixes lost generic values."); }
        foreach (var assembly in new[] { before, after })
        {
            foreach (var test in new[] { (Method: "Sync", Type: "DynamicPatternOnly"), (Method: "Use", Type: "DynamicPatternResource") })
            {
                var method = assembly.GetType("DynamicLoopProbe")!.GetMethod(test.Method)!;
                if (method.IsGenericMethodDefinition)
                { method = method.MakeGenericMethod(typeof(int)); }
                try
                {
                    method.Invoke(null, [Activator.CreateInstance(assembly.GetType(test.Type)!), 0]);
                    throw new InvalidOperationException("Dynamic protocol unexpectedly accepted only a pattern.");
                }
                catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException?.GetType().FullName == "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException") { }
            }
            var localControl = assembly.GetType("DynamicLoopControl")!;
            localControl.GetMethod("Reset")!.Invoke(null, [true, true]);
            var throwing = assembly.GetType("DynamicLoopProbe")!.GetMethod("Throwing")!;
            if (throwing.IsGenericMethodDefinition)
            { throwing = throwing.MakeGenericMethod(typeof(int)); }
            try
            {
                throwing.Invoke(null, [Activator.CreateInstance(assembly.GetType("DynamicLoopSequence")!), 0, true]);
                throw new InvalidOperationException("Dynamic loop body did not throw.");
            }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is InvalidOperationException { Message: "loop" }) { }
            if (!((string)localControl.GetField("Trace")!.GetValue(null)!).Contains("interface-dispose;", StringComparison.Ordinal))
            { throw new InvalidOperationException("Dynamic loop body failure skipped disposal."); }
            localControl.GetMethod("Reset")!.Invoke(null, [true, true]);
            var iterator = assembly.GetType("DynamicLoopProbe")!.GetMethod("Iterator")!;
            if (iterator.IsGenericMethodDefinition)
            { iterator = iterator.MakeGenericMethod(typeof(int)); }
            var values = (IEnumerable<object>)iterator.Invoke(null, [Activator.CreateInstance(assembly.GetType("DynamicLoopSequence")!), 0])!;
            if (!Equals(localControl.GetField("Trace")!.GetValue(null), ""))
            { throw new InvalidOperationException("Dynamic iterator executed before enumeration."); }
            using (var enumerator = values.GetEnumerator())
            {
                if (!enumerator.MoveNext() || !Equals(enumerator.Current, 1))
                { throw new InvalidOperationException("Dynamic iterator lost its first value."); }
            }
            if (!((string)localControl.GetField("Trace")!.GetValue(null)!).Contains("interface-dispose;", StringComparison.Ordinal))
            { throw new InvalidOperationException("Dynamic iterator early disposal skipped its inner enumerator."); }

        }
        var asyncMethod = probe.GetMethod("Async")!.MakeGenericMethod(typeof(Uri));
        var asyncSequence = Activator.CreateInstance(after.GetType("DynamicAsyncLoop")!)!;
        reset.Invoke(null, [false, false]);
        var pending = (Task<Uri>)asyncMethod.Invoke(null, [asyncSequence, marker])!;
        if (pending.IsCompleted)
        { throw new InvalidOperationException("Dynamic-element async enumeration completed before MoveNextAsync."); }
        ((TaskCompletionSource<bool>)control.GetField("Move")!.GetValue(null)!).SetResult(true);
        await ((TaskCompletionSource<bool>)control.GetField("EnteredDispose")!.GetValue(null)!).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (pending.IsCompleted)
        { throw new InvalidOperationException("Dynamic-element async enumeration skipped disposal suspension."); }
        ((TaskCompletionSource<bool>)control.GetField("Dispose")!.GetValue(null)!).SetResult(true);
        if (!ReferenceEquals(await pending.WaitAsync(cancellationToken).ConfigureAwait(false), marker))
        { throw new InvalidOperationException("Dynamic-element async enumeration lost its generic result."); }
        reset.Invoke(null, [false, true]);
        var faulted = (Task<Uri>)asyncMethod.Invoke(null, [asyncSequence, marker])!;
        var fault = new InvalidOperationException("dynamic MoveNextAsync");
        ((TaskCompletionSource<bool>)control.GetField("Move")!.GetValue(null)!).SetException(fault);
        try
        {
            await faulted.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Dynamic-element async enumeration lost its fault.");
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, fault)) { }
        if (!((string)control.GetField("Trace")!.GetValue(null)!).Contains("async-disposed;", StringComparison.Ordinal))
        { throw new InvalidOperationException("Async enumeration fault skipped disposal."); }
        reset.Invoke(null, [false, true]);
        var cancelled = (Task<Uri>)asyncMethod.Invoke(null, [asyncSequence, marker])!;
        ((TaskCompletionSource<bool>)control.GetField("Move")!.GetValue(null)!).SetCanceled(cancellationToken);
        try
        {
            await cancelled.WaitAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Dynamic-element async enumeration lost cancellation.");
        }
        catch (OperationCanceledException) when (cancelled.IsCanceled) { }
        if (!((string)control.GetField("Trace")!.GetValue(null)!).Contains("async-disposed;", StringComparison.Ordinal))
        { throw new InvalidOperationException("Async enumeration cancellation skipped disposal."); }
        reset.Invoke(null, [false, false]);
        var iteratorMethod = probe.GetMethod("AsyncIterator")!.MakeGenericMethod(typeof(Uri));
        var asyncValues = (IAsyncEnumerable<object>)iteratorMethod.Invoke(null, [asyncSequence, marker])!;
        var asyncIteratorEnumerator = asyncValues.GetAsyncEnumerator(cancellationToken);
        await using (asyncIteratorEnumerator.ConfigureAwait(false))
        {
            var moving = asyncIteratorEnumerator.MoveNextAsync();
            if (moving.IsCompleted)
            { throw new InvalidOperationException("Dynamic-element async iterator completed before its source."); }
            ((TaskCompletionSource<bool>)control.GetField("Move")!.GetValue(null)!).SetResult(true);
            if (!await moving.ConfigureAwait(false) || !Equals(asyncIteratorEnumerator.Current, 1))
            { throw new InvalidOperationException("Dynamic-element async iterator lost its first value."); }
            var disposing = asyncIteratorEnumerator.DisposeAsync();
            await ((TaskCompletionSource<bool>)control.GetField("EnteredDispose")!.GetValue(null)!).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (disposing.IsCompleted)
            { throw new InvalidOperationException("Dynamic-element async iterator skipped pending inner disposal."); }
            ((TaskCompletionSource<bool>)control.GetField("Dispose")!.GetValue(null)!).SetResult(true);
            await disposing.ConfigureAwait(false);
        }
        Program.Log.Info("PASS DynamicElementIteratorsDeferAndDisposeInnerEnumerators");

        Program.Log.Info("PASS DynamicEnumerationAndResourceCodeFixRuntime");
        Program.Log.Info("PASS DynamicEnumerationInterfaceProtocolsAndFailureCleanup");
        Program.Log.Info("PASS DynamicElementAsyncEnumerationSuspensionFaultAndCancellation");
        return 4;
    }


    private static async Task<int> ReceiverRuntime(CancellationToken cancellationToken)
    {
        const string targetSource = """
            #nullable enable
            public class ReceiverTarget {
                public int Rebound(int value)=>value;
                public int Rebound(string value)=>value.Length;
                public int Accept(int value)=>value;
            }
            """;
        const string callerSource = """
            #nullable enable
            public static class ReceiverCaller {
                public static object Call()=>Invoke(new ReceiverTarget());
                public static dynamic Invoke(dynamic target)=>target.Rebound(1.5);
                public static object CallExplicit()=>InvokeExplicit(new ReceiverTarget());
                public static dynamic InvokeExplicit(dynamic target)=>target.Rebound<int>(1);
                public static int Good()=>new ReceiverTarget().Rebound(3);
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "DynamicReceiver", "DynamicReceiver", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var targetDocument = project.AddDocument("ReceiverTarget.cs", targetSource);
        var callerDocument = targetDocument.Project.AddDocument("ReceiverCaller.cs", callerSource);
        var document = callerDocument.Project.GetDocument(targetDocument.Id)!;
        var beforeCompilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var before = Load(beforeCompilation, cancellationToken);
        var unsafeCompilation = Program.Compile([
            CSharpSyntaxTree.ParseText(targetSource.Replace("public int Rebound(int value)=>value;", "public T Rebound<T>(T value)=>value;", StringComparison.Ordinal), cancellationToken: cancellationToken),
            CSharpSyntaxTree.ParseText(callerSource, cancellationToken: cancellationToken)]);
        var unsafeCandidate = Load(unsafeCompilation, cancellationToken);
        var changedCaller = unsafeCandidate.GetType("ReceiverCaller")!;
        if (!Equals(changedCaller.GetMethod("Call")!.Invoke(null, null), 1.5)
            || !Equals(changedCaller.GetMethod("CallExplicit")!.Invoke(null, null), 1))
        { throw new InvalidOperationException("Dynamic receiver regression did not reproduce the unsafe candidate behavior."); }
        var diagnostics = await beforeCompilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false))
            .GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        var candidates = diagnostics.Where(diagnostic =>
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            return diagnostic.Id == "HM0001" && diagnostic.Location.SourceTree == root.SyntaxTree
                && root.FindNode(diagnostic.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent
                    is ClassDeclarationSyntax { Identifier.ValueText: "ReceiverTarget" };
        }).ToArray();
        if (candidates.Length != 1 || root.FindNode(candidates[0].Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().First().Identifier.ValueText != "Accept")
        { throw new InvalidOperationException("Unresolved dynamic receiver either exposed an unsafe fix or suppressed an unrelated method."); }
        var actions = new List<CodeAction>();
        var provider = new HmSignatureCodeFixProvider();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, candidates[0], (action, _) =>
        { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var assembly in new[] { before, after })
        {
            var caller = assembly.GetType("ReceiverCaller")!;
            foreach (var name in new[] { "Call", "CallExplicit" })
            {
                try
                {
                    caller.GetMethod(name)!.Invoke(null, null);
                    throw new InvalidOperationException("Dynamic receiver call stopped preserving its original binding failure.");
                }
                catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException?.GetType().FullName == "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException") { }
            }
            if (!Equals(caller.GetMethod("Good")!.Invoke(null, null), 3))
            { throw new InvalidOperationException("An existing successful instance call changed."); }
        }
        var target = Activator.CreateInstance(after.GetType("ReceiverTarget")!)!;
        var marker = new Uri("https://example.invalid/dynamic-receiver");
        if (!ReferenceEquals(after.GetType("ReceiverTarget")!.GetMethod("Accept")!.MakeGenericMethod(typeof(Uri)).Invoke(target, [marker]), marker))
        { throw new InvalidOperationException("An unrelated instance fix lost its new generic domain."); }
        Program.Log.Info("PASS UnresolvedDynamicReceiverRebindingProof");
        Program.Log.Info("PASS CrossFileDynamicReceiverProtectionAndUnrelatedIdeFix");
        return 2;
    }


    private static async Task<int> RefModifierRuntime(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            public class RefModifierTarget {
                public int M(ref int value,string tag){value++;return value;}
            }
            public static class RefModifierCaller {
                public static dynamic Wrong(dynamic target,out int value)=>target.M(out value,"tag");
                public static int Good(RefModifierTarget target){int value=3;return target.M(ref value,"tag");}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "DynamicRefModifier", "DynamicRefModifier", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("DynamicRefModifier.cs", source);
        var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var before = Load(compilation, cancellationToken);
        var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false))
            .GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        var candidates = diagnostics.Where(diagnostic =>
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            return diagnostic.Id == "HM0001" && root.FindNode(diagnostic.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>()
                .FirstOrDefault()?.Identifier.ValueText == "M";
        }).ToArray();
        if (candidates.Length != 1)
        { throw new InvalidOperationException("A mismatched out argument suppressed a safe ref-method suggestion."); }
        var actions = new List<CodeAction>();
        var provider = new HmSignatureCodeFixProvider();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, candidates[0], (action, _) =>
        { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
        var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var assembly in new[] { before, after })
        {
            var target = Activator.CreateInstance(assembly.GetType("RefModifierTarget")!)!;
            var caller = assembly.GetType("RefModifierCaller")!;
            try
            {
                caller.GetMethod("Wrong")!.Invoke(null, [target, null]);
                throw new InvalidOperationException("An out argument unexpectedly bound to a ref parameter.");
            }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException?.GetType().FullName == "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException") { }
            if (!Equals(caller.GetMethod("Good")!.Invoke(null, [target]), 4))
            { throw new InvalidOperationException("The valid ref call changed its result."); }
        }
        var changedMethod = after.GetType("RefModifierTarget")!.GetMethod("M")!;
        var typeParameters = changedMethod.GetGenericArguments();
        var tagType = changedMethod.GetParameters()[1].ParameterType;
        if (!changedMethod.IsGenericMethodDefinition || !tagType.IsGenericParameter)
        { throw new InvalidOperationException("The unrelated tag was not generalized."); }
        var typeArguments = Enumerable.Repeat(typeof(int), typeParameters.Length).ToArray();
        typeArguments[tagType.GenericParameterPosition] = typeof(Uri);
        var arguments = new object[] { 3, new Uri("https://example.invalid/dynamic-ref") };
        if (!Equals(changedMethod.MakeGenericMethod(typeArguments).Invoke(Activator.CreateInstance(after.GetType("RefModifierTarget")!)!, arguments), 4)
            || !Equals(arguments[0], 4))
        { throw new InvalidOperationException("Generalizing the unrelated tag changed ref behavior."); }
        Program.Log.Info("PASS DynamicReceiverRefModifierAndIndependentTag");
        return 1;
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
