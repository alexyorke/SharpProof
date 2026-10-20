using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class VariantProjectionChecks
{
    internal const string Support = VariantConstraintChecks.Support + """
        public class SplitProducer<T>:BoundProducer<T[]>,BoundProducer<int>{
            public T[] Values=System.Array.Empty<T>();
            T[] BoundProducer<T[]>.Read(){BoundTrace.Value+="array;";return Values;}
            int BoundProducer<int>.Read(){BoundTrace.Value+="fixed;";return 23;}
        }
        public class ReverseSplitProducer<T>:BoundProducer<int>,BoundProducer<T[]>{
            public T[] Values=System.Array.Empty<T>();
            T[] BoundProducer<T[]>.Read(){BoundTrace.Value+="array;";return Values;}
            int BoundProducer<int>.Read(){BoundTrace.Value+="fixed;";return 23;}
        }
        public class InheritedSplitProducer<T>:SplitProducer<T>{}
        public struct ValueSplitProducer<T>:BoundProducer<T[]>,BoundProducer<int>{
            public T[] Values;
            T[] BoundProducer<T[]>.Read(){BoundTrace.Value+="array;";return Values;}
            int BoundProducer<int>.Read(){BoundTrace.Value+="fixed;";return 23;}
        }
        public class SplitConsumer<T>:BoundConsumer<System.Collections.Generic.IEnumerable<T>>,BoundConsumer<int>{
            public System.Collections.Generic.IEnumerable<T> Values=System.Array.Empty<T>();public int Fixed;
            void BoundConsumer<System.Collections.Generic.IEnumerable<T>>.Write(System.Collections.Generic.IEnumerable<T> value){BoundTrace.Value+="sequence;";Values=value;}
            void BoundConsumer<int>.Write(int value){BoundTrace.Value+="fixed;";Fixed=value;}
        }
        public class NestedSplitProducer<T>:BoundProducer<System.Func<T[]>>,BoundProducer<System.Func<int>>{
            public System.Func<T[]> Callback=()=>System.Array.Empty<T>();
            System.Func<T[]> BoundProducer<System.Func<T[]>>.Read(){BoundTrace.Value+="nested;";return Callback;}
            System.Func<int> BoundProducer<System.Func<int>>.Read(){BoundTrace.Value+="fixed;";return ()=>23;}
        }
        public class InterfaceSplitProducer<T>:BoundProducer<T[]>,BoundProducer<System.Collections.Generic.IEnumerable<T>>{
            public T[] Values=System.Array.Empty<T>();public System.Collections.Generic.IEnumerable<T> Sequence=System.Array.Empty<T>();
            T[] BoundProducer<T[]>.Read(){BoundTrace.Value+="array;";return Values;}
            System.Collections.Generic.IEnumerable<T> BoundProducer<System.Collections.Generic.IEnumerable<T>>.Read(){BoundTrace.Value+="interface;";return Sequence;}
        }
        public class ExactSplitProducer<T>:BoundProducer<System.Collections.Generic.List<T>>,BoundProducer<System.Collections.Generic.IEnumerable<object>>{
            public System.Collections.Generic.List<T> Values=new System.Collections.Generic.List<T>();public System.Collections.Generic.IEnumerable<object> Fixed=System.Array.Empty<object>();
            System.Collections.Generic.List<T> BoundProducer<System.Collections.Generic.List<T>>.Read(){BoundTrace.Value+="list;";return Values;}
            System.Collections.Generic.IEnumerable<object> BoundProducer<System.Collections.Generic.IEnumerable<object>>.Read(){BoundTrace.Value+="exact;";return Fixed;}
        }
        public class AmbiguousSplitProducer<T>:BoundProducer<T[]>,BoundProducer<System.Collections.Generic.List<T>>{
            T[] BoundProducer<T[]>.Read()=>System.Array.Empty<T>();
            System.Collections.Generic.List<T> BoundProducer<System.Collections.Generic.List<T>>.Read()=>new System.Collections.Generic.List<T>();
        }
        """;

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Body, string Signature)[] tests =
        [
            ("Async", "static async System.Threading.Tasks.Task<SplitProducer<string>> M(SplitProducer<string> value,object other){await System.Threading.Tasks.Task.Yield();return Need(value,other);}static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::System.Threading.Tasks.Task<global::SplitProducer<T1>> M<T1>(global::SplitProducer<T1> value, object other)"),
            ("NestedDelegate", "static NestedSplitProducer<string> M(NestedSplitProducer<string> value,System.Func<object> other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::NestedSplitProducer<T1> M<T1>(global::NestedSplitProducer<T1> value, global::System.Func<object> other)"),
            ("ExactInterfaceControl", "static InterfaceSplitProducer<string> M(InterfaceSplitProducer<string> value,System.Collections.Generic.IEnumerable<string> other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::InterfaceSplitProducer<T1> M<T1>(global::InterfaceSplitProducer<T1> value, global::System.Collections.Generic.IEnumerable<T1> other)"),
            ("IdentityDominatesVariance", "static ExactSplitProducer<string> M(ExactSplitProducer<string> value,System.Collections.Generic.IEnumerable<object> other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::ExactSplitProducer<T1> M<T1>(global::ExactSplitProducer<T1> value, global::System.Collections.Generic.IEnumerable<object> other)"),
            ("CovariantArray", "static SplitProducer<string> M(SplitProducer<string> value,object other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::SplitProducer<T1> M<T1>(global::SplitProducer<T1> value, object other)"),
            ("ReversedDeclarations", "static ReverseSplitProducer<string> M(ReverseSplitProducer<string> value,object other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::ReverseSplitProducer<T1> M<T1>(global::ReverseSplitProducer<T1> value, object other)"),
            ("Inherited", "static InheritedSplitProducer<string> M(InheritedSplitProducer<string> value,object other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::InheritedSplitProducer<T1> M<T1>(global::InheritedSplitProducer<T1> value, object other)"),
            ("ValueReceiver", "static ValueSplitProducer<string> M(ValueSplitProducer<string> value,object other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::ValueSplitProducer<T1> M<T1>(global::ValueSplitProducer<T1> value, object other)"),
            ("FixedProducer", "static SplitProducer<string> M(SplitProducer<string> value,int other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::SplitProducer<T1> M<T1>(global::SplitProducer<T1> value, int other)"),
            ("ContravariantList", "static SplitConsumer<string> M(SplitConsumer<string> value,System.Collections.Generic.List<string> other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundConsumer<U> =>value;", "global::SplitConsumer<T1> M<T1>(global::SplitConsumer<T1> value, global::System.Collections.Generic.List<T1> other)"),
            ("ContravariantVariance", "static SplitConsumer<object> M(SplitConsumer<object> value,System.Collections.Generic.List<string> other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundConsumer<U> =>value;", "global::SplitConsumer<T1> M<T1, T2>(global::SplitConsumer<T1> value, global::System.Collections.Generic.List<T2> other) where T1 : class where T2 : class, T1"),
            ("FixedConsumer", "static SplitConsumer<string> M(SplitConsumer<string> value,int other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundConsumer<U> =>value;", "global::SplitConsumer<T1> M<T1>(global::SplitConsumer<T1> value, int other)"),
            ("ExactArrayControl", "static SplitProducer<string> M(SplitProducer<string> value,string[] other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::SplitProducer<T1> M<T1>(global::SplitProducer<T1> value, T1[] other)"),
            ("IndependentCalls", "static SplitProducer<string> M(SplitProducer<string> first,SplitProducer<System.DateTime> second,object other){Need(first,other);Need(second,other);return first;}static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::SplitProducer<T1> M<T1, T2>(global::SplitProducer<T1> first, global::SplitProducer<T2> second, object other)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(Support + "class Cases{" + test.Body + "}", cancellationToken: cancellationToken)]);
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
            if (!emitted.Success)
            { throw new InvalidOperationException("Variant projection fixture " + test.Name + ": " + string.Join("; ", emitted.Diagnostics)); }
            var result = Program.Results(compilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { failures.Add(test.Name + ": " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS VariantProjection" + test.Name); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        var ambiguous = Program.Compile([CSharpSyntaxTree.ParseText(Support + "class Cases{static AmbiguousSplitProducer<string> M(AmbiguousSplitProducer<string> value,object other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;}", cancellationToken: cancellationToken)]);
        using var ambiguousImage = new MemoryStream();
        var ambiguousEmission = ambiguous.Emit(ambiguousImage, cancellationToken: cancellationToken);
        if (!ambiguousEmission.Success)
        { throw new InvalidOperationException("Ambiguous projection fixture: " + string.Join("; ", ambiguousEmission.Diagnostics)); }
        if (Program.Results(ambiguous, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; }).Status == "Suggestion")
        { throw new InvalidOperationException("Distinct viable projections must not select an arbitrary interface."); }
        Program.Log.Info("PASS VariantProjectionPreservesAmbiguousAlternatives");
        return tests.Length + 1 + await VariantProjectionRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
