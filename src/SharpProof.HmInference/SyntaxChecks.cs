using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class SyntaxChecks
{
    public static async Task<int> Run()
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("Deconstruct", "static int M((int,bool) pair) { var (x,y)=pair; return x; }", "Suggestion", "T1 M<T1, T2>((T1, T2) pair)"),
            ("DeconstructDiscard", "static int M((int,bool) pair) { var (x,_)=pair; return x; }", "Suggestion", "T1 M<T1, T2>((T1, T2) pair)"),
            ("DeconstructNested", "static int M((int,(bool,string)) pair) { var (x,(y,z))=pair; return x; }", "Suggestion", "T1 M<T1, T2, T3>((T1, (T2, T3)) pair)"),
            ("DeconstructExplicit", "static int M((int,bool) pair) { (int x,bool y)=pair; return x; }", "Concrete", null),
            ("DeconstructVarElements", "static int M((int,bool) pair) { (var x,var y)=pair; return x; }", "Suggestion", "T1 M<T1, T2>((T1, T2) pair)"),
            ("DeconstructTypedUse", "static int M((int,bool) pair) { var (x,y)=pair; return x+1; }", "Suggestion", "int M<T1>((int, T1) pair)"),
            ("DeconstructBranchScope", "static int M(bool b,(int,bool) pair) { if(b) { var (x,y)=pair; return x; } else { var (x,y)=pair; return x; } }", "Suggestion", "T1 M<T1, T2>(bool b, (T1, T2) pair)"),
            ("MixedDeconstruction", "static int M((int,bool) pair,int x) { (x,var y)=pair; return x; }", "Skipped", null),
            ("CustomDeconstruct", "static int M(System.Collections.Generic.KeyValuePair<int,bool> pair) { var (x,y)=pair; return x; }", "Skipped", null),
            ("NestedCustomDeconstruct", "static int M((int,System.Collections.Generic.KeyValuePair<int,bool>) pair) { var (x,(y,z))=pair; return y; }", "Skipped", null),
            ("Construct", "static System.Text.StringBuilder M(int size) => new System.Text.StringBuilder(size);", "Concrete", null),
            ("ConstructWithUnused", "static System.Text.StringBuilder M(int size,bool ignored) => new System.Text.StringBuilder(size);", "Suggestion", "global::System.Text.StringBuilder M<T1>(int size, T1 ignored)"),
            ("ConstructTargetTyped", "static System.Text.StringBuilder M(int size,bool ignored) => new(size);", "Suggestion", "global::System.Text.StringBuilder M<T1>(int size, T1 ignored)"),
            ("ConstructNamed", "static System.Text.StringBuilder M(int size,bool ignored) => new System.Text.StringBuilder(capacity:size);", "Suggestion", "global::System.Text.StringBuilder M<T1>(int size, T1 ignored)"),
            ("ConstructGeneric", "static System.Collections.Generic.List<int> M(int size,bool ignored) => new System.Collections.Generic.List<int>(size);", "Suggestion", "global::System.Collections.Generic.List<int> M<T1>(int size, T1 ignored)"),
            ("ConstructParams", "static Box M(int x,bool ignored) => new Box(x); class Box { public Box(params int[] values) {} }", "Suggestion", "global::Cases.Box M<T1>(int x, T1 ignored)"),
            ("ConstructOptional", "static Box M(bool ignored) => new Box(); class Box { public Box(int size=1) {} }", "Suggestion", "global::Cases.Box M<T1>(T1 ignored)"),
            ("ConstructInitializer", "static System.Collections.Generic.List<int> M(int x) => new System.Collections.Generic.List<int> { x };", "Skipped", null),
            ("Interpolate", "static string M(int x) => $\"value={x}\";", "Concrete", null),
            ("InterpolateUnused", "static string M(int x,bool ignored) => $\"value={x}\";", "Suggestion", "string M<T1>(int x, T1 ignored)"),
            ("InterpolateFormat", "static string M(int x,bool ignored) => $\"{x,8:D4}\";", "Suggestion", "string M<T1>(int x, T1 ignored)"),
            ("InterpolateRaw", "static string M(int x,bool ignored) => $\"\"\"value={x}\"\"\";", "Suggestion", "string M<T1>(int x, T1 ignored)"),
            ("FormattableSkipped", "static System.FormattableString M(int x) => $\"{x}\";", "Skipped", null),
            ("NameofParameter", "static string M(int x) => nameof(x);", "Suggestion", "string M<T1>(T1 x)"),
            ("NameofMember", "static string M(string x) => nameof(x.Length);", "Rejected", null),
            ("EscapedIdentifier", "static int @M(int @class) => @class;", "Suggestion", "T1 @M<T1>(T1 @class)")
        ];
        var failures = new List<string>();
        var passed = 0;
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("class Cases { " + test.Source + " }")]);
            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0) { failures.Add($"Invalid test {test.Name}: {errors[0]}"); continue; }
            var result = Program.Results(compilation).Single(r => r.Method == "M");
            if (result.Status != test.Status || result.Signature != test.Signature)
            {
                failures.Add($"{test.Name}: expected {test.Status}: {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}");
                continue;
            }
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()]).GetAnalyzerDiagnosticsAsync();
            if (diagnostics.Any(d => d.Id == "AD0001") || diagnostics.Count(d => d.Id == "HM0001") != Program.Results(compilation).Count(r => r.Status == "Suggestion"))
                failures.Add($"Analyzer diagnostics disagree in {test.Name}.");
            else { passed++; Console.WriteLine($"PASS {test.Name}"); }
        }
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
        foreach (var test in tests.Where(t => t.Name is "Deconstruct" or "ConstructTargetTyped" or "InterpolateUnused" or "NameofParameter"))
        {
            var original = Program.Compile([CSharpSyntaxTree.ParseText("class Cases { " + test.Source + " }")]);
            var signature = Program.Results(original).Single(r => r.Method == "M").Signature!;
            var tree = original.SyntaxTrees.Single();
            var root = tree.GetRoot();
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "M");
            var body = method.Body?.ToString() ?? method.ExpressionBody + ";";
            var replacement = SyntaxFactory.ParseMemberDeclaration("static " + signature + " " + body)!;
            var changed = original.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(root.ReplaceNode(method, replacement), tree.Options));
            using var stream = new MemoryStream();
            var emitted = changed.Emit(stream);
            if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
            var assembly = System.Reflection.Assembly.Load(stream.ToArray());
            var reflected = assembly.GetType("Cases")!.GetMethod("M", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var types = test.Name == "Deconstruct" ? new[] { typeof(string), typeof(bool) } : [typeof(string)];
            object?[] arguments = test.Name == "Deconstruct" ? [("hello", true)]
                : test.Name == "NameofParameter" ? ["hello"] : [7, "hello"];
            var actual = reflected.MakeGenericMethod(types).Invoke(null, arguments);
            var valid = test.Name == "ConstructTargetTyped" ? actual is System.Text.StringBuilder { Capacity: 7 }
                : Equals(actual, test.Name == "Deconstruct" ? "hello" : test.Name == "NameofParameter" ? "x" : "value=7");
            if (!valid) throw new InvalidOperationException($"Runtime result changed for {test.Name}.");
            passed++;
            Console.WriteLine($"PASS {test.Name}Runtime");
        }
        return passed;
    }
}
