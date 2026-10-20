using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class UnsafeChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        var tests = new (string Name, string Source, string Signature)[]
        {
            ("FunctionPointerContextCaller", "static delegate*<int,int> M(delegate*<int,int> pointer,int tag)=>pointer;static int Echo(int value)=>value;static delegate*<int,int> Use()=>M(&Echo,0);", "delegate*<int, T1> M<T1, T2>(delegate*<int, T1> pointer, T2 tag)"),
            ("FunctionPointerGenericContextCaller", "static delegate*<int,int> M(delegate*<int,int> pointer,int tag)=>pointer;static T Echo<T>(T value)=>value;static delegate*<int,int> Use()=>M(&Echo,0);", "delegate*<int, T1> M<T1, T2>(delegate*<int, T1> pointer, T2 tag)"),
            ("FunctionPointerNoInputContextCaller", "static delegate*<int> M(delegate*<int> pointer,int tag)=>pointer;static int Echo()=>1;static delegate*<int> Use()=>M(&Echo,0);", "delegate*<T1> M<T1, T2>(delegate*<T1> pointer, T2 tag)"),
            ("FunctionPointerRefContextCaller", "static delegate*<ref int,ref int> M(delegate*<ref int,ref int> pointer,int tag)=>pointer;static ref int Echo(ref int value)=>ref value;static delegate*<ref int,ref int> Use()=>M(&Echo,0);", "delegate*<ref int, ref T1> M<T1, T2>(delegate*<ref int, ref T1> pointer, T2 tag)"),
            ("FunctionPointerTypedContextControl", "static delegate*<int,int> M(delegate*<int,int> pointer,int tag)=>pointer;static int Echo(int value)=>value;static delegate*<int,int> Use()=>M((delegate*<int,int>)&Echo,0);", "delegate*<T1, T2> M<T1, T2, T3>(delegate*<T1, T2> pointer, T3 tag)"),
            ("FunctionPointerContextOutputCaller", "static int M(delegate*<int,int> pointer)=>pointer(default);static int Echo(int value)=>value;static int Use()=>M(&Echo);", "T1 M<T1>(delegate*<int, T1> pointer)"),
            ("FunctionPointerNativeContextCaller", "static delegate* unmanaged[Cdecl]<int,int> M(delegate* unmanaged[Cdecl]<int,int> pointer,int tag)=>pointer;[System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs=new[]{typeof(System.Runtime.CompilerServices.CallConvCdecl)})]static int Native(int value)=>value;static delegate* unmanaged[Cdecl]<int,int> Use()=>M(&Native,0);", "delegate* unmanaged[Cdecl]<int, T1> M<T1, T2>(delegate* unmanaged[Cdecl]<int, T1> pointer, T2 tag)"),
            ("FunctionPointerNestedContextCaller", "static delegate*<delegate*<int,int>,int> M(delegate*<delegate*<int,int>,int> pointer,int tag)=>pointer;static int Echo(delegate*<int,int> value)=>value(1);static delegate*<delegate*<int,int>,int> Use()=>M(&Echo,0);", "delegate*<delegate*<int, int>, T1> M<T1, T2>(delegate*<delegate*<int, int>, T1> pointer, T2 tag)"),
            ("FunctionPointerIdentity", "static delegate*<int,int> M(delegate*<int,int> pointer)=>pointer;", "delegate*<T1, T2> M<T1, T2>(delegate*<T1, T2> pointer)"),
            ("FunctionPointerExistingAddressCaller", "static int M(int value)=>value;static delegate*<int,int> Use()=>&M;", "T1 M<T1>(T1 value)"),
            ("FunctionPointerOptionalReadonly", "static int M(delegate*<in int,int> pointer,int value)=>pointer(value);", "T2 M<T1, T2>(delegate*<in T1, T2> pointer, T1 value)"),
            ("FunctionPointerWidening", "static long M(delegate*<long,long> pointer,int value,int ignored)=>pointer(value);", "T1 M<T1, T2>(delegate*<long, T1> pointer, int value, T2 ignored)"),
            ("FunctionPointerDefaultArgument", "static int M(delegate*<int,int> pointer)=>pointer(default);", "T2 M<T1, T2>(delegate*<T1, T2> pointer)"),
            ("FunctionPointerPointerToPointer", "static delegate*<int,int>* M(delegate*<int,int>* pointer)=>pointer;", "delegate*<T1, T2>* M<T1, T2>(delegate*<T1, T2>* pointer)"),
            ("FunctionPointerGenericContainer", "static Container<int> M(Container<int> container,int value){container.Pointer(value);return container;}", "global::Container<T1> M<T1>(global::Container<T1> container, T1 value)"),
            ("FunctionPointerAddress", "static delegate*<int,int> M(delegate*<int,int> pointer,bool choose)=>choose?&Echo:pointer;static T Echo<T>(T value)=>value;", "delegate*<T1, T1> M<T1>(delegate*<T1, T1> pointer, bool choose)"),
            ("FunctionPointerExplicitAddress", "static delegate*<int,int> M(int ignored)=>&Echo<int>;static T Echo<T>(T value)=>value;", "delegate*<int, int> M<T1>(T1 ignored)"),
            ("FunctionPointerConstrainedAddress", "static delegate*<int,int> M(delegate*<int,int> pointer,bool choose)=>choose?&Echo:pointer;static T Echo<T>(T value) where T:unmanaged=>value;", "delegate*<T1, T1> M<T1>(delegate*<T1, T1> pointer, bool choose) where T1 : unmanaged"),
            ("FunctionPointerNull", "static bool M(delegate*<int,int> pointer)=>pointer==null;", "bool M<T1, T2>(delegate*<T1, T2> pointer)"),
            ("FunctionPointerNoArguments", "static int M(delegate*<int> pointer)=>pointer();", "T1 M<T1>(delegate*<T1> pointer)"),
            ("FunctionPointerRefStore", "static int M(delegate*<ref int,ref int> pointer,ref int value,int replacement){pointer(ref value)=replacement;return value;}", "T1 M<T1, T2>(delegate*<ref T1, ref T2> pointer, ref T1 value, T2 replacement)"),
            ("FunctionPointerReadonlyReturn", "static ref readonly int M(delegate*<in int,ref readonly int> pointer,in int value)=>ref pointer(in value);", "ref readonly T2 M<T1, T2>(delegate*<in T1, ref readonly T2> pointer, in T1 value)"),
            ("FunctionPointerRefReadonlyParameter", "static int M(delegate*<ref readonly int,int> pointer,in int value)=>pointer(in value);", "T2 M<T1, T2>(delegate*<ref readonly T1, T2> pointer, in T1 value)"),
            ("FunctionPointerDefaultUnmanaged", "static int M(delegate* unmanaged<int,int> pointer,int value)=>pointer(value);", "T2 M<T1, T2>(delegate* unmanaged<T1, T2> pointer, T1 value)"),
            ("FunctionPointerSuppressedTransition", "static int M(delegate* unmanaged[Cdecl,SuppressGCTransition]<int,int> pointer,int value)=>pointer(value);", "T2 M<T1, T2>(delegate* unmanaged[Cdecl, SuppressGCTransition]<T1, T2> pointer, T1 value)"),
            ("FunctionPointerCall", "static int M(delegate*<int,int> pointer,int value)=>pointer(value);", "T2 M<T1, T2>(delegate*<T1, T2> pointer, T1 value)"),
            ("FunctionPointerVoid", "static void M(delegate*<int,void> pointer,int value)=>pointer(value);", "void M<T1>(delegate*<T1, void> pointer, T1 value)"),
            ("FunctionPointerRef", "static int M(delegate*<ref int,ref int> pointer,ref int value)=>pointer(ref value);", "T2 M<T1, T2>(delegate*<ref T1, ref T2> pointer, ref T1 value)"),
            ("FunctionPointerOut", "static int M(delegate*<out int,void> pointer){pointer(out var value);return value;}", "T1 M<T1>(delegate*<out T1, void> pointer)"),
            ("FunctionPointerIn", "static int M(delegate*<in int,int> pointer,in int value)=>pointer(in value);", "T2 M<T1, T2>(delegate*<in T1, T2> pointer, in T1 value)"),
            ("FunctionPointerUnmanaged", "static int M(delegate* unmanaged[Cdecl]<int,int> pointer,int value)=>pointer(value);", "T2 M<T1, T2>(delegate* unmanaged[Cdecl]<T1, T2> pointer, T1 value)"),
            ("FunctionPointerDataPointer", "static int M(delegate*<int*,int> pointer,int* value)=>pointer(value);", "T2 M<T1, T2>(delegate*<T1*, T2> pointer, T1* value) where T1 : unmanaged"),
            ("FunctionPointerNested", "static int M(delegate*<delegate*<int,int>,int,int> pointer,delegate*<int,int> callback,int value)=>pointer(callback,value);", "T4 M<T1, T2, T3, T4>(delegate*<delegate*<T1, T2>, T3, T4> pointer, delegate*<T1, T2> callback, T3 value)"),
            ("FunctionPointerArray", "static delegate*<int,int>[] M(delegate*<int,int>[] pointers)=>pointers;", "delegate*<T1, T2>[] M<T1, T2>(delegate*<T1, T2>[] pointers)"),
            ("FunctionPointerNullable", "static string? M(delegate*<string?,string?> pointer,string? value)=>pointer(value);", "T2? M<T1, T2>(delegate*<T1?, T2?> pointer, T1? value) where T1 : class where T2 : class"),
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
            var source = "public unsafe struct Container<T>{public delegate*<T,T> Pointer;}public struct Cell<T>{public T Value;public T Read()=>Value;public T Property=>Value;}public struct Phantom<T>{public int Value;}unsafe class Cases{" + test.Source + "}";
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
        string[] uninferrableCallers =
        [
            "static int M(delegate*<out int,void> pointer){pointer(out var value);return value;}static void Copy(out int value)=>value=1;static int Use()=>M(&Copy);",
            "static delegate*<int,int> M(delegate*<int,int> pointer,bool choose)=>choose?&Echo:pointer;static T Echo<T>(T value)=>value;static int Copy(int value)=>value;static delegate*<int,int> Use()=>M(&Copy,true);"
        ];
        foreach (var source in uninferrableCallers)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("unsafe class Cases{" + source + "}", cancellationToken: cancellationToken)]);
            compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true));
            if (compilation.GetDiagnostics(cancellationToken).Any(Inference.IsCompilationError))
            { throw new InvalidOperationException("The original bare method-address caller must compile."); }
            var result = Program.Results(compilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            if (result.Status != "Rejected")
            { throw new InvalidOperationException("An uninferrable method-address caller must reject the candidate: " + result); }
        }
        Program.Log.Info("PASS FunctionPointerBareAddressCallersWithNoFreeComponentReject");
        return tests.Length + uninferrableCallers.Length + await UnsafeRuntimeChecks.Run(cancellationToken).ConfigureAwait(false)
            + await PinningChecks.Run(cancellationToken).ConfigureAwait(false);
    }
}
