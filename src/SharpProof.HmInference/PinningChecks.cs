using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class PinningChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string support = """
            public class Pin<TTag,TValue>{public TValue Value=default!;public ref TValue GetPinnableReference()=>ref Value;}
            public class DerivedPin<TTag,TValue>:Pin<TTag,TValue>{}
            public class ReadonlyPin<TTag,TValue>{public TValue Value=default!;public ref readonly TValue GetPinnableReference()=>ref Value;}
            public class ExtensionPin<TTag,TValue>{public TValue Value=default!;}
            public static class PinExtensions{public static ref TValue GetPinnableReference<TTag,TValue>(this ExtensionPin<TTag,TValue> value) where TValue:unmanaged=>ref value.Value;}
            public unsafe struct Inline<TTag>{public fixed int Values[4];public TTag Tag;}
            public struct RefPin<TTag,TValue>{public TValue Value;}
            public static class RefPinExtensions{public static ref TValue GetPinnableReference<TTag,TValue>(this ref RefPin<TTag,TValue> value) where TValue:unmanaged=>ref value.Value;}
            public class ConstrainedPin<TTag>{public int Value;}
            public static class ConstrainedPinExtensions{public static ref int GetPinnableReference<TTag>(this ConstrainedPin<TTag> value) where TTag:class=>ref value.Value;}
            public class SpecializedPin<TTag,TValue>{public TValue Value=default!;public int Selected;}
            public static class SpecializedPinExtensions{public static ref TValue GetPinnableReference<TTag,TValue>(this SpecializedPin<TTag,TValue> value) where TValue:unmanaged=>ref value.Value;public static ref int GetPinnableReference(this SpecializedPin<int,int> value)=>ref value.Selected;}
            public class Owner<TTag>{public Inline<TTag> Buffer;public int Value;}
            """;
        (string Name, string Source, string Signature)[] tests =
        [
            ("FixedRefExtensionPattern", "static int M(ref RefPin<int,int> value){fixed(int* pointer=value){return *pointer;}}", "int M<T1>(ref global::RefPin<T1, int> value)"),
            ("FixedExtensionConstraint", "static int M(ConstrainedPin<string> value){fixed(int* pointer=value){return *pointer;}}", "int M<T1>(global::ConstrainedPin<T1> value) where T1 : class"),
            ("FixedNullableTag", "static int M(Pin<string?,int> value){fixed(int* pointer=value){return *pointer;}}", "int M<T1>(global::Pin<T1?, int> value) where T1 : class"),
            ("FixedNullableSource", "static int M(Pin<int,int>? value){fixed(int* pointer=value){return pointer==null?-1:*pointer;}}", "int M<T1>(global::Pin<T1, int>? value)"),
            ("FixedSpecializedExtension", "static int M(SpecializedPin<int,int> value,int ignored){fixed(int* pointer=value){return *pointer;}}", "int M<T1>(global::SpecializedPin<int, int> value, T1 ignored)"),
            ("FixedCustomPattern", "static int M(Pin<int,int> value){fixed(int* pointer=value){return *pointer;}}", "int M<T1>(global::Pin<T1, int> value)"),
            ("FixedInheritedPattern", "static int M(DerivedPin<int,int> value){fixed(int* pointer=value){return *pointer;}}", "int M<T1>(global::DerivedPin<T1, int> value)"),
            ("FixedReadonlyPattern", "static int M(ReadonlyPin<int,int> value){fixed(int* pointer=value){return *pointer;}}", "int M<T1>(global::ReadonlyPin<T1, int> value)"),
            ("FixedExtensionPattern", "static int M(ExtensionPin<int,int> value){fixed(int* pointer=value){return *pointer;}}", "int M<T1>(global::ExtensionPin<T1, int> value)"),
            ("FixedPatternStore", "static void M(Pin<int,int> destination,int value){fixed(int* pointer=destination){*pointer=value;}}", "void M<T1>(global::Pin<T1, int> destination, int value)"),
            ("FixedVoidSpan", "static bool M(System.Span<int> values){fixed(void* pointer=values){return pointer==null;}}", "bool M<T1>(global::System.Span<T1> values) where T1 : unmanaged"),
            ("FixedVoidReadonlySpan", "static bool M(System.ReadOnlySpan<int> values){fixed(void* pointer=values){return pointer==null;}}", "bool M<T1>(global::System.ReadOnlySpan<T1> values) where T1 : unmanaged"),
            ("FixedVoidMultidimensionalArray", "static bool M(int[,] values){fixed(void* pointer=values){return pointer==null;}}", "bool M<T1>(T1[,] values) where T1 : unmanaged"),
            ("FixedVoidArray", "static bool M(int[] values){fixed(void* pointer=values){return pointer==null;}}", "bool M<T1>(T1[] values) where T1 : unmanaged"),
            ("FixedVoidPattern", "static bool M(Pin<int,int> value){fixed(void* pointer=value){return pointer==null;}}", "bool M<T1, T2>(global::Pin<T1, T2> value) where T2 : unmanaged"),
            ("FixedMultiplePatterns", "static int M(Pin<int,int> left,Pin<byte,int> right){fixed(int* a=left,b=right){return *a;}}", "int M<T1, T2>(global::Pin<T1, int> left, global::Pin<T2, int> right)"),
            ("FixedBufferRead", "static int M(ref Inline<int> value){fixed(int* pointer=value.Values){return pointer[1];}}", "int M<T1>(ref global::Inline<T1> value)"),
            ("FixedBufferStore", "static void M(ref Inline<int> destination,int value){fixed(int* pointer=destination.Values){pointer[1]=value;}}", "void M<T1>(ref global::Inline<T1> destination, int value)"),
            ("FixedBufferDirectRead", "static int M(ref Inline<int> value)=>value.Values[1];", "int M<T1>(ref global::Inline<T1> value)"),
            ("FixedBufferDirectStore", "static void M(ref Inline<int> destination,int value){destination.Values[1]=value;}", "void M<T1>(ref global::Inline<T1> destination, int value)"),
            ("FixedBufferPointer", "static int M(Inline<int>* value)=>value->Values[1];", "int M<T1>(global::Inline<T1>* value) where T1 : unmanaged"),
            ("FixedBufferManagedOwner", "static int M(Owner<int> value){fixed(int* pointer=value.Buffer.Values){return pointer[0];}}", "int M<T1>(global::Owner<T1> value)"),
            ("FixedAddressField", "static int M(Owner<int> value){fixed(int* pointer=&value.Value){return *pointer;}}", "int M<T1>(global::Owner<T1> value)"),
            ("FixedString", "static char M(string value,int ignored){fixed(char* pointer=value){return *pointer;}}", "char M<T1>(string value, T1 ignored)"),
            ("FixedSpan", "static int M(System.Span<int> value,int ignored){fixed(int* pointer=value){return *pointer;}}", "int M<T1>(global::System.Span<int> value, T1 ignored)"),
            ("FixedReadonlySpan", "static int M(System.ReadOnlySpan<int> value,int ignored){fixed(int* pointer=value){return *pointer;}}", "int M<T1>(global::System.ReadOnlySpan<int> value, T1 ignored)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(support + "unsafe class Cases{" + test.Source + "}", cancellationToken: cancellationToken)]);
            compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true));
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
        var helper = CSharpSyntaxTree.ParseText("public class PlainPin<TTag,TValue>{public TValue Value=default!;}public static class Helpers{public static ref int GetPinnableReference(this PlainPin<int,int> value)=>ref value.Value;}", path: "PinHelper.cs", cancellationToken: cancellationToken);
        var caller = CSharpSyntaxTree.ParseText("public unsafe static class Caller{public static int Read(PlainPin<int,int> value){fixed(int* pointer=value){return *pointer;}}}", path: "PinCaller.cs", cancellationToken: cancellationToken);
        var shared = Program.Compile([helper, caller]);
        shared = shared.WithOptions(shared.Options.WithAllowUnsafe(true));
        var inferredHelper = Program.Results(shared, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "GetPinnableReference"; });
        if (inferredHelper.Status != "Suggestion")
        { throw new InvalidOperationException("A supplied implicit pinning helper must retain its instantiated signature: " + inferredHelper); }
        Program.Log.Info("PASS FixedCrossFileGeneralizedExtensionHelper");
        string[] uninferrableCallers =
        [
            "static bool M(int[] values){fixed(void* pointer=values){return pointer==null;}}static bool Use()=>M(null);",
            "static int M(Pin<int,int> value){fixed(int* pointer=value){return pointer==null?-1:*pointer;}}static int Use()=>M(null);"
        ];
        foreach (var source in uninferrableCallers)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(support + "unsafe class Cases{" + source + "}", cancellationToken: cancellationToken)]);
            compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true));
            if (compilation.GetDiagnostics(cancellationToken).Any(Inference.IsCompilationError))
            { throw new InvalidOperationException("The original null caller must compile."); }
            var result = Program.Results(compilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            if (result.Status != "Rejected")
            { throw new InvalidOperationException("An uninferrable null caller must reject the candidate: " + result); }
        }
        Program.Log.Info("PASS FixedNullCallersRequireInferableTypeArguments");
        return tests.Length + 1 + uninferrableCallers.Length + await PinningRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
