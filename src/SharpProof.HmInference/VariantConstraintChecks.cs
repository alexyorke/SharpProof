using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class VariantConstraintChecks
{
    internal const string Support = """
        #nullable enable
        public static class BoundTrace{public static string Value="";}
        public interface BoundProducer<out T>{T Read();}
        public interface BoundConsumer<in T>{void Write(T value);}
        public class BoundProducerBox<T>:BoundProducer<T>{public T Value=default!;public T Read(){BoundTrace.Value+="read;";return Value;}}
        public class BoundConsumerBox<T>:BoundConsumer<T>{public T Value=default!;public void Write(T value){BoundTrace.Value+="write;";Value=value;}}
        public class BoundProducerDerived<T>:BoundProducerBox<T>{}
        public struct BoundProducerValue<T>:BoundProducer<T>{public T Value;public T Read(){BoundTrace.Value+="read;";return Value;}}
        public interface BoundTransform<in TIn,out TOut>{TOut Invoke(TIn value);}
        public class BoundTransformBox<TIn,TOut>:BoundTransform<TIn,TOut>{public TIn Input=default!;public TOut Value=default!;public TOut Invoke(TIn value){BoundTrace.Value+="invoke;";Input=value;return Value;}}
        """;

    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Body, string Signature)[] tests =
        [
            ("ReferenceIdentity", "static BoundProducerBox<string> M(BoundProducerBox<string> value,string other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerBox<T1> M<T1>(global::BoundProducerBox<T1> value, T1 other)"),
            ("Inherited", "static BoundProducerDerived<string> M(BoundProducerDerived<string> value,object other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerDerived<T1> M<T1, T2>(global::BoundProducerDerived<T1> value, T2 other) where T1 : class, T2 where T2 : class"),
            ("StructReceiver", "static BoundProducerValue<string> M(BoundProducerValue<string> value,object other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerValue<T1> M<T1, T2>(global::BoundProducerValue<T1> value, T2 other) where T1 : class, T2 where T2 : class"),
            ("NestedConstraint", "static BoundProducerBox<System.Collections.Generic.List<string>> M(BoundProducerBox<System.Collections.Generic.List<string>> value,object other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<System.Collections.Generic.IEnumerable<U>> =>value;", "global::BoundProducerBox<global::System.Collections.Generic.List<T1>> M<T1, T2>(global::BoundProducerBox<global::System.Collections.Generic.List<T1>> value, T2 other) where T1 : class, T2 where T2 : class"),
            ("ExistingGenericScope", "static BoundProducerBox<TBase> M<TBase>(BoundProducerBox<TBase> value,TBase other,string ignored) where TBase:class=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerBox<TBase> M<TBase, T1>(global::BoundProducerBox<TBase> value, TBase other, T1 ignored) where TBase : class"),
            ("MultipleCalls", "static BoundProducerBox<string> M(BoundProducerBox<string> first,BoundProducerBox<System.Uri> second,object other,int ignored){Need(first,other);Need(second,other);return first;}static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerBox<T1> M<T1, T2, T3, T4>(global::BoundProducerBox<T1> first, global::BoundProducerBox<T2> second, T3 other, T4 ignored) where T1 : class, T3 where T2 : class, T3 where T3 : class"),
            ("ReversedArguments", "static BoundProducerBox<string> M(object other,BoundProducerBox<string> value)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerBox<T2> M<T1, T2>(T1 other, global::BoundProducerBox<T2> value) where T1 : class where T2 : class, T1"),
            ("MixedVariance", "static BoundTransformBox<object,string> M(BoundTransformBox<object,string> value,string input,object output)=>Need(value,input,output);static T Need<T,U,V>(T value,U input,V output) where T:BoundTransform<U,V> =>value;", "global::BoundTransformBox<T1, T2> M<T1, T2, T3, T4>(global::BoundTransformBox<T1, T2> value, T3 input, T4 output) where T1 : class where T2 : class, T4 where T3 : class, T1 where T4 : class"),
            ("NullableCovariant", "static BoundProducerBox<string?> M(BoundProducerBox<string?> value,object? other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerBox<T1?> M<T1, T2>(global::BoundProducerBox<T1?> value, T2? other) where T1 : class, T2 where T2 : class"),
            ("NullableContravariant", "static BoundConsumerBox<object?> M(BoundConsumerBox<object?> value,string? other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundConsumer<U> =>value;", "global::BoundConsumerBox<T1?> M<T1, T2>(global::BoundConsumerBox<T1?> value, T2? other) where T1 : class where T2 : class, T1"),
            ("ArrayReceiver", "static string[] M(string[] value,object other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:System.Collections.Generic.IEnumerable<U> =>value;", "T1[] M<T1, T2>(T1[] value, T2 other) where T1 : class, T2 where T2 : class"),
            ("ReadOnlyArrayReceiver", "static string[] M(string[] value,object other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:System.Collections.Generic.IReadOnlyList<U> =>value;", "T1[] M<T1, T2>(T1[] value, T2 other) where T1 : class, T2 where T2 : class"),
            ("ArrayReceiverIdentity", "static int[] M(int[] value,int other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:System.Collections.Generic.IEnumerable<U> =>value;", "T1[] M<T1>(T1[] value, T1 other)"),
            ("NullableValueArrayIdentity", "static int?[] M(int?[] value,int? other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:System.Collections.Generic.IEnumerable<U> =>value;", "T1?[] M<T1>(T1?[] value, T1? other) where T1 : struct"),
            ("Covariant", "static BoundProducerBox<string> M(BoundProducerBox<string> value,object other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerBox<T1> M<T1, T2>(global::BoundProducerBox<T1> value, T2 other) where T1 : class, T2 where T2 : class"),
            ("Contravariant", "static BoundConsumerBox<object> M(BoundConsumerBox<object> value,string other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundConsumer<U> =>value;", "global::BoundConsumerBox<T1> M<T1, T2>(global::BoundConsumerBox<T1> value, T2 other) where T1 : class where T2 : class, T1"),
            ("ValueIdentity", "static BoundProducerBox<int> M(BoundProducerBox<int> value,int other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerBox<T1> M<T1>(global::BoundProducerBox<T1> value, T1 other)"),
            ("ArrayCovariant", "static BoundProducerBox<string[]> M(BoundProducerBox<string[]> value,object[] other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerBox<T1[]> M<T1, T2>(global::BoundProducerBox<T1[]> value, T2[] other) where T1 : class, T2 where T2 : class"),
            ("NestedCovariant", "static BoundProducerBox<System.Collections.Generic.List<string>> M(BoundProducerBox<System.Collections.Generic.List<string>> value,System.Collections.Generic.IEnumerable<object> other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerBox<global::System.Collections.Generic.List<T1>> M<T1, T2>(global::BoundProducerBox<global::System.Collections.Generic.List<T1>> value, global::System.Collections.Generic.IEnumerable<T2> other) where T1 : class, T2 where T2 : class"),
            ("DelegateCovariant", "static BoundProducerBox<System.Func<string>> M(BoundProducerBox<System.Func<string>> value,System.Func<object> other)=>Need(value,other);static T Need<T,U>(T value,U other) where T:BoundProducer<U> =>value;", "global::BoundProducerBox<global::System.Func<T1>> M<T1, T2>(global::BoundProducerBox<global::System.Func<T1>> value, global::System.Func<T2> other) where T1 : class, T2 where T2 : class")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(Support + "class Cases{" + test.Body + "}", cancellationToken: cancellationToken)]);
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
            if (!emitted.Success)
            { throw new InvalidOperationException("Variant constraint fixture " + test.Name + ": " + string.Join("; ", emitted.Diagnostics)); }
            var result = Program.Results(compilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { failures.Add(test.Name + ": " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS VariantConstraint" + test.Name); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return tests.Length + await VariantConstraintRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
