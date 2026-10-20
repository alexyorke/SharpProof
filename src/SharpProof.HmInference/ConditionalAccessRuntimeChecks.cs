using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class ConditionalAccessRuntimeChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            public static class ConditionalAudit{public static int Reads;}
            public interface IConditionalValue<T>{T Value{get;}}
            public struct ConditionalStruct<T>:IConditionalValue<T>{public T Stored;public T Value{get{ConditionalAudit.Reads++;return Stored;}}}
            public static class ConditionalExtension{public static T Read<T>(this IConditionalValue<T> value)=>value.Value;}
            public class ConditionalBox<T>{
                public T Value=default!;
                public ConditionalBox<T>? Next;
                public int Reads;
                public T Property{get{Reads++;return Value;}}
                public T Read(){Reads++;return Value;}
                public T Read(int tag){Reads++;return Value;}
                public T this[int index]{get{Reads++;return Value;}}
                public Task<T> ReadAsync(){Reads++;return Task.FromResult(Value);}
            }
            public static class ConditionalProbe{
                public static string ReferenceFallback(ConditionalBox<string>? x,string fallback)=>x?.Read()??fallback;
                public static int? Boxed(ConditionalStruct<int>? x,int ignored)=>x?.Read();
                public static int? Field(ConditionalBox<int>? x)=>x?.Value;
                public static string? Property(ConditionalBox<string>? x)=>x?.Property;
                public static int? Method(ConditionalBox<int>? x,Func<int> get)=>x?.Read(get());
                public static int? Index(ConditionalBox<int>? x,Func<int> get)=>x?[get()];
                public static string? Delegate(Func<int,string>? map,int value)=>map?.Invoke(value);
                public static void Action(Action<int>? sink,int value){sink?.Invoke(value);}
                public static int? Chain(ConditionalBox<int>? x)=>x?.Next?.Read();
                public static async Task<int> Async(ConditionalBox<int>? x,int fallback)=>await(x?.ReadAsync()??Task.FromResult(fallback));
            }
            public static class OriginalConditionalRunner{
                public static int Calls;
                public static bool Run(){
                    ConditionalAudit.Reads=0;
                    if(ConditionalProbe.Boxed((ConditionalStruct<int>?)null,0)!=null||ConditionalAudit.Reads!=0)return false;
                    if(ConditionalProbe.Boxed((ConditionalStruct<int>?)new ConditionalStruct<int>{Stored=19},0)!=19||ConditionalAudit.Reads!=1)return false;
                    var fallback=new string('x',2);var referenceBox=new ConditionalBox<string>{Value="left"};
                    if(!object.ReferenceEquals(ConditionalProbe.ReferenceFallback(null,fallback),fallback)||ConditionalProbe.ReferenceFallback(referenceBox,fallback)!="left"||referenceBox.Reads!=1)return false;
                    Calls=0;var box=new ConditionalBox<int>{Value=7};
                    if(ConditionalProbe.Field((ConditionalBox<int>?)null)!=null||ConditionalProbe.Field(box)!=7||ConditionalProbe.Property((ConditionalBox<string>?)null)!=null||ConditionalProbe.Property(new ConditionalBox<string>{Value="ok"})!="ok")return false;
                    if(ConditionalProbe.Method((ConditionalBox<int>?)null,()=>++Calls)!=null||ConditionalProbe.Index((ConditionalBox<int>?)null,()=>++Calls)!=null||Calls!=0||box.Reads!=0)return false;
                    if(ConditionalProbe.Method(box,()=>++Calls)!=7||ConditionalProbe.Index(box,()=>++Calls)!=7||Calls!=2||box.Reads!=2)return false;
                    if(ConditionalProbe.Delegate((Func<int,string>?)null,7)!=null||ConditionalProbe.Delegate(item=>{Calls++;return item.ToString();},7)!="7"||Calls!=3)return false;
                    ConditionalProbe.Action(null,7);ConditionalProbe.Action(item=>Calls+=item,7);if(Calls!=10)return false;
                    if(ConditionalProbe.Chain((ConditionalBox<int>?)null)!=null||ConditionalProbe.Chain(box)!=null)return false;
                    box.Next=new ConditionalBox<int>{Value=11};
                    if(ConditionalProbe.Chain(box)!=11||box.Next.Reads!=1||box.Reads!=2)return false;
                    return ConditionalProbe.Async(null,13).GetAwaiter().GetResult()==13&&ConditionalProbe.Async(box,13).GetAwaiter().GetResult()==7&&box.Reads==3;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ConditionalRuntime", "ConditionalRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("ConditionalRuntime.cs", source);
        Verify((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, false, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 11; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Conditional-access analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "ConditionalProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 10)
        { throw new InvalidOperationException("Expected ten conditional-access fixes; got " + fixes); }
        document = document.Project.AddDocument("NewConditionalDomains.cs", """
            public static class NewConditionalRunner{
                public static bool Run(){
                    ConditionalAudit.Reads=0;
                    if(ConditionalProbe.Boxed((ConditionalStruct<decimal>?)null,"tag")!=null||ConditionalAudit.Reads!=0)return false;
                    if(ConditionalProbe.Boxed((ConditionalStruct<decimal>?)new ConditionalStruct<decimal>{Stored=19m},"tag")!=19m||ConditionalAudit.Reads!=1)return false;
                    var box=new ConditionalBox<decimal>{Value=7m};
                    if(ConditionalProbe.Field(box)!=7m||ConditionalProbe.Field((ConditionalBox<decimal>?)null)!=null)return false;
                    var payload=new[]{"new"};var reference=new ConditionalBox<string[]>{Value=payload};
                    if(!object.ReferenceEquals(ConditionalProbe.Property(reference),payload))return false;
                    if(!object.ReferenceEquals(ConditionalProbe.ReferenceFallback(null,payload),payload)||!object.ReferenceEquals(ConditionalProbe.ReferenceFallback(reference,payload),payload))return false;
                    int calls=0;
                    if(ConditionalProbe.Method((ConditionalBox<decimal>?)null,()=>++calls)!=null||ConditionalProbe.Index((ConditionalBox<decimal>?)null,()=>++calls)!=null||calls!=0)return false;
                    if(ConditionalProbe.Method(box,()=>++calls)!=7m||ConditionalProbe.Index(box,()=>++calls)!=7m||calls!=2||box.Reads!=2)return false;
                    if(!object.ReferenceEquals(ConditionalProbe.Delegate((string[] item)=>item,payload),payload))return false;
                    ConditionalProbe.Action((string item)=>calls=item.Length,"tag");if(calls!=3)return false;
                    var chain=new ConditionalBox<long>{Next=new ConditionalBox<long>{Value=17L}};
                    if(ConditionalProbe.Chain(chain)!=17L||chain.Reads!=0||chain.Next.Reads!=1)return false;
                    return object.ReferenceEquals(ConditionalProbe.Async(reference,payload).GetAwaiter().GetResult(),payload)
                        &&object.ReferenceEquals(ConditionalProbe.Async(null,payload).GetAwaiter().GetResult(),payload);
                }
            }
            """);
        Verify((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, true, cancellationToken);
        Program.Log.Info("PASS ConditionalAccessFixesPreserveShortCircuitOrderIdentityLiftingAndNewDomains");
        return fixes + 2;
    }

    private static void Verify(Compilation compilation, bool includeNew, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Conditional-access emission failed: " + string.Join("\n", emitted.Diagnostics)); }
        var assembly = System.Reflection.Assembly.Load(image.ToArray());
        if (assembly.GetType("OriginalConditionalRunner")!.GetMethod("Run")!.Invoke(null, null) is not true
            || includeNew && assembly.GetType("NewConditionalRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Conditional-access runtime behavior changed."); }
    }
}
