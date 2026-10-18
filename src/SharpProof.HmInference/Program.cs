using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class Program
{
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToArray();

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--self-test"]) return await SelfTest();
        if (args is ["--hm-samples"]) { CoreChecks.Run(samplesOnly: true); return 0; }
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("HM inference prototype: <source.cs> [...] | --samples | --hm-samples | --self-test\nAnalyzes supplied files together as a library. Suggestions change APIs; review manually.");
            return 0;
        }
        var files = args is ["--samples"]
            ? Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Samples"), "*.cs.txt").Order().ToArray() : args;
        foreach (var file in files)
            if (!File.Exists(file)) { Console.Error.WriteLine($"File not found: {file}"); return 2; }
        var compilation = Compile(files.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file)));
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
        {
            foreach (var error in errors.Take(10)) Console.Error.WriteLine(error);
            Console.Error.WriteLine($"{errors.Length} compiler error(s); inference skipped.");
            return 1;
        }
        var results = Results(compilation);
        foreach (var result in results)
            Console.WriteLine($"{result.Method}: {result.Status}: {result.Signature ?? result.Message}");
        var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        foreach (var diagnostic in diagnostics.OrderBy(d => d.Location.SourceTree?.FilePath, StringComparer.Ordinal).ThenBy(d => d.Location.SourceSpan.Start)) Console.WriteLine(diagnostic);
        Console.WriteLine($"Methods: {results.Length}; suggestions: {results.Count(r => r.Status == "Suggestion")}; concrete: {results.Count(r => r.Status == "Concrete")}; rejected: {results.Count(r => r.Status == "Rejected")}; skipped: {results.Count(r => r.Status == "Skipped")}");
        return diagnostics.Any(d => d.Id == "AD0001") ? 1 : 0;
    }

    internal static CSharpCompilation Compile(IEnumerable<SyntaxTree> trees) => CSharpCompilation.Create(
        "HmInput", trees, References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    internal static MethodResult[] Results(CSharpCompilation compilation) => compilation.SyntaxTrees
        .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Select(method => Inference.Analyze(method, compilation.GetSemanticModel(tree)))).ToArray();

    private static async Task<int> SelfTest()
    {
        (string Name, string Source, string Status, string? Signature)[] cases =
        [
            ("Identity", "static int M(int x) => x;", "Suggestion", "T1 M<T1>(T1 x)"),
            ("Constant", "static int M(bool unused) => 42;", "Suggestion", "int M<T1>(T1 unused)"),
            ("Concrete", "static int M(int x) => x + 1;", "Concrete", null),
            ("Choose", "static int M(bool b, int x, int y) => b ? x : y;", "Suggestion", "T1 M<T1>(bool b, T1 x, T1 y)"),
            ("Tuple", "static (int, bool) M(int x, bool y) => (x, y);", "Suggestion", "(T1, T2) M<T1, T2>(T1 x, T2 y)"),
            ("Apply", "static int M(System.Func<int,int> f, int x) => f(x);", "Suggestion", "T2 M<T1, T2>(global::System.Func<T1, T2> f, T1 x)"),
            ("Compose", "static int M(System.Func<int,int> f, System.Func<int,int> g, int x) => f(g(x));", "Suggestion", "T2 M<T1, T2, T3>(global::System.Func<T1, T2> f, global::System.Func<T3, T1> g, T3 x)"),
            ("Lambda", "static System.Func<int,int> M(int x) => y => x;", "Suggestion", "global::System.Func<int, T1> M<T1>(T1 x)"),
            ("Closure", "static System.Func<int,int> M(int x) => y => x + y;", "Concrete", null),
            ("Local", "static int M(int x) { var y = x; return y; }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("TypedLocal", "static int M(int x) { int y = x; return y; }", "Rejected", null),
            ("MonomorphicLocal", "static (int,bool) M(int x, bool b) { System.Func<object,object> id = y => y; return ((int)id(x), (bool)id(b)); }", "Concrete", null),
            ("SharedLocalType", "static int M(int x) { System.Func<int,int> id = y => y; var value = id(1); return id(x); }", "Concrete", null),
            ("Member", "static int M(string x) => x.Length;", "Concrete", null),
            ("Call", "static int M(int x) => System.Math.Abs(x);", "Concrete", null),
            ("Mutation", "static int M(int x) { x++; return x; }", "Skipped", null),
            ("Ref", "static int M(ref int x) => x;", "Skipped", null),
            ("Instance", "int M(int x) => x;", "Skipped", null),
            ("AlreadyGeneric", "static T M<T>(T x) => x;", "Skipped", null),
            ("StringAddition", "static string M(string x) => x + x;", "Concrete", null),
            ("OverloadCallsite", "static int M(int x) => x; static int Use() => M(1);", "Suggestion", "T1 M<T1>(T1 x)"),
            ("MethodGroup", "static int M(int x, int y) => x; static System.Func<int,int,int> Use() => M;", "Suggestion", "T1 M<T1, T2>(T1 x, T2 y)"),
            ("NullCallsite", "static object M(object x) => x; static object Use() => M(null);", "Rejected", null),
            ("ArrayIdentity", "static int[] M(int[] xs) => xs;", "Suggestion", "T1[] M<T1>(T1[] xs)"),
            ("ArrayIndex", "static int M(int[] xs, int i) => xs[i];", "Suggestion", "T1 M<T1>(T1[] xs, int i)"),
            ("ArrayLength", "static int M(int[] xs) => xs.Length;", "Suggestion", "int M<T1>(T1[] xs)"),
            ("ArrayRank", "static int M(int[,] xs) => xs.Rank;", "Suggestion", "int M<T1>(T1[,] xs)"),
            ("MatrixIndex", "static int M(int[,] xs, int i, int j) => xs[i,j];", "Suggestion", "T1 M<T1>(T1[,] xs, int i, int j)"),
            ("JaggedArray", "static int M(int[][] xs, int i, int j) => xs[i][j];", "Suggestion", "T1 M<T1>(T1[][] xs, int i, int j)"),
            ("TupleFirst", "static int M((int,bool) pair) => pair.Item1;", "Suggestion", "T1 M<T1, T2>((T1, T2) pair)"),
            ("NamedTuple", "static int M((int value,bool flag) pair) => pair.value;", "Suggestion", "T1 M<T1, T2>((T1 value, T2 flag) pair)"),
            ("GenericContainer", "static System.Collections.Generic.List<int> M(System.Collections.Generic.List<int> xs) => xs;", "Suggestion", "global::System.Collections.Generic.List<T1> M<T1>(global::System.Collections.Generic.List<T1> xs)"),
            ("GenericProperty", "static int M(System.Collections.Generic.List<int> xs) => xs.Count;", "Concrete", null),
            ("NominalIdentity", "static System.DateTime M(System.DateTime value) => value;", "Suggestion", "T1 M<T1>(T1 value)"),
            ("IfElse", "static int M(bool b,int x,int y) { if (b) return x; else return y; }", "Suggestion", "T1 M<T1>(bool b, T1 x, T1 y)"),
            ("GuardReturn", "static int M(bool b,int x,int y) { if (b) return x; return y; }", "Suggestion", "T1 M<T1>(bool b, T1 x, T1 y)"),
            ("ScopedBranches", "static int M(bool b,int x,int y) { if (b) { var z=x; return z; } else { var z=y; return z; } }", "Suggestion", "T1 M<T1>(bool b, T1 x, T1 y)"),
            ("RecursiveChoose", "static int M(bool b,int x) => b ? x : M(true,x);", "Suggestion", "T1 M<T1>(bool b, T1 x)"),
            ("GenericCall", "static int M(int x) => Id(x); static T Id<T>(T x) => x;", "Suggestion", "T1 M<T1>(T1 x)"),
            ("GenericCallsFresh", "static (int,string) M(int x,string y) => (Id(x),Id(y)); static T Id<T>(T x) => x;", "Suggestion", "(T1, T2) M<T1, T2>(T1 x, T2 y)"),
            ("GenericConstraint", "static object M(object x) => Id(x); static T Id<T>(T x) where T:class => x;", "Rejected", null),
            ("ExplicitTypeArgument", "static int M(int x) => Id<int>(x); static T Id<T>(T x) => x;", "Rejected", null),
            ("NumericPromotion", "static long M(int x,long y) => x+y;", "Concrete", null),
            ("Comparison", "static bool M(double x,double y) => x < y;", "Concrete", null),
            ("BooleanLogic", "static bool M(bool x,bool y) => x && !y;", "Concrete", null),
            ("Bitwise", "static int M(int x,int y) => x << y;", "Concrete", null),
            ("Decimal", "static decimal M(decimal x) => x + 1.5m;", "Concrete", null),
            ("Negate", "static int M(int x) => -x;", "Concrete", null),
            ("Checked", "static int M(int x) => checked(x+1);", "Concrete", null),
            ("TypedLambda", "static int M(int x) { System.Func<int,int> f = (int y) => y; return f(x); }", "Concrete", null),
            ("Switch", "static int M(int tag,int x,int y) => tag switch { 0=>x, _=>y };", "Suggestion", "T1 M<T1>(int tag, T1 x, T1 y)"),
            ("Cast", "static long M(int x) => (long)x;", "Concrete", null),
            ("Default", "static int M(bool ignored) => default(int);", "Suggestion", "int M<T1>(T1 ignored)"),
            ("ParamsCall", "static int M(int x) => Sum(x); static int Sum(params int[] xs) => 0;", "Concrete", null),
            ("Nullable", "static string? M(string? x) => x;", "Skipped", null),
            ("Loop", "static int M(int x) { while (x>0) x--; return x; }", "Skipped", null),
            ("ImplicitArray", "static int[] M(int x,int y) => new[] { x,y };", "Suggestion", "T1[] M<T1>(T1 x, T1 y)"),
            ("ExplicitArray", "static int[] M(int x) => new int[] { x };", "Concrete", null),
            ("AllocateArray", "static int[] M(int n) => new int[n];", "Concrete", null),
            ("GenericLocal", "static int M(int x) { T Id<T>(T value) => value; return Id(x); }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("CapturedLocal", "static int M(int x) { int Read() => x; return Read(); }", "Rejected", null),
            ("TypedArrayLocal", "static int M(int[] xs,int i) { int[] copy=xs; return copy[i]; }", "Rejected", null)
        ];
        var passed = 0;
        var failures = new List<string>();
        foreach (var test in cases)
        {
            var compilation = Compile([CSharpSyntaxTree.ParseText("class Cases { " + test.Source + " }")]);
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0) throw new InvalidOperationException($"Invalid test {test.Name}: {errors[0]}");
            var result = Results(compilation)[0];
            if (result.Status != test.Status || result.Signature != test.Signature)
            {
                failures.Add($"{test.Name}: expected {test.Status}: {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}");
                continue;
            }
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()]).GetAnalyzerDiagnosticsAsync();
            if (diagnostics.Any(d => d.Id == "AD0001") || diagnostics.Count(d => d.Id == "HM0001") != Results(compilation).Count(r => r.Status == "Suggestion"))
                throw new InvalidOperationException($"Analyzer diagnostics disagree for {test.Name}.");
            Console.WriteLine($"PASS {test.Name}");
            passed++;
        }
        var variable = HmType.Variable();
        try { HmType.Unify(variable, HmType.Function([variable], new("int"))); throw new InvalidOperationException("Occurs check missed infinite type."); }
        catch (InferenceException) { passed++; Console.WriteLine("PASS OccursCheck"); }
        try { HmType.Unify(new("int"), new("bool")); throw new InvalidOperationException("Conflicting types accepted."); }
        catch (InferenceException) { passed++; Console.WriteLine("PASS TypeConflict"); }
        var invalid = Compile([CSharpSyntaxTree.ParseText("class C { static int M(int x) => missing; }")]);
        if ((await invalid.WithAnalyzers([new HmInferenceAnalyzer()]).GetAnalyzerDiagnosticsAsync()).Length != 0)
            throw new InvalidOperationException("Analyzer should skip invalid input.");
        passed++;
        Console.WriteLine("PASS InvalidInput");
        // Apply inferred signatures to a separate program, compile it and execute
        // genuinely polymorphic calls (including different delegate input/output types).
        var sample = Compile([CSharpSyntaxTree.ParseText("""
            public static class GenericProbe {
                public static int Identity(int value) => value;
                public static int Apply(System.Func<int,int> function, int value) => function(value);
                public static int Choose(bool condition, int yes, int no) => condition ? yes : no;
            }
            """)]);
        var methods = sample.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var generalized = methods.Select(method =>
        {
            var inferred = Inference.Analyze(method, sample.GetSemanticModel(method.SyntaxTree));
            if (inferred.Signature is null) throw new InvalidOperationException("Expected a generic signature.");
            return $"public static {inferred.Signature} {method.ExpressionBody};";
        });
        var source = "public static class GenericProbe { " + string.Join("\n", generalized) + """
            public static bool Run() => Identity(42) == 42 && Identity("hello") == "hello"
                && Apply((string s) => s.Length, "hello") == 5
                && Apply((bool b) => b ? "yes" : "no", true) == "yes"
                && Choose(false, "left", "right") == "right";
            }
            """;
        using var image = new MemoryStream();
        var emitted = Compile([CSharpSyntaxTree.ParseText(source)]).Emit(image);
        if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
        var assembly = System.Reflection.Assembly.Load(image.ToArray());
        if (assembly.GetType("GenericProbe")!.GetMethod("Run")!.Invoke(null, null) is not true)
            throw new InvalidOperationException("Generalized methods failed runtime checks.");
        passed++;
        Console.WriteLine("PASS PolymorphicRuntime");
        passed += await CallChecks.Run();
        passed += await SyntaxChecks.Run();
        passed += CoreChecks.Run();
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
        Console.WriteLine($"{passed} checks passed.");
        return 0;
    }
}
