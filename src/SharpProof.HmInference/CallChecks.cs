using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class CallChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests = [("ReorderedNamed", "static int M(int x,string y) => First(second:y,first:x); static A First<A,B>(A first,B second) => first;", "Suggestion", "T1 M<T1, T2>(T1 x, T2 y)"), ("NamedDelegate", "static int M(System.Func<int,int> f,int x) => f(arg:x);", "Suggestion", "T2 M<T1, T2>(global::System.Func<T1, T2> f, T1 x)"), ("NamedRecursive", "static int M(bool done,int x) => done ? x : M(x:x,done:true);", "Suggestion", "T1 M<T1>(bool done, T1 x)"), ("Optional", "static int M(int x) => Id(x); static T Id<T>(T x,int count=1) => x;", "Suggestion", "T1 M<T1>(T1 x)"), ("OptionalGenericDefault", "static int M(int x) => Id(x); static T Id<T>(T x,T fallback=default) => x;", "Suggestion", "T1 M<T1>(T1 x)"), ("ExplicitOptional", "static int M(int x,int count) => Id(x,count:count); static T Id<T>(T x,int count=1) => x;", "Suggestion", "T1 M<T1>(T1 x, int count)"), ("ParamsExpanded", "static int M(int x,int y) => First(x,y); static T First<T>(params T[] values) => values[0];", "Suggestion", "T1 M<T1>(T1 x, T1 y)"), ("ParamsArray", "static int M(int[] xs) => First(xs); static T First<T>(params T[] values) => values[0];", "Suggestion", "T1 M<T1>(T1[] xs)"), ("ParamsEmpty", "static int M(bool ignored) => Count<int>(); static int Count<T>(params T[] values) => values.Length;", "Suggestion", "int M<T1>(T1 ignored)"), ("ParamsNamedArray", "static int M(int[] xs) => First(values:xs); static T First<T>(params T[] values) => values[0];", "Suggestion", "T1 M<T1>(T1[] xs)"), ("ParamsMixed", "static int M(int x) => First(x,1); static T First<T>(params T[] values) => values[0];", "Concrete", null), ("OptionalAndParams", "static int M(int x,int y) => First(0,x,y); static T First<T>(int count=0,params T[] values) => values[0];", "Suggestion", "T1 M<T1>(T1 x, T1 y)"), ("RefStillSkipped", "static int M(int x) { return Id(ref x); } static int Id(ref int x) => x;", "Skipped", null), ("Extension", "static int M(int x) => x.Identity(); } static class Extensions { public static T Identity<T>(this T value) => value;", "Suggestion", "T1 M<T1>(T1 x)"), ("ExtensionArray", "static int M(int[] xs,int i) => xs.At(i); } static class Extensions { public static T At<T>(this T[] xs,int i) => xs[i];", "Suggestion", "T1 M<T1>(T1[] xs, int i)"), ("ExtensionOptional", "static int M(int x) => x.Identity(); } static class Extensions { public static T Identity<T>(this T value,int unused=0) => value;", "Suggestion", "T1 M<T1>(T1 x)"), ("ExtensionNamed", "static int M(int x,int y) => x.First(other:y); } static class Extensions { public static T First<T>(this T value,T other) => value;", "Suggestion", "T1 M<T1>(T1 x, T1 y)"), ("ExtensionConstraint", "static object M(object x) => x.Identity(); } static class Extensions { public static T Identity<T>(this T value) where T:class => value;", "Rejected", null), ("RefExtension", "static int M(int x) => x.Identity(); } static class Extensions { public static int Identity(this ref int value) => value;", "Skipped", null), ("ParamsCollection", "static int M(int x) => Sum(x); static int Sum(params System.Collections.Generic.List<int> values) => values.Count;", "Skipped", null)];
        var failures = new List<string>();
        var passed = 0;
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("class Cases { " + test.Source + " }", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken: cancellationToken).Where(d =>
            {
                global::System.ArgumentNullException.ThrowIfNull(d);
                return d.Severity == DiagnosticSeverity.Error;
            }).ToArray();
            if (errors.Length != 0)
            {
                failures.Add($"Invalid test {test.Name}: {errors[0]}");
                continue;
            }

            var result = Program.Results(compilation, cancellationToken: cancellationToken).Single(r =>
            {
                global::System.ArgumentNullException.ThrowIfNull(r);
                return r.Method == "M";
            });
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
            }) != Program.Results(compilation, cancellationToken: cancellationToken).Count(r =>
            {
                global::System.ArgumentNullException.ThrowIfNull(r);
                return r.Status == "Suggestion";
            }))
            {
                failures.Add($"Analyzer diagnostics disagree in {test.Name}.");
            }
            else
            {
                passed++;
                Program.Log.Info($"PASS {test.Name}");
            }
        }

        if (failures.Count != 0)
        {
            throw new InvalidOperationException(string.Join("\n", failures));
        }

        // Execute the inferred M signatures at new types, retaining the original
        // source bodies and helper definitions. This also tests argument mapping
        // where parameter order differs from source evaluation order.
        foreach (var test in tests.Where(t => t.Name is "ReorderedNamed" or "Optional" or "OptionalGenericDefault" or "ParamsExpanded" or "Extension"))
        {
            var original = Program.Compile([CSharpSyntaxTree.ParseText("class Cases { " + test.Source + " }", cancellationToken: cancellationToken)]);
            var signature = Program.Results(original, cancellationToken: cancellationToken).Single(r =>
            {
                global::System.ArgumentNullException.ThrowIfNull(r);
                return r.Method == "M";
            }).Signature!;
            var tree = original.SyntaxTrees.Single();
            var root = (await (tree.GetRootAsync(cancellationToken: cancellationToken)).ConfigureAwait(false));
            var method = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single(m =>
            {
                global::System.ArgumentNullException.ThrowIfNull(m);
                return m.Identifier.ValueText == "M";
            });
            var replacement = SyntaxFactory.ParseMemberDeclaration("static " + signature + " " + method.ExpressionBody + ";")!;
            var changed = original.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(root.ReplaceNode(method, replacement), tree.Options));
            using var stream = new MemoryStream();
            var emitted = changed.Emit(stream, cancellationToken: cancellationToken);
            if (!emitted.Success)
            {
                throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
            }

            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            var reflected = assembly.GetType("Cases")!.GetMethod("M", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            object?[] arguments = test.Name == "ReorderedNamed" ? ["left", true] : test.Name == "ParamsExpanded" ? ["left", "right"] : ["left"];
            var typeArguments = reflected.GetGenericArguments().Length == 2 ? new[]
            {
                typeof(string),
                typeof(bool)
            }

            : [typeof(string)];
            if (!Equals(reflected.MakeGenericMethod(typeArguments).Invoke(null, arguments), "left"))
            {
                throw new InvalidOperationException($"Runtime result changed for {test.Name}.");
            }

            passed++;
            Program.Log.Info($"PASS {test.Name}Runtime");
        }

        return passed;
    }
}
