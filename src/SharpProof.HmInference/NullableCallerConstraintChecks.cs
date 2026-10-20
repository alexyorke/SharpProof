using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class NullableCallerConstraintChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string? Signature)[] cases =
        [
            ("Async", "public static async System.Threading.Tasks.Task<object> M(object value,int tag){await System.Threading.Tasks.Task.Yield();Need(value);return value;}static void Need<T>(T value)where T:notnull{}public static System.Threading.Tasks.Task<object> Use(string? value)=>M(value,1);", "global::System.Threading.Tasks.Task<object> M<T1>(object value, T1 tag)"),
            ("Satisfied", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:notnull{}public static object Use(object value)=>M(value,1);", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("NoResidual", "public static object M(object value){Need(value);return value;}static void Need<T>(T value)where T:notnull{}public static object? Use(string? value)=>M(value);", null),

            ("Return", "public static string M(string value,int tag)=>value;public static string Use(string? value)=>M(value,1);", "string M<T1>(string value, T1 tag)"),
            ("ArrayReturn", "public static string[] M(string[] value,int tag)=>value;public static string[] Use(string?[] value)=>M(value,1);", "string[] M<T1>(string[] value, T1 tag)"),
            ("TupleReturn", "public static (string left,string right) M((string left,string right) pair,int tag)=>pair;public static (string left,string right) Use((string? left,string right) pair)=>M(pair,1);", "(string left, T1 right) M<T1, T2>((string left, T1 right) pair, T2 tag)"),

            ("NotNullObject", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:notnull{}public static object? Use(string? value)=>M(value,1);", "object M<T1>(object value, T1 tag)"),
            ("NotNullString", "public static string M(string value,int tag){Need(value);return value;}static void Need<T>(T value)where T:notnull{}public static string? Use(string? value)=>M(value,1);", "string M<T1>(string value, T1 tag)"),
            ("NotNullValue", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:notnull{}public static object? Use(int? value)=>M(value,1);", "object M<T1>(object value, T1 tag)"),
            ("Class", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:class{}public static object? Use(string? value)=>M(value,1);", "object M<T1>(object value, T1 tag)"),
            ("Interface", "public static System.IComparable M(System.IComparable value,int tag){Need(value);return value;}static void Need<T>(T value)where T:System.IComparable{}public static System.IComparable? Use(string? value)=>M(value,1);", "global::System.IComparable M<T1>(global::System.IComparable value, T1 tag)"),
            ("Base", "public class Base{}public static Base M(Base value,int tag){Need(value);return value;}static void Need<T>(T value)where T:Base{}public static Base? Use(Base? value)=>M(value,1);", "global::Cases.Base M<T1>(global::Cases.Base value, T1 tag)")
        ];
        var failures = new List<string>();
        foreach (var test in cases)
        {
            foreach (var separateCaller in new[] { false, true })
            {
                foreach (var policy in new[] { "Warnings", "SuppressOriginal", "PromoteNew" })
                {
                    var tree = CSharpSyntaxTree.ParseText("#nullable enable\npublic static partial class Cases{" + test.Source + "}", path: "Implementation.cs", cancellationToken: cancellationToken);
                    SyntaxTree[] trees = [tree];
                    if (separateCaller)
                    {
                        var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                        var callers = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Where(method =>
                        { ArgumentNullException.ThrowIfNull(method); return method.Identifier.ValueText == "Use"; }).ToArray();
                        var removed = root.RemoveNodes(callers, SyntaxRemoveOptions.KeepNoTrivia)!;
                        var callerRoot = SyntaxFactory.ParseCompilationUnit("#nullable enable\npublic static partial class Cases{}");
                        var owner = (Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax)callerRoot.Members.Single();
                        callerRoot = callerRoot.ReplaceNode(owner, owner.WithMembers(SyntaxFactory.List<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>(callers)));
                        trees = [tree.WithRootAndOptions(removed, tree.Options), CSharpSyntaxTree.Create(callerRoot, (CSharpParseOptions)tree.Options, "Caller.cs")];
                    }
                    var compilation = Program.Compile(trees).WithOptions(Program.Compile([]).Options.WithWarningLevel(9999));
                    var originalDiagnostics = compilation.GetDiagnostics(cancellationToken);
                    if (policy != "Warnings")
                    {
                        var suppressed = new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal);
                        foreach (var diagnostic in originalDiagnostics)
                        {
                            if (diagnostic.Severity == DiagnosticSeverity.Warning)
                            { suppressed[diagnostic.Id] = ReportDiagnostic.Suppress; }
                        }
                        compilation = compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(suppressed)
                            .WithGeneralDiagnosticOption(policy == "PromoteNew" ? ReportDiagnostic.Error : ReportDiagnostic.Default));
                    }
                    var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
                    { ArgumentNullException.ThrowIfNull(diagnostic); return Inference.IsCompilationError(diagnostic); }).ToArray();
                    if (errors.Length != 0)
                    { throw new InvalidOperationException("Invalid nullable caller fixture " + test.Name + ": " + errors[0]); }
                    var result = Program.Results(compilation, cancellationToken).Single(result =>
                    { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
                    if (test.Signature is null ? result.Status == "Suggestion" : result.Status != "Suggestion" || result.Signature != test.Signature)
                    { failures.Add(test.Name + " " + policy + ": " + (result.Signature ?? result.Message)); }
                    else
                    { Program.Log.Info("PASS NullableCaller" + test.Name + policy + (separateCaller ? "CrossFile" : "SameFile")); }
                }
            }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return cases.Length * 6 + await NullableCallerRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
