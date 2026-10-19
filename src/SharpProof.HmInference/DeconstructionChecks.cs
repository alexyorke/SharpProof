using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class DeconstructionChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("PairInstance", "static int M(System.Collections.Generic.KeyValuePair<int,bool> pair) {var(x,y)=pair;return x;}", "Suggestion", "T1 M<T1, T2>(global::System.Collections.Generic.KeyValuePair<T1, T2> pair)"),
            ("PairNested", "static string M(System.Collections.Generic.KeyValuePair<int,System.Collections.Generic.KeyValuePair<string,bool>> pair) {var(x,(y,z))=pair;return y;}", "Suggestion", "T2 M<T1, T2, T3>(global::System.Collections.Generic.KeyValuePair<T1, global::System.Collections.Generic.KeyValuePair<T2, T3>> pair)"),
            ("PairDiscard", "static bool M(System.Collections.Generic.KeyValuePair<int,bool> pair) {var(_,y)=pair;return y;}", "Suggestion", "T2 M<T1, T2>(global::System.Collections.Generic.KeyValuePair<T1, T2> pair)"),
            ("PairTypedConversion", "static long M(System.Collections.Generic.KeyValuePair<int,bool> pair) {(long x,var y)=pair;return x;}", "Suggestion", "long M<T1>(global::System.Collections.Generic.KeyValuePair<int, T1> pair)"),
            ("PairReassignment", "static int M(System.Collections.Generic.KeyValuePair<int,bool> pair,int x,bool y) {(x,y)=pair;return x;}", "Suggestion", "T1 M<T1, T2>(global::System.Collections.Generic.KeyValuePair<T1, T2> pair, T1 x, T2 y)"),
            ("PairMixedBinding", "static int M(System.Collections.Generic.KeyValuePair<int,bool> pair,int x) {(x,var y)=pair;return x;}", "Suggestion", "T1 M<T1, T2>(global::System.Collections.Generic.KeyValuePair<T1, T2> pair, T1 x)"),
            ("AssignmentResult", "static (int,bool) M(System.Collections.Generic.KeyValuePair<int,bool> pair,int x,bool y)=>(x,y)=pair;", "Suggestion", "(T1, T2) M<T1, T2>(global::System.Collections.Generic.KeyValuePair<T1, T2> pair, T1 x, T2 y)"),
            ("AssignmentResultConversion", "static (long,bool) M(System.Collections.Generic.KeyValuePair<int,bool> pair,long x,bool y)=>(x,y)=pair;", "Suggestion", "(long, T1) M<T1>(global::System.Collections.Generic.KeyValuePair<int, T1> pair, long x, T1 y)"),
            ("NativeAssignmentConversion", "static (long,bool) M((int,bool) pair,long x,bool y)=>(x,y)=pair;", "Suggestion", "(long, T1) M<T1>((int, T1) pair, long x, T1 y)"),
            ("InstanceCustom", "static int M(Pair<int> pair) {var(x,y)=pair;return x;} } class Pair<T> {public T Value=default!;public void Deconstruct(out T value,out bool flag){value=Value;flag=true;} } class Tail {", "Suggestion", "T1 M<T1>(global::Pair<T1> pair)"),
            ("InheritedInstance", "static int[] M(Derived<int> pair) {var(x,y)=pair;return x;} } class Pair<T> {public void Deconstruct(out T value,out bool flag){value=default!;flag=true;} } class Derived<T>:Pair<T[]> {} class Tail {", "Suggestion", "T1[] M<T1>(global::Derived<T1> pair)"),
            ("NonGenericInstance", "static int M(Pair pair,bool ignored) {var(x,y)=pair;return x;} } class Pair {public void Deconstruct(out int value,out bool flag){value=1;flag=true;} } class Tail {", "Suggestion", "int M<T1>(global::Pair pair, T1 ignored)"),
            ("PositionalRecord", "static int M(Pair<int> pair) {var(x,y)=pair;return x;} } record Pair<T>(T Value,bool Flag); class Tail {", "Suggestion", "T1 M<T1>(global::Pair<T1> pair)"),
            ("ExtensionCustom", "static int M(Box<int> pair) {var(x,y)=pair;return x;} } class Box<T> {public T Value=default!;} static class Extensions {public static void Deconstruct<T>(this Box<T> pair,out T value,out bool flag){value=pair.Value;flag=true;} } class Tail {", "Suggestion", "T1 M<T1>(global::Box<T1> pair)"),
            ("UniversalExtension", "static int M(int pair) {var(x,y)=pair;return x;} } static class Extensions {public static void Deconstruct<T>(this T pair,out T value,out bool flag){value=pair;flag=true;} } class Tail {", "Suggestion", "T1 M<T1>(T1 pair)"),
            ("ArrayExtension", "static int M(int[] pair) {var(x,y)=pair;return x;} } static class Extensions {public static void Deconstruct<T>(this T[] pair,out T value,out bool flag){value=pair[0];flag=true;} } class Tail {", "Suggestion", "T1 M<T1>(T1[] pair)"),
            ("ExtensionConstraint", "static string M(Box<string> pair) {var(x,y)=pair;return x;} } class Box<T> {public T Value=default!;} static class Extensions {public static void Deconstruct<T>(this Box<T> pair,out T value,out bool flag) where T:notnull {value=pair.Value;flag=true;} } class Tail {", "Suggestion", "T1 M<T1>(global::Box<T1> pair) where T1 : notnull"),
            ("BaseExtension", "static int[] M(Derived<int> pair) {var(x,y)=pair;return x;} } class Box<T> {public T Value=default!;} class Derived<T>:Box<T[]> {} static class Extensions {public static void Deconstruct<T>(this Box<T> pair,out T value,out bool flag){value=pair.Value;flag=true;} } class Tail {", "Suggestion", "T1[] M<T1>(global::Derived<T1> pair)"),
            ("InterfaceExtension", "static int M(Pair<int> pair) {var(x,y)=pair;return x;} } interface IPair<T>{T Value{get;}} struct Pair<T>:IPair<T>{public T Value=>default!;} static class Extensions {public static void Deconstruct<T>(this IPair<T> pair,out T value,out bool flag){value=pair.Value;flag=true;} } class Tail {", "Suggestion", "T1 M<T1>(global::Pair<T1> pair)"),
            ("ObjectExtensionBoxing", "static int M(int pair,bool ignored) {var(x,y)=pair;return x;} } static class Extensions {public static void Deconstruct(this object pair,out int value,out bool flag){value=1;flag=true;} } class Tail {", "Suggestion", "int M<T1>(int pair, T1 ignored)"),
            ("NullableValueExtension", "static int M(int? pair) {var(x,y)=pair;return x;} } static class Extensions {public static void Deconstruct<T>(this T? pair,out T value,out bool flag) where T:struct {value=pair.GetValueOrDefault();flag=pair.HasValue;} } class Tail {", "Suggestion", "T1 M<T1>(T1? pair) where T1 : struct"),
            ("NullableOut", "static string? M(Pair<string> pair) {var(x,y)=pair;return x;} } class Pair<T> where T:class {public void Deconstruct(out T? value,out bool flag){value=null;flag=true;} } class Tail {", "Suggestion", "T1? M<T1>(global::Pair<T1> pair) where T1 : class"),
            ("ArrayForeachCustom", "static int M(System.Collections.Generic.KeyValuePair<int,bool>[] pairs,int seed) {foreach(var(x,y) in pairs)seed=x;return seed;}", "Suggestion", "T1 M<T1, T2>(global::System.Collections.Generic.KeyValuePair<T1, T2>[] pairs, T1 seed)"),
            ("EnumerableForeachCustom", "static int M(System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<int,bool>> pairs,int seed) {foreach(var(x,y) in pairs)seed=x;return seed;}", "Suggestion", "T1 M<T1, T2>(global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<T1, T2>> pairs, T1 seed)"),
            ("AsyncForeachCustom", "static async Task<int> M(System.Collections.Generic.IAsyncEnumerable<System.Collections.Generic.KeyValuePair<int,bool>> pairs,int seed) {await foreach(var(x,y) in pairs)seed=x;return seed;}", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1, T2>(global::System.Collections.Generic.IAsyncEnumerable<global::System.Collections.Generic.KeyValuePair<T1, T2>> pairs, T1 seed)"),
            ("PropertyTarget", "static int M(System.Collections.Generic.KeyValuePair<int,bool> pair,Box box) {(box.Value,_)=pair;return box.Value;} } class Box {public int Value{get;set;}} class Tail {", "Suggestion", "int M<T1>(global::System.Collections.Generic.KeyValuePair<int, T1> pair, global::Box box)"),
            ("IndexerConversionTarget", "static long M((int,bool) pair,long[] target,int index) {(target[index],_)=pair;return target[index];}", "Suggestion", "long M<T1>((int, T1) pair, long[] target, int index)"),
            ("ExistingNullableTypeParameter", "static T? M<T>(T? value,int ignored)=>value;", "Suggestion", "T? M<T, T1>(T? value, T1 ignored)"),
            ("ExistingNullableDelegate", "static System.Func<T?> M<T>(T? value,int ignored)=>()=>value;", "Suggestion", "global::System.Func<T?> M<T, T1>(T? value, T1 ignored)"),
            ("ExistingNullableField", "static T? M<T>(Box<T> box,int ignored)=>box.Value; } class Box<T>{public T Value=default!;} class Tail {", "Suggestion", "T? M<T, T1>(global::Box<T> box, T1 ignored)"),
            ("ConstraintDeconstructShadowing", "static int M(Pair pair) {var(value,flag)=pair;return value;} } interface IDeconstruct {void Deconstruct(out int value,out bool flag);} class Pair:IDeconstruct {void IDeconstruct.Deconstruct(out int value,out bool flag){value=2;flag=true;}} static class Extensions {public static void Deconstruct<T>(this T pair,out int value,out bool flag) where T:IDeconstruct {value=1;flag=true;} } class Tail {", "Rejected", null),
            ("NewWarningStillRejected", "static int M(int x)=>Id(x); [return:System.Diagnostics.CodeAnalysis.MaybeNull] static T Id<T>(T x)=>x;", "Rejected", null),
            ("NewWarningSeparateSiteRejected", "static int M(int x) {string? note=null;string Local()=>note;return Id(x);} [return:System.Diagnostics.CodeAnalysis.MaybeNull] static T Id<T>(T x)=>x;", "Rejected", null),
            ("NestedTupleResult", "static (int,(int,bool)) M((int,System.Collections.Generic.KeyValuePair<int,bool>) pair,int x,int y,bool z)=>(x,(y,z))=pair;", "Suggestion", "(T1, (T2, T3)) M<T1, T2, T3>((T1, global::System.Collections.Generic.KeyValuePair<T2, T3>) pair, T1 x, T2 y, T3 z)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("#nullable enable\nusing System.Threading.Tasks; class Cases { " + test.Source + " }", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException($"Invalid fixture {test.Name}: {errors[0]}"); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != test.Status || result.Signature != test.Signature)
            {
                failures.Add($"{test.Name}: expected {test.Status}: {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}");
                continue;
            }
            if (test.Name == "ConstraintDeconstructShadowing" && !result.Message.Contains("deconstruction binding", StringComparison.Ordinal))
            { throw new InvalidOperationException("Constraint shadowing must be rejected by binding validation: " + result.Message); }
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Analyzer failure: " + test.Name); }
            Program.Log.Info("PASS " + test.Name);
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        var crossFile = Program.Compile([
            CSharpSyntaxTree.ParseText("public class Box<T> {} public static class Ext { public static void Deconstruct(this Box<int> pair,out int value,out bool flag){value=1;flag=true;} }", path:"Api.cs", cancellationToken:cancellationToken),
            CSharpSyntaxTree.ParseText("class Use { static int Read(Box<int> pair) {var(value,_)=pair;return value;} }", path:"Use.cs", cancellationToken:cancellationToken)
        ]);
        var extension = Program.Results(crossFile, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "Deconstruct"; });
        if (extension.Signature != "void Deconstruct<T1>(this global::Box<T1> pair, out int value, out bool flag)")
        { throw new InvalidOperationException("Cross-file implicit deconstruction signature was not preserved: " + (extension.Signature ?? extension.Message)); }
        Program.Log.Info("PASS CrossFileDeconstructRootBinding");
        return tests.Length + 1;
    }
}
