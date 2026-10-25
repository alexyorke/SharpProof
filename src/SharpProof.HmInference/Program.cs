using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class Program
{
    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path =>
    {
        global::System.ArgumentNullException.ThrowIfNull(path);
        return MetadataReference.CreateFromFile(path);
    }).ToArray();
    public static async Task<int> Main(string[] args)
    {
        global::System.ArgumentNullException.ThrowIfNull(args);
        using var cancellation = new CancellationTokenSource();
        void Cancel(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            global::System.ArgumentNullException.ThrowIfNull(eventArgs);
            eventArgs.Cancel = true;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += Cancel;
        try
        {
            return await (RunAsync(args, cancellation.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }
    }

    internal static readonly SharpProof.Logging.RepositoryLogger Log = new(Console.Out, Console.Error);
    private static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args is ["--self-test"])
        {
            return await (SelfTest(cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        if (args is ["--recursive-group-checks"])
        {
            RecursiveGroupChecks.Run(cancellationToken);
            await RecursiveGroupCodeFixChecks.Run(cancellationToken).ConfigureAwait(false);
            return 0;
        }

        if (args is ["--hm-samples"])
        {
            CoreChecks.Run(samplesOnly: true, cancellationToken: cancellationToken);
            return 0;
        }

        if (args.Length == 0 || args is ["--help"])
        {
            Program.Log.Info("HM inference: [--unsafe] <source.cs> [...] | --project <project.csproj> | --solution <solution.sln[x]> | --samples | --hm-samples | --recursive-group-checks | --self-test\nProject options: --framework <tfm> --configuration <name> --property <Name=Value> --no-restore --no-build\nDependencies are restored and declared tool projects are built by default. Loose files are analyzed together as a library. Suggestions change APIs; review manually.");
            return 0;
        }

        if (args[0] is "--project" or "--solution" || ProjectLoading.IsProjectInput(args[0]))
        { return await ProjectLoading.Run(args, cancellationToken).ConfigureAwait(false); }

        var unsafeOptions = args.Count(argument => { ArgumentNullException.ThrowIfNull(argument); return argument == "--unsafe"; });
        if (unsafeOptions > 1)
        { Program.Log.Error("Duplicate --unsafe option."); return 2; }
        var sourceArguments = unsafeOptions == 0 ? args : args.Where(argument => { ArgumentNullException.ThrowIfNull(argument); return argument != "--unsafe"; }).ToArray();
        if (sourceArguments.Length == 0)
        { Program.Log.Error("Expected source files after --unsafe."); return 2; }
        var files = sourceArguments is ["--samples"] ? Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Samples"), "*.cs.txt").Order().ToArray() : sourceArguments;
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                Program.Log.Error($"File not found: {file}");
                return 2;
            }
        }

        var compilation = Compile(files.Select(file =>
        {
            global::System.ArgumentNullException.ThrowIfNull(file);
            return CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file, cancellationToken: cancellationToken);
        }));
        if (unsafeOptions != 0)
        { compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true)); }
        return await AnalyzeCompilationAsync(compilation, null, diagnosticsOnly: false, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> AnalyzeCompilationAsync(CSharpCompilation compilation, AnalyzerOptions? analyzerOptions, bool diagnosticsOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (ReportCompilerErrors(compilation, cancellationToken))
        { return 1; }

        var results = diagnosticsOnly ? [] : Results(compilation, cancellationToken: cancellationToken);
        foreach (var result in results)
        {
            Program.Log.Info($"{result.Method}: {result.Status}: {result.Signature ?? result.Message}");
        }
        if (!diagnosticsOnly)
        {
            foreach (var notice in RecursiveGroupNotices(results))
            { Program.Log.Info(notice); }
        }

        var diagnostics = await (compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(analyzerOptions ?? new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false)).GetAnalyzerDiagnosticsAsync(cancellationToken: cancellationToken)).ConfigureAwait(false);
        foreach (var diagnostic in diagnostics.OrderBy(d =>
        {
            global::System.ArgumentNullException.ThrowIfNull(d);
            return d.Location.SourceTree?.FilePath;
        }, StringComparer.Ordinal).ThenBy(d =>
        {
            global::System.ArgumentNullException.ThrowIfNull(d);
            return d.Location.SourceSpan.Start;
        }))
        {
            Program.Log.Info(diagnostic.ToString());
        }
        if (diagnosticsOnly)
        {
            foreach (var notice in RecursiveGroupNotices(diagnostics))
            { Program.Log.Info(notice); }
        }

        if (diagnosticsOnly)
        { Program.Log.Info($"Analyzer suggestions: {diagnostics.Count(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001"; })}"); }
        else
        { Program.Log.Info($"Methods: {results.Length}; suggestions: {results.Count(r => { global::System.ArgumentNullException.ThrowIfNull(r); return r.Status == "Suggestion"; })}; concrete: {results.Count(r => { global::System.ArgumentNullException.ThrowIfNull(r); return r.Status == "Concrete"; })}; rejected: {results.Count(r => { global::System.ArgumentNullException.ThrowIfNull(r); return r.Status == "Rejected"; })}; skipped: {results.Count(r => { global::System.ArgumentNullException.ThrowIfNull(r); return r.Status == "Skipped"; })}"); }
        return diagnostics.Any(d =>
        {
            global::System.ArgumentNullException.ThrowIfNull(d);
            return d.Id == "AD0001";
        }) ? 1 : 0;
    }

    internal static bool ReportCompilerErrors(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var errors = compilation.GetDiagnostics(cancellationToken: cancellationToken).Where(d =>
        {
            global::System.ArgumentNullException.ThrowIfNull(d);
            return Inference.IsCompilationError(d) || d.Id is "CS8032" or "CS8784" or "CS8785";
        }).ToArray();
        if (errors.Length != 0)
        {
            foreach (var error in errors.Take(10))
            {
                Program.Log.Error(error.ToString());
            }

            Program.Log.Error($"{errors.Length} compiler error(s); inference skipped.");
            return true;
        }

        return false;
    }

    internal static CSharpCompilation Compile(IEnumerable<SyntaxTree> trees)
    {
        global::System.ArgumentNullException.ThrowIfNull(trees);
        return CSharpCompilation.Create("HmInput", trees, References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static MethodResult[] Results(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        global::System.ArgumentNullException.ThrowIfNull(compilation);
        return compilation.SyntaxTrees.SelectMany(tree =>
        {
            global::System.ArgumentNullException.ThrowIfNull(tree);
            return tree.GetRoot(cancellationToken: cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Select(method =>
            {
                global::System.ArgumentNullException.ThrowIfNull(method);
                return Inference.Analyze(method, compilation.GetSemanticModel(tree), cancellationToken: cancellationToken);
            });
        }).ToArray();
    }

    internal static string[] RecursiveGroupNotices(IEnumerable<MethodResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var seen = new HashSet<(SyntaxTree Tree, int AnchorStart)>();
        var notices = new List<string>();
        foreach (var result in results)
        {
            ArgumentNullException.ThrowIfNull(result);
            var group = result.RecursiveGroup;
            if (group is null || !seen.Add((group.Tree, group.AnchorStart)))
            { continue; }
            notices.Add(RecursiveGroupNotice(group.Signatures));
        }
        return notices.ToArray();
    }

    internal static string[] RecursiveGroupNotices(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var seen = new HashSet<(SyntaxTree Tree, int AnchorStart)>();
        var notices = new List<string>();
        foreach (var diagnostic in diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            var tree = diagnostic.Location.SourceTree;
            if (diagnostic.Id != "HM0001" || tree is null
                || !diagnostic.Properties.TryGetValue(RecursiveGroupInfo.DiagnosticAnchorProperty, out var anchorText)
                || !int.TryParse(anchorText, global::System.Globalization.NumberStyles.None,
                    global::System.Globalization.CultureInfo.InvariantCulture, out var anchor)
                || !diagnostic.Properties.TryGetValue(RecursiveGroupInfo.DiagnosticCountProperty, out var countText)
                || !int.TryParse(countText, global::System.Globalization.NumberStyles.None,
                    global::System.Globalization.CultureInfo.InvariantCulture, out var count)
                || count < 2 || count > diagnostic.Properties.Count)
            { continue; }
            var signatures = new List<string>();
            for (var i = 0; i < count; i++)
            {
                if (!diagnostic.Properties.TryGetValue(RecursiveGroupInfo.DiagnosticSignaturePrefix + i,
                    out var signature) || signature is null)
                { break; }
                signatures.Add(signature);
            }
            if (signatures.Count == count && seen.Add((tree, anchor)))
            { notices.Add(RecursiveGroupNotice(signatures)); }
        }
        return notices.ToArray();
    }

    private static string RecursiveGroupNotice(IReadOnlyList<string> signatures)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        return $"Recursive group ({signatures.Count} methods): apply all member signatures together: {string.Join("; ", signatures)}";
    }

    private static async Task<int> SelfTest(CancellationToken cancellationToken)
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
            ("TypedLocal", "static int M(int x) { int y = x; return y; }", "Concrete", null),
            ("MonomorphicLocal", "static (int,bool) M(int x, bool b) { System.Func<object,object> id = y => y; return ((int)id(x), (bool)id(b)); }", "Suggestion", "(int, bool) M<T1, T2>(T1 x, T2 b)"),
            ("SharedLocalType", "static int M(int x) { System.Func<int,int> id = y => y; var value = id(1); return id(x); }", "Concrete", null),
            ("Member", "static int M(string x) => x.Length;", "Concrete", null),
            ("Call", "static int M(int x) => System.Math.Abs(x);", "Concrete", null),
            ("Mutation", "static int M(int x) { x++; return x; }", "Concrete", null),
            ("Ref", "static int M(ref int x) => x;", "Suggestion", "T1 M<T1>(ref T1 x)"),
            ("Instance", "int M(int x) => x;", "Suggestion", "T1 M<T1>(T1 x)"),
            ("AlreadyGeneric", "static T M<T>(T x) => x;", "Concrete", null),
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
            ("GenericProperty", "static int M(System.Collections.Generic.List<int> xs) => xs.Count;", "Suggestion", "int M<T1>(global::System.Collections.Generic.List<T1> xs)"),
            ("NominalIdentity", "static System.DateTime M(System.DateTime value) => value;", "Suggestion", "T1 M<T1>(T1 value)"),
            ("IfElse", "static int M(bool b,int x,int y) { if (b) return x; else return y; }", "Suggestion", "T1 M<T1>(bool b, T1 x, T1 y)"),
            ("GuardReturn", "static int M(bool b,int x,int y) { if (b) return x; return y; }", "Suggestion", "T1 M<T1>(bool b, T1 x, T1 y)"),
            ("ScopedBranches", "static int M(bool b,int x,int y) { if (b) { var z=x; return z; } else { var z=y; return z; } }", "Suggestion", "T1 M<T1>(bool b, T1 x, T1 y)"),
            ("RecursiveChoose", "static int M(bool b,int x) => b ? x : M(true,x);", "Suggestion", "T1 M<T1>(bool b, T1 x)"),
            ("GenericCall", "static int M(int x) => Id(x); static T Id<T>(T x) => x;", "Suggestion", "T1 M<T1>(T1 x)"),
            ("GenericCallsFresh", "static (int,string) M(int x,string y) => (Id(x),Id(y)); static T Id<T>(T x) => x;", "Suggestion", "(T1, T2) M<T1, T2>(T1 x, T2 y)"),
            ("GenericConstraint", "static object M(object x) => Id(x); static T Id<T>(T x) where T:class => x;", "Suggestion", "T1 M<T1>(T1 x) where T1 : class"),
            ("ExplicitTypeArgument", "static int M(int x) => Id<int>(x); static T Id<T>(T x) => x;", "Concrete", null),
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
            ("Nullable", "static string? M(string? x) => x;", "Suggestion", "T1? M<T1>(T1? x) where T1 : class"),
            ("Loop", "static int M(int x) { while (x>0) x--; return x; }", "Concrete", null),
            ("ImplicitArray", "static int[] M(int x,int y) => new[] { x,y };", "Suggestion", "T1[] M<T1>(T1 x, T1 y)"),
            ("ExplicitArray", "static int[] M(int x) => new int[] { x };", "Concrete", null),
            ("AllocateArray", "static int[] M(int n) => new int[n];", "Concrete", null),
            ("GenericLocal", "static int M(int x) { T Id<T>(T value) => value; return Id(x); }", "Suggestion", "T1 M<T1>(T1 x)"),
            ("CapturedLocal", "static int M(int x) { int Read() => x; return Read(); }", "Concrete", null),
            ("TypedArrayLocal", "static int M(int[] xs,int i) { int[] copy=xs; return copy[i]; }", "Concrete", null)
        ];
        var passed = 0;
        var failures = new List<string>();
        foreach (var test in cases)
        {
            var compilation = Compile([CSharpSyntaxTree.ParseText("class Cases { " + test.Source + " }", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken: cancellationToken).Where(d =>
            {
                global::System.ArgumentNullException.ThrowIfNull(d);
                return d.Severity == DiagnosticSeverity.Error;
            }).ToArray();
            if (errors.Length != 0)
            {
                throw new InvalidOperationException($"Invalid test {test.Name}: {errors[0]}");
            }

            var result = Results(compilation, cancellationToken: cancellationToken)[0];
            if (result.Status != test.Status || result.Signature != test.Signature)
            {
                failures.Add($"{test.Name}: expected {test.Status}: {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}");
                continue;
            }

            var diagnostics = await (compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false)).GetAnalyzerDiagnosticsAsync(cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (diagnostics.Any(d =>
            {
                global::System.ArgumentNullException.ThrowIfNull(d);
                return d.Id == "AD0001";
            }) || diagnostics.Count(d =>
            {
                global::System.ArgumentNullException.ThrowIfNull(d);
                return d.Id == "HM0001";
            }) != Results(compilation, cancellationToken: cancellationToken).Count(r =>
            {
                global::System.ArgumentNullException.ThrowIfNull(r);
                return r.Status == "Suggestion";
            }))
            {
                throw new InvalidOperationException($"Analyzer diagnostics disagree for {test.Name}.");
            }

            Program.Log.Info($"PASS {test.Name}");
            passed++;
        }

        var variable = HmType.Variable();
        try
        {
            HmType.Unify(variable, HmType.Function([variable], new("int")));
            throw new InvalidOperationException("Occurs check missed infinite type.");
        }
        catch (InferenceException)
        {
            passed++;
            Program.Log.Info("PASS OccursCheck");
        }

        try
        {
            HmType.Unify(new("int"), new("bool"));
            throw new InvalidOperationException("Conflicting types accepted.");
        }
        catch (InferenceException)
        {
            passed++;
            Program.Log.Info("PASS TypeConflict");
        }

        var invalid = Compile([CSharpSyntaxTree.ParseText("class C { static int M(int x) => missing; }", cancellationToken: cancellationToken)]);
        if ((await (invalid.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false)).GetAnalyzerDiagnosticsAsync(cancellationToken: cancellationToken)).ConfigureAwait(false)).Length != 0)
        {
            throw new InvalidOperationException("Analyzer should skip invalid input.");
        }

        passed++;
        Program.Log.Info("PASS InvalidInput");
        // Apply inferred signatures to a separate program, compile it and execute
        // genuinely polymorphic calls (including different delegate input/output types).
        var sample = Compile([CSharpSyntaxTree.ParseText("""
            public static class GenericProbe {
                public static int Identity(int value) => value;
                public static int Apply(System.Func<int,int> function, int value) => function(value);
                public static int Choose(bool condition, int yes, int no) => condition ? yes : no;
            }
            """, cancellationToken: cancellationToken)]);
        var methods = (await (sample.SyntaxTrees.Single().GetRootAsync(cancellationToken: cancellationToken)).ConfigureAwait(false)).DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var generalized = methods.Select(method =>
        {
            var inferred = Inference.Analyze(method, sample.GetSemanticModel(method.SyntaxTree), cancellationToken: cancellationToken);
            if (inferred.Signature is null)
            {
                throw new InvalidOperationException("Expected a generic signature.");
            }

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
        var emitted = Compile([CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)]).Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        {
            throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
        }

        var assembly = System.Reflection.Assembly.Load(image.ToArray());
        if (assembly.GetType("GenericProbe")!.GetMethod("Run")!.Invoke(null, null) is not true)
        {
            throw new InvalidOperationException("Generalized methods failed runtime checks.");
        }

        passed++;
        Program.Log.Info("PASS PolymorphicRuntime");
        passed += await CompilerPolicyChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await TypeShapeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await UnsafeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await NominalIdentityChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await ArrayConstructionChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await PartialMethodChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await PartialContractChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += DefaultValueChecks.Run(cancellationToken);
        passed += await IndirectStorageChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += LambdaReceiverChecks.Run(cancellationToken);
        passed += await LambdaReceiverChecks.RunApplied(cancellationToken).ConfigureAwait(false);
        passed += QueryChecks.Run(cancellationToken);
        passed += await QueryRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await DefaultValueRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += NullJoinChecks.Run(cancellationToken);
        passed += ConvertedCoalescingChecks.Run(cancellationToken);
        passed += ReturnConversionChecks.Run(cancellationToken);
        passed += await ReturnConversionRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await NullJoinRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await CoalescingRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += NullableReturnChecks.Run(cancellationToken);
        passed += await NullableReturnRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += FixedLambdaChecks.Run(cancellationToken);
        passed += await FixedLambdaRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += FixedArgumentChecks.Run(cancellationToken);
        passed += await FixedArgumentRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += StorageConversionChecks.Run(cancellationToken);
        passed += await StorageConversionRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += JoinChecks.Run(cancellationToken);
        passed += await JoinRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await LocalFunctionChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await ReferenceChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await ExpressionTreeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await StructuralValueChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await CollectionExpressionChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await UserOperatorChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await InterpolationChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await ProtocolEnumerationChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await PatternChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await DynamicChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await (CallChecks.Run(cancellationToken: cancellationToken)).ConfigureAwait(false);
        passed += await (SyntaxChecks.Run(cancellationToken: cancellationToken)).ConfigureAwait(false);
        passed += await (FlowChecks.Run(cancellationToken: cancellationToken)).ConfigureAwait(false);
        passed += await (ExitChecks.Run(cancellationToken: cancellationToken)).ConfigureAwait(false);
        passed += await NullableAsyncChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await ResourceChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await BuilderLayoutChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await LegacyBuilderLayoutChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await CustomBuilderChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await ConversionChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await ConstraintFlowChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await ConstraintNormalizationChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await VariantConstraintChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await VariantProjectionChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await CallerRefinementChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await CallerConstraintChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await NullableCallerConstraintChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await NullableMethodGroupChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += UntypedCallerChecks.Run(cancellationToken);
        passed += await UntypedCallerRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await NullableConstraintChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await InterfaceProjectionChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await DependentBoundChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await DependentBoundRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await DelegateAsyncChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await DelegateRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await ParameterEnumeratorChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await EnumerationChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await EnumerationRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await ControlFlowChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await ControlFlowRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await DeconstructionChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await DeconstructionRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += BindingChecks.Run(cancellationToken);
        passed += await StorageChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await (CodeFixChecks.Run(cancellationToken: cancellationToken)).ConfigureAwait(false);
        passed += RecursiveGroupChecks.Run(cancellationToken);
        passed += await RecursiveGroupCodeFixChecks.Run(cancellationToken).ConfigureAwait(false);
        passed += await (DeclarationChecks.Run(cancellationToken: cancellationToken)).ConfigureAwait(false);
        passed += CoreChecks.Run(cancellationToken: cancellationToken);
        if (failures.Count != 0)
        {
            throw new InvalidOperationException(string.Join("\n", failures));
        }

        Program.Log.Info($"{passed} checks passed.");
        return 0;
    }
}
