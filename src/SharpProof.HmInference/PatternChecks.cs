using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class PatternChecks
{
    private static readonly string[] PatternStrings = ["first", "second"];
    private const string Support = """
        #nullable enable
        using System;
        public static class PatternTrace { public static string Value=""; }
        public class PatternBox<T> {
            public T Item=default!;
            public T Value { get { PatternTrace.Value+="get;";return Item; } }
        }
        public class PatternDerived<T>:PatternBox<T> {}
        public class PatternHolder<T> { public PatternBox<T> Box=new(); }
        public class PatternPoint<T> {
            public T Item=default!;
            public void Deconstruct(out T value,out bool flag) { PatternTrace.Value+="deconstruct;";value=Item;flag=true; }
        }
        public class PatternExt<T> { public T Item=default!; }
        public static class PatternExtensions {
            public static void Deconstruct<T>(this PatternExt<T> point,out T value,out bool flag) { value=point.Item;flag=true; }
        }
        public class PatternList<T> {
            public T[] Values=[];
            public int Count {get { PatternTrace.Value+="count;";return Values.Length; }}
            public T this[int index] {get { PatternTrace.Value+="index;";return Values[index]; }}
            public PatternList<T> Slice(int start,int length) { PatternTrace.Value+="slice;";return new(){Values=Values[start..(start+length)]}; }
        }
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("NullableReferenceRecursivePattern","static string M(string? value,string fallback)=>value is {} captured?captured:fallback;","Suggestion","T1 M<T1>(T1? value, T1 fallback) where T1 : class"),
            ("ExplicitRecursiveTypePattern","static int M(PatternBox<int> value,int fallback)=>value is PatternBox<int>{Value:var item}?item:fallback;","Suggestion","int M<T1>(global::PatternBox<T1> value, int fallback)"),
            ("NullableRecursivePattern","static int M(int? value,int fallback)=>value is {} captured?captured:fallback;","Suggestion","T1 M<T1>(T1? value, T1 fallback) where T1 : struct"),
            ("VarPatternIdentity","static int M(int value)=>value is var captured?captured:value;","Suggestion","T1 M<T1>(T1 value)"),
            ("DeclarationPattern","static int M(object value)=>value is string text?text.Length:0;","Suggestion","int M<T1>(T1 value)"),
            ("ParenthesizedTypePattern","static bool M(object value)=>value is (int);","Suggestion","bool M<T1>(T1 value)"),
            ("InheritedPropertyPattern","static int M(PatternDerived<int> box,int fallback)=>box is {Value:var item}?item:fallback;","Suggestion","T1 M<T1>(global::PatternDerived<T1> box, T1 fallback)"),
            ("FieldPattern","static int M(PatternBox<int> box,int fallback)=>box is {Item:var item}?item:fallback;","Suggestion","T1 M<T1>(global::PatternBox<T1> box, T1 fallback)"),
            ("PropertyPattern","static int M(PatternBox<int> box,int fallback)=>box is {Value:var item}?item:fallback;","Suggestion","T1 M<T1>(global::PatternBox<T1> box, T1 fallback)"),
            ("ExtendedPropertyPattern","static int M(PatternHolder<int> box,int fallback)=>box is {Box.Value:var item}?item:fallback;","Suggestion","T1 M<T1>(global::PatternHolder<T1> box, T1 fallback)"),
            ("TypedPropertyPattern","static int M(object box,int fallback)=>box is PatternBox<int>{Value:var item}?item:fallback;","Suggestion","int M<T1>(T1 box, int fallback)"),
            ("RecursivePatternDesignation","static PatternBox<int> M(PatternBox<int> box,PatternBox<int> fallback)=>box is {} captured?captured:fallback;","Suggestion","global::PatternBox<T1> M<T1>(global::PatternBox<T1> box, global::PatternBox<T1> fallback)"),
            ("TuplePositionalPattern","static int M((int,bool) pair,int fallback)=>pair is (var item,var flag)?item:fallback;","Suggestion","T1 M<T1, T2>((T1, T2) pair, T1 fallback)"),
            ("CustomPositionalPattern","static int M(PatternPoint<int> point,int fallback)=>point is (var item,var flag)?item:fallback;","Suggestion","T1 M<T1>(global::PatternPoint<T1> point, T1 fallback)"),
            ("ExtensionPositionalPattern","static int M(PatternExt<int> point,int fallback)=>point is (var item,var flag)?item:fallback;","Suggestion","T1 M<T1>(global::PatternExt<T1> point, T1 fallback)"),
            ("RuntimeTuplePattern","static int M(object value,bool ignored)=>value is (int first,int second)?first:0;","Suggestion","int M<T1>(object value, T1 ignored)"),
            ("EmptyArrayListPattern","static bool M(int[] values)=>values is [];","Suggestion","bool M<T1>(T1[] values)"),
            ("EmptyCustomListPattern","static bool M(PatternList<int> values)=>values is [];","Suggestion","bool M<T1>(global::PatternList<T1> values)"),
            ("WholeListCapture","static int[] M(int[] values,int[] fallback)=>values is [..] captured?captured:fallback;","Suggestion","T1[] M<T1>(T1[] values, T1[] fallback)"),
            ("NestedListPattern","static int M(int[][] values,int fallback)=>values is [[var item,..],..]?item:fallback;","Suggestion","T1 M<T1>(T1[][] values, T1 fallback)"),
            ("ArrayListPattern","static int M(int[] values,int fallback)=>values is [var first,..]?first:fallback;","Suggestion","T1 M<T1>(T1[] values, T1 fallback)"),
            ("ArraySlicePattern","static int[] M(int[] values,int[] fallback)=>values is [_,..var rest]?rest:fallback;","Suggestion","T1[] M<T1>(T1[] values, T1[] fallback)"),
            ("CustomListPattern","static int M(PatternList<int> values,int fallback)=>values is [var first,..]?first:fallback;","Suggestion","T1 M<T1>(global::PatternList<T1> values, T1 fallback)"),
            ("CustomSlicePattern","static PatternList<int> M(PatternList<int> values,PatternList<int> fallback)=>values is [_,..var rest]?rest:fallback;","Suggestion","global::PatternList<T1> M<T1>(global::PatternList<T1> values, global::PatternList<T1> fallback)"),
            ("StringListPattern","static char M(string text,char fallback,bool ignored)=>text is [var first,..]?first:fallback;","Suggestion","char M<T1>(string text, char fallback, T1 ignored)"),
            ("RelationalPattern","static bool M(int value,bool ignored)=>value is >0 and <10;","Suggestion","bool M<T1>(int value, T1 ignored)"),
            ("LogicalOrPattern","static bool M(object value)=>value is (string or int);","Suggestion","bool M<T1>(T1 value)"),
            ("NegatedDeclarationPattern","static int M(object value) { if(value is not string text)return 0;return text.Length; }","Suggestion","int M<T1>(T1 value)"),
            ("PatternSwitchExpression","static int M(object value)=>value switch {string text=>text.Length,int number=>number,_=>-1};","Suggestion","int M<T1>(T1 value)"),
            ("PatternSwitchStatement","static int M(object value) { switch(value) {case string text when text.Length>0:return text.Length;case int number:return number;default:return -1;} }","Suggestion","int M<T1>(T1 value)"),
            ("ListSwitchExpression","static int M(int[] values,int fallback)=>values switch {[var item,..]=>item,_=>fallback};","Suggestion","T1 M<T1>(T1[] values, T1 fallback)"),
            ("ListSwitchStatement","static int M(int[] values,int fallback) { switch(values) {case [var item,..]:return item;default:return fallback;} }","Suggestion","T1 M<T1>(T1[] values, T1 fallback)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var tree = CSharpSyntaxTree.ParseText(Support + "\nclass Cases {" + test.Source + "}", cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != test.Status || result.Signature != test.Signature)
            { failures.Add(test.Name + ": expected " + test.Status + ": " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS " + test.Name); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        var protocolTree = CSharpSyntaxTree.ParseText("public class PatternProtocolBox<T>{} public static class PatternProtocol { public static void Deconstruct(this PatternProtocolBox<int> point,out int value,out bool flag){value=1;flag=true;} }", cancellationToken: cancellationToken);
        var consumerTree = CSharpSyntaxTree.ParseText("class Reader { static int Read(PatternProtocolBox<int> point)=>point is (var value,var flag)?value:0; }", cancellationToken: cancellationToken);
        var protocol = Program.Results(Program.Compile([protocolTree, consumerTree]), cancellationToken).Single(result =>
        { ArgumentNullException.ThrowIfNull(result); return result.Method == "Deconstruct"; });
        if (protocol.Status != "Suggestion" || protocol.Signature != "void Deconstruct<T1>(this global::PatternProtocolBox<T1> point, out int value, out bool flag)")
        { throw new InvalidOperationException("ImplicitPatternMethodGeneralization: " + protocol.Status + ": " + (protocol.Signature ?? protocol.Message)); }
        Program.Log.Info("PASS ImplicitPatternMethodGeneralization");
        return tests.Length + 1 + await Runtime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        var source = Support + """
            public static class PatternProbe {
                public static int Var(int value)=>value is var item?item:value;
                public static int Property(PatternBox<int> box,int fallback)=>box is {Value:var item}?item:fallback;
                public static int Position(PatternPoint<int> point,int fallback)=>point is (var item,var flag)?item:fallback;
                public static int List(int[] values,int fallback)=>values is [var first,..]?first:fallback;
                public static int[] Slice(int[] values,int[] fallback)=>values is [_,..var rest]?rest:fallback;
                public static int CustomList(PatternList<int> values,int fallback)=>values is [var first,..]?first:fallback;
                public static PatternList<int> CustomSlice(PatternList<int> values,PatternList<int> fallback)=>values is [_,..var rest]?rest:fallback;
                public static int Switch(object value)=>value switch {string text=>text.Length,int number=>number,_=>-1};
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "PatternRuntime", "PatternRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("PatternRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && root.FindNode(d.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "PatternProbe" }; });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 8)
        { throw new InvalidOperationException("Expected eight pattern fixes; got " + fixes); }
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        foreach (var name in new[] { "Var", "Property", "Position", "List", "Slice", "CustomList", "CustomSlice", "Switch" })
        {
            foreach (var empty in new[] { false, true })
            {
                var expected = Execute(before, name, false, empty);
                var actual = Execute(after, name, true, empty);
                if (expected != actual)
                { throw new InvalidOperationException(name + " pattern effects changed: " + expected + " -> " + actual); }
            }
        }
        var probe = after.GetType("PatternProbe")!;
        var sentinel = new Uri("https://example.invalid/pattern");
        if (!ReferenceEquals(probe.GetMethod("Var")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [sentinel]), sentinel)
            || !Equals(probe.GetMethod("List")!.MakeGenericMethod(typeof(string)).Invoke(null, [PatternStrings, "fallback"]), "first"))
        { throw new InvalidOperationException("Generalized pattern domains lost values."); }
        Program.Log.Info("PASS PatternCodeFixRuntime");
        Program.Log.Info("PASS PatternOriginalAndFixedProtocolEffects");
        return 2;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var problems = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error; }).ToArray();
        if (problems.Length != 0)
        { throw new InvalidOperationException(string.Join("\n", problems.Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }

    private static string Execute(System.Reflection.Assembly assembly, string name, bool generic, bool empty)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(name);
        var trace = assembly.GetType("PatternTrace")!.GetField("Value")!;
        trace.SetValue(null, "");
        var method = assembly.GetType("PatternProbe")!.GetMethod(name)!;
        if (generic)
        { method = method.MakeGenericMethod(typeof(int)); }
        object?[] arguments;
        var values = empty ? Array.Empty<int>() : new[] { 42, 7 };
        if (name is "Var" or "Switch")
        { arguments = [42]; }
        else if (name is "List" or "Slice")
        { arguments = [values, name == "List" ? (object)-1 : Array.Empty<int>()]; }
        else
        {
            var owner = name == "Property" ? "PatternBox`1" : name == "Position" ? "PatternPoint`1" : "PatternList`1";
            var type = assembly.GetType(owner)!.MakeGenericType(typeof(int));
            var instance = Activator.CreateInstance(type)!;
            type.GetField(owner == "PatternList`1" ? "Values" : "Item")!.SetValue(instance, owner == "PatternList`1" ? (object)values : 42);
            arguments = [empty && name is "Property" or "Position" ? null : instance, name == "CustomSlice" ? Activator.CreateInstance(type) : (object)-1];
        }
        var result = method.Invoke(null, arguments);
        if (result is int[] array)
        { result = string.Join(",", array); }
        else if (name == "CustomSlice" && result is not null)
        { result = string.Join(",", (int[])result.GetType().GetField("Values")!.GetValue(result)!); }
        return result + ":" + trace.GetValue(null);
    }
}
