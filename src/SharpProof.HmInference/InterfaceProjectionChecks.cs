using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class InterfaceProjectionChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string support = """
            #nullable enable
            public interface ProjectionContract<T>{}
            public class ProjectionDual<T>:ProjectionContract<T[]>,ProjectionContract<System.Collections.Generic.List<T>>{}
            public class ProjectionReverse<T>:ProjectionContract<System.Collections.Generic.List<T>>,ProjectionContract<T[]>{}
            public struct ProjectionValue<T>:ProjectionContract<T[]>,ProjectionContract<System.Collections.Generic.List<T>>{}
            public class ProjectionPair<A,B>:ProjectionContract<(A[],System.Collections.Generic.List<B>)>,ProjectionContract<(B[],A[])>{}
            public class ProjectionLeaf<T>:ProjectionDual<T>{}
            """;
        (string Name, string Source, string Signature)[] tests =
        [
            ("ProjectionTransactional", "static ProjectionPair<string,int> M(ProjectionPair<string,int> value,(int[],string[]) other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:ProjectionContract<U> =>value;", "global::ProjectionPair<T1, T2> M<T1, T2>(global::ProjectionPair<T1, T2> value, (T2[], T1[]) other)"),
            ("ProjectionArray", "static ProjectionDual<int> M(ProjectionDual<int> value,int[] other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:ProjectionContract<U> =>value;", "global::ProjectionDual<T1> M<T1>(global::ProjectionDual<T1> value, T1[] other)"),
            ("ProjectionList", "static ProjectionDual<int> M(ProjectionDual<int> value,System.Collections.Generic.List<int> other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:ProjectionContract<U> =>value;", "global::ProjectionDual<T1> M<T1>(global::ProjectionDual<T1> value, global::System.Collections.Generic.List<T1> other)"),
            ("ProjectionDeclarationOrder", "static ProjectionReverse<int> M(ProjectionReverse<int> value,int[] other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:ProjectionContract<U> =>value;", "global::ProjectionReverse<T1> M<T1>(global::ProjectionReverse<T1> value, T1[] other)"),
            ("ProjectionValueDomain", "static ProjectionValue<int> M(ProjectionValue<int> value,int[] other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:ProjectionContract<U> =>value;", "global::ProjectionValue<T1> M<T1>(global::ProjectionValue<T1> value, T1[] other)"),
            ("ProjectionInherited", "static ProjectionLeaf<int> M(ProjectionLeaf<int> value,int[] other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:ProjectionContract<U> =>value;", "global::ProjectionLeaf<T1> M<T1>(global::ProjectionLeaf<T1> value, T1[] other)")
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
        var runtimeChecks = await InterfaceProjectionRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
        return tests.Length + runtimeChecks;
    }
}
