using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class UnsafeRuntimeChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            public struct Cell<T>{public T Value;}
            public struct Phantom<T>{public int Value;}
            public unsafe static class UnsafeProbe {
                public static delegate*<int,int> ContextPointer(delegate*<int,int> pointer,int tag)=>pointer;
                public static int ContextDefaultPointer(delegate*<int,int> pointer,int tag)=>pointer(default);
                public static delegate*<ref int,ref int> ContextRefPointer(delegate*<ref int,ref int> pointer,int tag)=>pointer;
                public static delegate* unmanaged[Cdecl]<int,int> ContextNativePointer(delegate* unmanaged[Cdecl]<int,int> pointer,int tag)=>pointer;
                public static int Read(int* value)=>*value;
                public static int* Store(int* destination,int value){*destination=value;return destination;}
                public static int* Offset(int* value,int offset)=>value+offset;
                public static long Distance(int* left,int* right)=>left-right;
                public static int Index(int* value,int index)=>value[index];
                public static bool IsNull(int* value)=>value==null;
                public static int** Nested(int** value)=>value;
                public static int Field(Cell<int>* value)=>value->Value;
                public static int FieldStore(Cell<int>* destination,int value){destination->Value=value;return destination->Value;}
                public static int* Increment(int* value){value++;return value;}
                public static int Stack(int value){var buffer=stackalloc[]{value,default};return buffer[0];}
                public static Phantom<int> PhantomStack(Phantom<int> value){var buffer=stackalloc[]{value};return buffer[0];}
                public static int Address(int value){var pointer=&value;return *pointer;}
                public static int CallPointer(delegate*<int,int> pointer,int value)=>pointer(value);
                public static int DefaultPointer(delegate*<int,int> pointer)=>pointer(default);
                public static ref int BorrowPointer(delegate*<ref int,ref int> pointer,ref int value)=>ref pointer(ref value);
                public static int StorePointerResult(delegate*<ref int,ref int> pointer,ref int value,int replacement){pointer(ref value)=replacement;return value;}
                public static int OutPointer(delegate*<out int,void> pointer){pointer(out var value);return value;}
                public static ref readonly int ReadonlyPointer(delegate*<in int,ref readonly int> pointer,in int value)=>ref pointer(in value);
                public static int NativePointer(delegate* unmanaged[Cdecl]<int,int> pointer,int value)=>pointer(value);
                public static delegate*<int,int> ChoosePointer(delegate*<int,int> pointer,bool choose)=>choose?&Echo:pointer;
                private static T Echo<T>(T value)=>value;
            }
            public unsafe static class OriginalUnsafeRunner {
                private static int Echo(int value)=>value;
                private static ref int Borrow(ref int value)=>ref value;
                private static ref readonly int Readonly(in int value)=>ref value;
                private static void Out(out int value)=>value=61;
                [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs=new[]{typeof(System.Runtime.CompilerServices.CallConvCdecl)})]
                private static int Native(int value)=>value+1;
                public static bool Run(){
                    var contextual=UnsafeProbe.ContextPointer(&Echo,0);delegate*<int,int> contextExpected=&Echo;
                    if(contextual!=contextExpected||contextual(5)!=5||UnsafeProbe.ContextDefaultPointer(&Echo,0)!=0)return false;
                    int contextInput=7;var contextualRef=UnsafeProbe.ContextRefPointer(&Borrow,0);contextualRef(ref contextInput)=17;
                    if(contextInput!=17)return false;
                    var contextualNative=UnsafeProbe.ContextNativePointer(&Native,0);if(contextualNative(5)!=6)return false;
                    delegate*<int,int> echo=&Echo;delegate*<out int,void> copy=&Out;
                    int* values=stackalloc int[]{11,17,23,31};int* absent=null;
                    if(UnsafeProbe.Read(values)!=11||UnsafeProbe.Index(values,2)!=23||!UnsafeProbe.IsNull(absent))return false;
                    if(UnsafeProbe.Store(values,41)!=values||values[0]!=41||UnsafeProbe.Offset(values,2)!=values+2||UnsafeProbe.Distance(values+3,values)!=3||UnsafeProbe.Increment(values)!=values+1)return false;
                    var pointer=values;if(UnsafeProbe.Nested(&pointer)!=&pointer)return false;
                    var cell=new Cell<int>{Value=37};if(UnsafeProbe.Field(&cell)!=37||UnsafeProbe.FieldStore(&cell,43)!=43||cell.Value!=43)return false;
                    int item=67;UnsafeProbe.BorrowPointer(&Borrow,ref item)=71;
                    if(item!=71||UnsafeProbe.StorePointerResult(&Borrow,ref item,73)!=73||item!=73||UnsafeProbe.ReadonlyPointer(&Readonly,in item)!=73)return false;
                    if(UnsafeProbe.CallPointer(&Echo,79)!=79||UnsafeProbe.DefaultPointer(echo)!=0||UnsafeProbe.OutPointer(copy)!=61||UnsafeProbe.NativePointer(&Native,83)!=84)return false;
                    var chosen=UnsafeProbe.ChoosePointer(echo,true);if(chosen(89)!=89)return false;
                    chosen=UnsafeProbe.ChoosePointer(echo,false);if(chosen!=echo||chosen(97)!=97)return false;
                    return UnsafeProbe.Stack(47)==47&&UnsafeProbe.Address(53)==53&&UnsafeProbe.PhantomStack(new Phantom<int>{Value=59}).Value==59;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "UnsafeRuntime", "UnsafeRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("UnsafeRuntime.cs", source);
        var original = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (original.GetType("OriginalUnsafeRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original unsafe runtime behavior failed."); }
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 28; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Unsafe analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "UnsafeProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 25)
        {
            var pending = (CSharpCompilation)(await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            throw new InvalidOperationException("Expected twenty-five unsafe fixes; got " + fixes + ": " + string.Join("; ", Program.Results(pending, cancellationToken)
                .Where(result => { ArgumentNullException.ThrowIfNull(result); return result.Status is "Rejected" or "Skipped"; })
                .Select(result => { ArgumentNullException.ThrowIfNull(result); return result.Method + ": " + result.Message; })));
        }
        document = document.Project.AddDocument("NewUnsafeDomains.cs", """
            public struct Datum {public long Number;public short Tag;}
            public unsafe static class NewUnsafeRunner {
                private static long contextSlot=1;
                private static long WideEcho(int value)=>value+100L;
                private static ref long WideBorrow(ref int value)=>ref contextSlot;
                [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs=new[]{typeof(System.Runtime.CompilerServices.CallConvCdecl)})]
                private static long WideNative(int value)=>value+200L;
                private static T Echo<T>(T value)=>value;
                private static long Length(string value)=>value.Length;
                private static string result="before";
                private static ref string Other(ref int value)=>ref result;
                private static bool IsNull(string value)=>value is null;
                private static ref T Borrow<T>(ref T value)=>ref value;
                private static ref readonly T Readonly<T>(in T value)=>ref value;
                private static void Out(out string value)=>value="from callback";
                [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs=new[]{typeof(System.Runtime.CompilerServices.CallConvCdecl)})]
                private static long Native(long value)=>value+3;
                public static bool Run(){
                    var contextual=UnsafeProbe.ContextPointer(&WideEcho,"tag");delegate*<int,long> contextExpected=&WideEcho;
                    if(contextual!=contextExpected||contextual(5)!=105L||UnsafeProbe.ContextDefaultPointer(&WideEcho,3m)!=100L)return false;
                    int refInput=5;var contextualRef=UnsafeProbe.ContextRefPointer(&WideBorrow,"tag");contextualRef(ref refInput)=123L;
                    if(refInput!=5||contextSlot!=123L)return false;
                    var contextualNative=UnsafeProbe.ContextNativePointer(&WideNative,"tag");if(contextualNative(5)!=205L)return false;
                    delegate*<string,bool> checker=&IsNull;delegate*<out string,void> copy=&Out;delegate*<Datum,Datum> echo=&Echo<Datum>;
                    byte* bytes=stackalloc byte[]{3,5,7,11};byte* absent=null;
                    if(UnsafeProbe.Read(bytes)!=(byte)3||UnsafeProbe.Store(bytes,(byte)13)!=bytes||bytes[0]!=13||UnsafeProbe.Index(bytes,2)!=7||UnsafeProbe.Offset(bytes,2)!=bytes+2||UnsafeProbe.Distance(bytes+3,bytes)!=3||UnsafeProbe.Increment(bytes)!=bytes+1||!UnsafeProbe.IsNull(absent))return false;
                    Datum* values=stackalloc Datum[2];values[0]=new Datum{Number=101,Tag=17};values[1]=new Datum{Number=103,Tag=19};
                    if(UnsafeProbe.Read(values).Number!=101||UnsafeProbe.Index(values,1).Tag!=19||UnsafeProbe.Distance(values+1,values)!=1||UnsafeProbe.Offset(values,1)!=values+1||UnsafeProbe.Increment(values)!=values+1)return false;
                    var pointer=values;if(UnsafeProbe.Nested(&pointer)!=&pointer)return false;
                    var cell=new Cell<Datum>{Value=values[0]};if(UnsafeProbe.Field(&cell).Number!=101||UnsafeProbe.FieldStore(&cell,values[1]).Tag!=19||cell.Value.Number!=103)return false;
                    if(UnsafeProbe.Stack(values[0]).Tag!=17||UnsafeProbe.Address(values[1]).Number!=103)return false;
                    var text=new string('x',3);var replacement=new string('y',4);
                    if(!object.ReferenceEquals(UnsafeProbe.CallPointer(&Echo<string>,text),text)||!UnsafeProbe.DefaultPointer(checker))return false;
                    if(UnsafeProbe.CallPointer(&Length,text)!=3L)return false;
                    int separate=127;UnsafeProbe.BorrowPointer(&Other,ref separate)=replacement;
                    if(separate!=127||!object.ReferenceEquals(result,replacement))return false;
                    UnsafeProbe.BorrowPointer(&Borrow<string>,ref text)=replacement;
                    if(!object.ReferenceEquals(text,replacement)||!object.ReferenceEquals(UnsafeProbe.ReadonlyPointer(&Readonly<string>,in text),replacement))return false;
                    if(!object.ReferenceEquals(UnsafeProbe.StorePointerResult(&Borrow<string>,ref text,replacement),replacement)||UnsafeProbe.OutPointer(copy)!="from callback")return false;
                    if(UnsafeProbe.NativePointer(&Native,113L)!=116L)return false;
                    var chosen=UnsafeProbe.ChoosePointer(echo,true);if(chosen(values[1]).Tag!=19)return false;
                    chosen=UnsafeProbe.ChoosePointer(echo,false);if(chosen!=echo||chosen(values[0]).Number!=101)return false;
                    return UnsafeProbe.PhantomStack(new Phantom<string>{Value=109}).Value==109;
                }
            }
            """);
        var changed = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var after = Load(changed, cancellationToken);
        if (after.GetType("OriginalUnsafeRunner")!.GetMethod("Run")!.Invoke(null, null) is not true
            || after.GetType("NewUnsafeRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Unsafe fixes changed aliasing, storage, element stride or new-domain values."); }
        Program.Log.Info("PASS UnsafeCodeFixRuntimeAliasingStrideStorageAndNewDomains");
        Program.Log.Info("PASS FunctionPointerCodeFixDispatchRefAliasesNativeAbiAndNewDomains");
        var invalid = document.Project.AddDocument("ManagedDomain.cs", "public unsafe static class InvalidDomain{public static string Read(string* value)=>UnsafeProbe.Read<string>(value);}");
        var rejected = (await invalid.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (!rejected.GetDiagnostics(cancellationToken).Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS8377" && diagnostic.Severity == DiagnosticSeverity.Error; }))
        { throw new InvalidOperationException("Inferred unmanaged requirements must reject managed payloads."); }
        Program.Log.Info("PASS UnsafeGenericConstraintsRejectManagedDomains");
        return fixes + 3;
    }
    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var result = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!result.Success)
        { throw new InvalidOperationException("Unsafe runtime emission failed: " + string.Join("\n", result.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
