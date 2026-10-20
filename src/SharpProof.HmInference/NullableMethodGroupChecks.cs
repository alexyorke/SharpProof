using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class NullableMethodGroupChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string? Signature)[] cases =
        [
            ("AsyncTupleSibling", "public static async System.Threading.Tasks.Task<(object left,int right)> M(object value,int sibling,int tag){await System.Threading.Tasks.Task.Yield();return (value,sibling);}public static System.Func<string?,int,int,System.Threading.Tasks.Task<(object,int)>> Use()=>M;", "global::System.Threading.Tasks.Task<(object left, T1 right)> M<T1, T2>(object value, T1 sibling, T2 tag)"),
            ("ValueTaskTupleSibling", "public static async System.Threading.Tasks.ValueTask<(object left,int right)> M(object value,int sibling,int tag){await System.Threading.Tasks.Task.Yield();return (value,sibling);}public static System.Func<string?,int,int,System.Threading.Tasks.ValueTask<(object,int)>> Use()=>(System.Func<string?,int,int,System.Threading.Tasks.ValueTask<(object,int)>>)M;", "global::System.Threading.Tasks.ValueTask<(object left, T1 right)> M<T1, T2>(object value, T1 sibling, T2 tag)"),
            ("ReducedNullableReceiver", "public static string M(this string value,int tag)=>value;public static System.Func<int,string> Use(string? value)=>value.M;", "string M<T1>(this string value, T1 tag)"),
            ("ReducedAsyncReceiver", "public static async System.Threading.Tasks.Task<string> M(this string value,int tag){await System.Threading.Tasks.Task.Yield();return value;}public static System.Func<int,System.Threading.Tasks.Task<string>> Use(string? value)=>value.M;", "global::System.Threading.Tasks.Task<string> M<T1>(this string value, T1 tag)"),
            ("ReducedValueTaskReceiver", "public static async System.Threading.Tasks.ValueTask<string> M(this string value,int tag){await System.Threading.Tasks.Task.Yield();return value;}public static System.Func<int,System.Threading.Tasks.ValueTask<string>> Use(string? value)=>value.M;", "global::System.Threading.Tasks.ValueTask<string> M<T1>(this string value, T1 tag)"),
            ("ReducedNotNullReceiver", "public static object M(this string value,int tag){Need(value);return new object();}static void Need<T>(T value)where T:notnull{}public static System.Func<int,object> Use(string? value)=>value.M;", "object M<T1>(this string value, T1 tag)"),
            ("ReducedClassReceiver", "public static object M(this string value,int tag){Need(value);return new object();}static void Need<T>(T value)where T:class{}public static System.Func<int,object> Use(string? value)=>value.M;", "object M<T1>(this string value, T1 tag)"),
            ("ReducedInterfaceReceiver", "public static object M(this System.IComparable value,int tag){Need(value);return new object();}static void Need<T>(T value)where T:System.IComparable{}public static System.Func<int,object> Use(System.IComparable? value)=>value.M;", "object M<T1>(this global::System.IComparable value, T1 tag)"),
            ("ReducedSatisfied", "public static string M(this string value,int tag){Need(value);return value;}static void Need<T>(T value)where T:class{}public static System.Func<int,string> Use(string value)=>value.M;", "T1 M<T1, T2>(this T1 value, T2 tag) where T1 : class"),
            ("ReducedNoResidual", "public static string M(this string value)=>value;public static System.Func<string> Use(string? value)=>value.M;", null),
            ("ReducedCastReceiver", "public static string M(this string value,int tag)=>value;public static System.Func<int,string> Use(string? value)=>(System.Func<int,string>)value.M;", "string M<T1>(this string value, T1 tag)"),
            ("AsyncTask", "public static async System.Threading.Tasks.Task<object> M(object value,int tag){await System.Threading.Tasks.Task.Yield();Need(value);return value;}static void Need<T>(T value)where T:notnull{}public static System.Func<string?,int,System.Threading.Tasks.Task<object>> Use()=>M;", "global::System.Threading.Tasks.Task<object> M<T1>(object value, T1 tag)"),
            ("AsyncValueTask", "public static async System.Threading.Tasks.ValueTask<object> M(object value,int tag){await System.Threading.Tasks.Task.Yield();Need(value);return value;}static void Need<T>(T value)where T:class{}public static System.Func<string?,int,System.Threading.Tasks.ValueTask<object>> Use()=>M;", "global::System.Threading.Tasks.ValueTask<object> M<T1>(object value, T1 tag)"),
            ("AsyncReturn", "public static async System.Threading.Tasks.Task<string> M(string value,int tag){await System.Threading.Tasks.Task.Yield();return value;}public static System.Func<string?,int,System.Threading.Tasks.Task<string>> Use()=>M;", "global::System.Threading.Tasks.Task<string> M<T1>(string value, T1 tag)"),
            ("Array", "public static string[] M(string[] value,int tag)=>value;public static System.Func<string?[],int,string[]> Use()=>M;", "string[] M<T1>(string[] value, T1 tag)"),
            ("Tuple", "public static (string left,int right) M((string left,int right) pair,int tag)=>pair;public static System.Func<(string?,int),int,(string,int)> Use()=>M;", "(string left, T1 right) M<T1, T2>((string left, T1 right) pair, T2 tag)"),
            ("Qualified", "public static string M(string value,int tag)=>value;public static System.Func<string?,int,string> Use()=>Cases.M;", "string M<T1>(string value, T1 tag)"),
            ("Satisfied", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:notnull{}public static System.Func<object,int,object> Use()=>M;", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("NoResidual", "public static string M(string value)=>value;public static System.Func<string?,string> Use()=>M;", null),
            ("Extension", "public static string M(this string value,int tag)=>value;public static System.Func<string?,int,string> Use()=>M;", "string M<T1>(this string value, T1 tag)"),
            ("CustomDelegate", "public delegate string Callback(string? value,int tag);public static string M(string value,int tag)=>value;public static Callback Use()=>M;", "string M<T1>(string value, T1 tag)"),
            ("CastTask", "public static async System.Threading.Tasks.Task<object> M(object value,int tag){await System.Threading.Tasks.Task.Yield();return value;}public static System.Func<string?,int,System.Threading.Tasks.Task<object>> Use()=>(System.Func<string?,int,System.Threading.Tasks.Task<object>>)M;", "global::System.Threading.Tasks.Task<object> M<T1>(object value, T1 tag)"),
            ("CastReturn", "public static string M(string value,int tag)=>value;public static System.Func<string?,int,string> Use()=>(System.Func<string?,int,string>)M;", "string M<T1>(string value, T1 tag)"),
            ("CastBoxing", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:notnull{}public static System.Func<object?,int,object> Use()=>(System.Func<object?,int,object>)M;", "object M<T1>(object value, T1 tag)"),
            ("Return", "public static string M(string value,int tag)=>value;public static System.Func<string?,int,string> Use()=>M;", "string M<T1>(string value, T1 tag)"),
            ("NotNull", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:notnull{}public static System.Func<string?,int,object> Use()=>M;", "object M<T1>(object value, T1 tag)"),
            ("Class", "public static object M(object value,int tag){Need(value);return value;}static void Need<T>(T value)where T:class{}public static System.Func<string?,int,object> Use()=>M;", "object M<T1>(object value, T1 tag)"),
            ("Interface", "public static System.IComparable M(System.IComparable value,int tag){Need(value);return value;}static void Need<T>(T value)where T:System.IComparable{}public static System.Func<string?,int,System.IComparable> Use()=>M;", "global::System.IComparable M<T1>(global::System.IComparable value, T1 tag)")
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
                    { Program.Log.Info("PASS NullableMethodGroup" + test.Name + policy + (separateCaller ? "CrossFile" : "SameFile")); }
                }
            }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return cases.Length * 6 + await NullableMethodGroupRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
