using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class UntypedCallerRuntimeChecks
{
    internal static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Threading.Tasks;
            public static class GroupSupport{
                public static Type? LastType;
                public static int Created;
                public static Receiver? LastReceiver;
                public static string Echo(int value)=>"int";
                public static string Echo(long value)=>"long";
                public static string GenericEcho<T>(T value){LastType=typeof(T);return "generic";}
                public static async Task<string?> AsyncEcho<T>(T value){LastType=typeof(T);await Task.Yield();return value is int number&&number==0?null:"async";}
                public static decimal Decimal(int value)=>value;
                public static async Task<string[]?> ArrayAsync(int value){await Task.Yield();return value==0?null:new[]{"new"};}
                public sealed class Receiver{public string Echo<T>(T value){LastType=typeof(T);return "instance";}}
                public static Receiver MakeReceiver(){Created++;var receiver=new Receiver();LastReceiver=receiver;return receiver;}
            }
            public static class ContextualProbe{
                public static Func<int,string> Group(Func<int,string> map,int tag)=>map;
                public static Func<int,string> GenericGroup(Func<int,string> map,int tag)=>map;
                public static Func<int,Task<string?>> AsyncGroup(Func<int,Task<string?>> map,int tag)=>map;
                public static System.Collections.Generic.List<Func<int,string>> GroupCollection(System.Collections.Generic.List<Func<int,string>> maps,int tag)=>maps;
                public static Func<int,string> InstanceGroup(Func<int,string> map,int tag)=>map;
                public static object? Shared(object? value,object? hint,object? tag,bool flag)=>flag?value:hint;
                public static object? SharedTuple((object? Value,object? Hint) pair,object? tag,bool flag)=>flag?pair.Value:pair.Hint;
                public static async Task<object?> SharedAsync(object? value,object? hint,object? tag,bool flag){await Task.Yield();return flag?value:hint;}
                public static object? SharedFree(object? value,object? hint,object? tag,int ignored,bool flag)=>flag?value:hint;
                public static (object? Value,int Count)? Optional((object? Value,int Count)? value,int tag)=>value;
                public static (object? Value,int Count) ConditionalTuple((object? Value,int Count) value,int tag)=>value;
                public static (object? Value,int Count) Tuple((object? Value,int Count) value,int tag)=>value;
                public static object? Conditional(object? value,int tag)=>value;
                public static object? Switch(object? value,int tag)=>value;
                public static int NullableCollection(System.Collections.Generic.List<object?> value,int tag)=>value.Count;
                public static Func<int,Task<(object? Value,int Count)>> TupleCallback(Func<int,Task<(object? Value,int Count)>> map,int tag)=>map;
                public static async Task<object?> Nullable(object? value,int tag){await Task.Yield();return value;}
                public static Func<int,Task<string?>> Callback(Func<int,Task<string?>> map,int tag)=>map;
                public static int Default(int value,int tag)=>value;
                public static int Collection(System.Collections.Generic.List<int> value,int tag)=>value.Count;
            }
            public static class OriginalContextualRunner{
                public static bool Run(){
                    var methodGroup=ContextualProbe.Group(GroupSupport.Echo,0);
                    if(methodGroup(3)!="int"||methodGroup.Method.GetParameters()[0].ParameterType!=typeof(int))return false;
                    var genericGroup=ContextualProbe.GenericGroup(GroupSupport.GenericEcho,0);
                    if(genericGroup(3)!="generic"||GroupSupport.LastType!=typeof(int)||genericGroup.Method.GetGenericArguments()[0]!=typeof(int))return false;
                    var asyncGroup=ContextualProbe.AsyncGroup(GroupSupport.AsyncEcho,0);
                    if(asyncGroup(3).GetAwaiter().GetResult()!="async"||GroupSupport.LastType!=typeof(int)||asyncGroup(0).GetAwaiter().GetResult()!=null)return false;
                    var collectionGroup=ContextualProbe.GroupCollection([GroupSupport.GenericEcho],0);
                    if(collectionGroup[0](3)!="generic"||GroupSupport.LastType!=typeof(int))return false;
                    GroupSupport.Created=0;
                    var instanceGroup=ContextualProbe.InstanceGroup(GroupSupport.MakeReceiver().Echo,0);
                    if(instanceGroup(3)!="instance"||GroupSupport.Created!=1||GroupSupport.LastType!=typeof(int)||!object.ReferenceEquals(instanceGroup.Target,GroupSupport.LastReceiver))return false;
                    object hint=new object();
                    if(ContextualProbe.Shared(null,(object?)hint,null,true)!=null||!object.ReferenceEquals(ContextualProbe.Shared(null,(object?)hint,null,false),hint))return false;
                    if(ContextualProbe.SharedTuple((null,(object?)hint),null,true)!=null||!object.ReferenceEquals(ContextualProbe.SharedTuple((null,(object?)hint),null,false),hint))return false;
                    if(ContextualProbe.SharedAsync(null,(object?)hint,null,true).GetAwaiter().GetResult()!=null||!object.ReferenceEquals(ContextualProbe.SharedAsync(null,(object?)hint,null,false).GetAwaiter().GetResult(),hint))return false;
                    if(ContextualProbe.SharedFree(null,(object?)hint,null,0,true)!=null||!object.ReferenceEquals(ContextualProbe.SharedFree(null,(object?)hint,null,0,false),hint))return false;
                    if(ContextualProbe.Nullable(null,0).GetAwaiter().GetResult()!=null)return false;
                    if(ContextualProbe.Callback(async value=>{await Task.Yield();return value==3?"ok":null;},0)(3).GetAwaiter().GetResult()!="ok")return false;
                    if(ContextualProbe.Callback(async value=>{await Task.Yield();return (string?)null;},0)(0).GetAwaiter().GetResult()!=null)return false;
                    var optional=ContextualProbe.Optional((null,3),0);
                    if(optional is not { } present||present.Count!=3||present.Value!=null||ContextualProbe.Optional(null,0)!=null)return false;
                    var selectionFlag=optional.HasValue;var selected=ContextualProbe.ConditionalTuple(selectionFlag?(null,3):(null,4),0);
                    if(selected.Value!=null||selected.Count!=3)return false;
                    var pair=ContextualProbe.Tuple((null,3),0);
                    if(pair.Value!=null||pair.Count!=3)return false;
                    foreach(var flag in new[]{true,false}){
                        if(ContextualProbe.Conditional(flag?null:default,0)!=null||ContextualProbe.Switch(flag switch{true=>null,false=>default},0)!=null)return false;
                    }
                    if(ContextualProbe.NullableCollection([null],0)!=1)return false;
                    var map=ContextualProbe.TupleCallback(async value=>{await Task.Yield();return (null,value);},0);
                    var mapped=map(7).GetAwaiter().GetResult();
                    if(mapped.Value!=null||mapped.Count!=7)return false;
                    return ContextualProbe.Default(default,0)==0&&ContextualProbe.Collection([],0)==0&&ContextualProbe.Collection(new(),0)==0;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ContextualRuntime", "ContextualRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("ContextualRuntime.cs", source);
        Verify((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, false, cancellationToken);
        var provider = new HmSignatureCodeFixProvider();
        var fixes = 0;
        for (var round = 0; round < 21; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "AD0001"; }))
            { throw new InvalidOperationException("Contextual caller analyzer failed."); }
            var diagnostic = diagnostics.FirstOrDefault(candidate =>
            {
                ArgumentNullException.ThrowIfNull(candidate);
                return candidate.Id == "HM0001" && root.FindNode(candidate.Location.SourceSpan).AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Parent is ClassDeclarationSyntax { Identifier.ValueText: "ContextualProbe" };
            });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            fixes++;
        }
        if (fixes != 20)
        { throw new InvalidOperationException("Expected twenty contextual caller fixes; got " + fixes); }
        document = document.Project.AddDocument("NewContextualDomains.cs", """
            public static class NewContextualRunner{
                public static bool Run(){
                    object value=new object();
                    var methodGroup=ContextualProbe.Group(GroupSupport.Decimal,"tag");
                    var genericGroup=ContextualProbe.GenericGroup(GroupSupport.Decimal,3m);
                    var asyncGroup=ContextualProbe.AsyncGroup(GroupSupport.ArrayAsync,"tag");
                    var collectionGroup=ContextualProbe.GroupCollection([GroupSupport.Decimal],3m);
                    var instanceGroup=ContextualProbe.InstanceGroup(GroupSupport.Decimal,"tag");
                    if(methodGroup(7)!=7m||genericGroup(7)!=7m||collectionGroup[0](7)!=7m||instanceGroup(7)!=7m)return false;
                    if(asyncGroup(7).GetAwaiter().GetResult()?.Length!=1||asyncGroup(0).GetAwaiter().GetResult()!=null)return false;
                    var payload=new[]{"new"};
                    var shared=ContextualProbe.Shared(null,payload,null,false);
                    var sharedTuple=ContextualProbe.SharedTuple((null,payload),null,false);
                    var sharedAsync=ContextualProbe.SharedAsync(null,payload,null,false).GetAwaiter().GetResult();
                    var sharedFree=ContextualProbe.SharedFree(null,payload,null,3m,false);
                    if(shared?.Length!=1||sharedTuple?.Length!=1||sharedAsync?.Length!=1||sharedFree?.Length!=1)return false;
                    if(!object.ReferenceEquals(shared,payload)||!object.ReferenceEquals(sharedTuple,payload)||!object.ReferenceEquals(sharedAsync,payload)||!object.ReferenceEquals(sharedFree,payload))return false;
                    if(!object.ReferenceEquals(ContextualProbe.Nullable(value,"tag").GetAwaiter().GetResult(),value))return false;
                    if(ContextualProbe.Nullable(null,3m).GetAwaiter().GetResult()!=null)return false;
                    var optional=ContextualProbe.Optional((value,3),"tag");
                    if(!object.ReferenceEquals(optional?.Value,value)||optional?.Count!=3||ContextualProbe.Optional(null,3m)!=null)return false;
                    var selected=ContextualProbe.ConditionalTuple((value,4),3m);
                    if(!object.ReferenceEquals(selected.Value,value)||selected.Count!=4)return false;
                    var pair=ContextualProbe.Tuple((value,3m),"tag");
                    if(!object.ReferenceEquals(pair.Value,value)||pair.Count!=3m)return false;
                    if(!object.ReferenceEquals(ContextualProbe.Conditional(value,3m),value)||ContextualProbe.Switch(null,"tag")!=null)return false;
                    if(ContextualProbe.NullableCollection([null,value],3m)!=2)return false;
                    var tupleMap=ContextualProbe.TupleCallback(async (int item)=>{await System.Threading.Tasks.Task.Yield();return ((object?)item,item);},"tag");
                    var mapped=tupleMap(7).GetAwaiter().GetResult();
                    if(mapped.Value is not int boxed||boxed!=7||mapped.Count!=7)return false;
                    var map=ContextualProbe.Callback(async (int value)=>{await System.Threading.Tasks.Task.Yield();return value==7?new[]{"new"}:null;},new object());
                    return map(7).GetAwaiter().GetResult()?[0]=="new"&&ContextualProbe.Default(4,"tag")==4&&ContextualProbe.Collection(new System.Collections.Generic.List<int>{1},3m)==1;
                }
            }
            """);
        Verify((await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!, true, cancellationToken);
        Program.Log.Info("PASS ContextualCallerCodeFixesPreserveNullableAsyncBehaviorAndFreeDomains");
        return fixes + 2;
    }

    private static void Verify(Compilation compilation, bool includeNew, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success || emitted.Diagnostics.Any(diagnostic => { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Warning; }))
        { throw new InvalidOperationException("Contextual caller emission failed: " + string.Join("\n", emitted.Diagnostics)); }
        var assembly = System.Reflection.Assembly.Load(image.ToArray());
        if (assembly.GetType("OriginalContextualRunner")!.GetMethod("Run")!.Invoke(null, null) is not true
            || includeNew && assembly.GetType("NewContextualRunner")!.GetMethod("Run")!.Invoke(null, null) is not true)
        { throw new InvalidOperationException("Contextual caller runtime behavior changed."); }
    }
}
