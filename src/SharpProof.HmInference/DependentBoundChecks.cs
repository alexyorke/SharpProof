using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class DependentBoundChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string support = """
            #nullable enable
            public class DependentRoot{}
            public class DependentMiddle:DependentRoot{}
            public class DependentLeaf:DependentMiddle{}
            public class DependentSibling:DependentRoot{}
            public sealed class DependentClosed{}
            """;
        (string Name, string Source, string Signature)[] tests =
        [
            ("DependentAsyncNullable", "static async System.Threading.Tasks.Task<DependentLeaf?> M(System.Threading.Tasks.Task<DependentLeaf?> value,int ignored){DependentRoot other=new();return Keep(await value,other);}static T Keep<T,U>(T value,U other) where T:U? =>value;", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 ignored) where T1 : global::DependentRoot"),
            ("DependentSealedString", "static string M(string value,int ignored){string other=\"fixed\";return Keep(value,other);}static T Keep<T,U>(T value,U other) where T:U=>value;", "string M<T1>(string value, T1 ignored)"),
            ("DependentSealedClass", "static DependentClosed M(DependentClosed value,int ignored){DependentClosed other=new();return Keep(value,other);}static T Keep<T,U>(T value,U other) where T:U=>value;", "global::DependentClosed M<T1>(global::DependentClosed value, T1 ignored)"),
            ("DependentValueType", "static int M(int value,int ignored){int other=0;return Keep(value,other);}static T Keep<T,U>(T value,U other) where T:U=>value;", "int M<T1>(int value, T1 ignored)"),
            ("DependentArrayIdentity", "static int[] M(int[] value,int ignored){int[] other=[];return Keep(value,other);}static T Keep<T,U>(T value,U other) where T:U=>value;", "int[] M<T1>(int[] value, T1 ignored)"),
            ("DependentArrayCovariance", "static string[] M(string[] value,int ignored){object[] other=[];return Keep(value,other);}static T Keep<T,U>(T value,U other) where T:U=>value;", "T1[] M<T1, T2>(T1[] value, T2 ignored) where T1 : class"),
            ("DependentNullableResolvedBase", "static DependentLeaf? M(DependentLeaf? value,int ignored){DependentRoot other=new();return Keep(value,other);}static T Keep<T,U>(T value,U other) where T:U? =>value;", "T1? M<T1, T2>(T1? value, T2 ignored) where T1 : global::DependentRoot"),
            ("DependentObjectNotNull", "static object M(object value,int ignored){object other=new();return Keep(value,other);}static T Keep<T,U>(T value,U other) where T:U=>value;", "T1 M<T1, T2>(T1 value, T2 ignored) where T1 : notnull"),
            ("DependentNullableObject", "static object M(object value,int ignored){object other=new();return Keep(value,other);}static T Keep<T,U>(T value,U other) where T:U? =>value;", "T1 M<T1, T2>(T1 value, T2 ignored)"),
            ("DependentTransitiveBase", "static DependentLeaf M(DependentLeaf left,DependentMiddle right){Keep(left,right);Root(left);Middle(right);return left;}static T Keep<T,U>(T value,U other) where T:U=>value;static T Root<T>(T value) where T:DependentRoot=>value;static T Middle<T>(T value) where T:DependentMiddle=>value;", "T1 M<T1, T2>(T1 left, T2 right) where T1 : T2 where T2 : global::DependentMiddle"),
            ("DependentNullableEnumReference", "static System.Enum? M(System.Enum? value,int ignored)=>Keep(value);static T Keep<T>(T value) where T:System.Enum? =>value;", "global::System.Enum? M<T1>(global::System.Enum? value, T1 ignored)"),
            ("DependentLowerRoot", "static DependentRoot M(DependentRoot value,int independent){Keep(new DependentLeaf(),value);return value;}static T Keep<T,U>(T source,U target) where T:U=>source;", "global::DependentRoot M<T1>(global::DependentRoot value, T1 independent)"),
            ("DependentLowerObject", "static object M(object value,int independent){Keep(\"fixed\",value);return value;}static T Keep<T,U>(T source,U target) where T:U=>source;", "object M<T1>(object value, T1 independent)"),
            ("DependentLowerTwoSources", "static DependentRoot M(DependentRoot value,int independent){Keep(new DependentLeaf(),value);Keep(new DependentSibling(),value);return value;}static T Keep<T,U>(T source,U target) where T:U=>source;", "global::DependentRoot M<T1>(global::DependentRoot value, T1 independent)"),
            ("DependentUpperDirection", "static DependentRoot M(DependentRoot value,int independent){Keep(value,new DependentRoot());return value;}static T Keep<T,U>(T source,U target) where T:U=>source;", "T1 M<T1, T2>(T1 value, T2 independent) where T1 : global::DependentRoot"),
            ("DependentFreeControl", "static DependentRoot M(DependentRoot value,int independent)=>value;", "T1 M<T1, T2>(T1 value, T2 independent)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(support + "class Cases{" + test.Source + "}", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(Inference.IsCompilationError).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + string.Join("; ", errors.Select(error => { ArgumentNullException.ThrowIfNull(error); return error.ToString(); }))); }
            var result = Program.Results(compilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { failures.Add(test.Name + ": " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS " + test.Name); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        await Task.CompletedTask.ConfigureAwait(false);
        return tests.Length;
    }
}
