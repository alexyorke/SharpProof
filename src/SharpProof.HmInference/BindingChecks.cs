using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

internal static class BindingChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Declaration, string Replacement, string Caller, int ExpectedAfter, string ExpectedStatus)[] tests =
        [
            ("CallerOptionalOverloadPreserved", "public static int M(int x) => x;", "public static T M<T>(T x) => x;",
                "public static int M(int x,int optional=0) => x+1; public static int Run() => M(1);", 1, "Suggestion"),
            ("CallerConstructorOverload", "public static object M(bool ignored) => 42;", "public static int M<T>(T ignored) => 42;",
                "public static int Run() => new Box(M(true)).Value; public sealed class Box { public int Value; public Box(object x) {Value=1;} public Box(int x) {Value=2;} }", 2, "Rejected"),
            ("CallerBoxingEquality", "public static object M(bool ignored) => 42;", "public static int M<T>(T ignored) => 42;",
                "public static int Run() => M(true)==M(false) ? 2 : 1;", 2, "Rejected"),
            ("CallerArgumentConversion", "public static int M(Target x) => 1;", "public static int M<T>(T x) => 1;",
                "static int conversions; public sealed class Target {} public sealed class Source { public static implicit operator Target(Source x) {conversions++; return new Target();} } public static int Run() { conversions=0; M(new Source()); return conversions==1 ? 1 : 2; }", 2, "Rejected")
        ];
        foreach (var test in tests)
        {
            // Separate files exercise compilation-wide validation. The raw
            // generalized programs compile; the last three change behavior.
            var declaration = CSharpSyntaxTree.ParseText("public static partial class CallerProbe { " + test.Declaration + " }", path: "Declaration.cs", cancellationToken: cancellationToken);
            var caller = CSharpSyntaxTree.ParseText("public static partial class CallerProbe { " + test.Caller + " }", path: "Caller.cs", cancellationToken: cancellationToken);
            var original = Program.Compile([declaration, caller]);
            var generalized = original.ReplaceSyntaxTree(declaration, CSharpSyntaxTree.ParseText("public static partial class CallerProbe { " + test.Replacement + " }", path: "Declaration.cs", cancellationToken: cancellationToken));
            var before = Execute(original, cancellationToken);
            var after = Execute(generalized, cancellationToken);
            if (before != 1 || after != test.ExpectedAfter)
            {
                throw new InvalidOperationException($"Caller reproduction mismatch for {test.Name}: {before} -> {after}.");
            }
            var method = declaration.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var result = Inference.Analyze(method, original.GetSemanticModel(declaration), cancellationToken);
            if (result.Status != test.ExpectedStatus || result.Status == "Rejected" && result.Message != "Generalization changes a supplied call's instantiated signature.")
            {
                throw new InvalidOperationException($"Caller check failed for {test.Name}: {result.Status}: {result.Message}");
            }
            Program.Log.Info("PASS " + test.Name);
        }
        return tests.Length;
    }

    private static int Execute(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        return (int)System.Reflection.Assembly.Load(image.ToArray()).GetType("CallerProbe")!.GetMethod("Run")!.Invoke(null, null)!;
    }
}
