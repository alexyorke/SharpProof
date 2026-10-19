using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class PinningRuntimeChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            public static class PinTrace {public static System.Collections.Generic.List<string> Events=new();public static void Compact()=>System.GC.Collect(2,System.GCCollectionMode.Forced,true,true);}
            public class RuntimePin<TTag,TValue>{public TValue Value;public RuntimePin(TValue value){Value=value;}public virtual ref TValue GetPinnableReference(){PinTrace.Events.Add("instance");return ref Value;}}
            public class RuntimeDerived<TTag,TValue>(TValue value):RuntimePin<TTag,TValue>(value){public override ref TValue GetPinnableReference(){PinTrace.Events.Add("derived");return ref Value;}}
            public class RuntimeReadonly<TTag,TValue>{public TValue Value;public ref readonly TValue GetPinnableReference(){PinTrace.Events.Add("readonly");return ref Value;}}
            public class RuntimeExtension<TTag,TValue>{public TValue Value;}
            public static class RuntimeExtensions{public static ref TValue GetPinnableReference<TTag,TValue>(this RuntimeExtension<TTag,TValue> value) where TValue:unmanaged{PinTrace.Events.Add("extension");return ref value.Value;}}
            public unsafe struct RuntimeBuffer<TTag>{public fixed int Values[4];public TTag Tag;}
            public class RuntimeOwner<TTag>{public RuntimeBuffer<TTag> Buffer;}
            public unsafe static class PinnedProbe {
                public static int Read(RuntimePin<int,int> value){fixed(int* pointer=value){PinTrace.Compact();return pointer==null?-1:*pointer;}}
                public static void Store(RuntimePin<int,int> destination,int value){fixed(int* pointer=destination){PinTrace.Compact();*pointer=value;}}
                public static int Readonly(RuntimeReadonly<int,int> value){fixed(int* pointer=value){PinTrace.Compact();return *pointer;}}
                public static int Extension(RuntimeExtension<int,int> value){fixed(int* pointer=value){PinTrace.Compact();return *pointer;}}
                public static bool Missing(int[] values){fixed(void* pointer=values){PinTrace.Compact();return pointer==null;}}
                public static bool Present(RuntimePin<int,int> value){fixed(void* pointer=value){PinTrace.Compact();return pointer!=null;}}
                public static int BufferRead(ref RuntimeBuffer<int> value){fixed(int* pointer=value.Values){PinTrace.Compact();return pointer[1];}}
                public static void BufferStore(ref RuntimeBuffer<int> destination,int value){fixed(int* pointer=destination.Values){PinTrace.Compact();pointer[1]=value;}}
                public static int DirectRead(ref RuntimeBuffer<int> value)=>value.Values[1];
                public static void DirectStore(ref RuntimeBuffer<int> destination,int value){destination.Values[1]=value;}
                public static int OwnerRead(RuntimeOwner<int> value){fixed(int* pointer=value.Buffer.Values){PinTrace.Compact();return pointer[1];}}
            }
            public unsafe static class OriginalPinnedRunner {
                public static bool Run(){
                    PinTrace.Events.Clear();var value=new RuntimePin<int,int>(11);RuntimePin<int,int> absent=null;int[] absentArray=null;
                    if(PinnedProbe.Read(value)!=11)return false;PinnedProbe.Store(value,13);if(value.Value!=13)return false;
                    if(PinnedProbe.Read(new RuntimeDerived<int,int>(17))!=17||PinnedProbe.Read(absent)!=-1)return false;
                    if(PinnedProbe.Readonly(new RuntimeReadonly<int,int>{Value=19})!=19||PinnedProbe.Extension(new RuntimeExtension<int,int>{Value=23})!=23)return false;
                    if(!PinnedProbe.Present(value)||PinnedProbe.Present(absent)||!PinnedProbe.Missing(absentArray)||!PinnedProbe.Missing(new int[0])||PinnedProbe.Missing(new[]{29}))return false;
                    RuntimeBuffer<int> buffer=default;PinnedProbe.BufferStore(ref buffer,31);if(PinnedProbe.BufferRead(ref buffer)!=31||PinnedProbe.DirectRead(ref buffer)!=31)return false;
                    PinnedProbe.DirectStore(ref buffer,37);if(PinnedProbe.BufferRead(ref buffer)!=37)return false;
                    var owner=new RuntimeOwner<int>();fixed(int* pointer=owner.Buffer.Values){pointer[1]=41;}if(PinnedProbe.OwnerRead(owner)!=41)return false;
                    return string.Join(",",PinTrace.Events)=="instance,instance,derived,readonly,extension,instance";
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "PinningRuntime", "PinningRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("PinningRuntime.cs", source);
        var before = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (before.GetType("OriginalPinnedRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Original pinning behavior failed."); }
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 14; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Pinning analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "PinnedProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 11)
        { throw new InvalidOperationException("Expected eleven pinning fixes; got " + fixes); }
        document = document.Project.AddDocument("NewPinnedDomains.cs", """
            public struct PinDatum{public long Number;public short Tag;}
            public unsafe static class NewPinnedRunner {
                public static bool Run(){
                    PinTrace.Events.Clear();var value=new RuntimePin<string,int>(43);
                    if(PinnedProbe.Read(value)!=43)return false;PinnedProbe.Store(value,47);if(value.Value!=47)return false;
                    if(PinnedProbe.Read(new RuntimeDerived<object,int>(53))!=53||PinnedProbe.Read<string>(null)!=-1)return false;
                    if(PinnedProbe.Readonly(new RuntimeReadonly<string,int>{Value=59})!=59||PinnedProbe.Extension(new RuntimeExtension<object,int>{Value=61})!=61)return false;
                    if(!PinnedProbe.Present(new RuntimePin<string,PinDatum>(new PinDatum{Number=67,Tag=71}))||PinnedProbe.Present<string,PinDatum>(null))return false;
                    if(!PinnedProbe.Missing<PinDatum>(null)||!PinnedProbe.Missing(new PinDatum[0])||PinnedProbe.Missing(new[]{new PinDatum{Number=73}}))return false;
                    RuntimeBuffer<string> buffer=default;buffer.Tag="retained";PinnedProbe.BufferStore(ref buffer,79);
                    if(PinnedProbe.BufferRead(ref buffer)!=79||PinnedProbe.DirectRead(ref buffer)!=79||buffer.Tag!="retained")return false;
                    PinnedProbe.DirectStore(ref buffer,83);if(PinnedProbe.BufferRead(ref buffer)!=83)return false;
                    var owner=new RuntimeOwner<string>();owner.Buffer.Tag="owner";fixed(int* pointer=owner.Buffer.Values){pointer[1]=89;}
                    if(PinnedProbe.OwnerRead(owner)!=89||owner.Buffer.Tag!="owner")return false;
                    return string.Join(",",PinTrace.Events)=="instance,instance,derived,readonly,extension,instance";
                }
            }
            """);
        var after = Load((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, cancellationToken);
        if (after.GetType("OriginalPinnedRunner")!.GetMethod("Run")!.Invoke(null, null) is not true
            || after.GetType("NewPinnedRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Pinning fixes changed storage, null/empty behavior, implicit calls or GC pinning."); }
        Program.Log.Info("PASS PinningCodeFixStorageNullEmptyDispatchCompactingGcAndNewDomains");
        var invalid = document.Project.AddDocument("InvalidPinnedDomain.cs", "public unsafe static class InvalidPinnedDomain{public static bool Run(string[] values)=>PinnedProbe.Missing(values);}");
        var rejected = (await invalid.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        if (!rejected.GetDiagnostics(cancellationToken).Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS8377" && diagnostic.Severity == DiagnosticSeverity.Error; }))
        { throw new InvalidOperationException("Void-pointer pinning must reject managed element domains."); }
        Program.Log.Info("PASS PinningGenericConstraintsRejectManagedElements");
        return fixes + 2;
    }

    private static System.Reflection.Assembly Load(Compilation compilation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException("Pinning runtime emission failed: " + string.Join("\n", emitted.Diagnostics)); }
        return System.Reflection.Assembly.Load(image.ToArray());
    }
}
