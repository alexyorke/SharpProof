using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class StorageChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =

        [
            ("StaticFieldStore", "static int field; static int M(int x,bool ignored) { field=x; return x; }", "int M<T1>(int x, T1 ignored)"),
            ("InstanceFieldStore", "int field; int M(int x,bool ignored) { field=x; return x; }", "int M<T1>(int x, T1 ignored)"),
            ("ExplicitFieldStore", "static int M(Box box,int x,bool ignored) { box.Value=x; return x; } class Box { public int Value; }", "int M<T1>(global::Cases.Box box, int x, T1 ignored)"),
            ("TupleMemberStore", "static (int,bool) M((int,bool) pair,int x) { pair.Item1=x; return pair; }", "(T1, T2) M<T1, T2>((T1, T2) pair, T1 x)"),
            ("NamedTupleMemberStore", "static (int value,bool flag) M((int value,bool flag) pair,int x) { pair.value=x; return pair; }", "(T1 value, T2 flag) M<T1, T2>((T1 value, T2 flag) pair, T1 x)"),
            ("PropertyStore", "static int M(Box box,int x,bool ignored) { box.Value=x; return x; } class Box { public int Value {get;set;} }", "int M<T1>(global::Cases.Box box, int x, T1 ignored)"),
            ("CompoundPropertyStore", "static int M(Box box,int x,bool ignored) { box.Value+=x; return box.Value; } class Box { public int Value {get;set;} }", "int M<T1>(global::Cases.Box box, int x, T1 ignored)"),
            ("IncrementPropertyStore", "static int M(Box box,bool ignored) => ++box.Value; class Box { public int Value {get;set;} }", "int M<T1>(global::Cases.Box box, T1 ignored)"),
            ("IndexerRead", "static int M(System.Collections.Generic.List<int> xs,int i,bool ignored) => xs[i];", "T1 M<T1, T2>(global::System.Collections.Generic.List<T1> xs, int i, T2 ignored)"),
            ("IndexerStore", "static int M(System.Collections.Generic.List<int> xs,int i,int x,bool ignored) { xs[i]=x; return xs[i]; }", "T1 M<T1, T2>(global::System.Collections.Generic.List<T1> xs, int i, T1 x, T2 ignored)"),
            ("IndexerCompoundStore", "static int M(System.Collections.Generic.List<int> xs,int i,int x,bool ignored) { xs[i]+=x; return xs[i]; }", "int M<T1>(global::System.Collections.Generic.List<int> xs, int i, int x, T1 ignored)"),
            ("IndexerIncrement", "static int M(System.Collections.Generic.List<int> xs,int i,bool ignored) => xs[i]++;", "int M<T1>(global::System.Collections.Generic.List<int> xs, int i, T1 ignored)"),
            ("ObjectInitializer", "static Box M(int x,bool ignored) => new Box { Value=x }; class Box { public int Value; }", "global::Cases.Box M<T1>(int x, T1 ignored)"),
            ("TargetTypedInitializer", "static Box M(int x,bool ignored) => new() { Value=x }; class Box { public int Value; }", "global::Cases.Box M<T1>(int x, T1 ignored)"),
            ("NestedObjectInitializer", "static Box M(int x,bool ignored) => new Box { Child = { Value=x } }; class Box { public Box Child {get;} = null; public int Value; }", "global::Cases.Box M<T1>(int x, T1 ignored)"),
            ("CollectionInitializer", "static System.Collections.Generic.List<int> M(int x,bool ignored) => new System.Collections.Generic.List<int> { x };", "global::System.Collections.Generic.List<int> M<T1>(int x, T1 ignored)"),
            ("DictionaryInitializer", "static System.Collections.Generic.Dictionary<int,string> M(int key,string value,bool ignored) => new System.Collections.Generic.Dictionary<int,string> { {key,value} };", "global::System.Collections.Generic.Dictionary<int, string> M<T1>(int key, string value, T1 ignored)"),
            ("IndexInitializer", "static System.Collections.Generic.Dictionary<int,string> M(int key,string value,bool ignored) => new System.Collections.Generic.Dictionary<int,string> { [key]=value };", "global::System.Collections.Generic.Dictionary<int, string> M<T1>(int key, string value, T1 ignored)"),
            ("NestedCollectionInitializer", "static Box M(int x,bool ignored) => new Box { Values = { x } }; class Box { public System.Collections.Generic.List<int> Values {get;} = new(); }", "global::Cases.Box M<T1>(int x, T1 ignored)"),
            ("BoxingInitializer", "static Box M(int x,bool ignored) => new Box { Value=x }; class Box { public object Value; }", "global::Cases.Box M<T1>(int x, T1 ignored)"),
            ("WideningInitializer", "static Box M(int x,bool ignored) => new Box { Value=x }; class Box { public long Value; }", "global::Cases.Box M<T1>(int x, T1 ignored)"),
            ("RequiredMemberInitializer", "static Box M(int x,bool ignored) => new Box { Value=x }; class Box { public required int Value {get;init;} }", "global::Cases.Box M<T1>(int x, T1 ignored)"),
            ("OverloadedCollectionAdd", "static Bag M(int x,bool ignored) => new Bag {x}; class Bag : System.Collections.IEnumerable { public void Add(int x) {} public void Add(object x) {} public System.Collections.IEnumerator GetEnumerator() => throw new System.NotImplementedException(); }", "global::Cases.Bag M<T1>(int x, T1 ignored)"),
            ("OverloadedIndexer", "static int M(Box box,int index,int x,bool ignored) { box[index]=x; return box[index]; } class Box { public int this[int index] { get=>0; set {} } public int this[object index] {get=>1; set {}} }", "int M<T1>(global::Cases.Box box, int index, int x, T1 ignored)"),
            ("BoxingCollectionInitializer", "static System.Collections.Generic.List<object> M(int x,bool ignored) => new System.Collections.Generic.List<object> {x};", "global::System.Collections.Generic.List<object> M<T1>(int x, T1 ignored)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("class Cases { " + test.Source + " }", cancellationToken: cancellationToken)]);
            if (compilation.GetDiagnostics(cancellationToken).Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }))
            {
                throw new InvalidOperationException("Invalid storage test: " + test.Name);
            }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            {
                failures.Add($"{test.Name}: expected {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}");
                continue;
            }
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Analyzer failed for " + test.Name); }
            var targetRoot = await compilation.SyntaxTrees.Single().GetRootAsync(cancellationToken).ConfigureAwait(false);
            var targetMethod = targetRoot.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(method => { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == "M"; });
            if (diagnostics.Count(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001" && d.Location.SourceSpan == targetMethod.Identifier.Span; }) != 1
                || diagnostics.Length != Program.Results(compilation, cancellationToken).Count(r => { ArgumentNullException.ThrowIfNull(r); return r.Status == "Suggestion"; }))
            { throw new InvalidOperationException("Storage diagnostics disagree for " + test.Name); }
            Program.Log.Info("PASS " + test.Name);
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }

        var source = """
            public static class StorageProbe {
                public static (int,bool) TupleStore((int,bool) pair,int x) { pair.Item1=x; return pair; }
                public static int Store(int x,bool ignored) { var box=new Box { Value=x }; box.Value+=2; var list=new System.Collections.Generic.List<int> {box.Value}; list[0]++; return list[0]; }
                public sealed class Box { public int Value {get;set;} }
            }
            """;
        var original = Program.Compile([CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)]);
        var tree = original.SyntaxTrees.Single();
        var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var updated = root.ReplaceNodes(methods, (before, _) =>
        {
            ArgumentNullException.ThrowIfNull(before);
            var signature = Inference.Analyze(before, original.GetSemanticModel(tree), cancellationToken).Signature ?? throw new InvalidOperationException("Missing storage runtime signature.");
            return SyntaxFactory.ParseMemberDeclaration("public static " + signature + " " + before.Body)!;
        });
        var candidate = original.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(updated, tree.Options));
        using var image = new MemoryStream();
        var emitted = candidate.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        var probe = System.Reflection.Assembly.Load(image.ToArray()).GetType("StorageProbe")!;
        var tuple = probe.GetMethod("TupleStore")!.MakeGenericMethod(typeof(string), typeof(bool)).Invoke(null, new object[] { ("before", true), "after" });
        if (tuple is not ValueTuple<string, bool> { Item1: "after", Item2: true } || !Equals(probe.GetMethod("Store")!.MakeGenericMethod(typeof(string)).Invoke(null, new object[] { 39, "unused" }), 42))
        {
            throw new InvalidOperationException("Generalized storage runtime probe failed.");
        }
        Program.Log.Info("PASS StorageRuntime");
        return tests.Length + 1;
    }
}
