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
            }
            public unsafe static class OriginalUnsafeRunner {
                public static bool Run(){
                    int* values=stackalloc int[]{11,17,23,31};int* absent=null;
                    if(UnsafeProbe.Read(values)!=11||UnsafeProbe.Index(values,2)!=23||!UnsafeProbe.IsNull(absent))return false;
                    if(UnsafeProbe.Store(values,41)!=values||values[0]!=41||UnsafeProbe.Offset(values,2)!=values+2||UnsafeProbe.Distance(values+3,values)!=3||UnsafeProbe.Increment(values)!=values+1)return false;
                    var pointer=values;if(UnsafeProbe.Nested(&pointer)!=&pointer)return false;
                    var cell=new Cell<int>{Value=37};if(UnsafeProbe.Field(&cell)!=37||UnsafeProbe.FieldStore(&cell,43)!=43||cell.Value!=43)return false;
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
        for (var round = 0; round < 16; round++)
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
        if (fixes != 13)
        { throw new InvalidOperationException("Expected thirteen unsafe fixes; got " + fixes); }
        document = document.Project.AddDocument("NewUnsafeDomains.cs", """
            public struct Datum {public long Number;public short Tag;}
            public unsafe static class NewUnsafeRunner {
                public static bool Run(){
                    byte* bytes=stackalloc byte[]{3,5,7,11};byte* absent=null;
                    if(UnsafeProbe.Read(bytes)!=(byte)3||UnsafeProbe.Store(bytes,(byte)13)!=bytes||bytes[0]!=13||UnsafeProbe.Index(bytes,2)!=7||UnsafeProbe.Offset(bytes,2)!=bytes+2||UnsafeProbe.Distance(bytes+3,bytes)!=3||UnsafeProbe.Increment(bytes)!=bytes+1||!UnsafeProbe.IsNull(absent))return false;
                    Datum* values=stackalloc Datum[2];values[0]=new Datum{Number=101,Tag=17};values[1]=new Datum{Number=103,Tag=19};
                    if(UnsafeProbe.Read(values).Number!=101||UnsafeProbe.Index(values,1).Tag!=19||UnsafeProbe.Distance(values+1,values)!=1||UnsafeProbe.Offset(values,1)!=values+1||UnsafeProbe.Increment(values)!=values+1)return false;
                    var pointer=values;if(UnsafeProbe.Nested(&pointer)!=&pointer)return false;
                    var cell=new Cell<Datum>{Value=values[0]};if(UnsafeProbe.Field(&cell).Number!=101||UnsafeProbe.FieldStore(&cell,values[1]).Tag!=19||cell.Value.Number!=103)return false;
                    if(UnsafeProbe.Stack(values[0]).Tag!=17||UnsafeProbe.Address(values[1]).Number!=103)return false;
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
        var invalid = document.Project.AddDocument("ManagedDomain.cs", "public unsafe static class InvalidDomain{public static string Read(string* value)=>UnsafeProbe.Read<string>(value);}");
        var rejected = (await invalid.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (!rejected.GetDiagnostics(cancellationToken).Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS8377" && diagnostic.Severity == DiagnosticSeverity.Error; }))
        { throw new InvalidOperationException("Inferred unmanaged requirements must reject managed payloads."); }
        Program.Log.Info("PASS UnsafeGenericConstraintsRejectManagedDomains");
        return fixes + 2;
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
