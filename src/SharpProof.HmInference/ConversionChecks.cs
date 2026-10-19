using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class ConversionChecks
{
    private static readonly string[] ElementValues = ["element"];

    private const string Imports = "#nullable enable\nusing System;using System.Collections;using System.Collections.Generic;using System.Threading.Tasks;\n";
    private const string Support = """
        public static class ConversionTrace { public static string Value=""; }
        public class ConversionBase { public virtual int Tag=>1; }
        public class ConversionDerived:ConversionBase { public override int Tag=>2; }
        public class GenericBase<T> { public T Value=default!; }
        public class GenericDerived<T>:GenericBase<T> {}
        public interface IValue<out T> { T Read(); }
        public class Dual<T>:IValue<int>,IValue<string> { int IValue<int>.Read()=>1;string IValue<string>.Read()=>"dual"; }
        public class IntSequence:IEnumerable<int> { public IEnumerator<int> GetEnumerator()=>((IEnumerable<int>)new[]{1}).GetEnumerator();IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator(); }
        public readonly struct ConversionInput(int value) {
            public static implicit operator ConversionTarget(ConversionInput input) { ConversionTrace.Value+="convert;";return new(input.value); }
            private readonly int value=value;
        }
        public readonly struct ConversionTarget(int value) { public int Value=>value; }
        public static class ConversionApi {
            public static ValueType BoxValueType(ValueType value)=>value;
            public static Enum BoxEnum(Enum value)=>value;
            public static object ObjectList(IList<object> values)=>values[0];
            public static byte Small(byte value)=>value;
            public static long Widen(long value)=>value;
            public static long? WidenNullable(long? value)=>value;
            public static int ReadBase(ConversionBase value)=>value.Tag;
            public static T ReadGeneric<T>(GenericBase<T> value)=>value.Value;
            public static object Box(object value) { ConversionTrace.Value+="box:"+value.GetType().Name+";";return value; }
            public static object? BoxNullable(object? value)=>value;
            public static T Pick<T>(T first,T second)=>first;
            public static T First<T>(IEnumerable<T> values) { using var e=values.GetEnumerator();if(!e.MoveNext())throw new InvalidOperationException();return e.Current; }
            public static T Fallback<T>(IEnumerable<T> values,T fallback)=>fallback;
            public static bool Any<T>(IEnumerable<T> values)=>true;
            public static object Invoke(Func<object> factory)=>factory();
            public static object ObjectAt(object[] values)=>values[0];
            public static object ObjectFirst(IEnumerable<object> values)=>First(values);
            public static int Compare(IComparable value)=>value.CompareTo(value);
            public static void Run<T>(Action<T> action,T value)=>action(value);
            public static T Mark<T>(T value,string tag) { ConversionTrace.Value+=tag+";";return value; }
            public static void Pack(params object[] values) { ConversionTrace.Value+="pack:";foreach(var value in values)ConversionTrace.Value+=value.GetType().Name+","; }
            public static void Pair((long number,object label) value) { ConversionTrace.Value+="pair:"+value.number+":"+value.label.GetType().Name+";"; }
            public static void Longs(params long[] values) {}
            public static int ReadTarget(ConversionTarget value) { ConversionTrace.Value+="take;";return value.Value; }
            public static object ReadDual(IValue<object> value)=>value.Read();
        }
        """;

    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("NullableResourceFlowState", "static int M(IEnumerable<int> values,bool ignored) { using var e=values.GetEnumerator();return e.MoveNext()?e.Current:default; }", "Suggestion", "int M<T1>(global::System.Collections.Generic.IEnumerable<int> values, T1 ignored)"),
            ("ValueTypeBoxingConstraint", "static ValueType M(int value)=>ConversionApi.BoxValueType(value);", "Suggestion", "global::System.ValueType M<T1>(T1 value) where T1 : struct"),
            ("EnumBoxingConstraint", "static Enum M(DayOfWeek value)=>ConversionApi.BoxEnum(value);", "Suggestion", "global::System.Enum M<T1>(T1 value) where T1 : global::System.Enum"),
            ("ArrayInvariantInterfaceConversion", "static object M(string[] values)=>ConversionApi.ObjectList(values);", "Suggestion", "object M<T1>(T1[] values) where T1 : class"),
            ("ConstantArgumentConversion", "static byte M(bool ignored)=>ConversionApi.Small(1);", "Suggestion", "byte M<T1>(T1 ignored)"),
            ("DerivedArgumentConstant", "static int M(bool ignored)=>ConversionApi.ReadBase(new ConversionDerived());", "Suggestion", "int M<T1>(T1 ignored)"),
            ("DerivedArgumentConstraint", "static int M(ConversionDerived value)=>ConversionApi.ReadBase(value);", "Suggestion", "int M<T1>(T1 value) where T1 : global::ConversionBase"),
            ("NumericArgument", "static long M(int value,bool ignored)=>ConversionApi.Widen(value);", "Suggestion", "long M<T1>(int value, T1 ignored)"),
            ("AsyncNullableBoxingArgument", "static async Task<object?> M(Task<int?> value)=>ConversionApi.BoxNullable(await value);", "Suggestion", "global::System.Threading.Tasks.Task<object?> M<T1>(global::System.Threading.Tasks.Task<T1?> value) where T1 : struct"),
            ("AsyncNullableNumericArgument", "static async Task<long?> M(Task<int?> value,bool ignored)=>ConversionApi.WidenNullable(await value);", "Suggestion", "global::System.Threading.Tasks.Task<long?> M<T1>(global::System.Threading.Tasks.Task<int?> value, T1 ignored)"),
            ("NullableNumericArgument", "static long? M(int? value,bool ignored)=>ConversionApi.WidenNullable(value);", "Suggestion", "long? M<T1>(int? value, T1 ignored)"),
            ("BoxingArgument", "static object M(int value)=>ConversionApi.Box(value);", "Suggestion", "object M<T1>(T1 value) where T1 : notnull"),
            ("BoxingDependentArgument", "static object M(int value,object other)=>ConversionApi.Pick(value,other);", "Suggestion", "T2 M<T1, T2>(T1 value, T2 other) where T1 : T2"),
            ("BoxingWithPinnedSource", "static object M(int value,object other,bool ignored) { var result=ConversionApi.Pick(value,other);ConversionApi.Widen(value);return result; }", "Suggestion", "object M<T1>(int value, object other, T1 ignored)"),
            ("ArrayInterfaceArgument", "static int M(int[] values)=>ConversionApi.First(values);", "Suggestion", "T1 M<T1>(T1[] values)"),
            ("ListInterfaceArgument", "static int M(List<int> values)=>ConversionApi.First(values);", "Suggestion", "T1 M<T1>(global::System.Collections.Generic.List<T1> values)"),
            ("GenericBaseArgument", "static int M(GenericDerived<int> value)=>ConversionApi.ReadGeneric(value);", "Suggestion", "T1 M<T1>(global::GenericDerived<T1> value)"),
            ("CollectionInterfaceConstraint", "static int M(IntSequence values)=>ConversionApi.First(values);", "Suggestion", "int M<T1>(T1 values) where T1 : global::System.Collections.Generic.IEnumerable<int>"),
            ("CollectionElementConstraint", "static int M(IntSequence values,int fallback)=>ConversionApi.Fallback(values,fallback);", "Suggestion", "T2 M<T1, T2>(T1 values, T2 fallback) where T1 : global::System.Collections.Generic.IEnumerable<T2>"),
            ("ConstraintOnlyElement", "static bool M(IntSequence values)=>ConversionApi.Any(values);", "Suggestion", "bool M<T1>(T1 values) where T1 : global::System.Collections.Generic.IEnumerable<int>"),
            ("DelegateCovarianceArgument", "static object M(Func<string> factory)=>ConversionApi.Invoke(factory);", "Suggestion", "object M<T1>(global::System.Func<T1> factory) where T1 : class"),
            ("ArrayCovarianceArgument", "static object M(string[] values)=>ConversionApi.ObjectAt(values);", "Suggestion", "object M<T1>(T1[] values) where T1 : class"),
            ("InterfaceCovarianceArgument", "static object M(IEnumerable<string> values)=>ConversionApi.ObjectFirst(values);", "Suggestion", "object M<T1>(global::System.Collections.Generic.IEnumerable<T1> values) where T1 : class"),
            ("ContravariantGenericArgument", "static void M(Action<object> action,string value)=>ConversionApi.Run(action,value);", "Suggestion", "void M<T1, T2>(global::System.Action<T1> action, T2 value) where T2 : T1"),
            ("ParamsNumericArguments", "static void M(int value,bool ignored)=>ConversionApi.Longs(value);", "Suggestion", "void M<T1>(int value, T1 ignored)"),
            ("ParamsBoxingArguments", "static void M(int value,string label)=>ConversionApi.Pack(value,label);", "Suggestion", "void M<T1, T2>(T1 value, T2 label) where T1 : notnull where T2 : notnull"),
            ("NullableBoxingArgument", "static object? M(int? value)=>ConversionApi.BoxNullable(value);", "Suggestion", "object? M<T1>(T1? value) where T1 : struct"),
            ("TupleArgumentConversion", "static void M((int number,string label) value)=>ConversionApi.Pair(value);", "Suggestion", "void M<T1>((int number, T1 label) value) where T1 : notnull"),
            ("TupleLiteralArgumentConversion", "static void M(int value,string label)=>ConversionApi.Pair((value,label));", "Suggestion", "void M<T1>(int value, T1 label) where T1 : notnull"),
            ("BoxingInterfaceConstraint", "static int M(int value)=>ConversionApi.Compare(value);", "Suggestion", "int M<T1>(T1 value) where T1 : global::System.IComparable"),
            ("UserArgumentConversion", "static int M(ConversionInput value,bool ignored)=>ConversionApi.ReadTarget(value);", "Suggestion", "int M<T1>(global::ConversionInput value, T1 ignored)"),
            ("ConstructedJoinTarget", "static IEnumerable<int> M(List<int> first,IEnumerable<int> second)=>ConversionApi.Pick(first,second);", "Suggestion", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(global::System.Collections.Generic.List<T1> first, global::System.Collections.Generic.IEnumerable<T1> second)"),
            ("RepeatedInterfaceDefinitions", "static object M(Dual<int> value,bool ignored)=>ConversionApi.ReadDual(value);", "Suggestion", "object M<T1, T2>(global::Dual<T1> value, T2 ignored)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var tree = CSharpSyntaxTree.ParseText(Imports + Support + "\nclass Cases {" + test.Source + "}", cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != test.Status || result.Signature != test.Signature)
            { failures.Add($"{test.Name}: expected {test.Status}: {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}"); continue; }
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Conversion analyzer crashed: " + test.Name); }
            Program.Log.Info("PASS " + test.Name);
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return tests.Length + await Runtime(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> Runtime(CancellationToken cancellationToken)
    {
        var source = Imports + Support + """
            public static class ConversionProbe {
                public static (int,bool) Monomorphic(int value,bool flag) { Func<object,object> id=x=>x;return ((int)id(value),(bool)id(flag)); }
                public static object Box(int value)=>ConversionApi.Box(value);
                public static long Widen(int value,bool ignored)=>ConversionApi.Widen(value);
                public static int Base(ConversionDerived value)=>ConversionApi.ReadBase(value);
                public static int First(int[] values)=>ConversionApi.First(values);
                public static object Variant(Func<string> factory)=>ConversionApi.Invoke(factory);
                public static void Pack(int value,string label)=>ConversionApi.Pack(ConversionApi.Mark(value,"left"),ConversionApi.Mark(label,"right"));
                public static void Pair(int value,string label)=>ConversionApi.Pair((ConversionApi.Mark(value,"left"),ConversionApi.Mark(label,"right")));
                public static int User(ConversionInput value,bool ignored)=>ConversionApi.ReadTarget(value);
                public static object? Nullable(int? value)=>ConversionApi.BoxNullable(value);
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ConversionRuntime", "ConversionRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("ConversionRuntime.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var before = Load(original, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 15; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && root.FindNode(d.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "ConversionProbe" }; });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 10)
        { throw new InvalidOperationException("Expected ten conversion fixes; got " + fixes); }
        var changed = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var after = Load(changed, cancellationToken);
        foreach (var name in new[] { "Box", "Widen", "Base", "First", "Variant", "Pack", "Pair", "User", "Nullable", "Monomorphic" })
        {
            var expected = Execute(before, name, false);
            var actual = Execute(after, name, true);
            if (expected != actual)
            { throw new InvalidOperationException(name + " conversion behavior changed: " + expected + " -> " + actual); }
        }
        var probe = after.GetType("ConversionProbe")!;
        var guid = Guid.NewGuid();
        if (!Equals(probe.GetMethod("Box")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { guid }), guid)
            || !Equals(probe.GetMethod("First")!.MakeGenericMethod(typeof(string)).Invoke(null, new object[] { ElementValues }), "element")
            || !Equals(probe.GetMethod("Nullable")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object?[] { guid }), guid)
            || probe.GetMethod("Nullable")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object?[] { null }) is not null)
        { throw new InvalidOperationException("Generalized conversion domains lost values."); }
        var sentinel = new Uri("https://example.invalid/conversion");
        Func<Uri> factory = () => sentinel;
        if (!ReferenceEquals(probe.GetMethod("Variant")!.MakeGenericMethod(typeof(Uri)).Invoke(null, new object[] { factory }), sentinel))
        { throw new InvalidOperationException("Covariant delegate conversion lost reference identity."); }
        Program.Log.Info("PASS ConversionCodeFixRuntime");
        Program.Log.Info("PASS ConversionOriginalAndFixedEffects");
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

    private static string Execute(System.Reflection.Assembly assembly, string name, bool generic)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(name);
        var trace = assembly.GetType("ConversionTrace")!.GetField("Value")!;
        trace.SetValue(null, "");
        var method = assembly.GetType("ConversionProbe")!.GetMethod(name)!;
        object?[] arguments;
        Type[] types;
        switch (name)
        {
            case "Monomorphic":
                arguments = [42, true];
                types = [typeof(int), typeof(bool)];
                break;
            case "Base":
                var derived = assembly.GetType("ConversionDerived")!;
                arguments = [Activator.CreateInstance(derived)!];
                types = [derived];
                break;
            case "First":
                arguments = [new[] { 42 }];
                types = [typeof(int)];
                break;
            case "Variant":
                Func<string> factory = () => "factory";
                arguments = [factory];
                types = [typeof(string)];
                break;
            case "Pack":
                arguments = [42, "label"];
                types = [typeof(int), typeof(string)];
                break;
            case "Pair":
                arguments = [42, "label"];
                types = [typeof(string)];
                break;
            case "User":
                arguments = [Activator.CreateInstance(assembly.GetType("ConversionInput")!, 42)!, true];
                types = [typeof(bool)];
                break;
            case "Widen":
                arguments = [42, true];
                types = [typeof(bool)];
                break;
            default:
                arguments = [42];
                types = [typeof(int)];
                break;
        }
        if (generic)
        { method = method.MakeGenericMethod(types); }
        var value = method.Invoke(null, arguments);
        return value + ":" + trace.GetValue(null);
    }
}
