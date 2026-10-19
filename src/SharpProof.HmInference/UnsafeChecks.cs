using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class UnsafeChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        var tests = new (string Name, string Source, string Signature)[]
        {
            ("PointerIdentity", "static int* M(int* value)=>value;", "T1* M<T1>(T1* value) where T1 : unmanaged"),
            ("PointerRead", "static int M(int* value)=>*value;", "T1 M<T1>(T1* value) where T1 : unmanaged"),
            ("PointerStore", "static int* M(int* destination,int value){*destination=value;return destination;}", "T1* M<T1>(T1* destination, T1 value) where T1 : unmanaged"),
            ("PointerIndex", "static int M(int* value,int index)=>value[index];", "T1 M<T1>(T1* value, int index) where T1 : unmanaged"),
            ("PointerIndexStore", "static int M(int* value,int index,int replacement){value[index]=replacement;return value[index];}", "T1 M<T1>(T1* value, int index, T1 replacement) where T1 : unmanaged"),
            ("PointerOffset", "static int* M(int* value,int offset)=>value+offset;", "T1* M<T1>(T1* value, int offset) where T1 : unmanaged"),
            ("PointerDifference", "static long M(int* left,int* right)=>left-right;", "long M<T1>(T1* left, T1* right) where T1 : unmanaged"),
            ("PointerNull", "static bool M(int* value)=>value==null;", "bool M<T1>(T1* value) where T1 : unmanaged"),
            ("PointerArray", "static int*[] M(int*[] value)=>value;", "T1*[] M<T1>(T1*[] value) where T1 : unmanaged"),
            ("NestedPointer", "static int** M(int** value)=>value;", "T1** M<T1>(T1** value) where T1 : unmanaged"),
            ("VoidPointer", "static void* M(void* value,int ignored)=>value;", "void* M<T1>(void* value, T1 ignored)"),
            ("AddressLocal", "static int M(int value){var pointer=&value;return *pointer;}", "T1 M<T1>(T1 value) where T1 : unmanaged"),
            ("PointerStructPayload", "static Cell<int>* M(Cell<int>* value)=>value;", "global::Cell<T1>* M<T1>(global::Cell<T1>* value) where T1 : unmanaged"),
            ("PointerPhantomPayload", "static Phantom<int>* M(Phantom<int>* value)=>value;", "global::Phantom<T1>* M<T1>(global::Phantom<T1>* value)"),
            ("PointerTuple", "static (int,byte)* M((int,byte)* value)=>value;", "(T1, T2)* M<T1, T2>((T1, T2)* value) where T1 : unmanaged where T2 : unmanaged"),
            ("ImplicitStackAllocation", "static int M(int value){var buffer=stackalloc[]{value};return buffer[0];}", "T1 M<T1>(T1 value) where T1 : unmanaged"),
            ("ExplicitStackAllocation", "static int M(int value,int ignored){var buffer=stackalloc int[]{value};return buffer[0];}", "int M<T1>(int value, T1 ignored)"),
            ("PointerIncrement", "static int* M(int* value){value++;return value;}", "T1* M<T1>(T1* value) where T1 : unmanaged"),
            ("PointerCompound", "static int* M(int* value,int offset){value+=offset;return value;}", "T1* M<T1>(T1* value, int offset) where T1 : unmanaged"),
            ("PointerField", "static int M(Cell<int>* value)=>value->Value;", "T1 M<T1>(global::Cell<T1>* value) where T1 : unmanaged"),
            ("PointerProperty", "static int M(Cell<int>* value)=>value->Property;", "T1 M<T1>(global::Cell<T1>* value) where T1 : unmanaged"),
            ("PointerMemberCall", "static int M(Cell<int>* value)=>value->Read();", "T1 M<T1>(global::Cell<T1>* value) where T1 : unmanaged"),
            ("PointerFieldStore", "static int M(Cell<int>* destination,int value){destination->Value=value;return destination->Value;}", "T1 M<T1>(global::Cell<T1>* destination, T1 value) where T1 : unmanaged"),
            ("PointerManagedDomain", "static string* M(string* value,int ignored)=>value;", "string* M<T1>(string* value, T1 ignored)"),
            ("StackAllocationDefault", "static int M(int value){var buffer=stackalloc[]{value,default};return buffer[1];}", "T1 M<T1>(T1 value) where T1 : unmanaged"),
            ("StackAllocationStruct", "static Cell<int> M(Cell<int> value){var buffer=stackalloc[]{value};return buffer[0];}", "global::Cell<T1> M<T1>(global::Cell<T1> value) where T1 : unmanaged"),
            ("StackAllocationPhantom", "static Phantom<int> M(Phantom<int> value){var buffer=stackalloc[]{value};return buffer[0];}", "global::Phantom<T1> M<T1>(global::Phantom<T1> value)"),
            ("UnsafeBlock", "static int M(int value){unsafe{var pointer=&value;return *pointer;}}", "T1 M<T1>(T1 value) where T1 : unmanaged"),
            ("FixedArray", "static int M(int[] values,int ignored){fixed(int* pointer=values){return *pointer;}}", "int M<T1>(int[] values, T1 ignored)"),
            ("SizeOf", "static int M(int value)=>sizeof(int);", "int M<T1>(T1 value)"),            ("SpanStackAllocation", "static int M(int value,int ignored){System.Span<int> buffer=stackalloc[]{value};return buffer[0];}", "int M<T1>(int value, T1 ignored)")
        };
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var source = "public struct Cell<T>{public T Value;public T Read()=>Value;public T Property=>Value;}public struct Phantom<T>{public int Value;}unsafe class Cases{" + test.Source + "}";
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)]);
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
        return tests.Length + await UnsafeRuntimeChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
