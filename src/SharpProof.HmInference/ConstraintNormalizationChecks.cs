using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class ConstraintNormalizationChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string support = """
            public class ConstraintRoot{}
            public class ConstraintMiddle:ConstraintRoot{}
            public class ConstraintLeaf:ConstraintMiddle{}
            public class ConstraintBox<T>{}
            public interface ConstraintInvariant<T>{}
            public class ConstraintBoxLeaf<T>:ConstraintBox<T>,ConstraintInvariant<T>{}
            public class ConstraintIntBox:ConstraintBoxLeaf<int>{}
            """;
        (string Name, string Source, string Signature)[] tests =
        [
            ("ConstraintCycleBaseMerge", "static ConstraintLeaf M(ConstraintLeaf left,ConstraintLeaf right){Keep(left,right);Keep(right,left);Root(left);Middle(right);return left;}static T Keep<T,U>(T value,U other) where T:U=>value;static T Root<T>(T value) where T:ConstraintRoot=>value;static T Middle<T>(T value) where T:ConstraintMiddle=>value;", "T1 M<T1>(T1 left, T1 right) where T1 : global::ConstraintMiddle"),
            ("ConstraintGenericBaseBounds", "static ConstraintIntBox M(ConstraintIntBox value,int first,int second){Root(value,first);Leaf(value,second);return value;}static T Root<T,U>(T value,U other) where T:ConstraintBox<U> =>value;static T Leaf<T,U>(T value,U other) where T:ConstraintBoxLeaf<U> =>value;", "T1 M<T1, T2>(T1 value, T2 first, T2 second) where T1 : global::ConstraintBoxLeaf<T2>"),
            ("ConstraintInvariantInterfaceProjection", "static ConstraintBoxLeaf<int> M(ConstraintBoxLeaf<int> value,int other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:ConstraintInvariant<U> =>value;", "global::ConstraintBoxLeaf<T1> M<T1>(global::ConstraintBoxLeaf<T1> value, T1 other)"),
            ("ConstraintAcyclicDependency", "static object M(object first,object second,object third){Keep(first,second);Keep(second,third);return first;}static T Keep<T,U>(T value,U other) where T:U=>value;", "T1 M<T1, T2, T3>(T1 first, T2 second, T3 third) where T1 : T2 where T2 : T3"),
            ("ConstraintSeparateCycles", "static object M(object first,object second,object third,object fourth){Keep(first,second);Keep(second,first);Keep(third,fourth);Keep(fourth,third);return first;}static T Keep<T,U>(T value,U other) where T:U=>value;", "T1 M<T1, T2>(T1 first, T1 second, T2 third, T2 fourth)"),
            ("ConstraintCycleOutgoingEdge", "static object M(object first,object second,object third){Keep(first,second);Keep(second,first);Keep(second,third);return first;}static T Keep<T,U>(T value,U other) where T:U=>value;", "T1 M<T1, T2>(T1 first, T1 second, T2 third) where T1 : T2"),
            ("ConstraintFBoundedInterface", "static int M(int value)=>Keep(value);static T Keep<T>(T value) where T:System.IComparable<T> =>value;", "T1 M<T1>(T1 value) where T1 : global::System.IComparable<T1>"),
            ("ConstraintSelfDependency", "static object M(object value)=>Keep(value,value);static T Keep<T,U>(T value,U other) where T:U=>value;", "T1 M<T1>(T1 value)"),
            ("ConstraintMutualDependency", "static object M(object left,object right){Keep(left,right);Keep(right,left);return left;}static T Keep<T,U>(T value,U other) where T:U=>value;", "T1 M<T1>(T1 left, T1 right)"),
            ("ConstraintThreeWayDependency", "static object M(object first,object second,object third){Keep(first,second);Keep(second,third);Keep(third,first);return first;}static T Keep<T,U>(T value,U other) where T:U=>value;", "T1 M<T1>(T1 first, T1 second, T1 third)"),
            ("ConstraintCycleWithStruct", "static int M(int left,int right){Keep(left,right);Keep(right,left);return Value(left);}static T Keep<T,U>(T value,U other) where T:U=>value;static T Value<T>(T value) where T:struct=>value;", "T1 M<T1>(T1 left, T1 right) where T1 : struct"),
            ("ConstraintStrongestBase", "static ConstraintLeaf M(ConstraintLeaf value)=>Root(Middle(value));static T Root<T>(T value) where T:ConstraintRoot=>value;static T Middle<T>(T value) where T:ConstraintMiddle=>value;", "T1 M<T1>(T1 value) where T1 : global::ConstraintMiddle"),
            ("ConstraintStrongestBaseReverse", "static ConstraintLeaf M(ConstraintLeaf value)=>Middle(Root(value));static T Root<T>(T value) where T:ConstraintRoot=>value;static T Middle<T>(T value) where T:ConstraintMiddle=>value;", "T1 M<T1>(T1 value) where T1 : global::ConstraintMiddle"),
            ("ConstraintGenericBaseProjection", "static ConstraintBoxLeaf<int> M(ConstraintBoxLeaf<int> value,int first,int second){Root(value,first);Leaf(value,second);return value;}static T Root<T,U>(T value,U other) where T:ConstraintBox<U> =>value;static T Leaf<T,U>(T value,U other) where T:ConstraintBoxLeaf<U> =>value;", "global::ConstraintBoxLeaf<T1> M<T1>(global::ConstraintBoxLeaf<T1> value, T1 first, T1 second)")
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
        return tests.Length + await ConstraintNormalizationRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
