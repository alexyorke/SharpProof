using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class NullJoinRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            public static class NullProbe{
                public static object? Stored;
                public static object? RecursiveReference(int depth,object value,int tag){if(depth==0)return null;if(depth==1)return value;return RecursiveReference(depth-1,value,tag);}
                public static int? RecursiveValue(int depth,int value,int tag){if(depth==0)return null;if(depth==1)return value;return RecursiveValue(depth-1,value,tag);}
                public static object[]? RecursiveArray(int depth,object[] value,int tag){if(depth==0)return null;if(depth==1)return value;return RecursiveArray(depth-1,value,tag);}
                public static (object? text,int number) RecursivePair(int depth,object value,int number,int tag){if(depth==0)return(null,number);if(depth==1)return(value,number);return RecursivePair(depth-1,value,number,tag);}
                public static async Task<object?> RecursiveTaskReference(int depth,Task<object> value,int tag){if(depth==0)return null;if(depth==1)return await value;return await RecursiveTaskReference(depth-1,value,tag);}
                public static async Task<int?> RecursiveTaskValue(int depth,Task<int> value,int tag){if(depth==0)return null;if(depth==1)return await value;return await RecursiveTaskValue(depth-1,value,tag);}
                public static async ValueTask<object?> RecursiveValueReference(int depth,ValueTask<object> value,int tag){if(depth==0)return null;if(depth==1)return await value;return await RecursiveValueReference(depth-1,value,tag);}
                public static async ValueTask<int?> RecursiveValueStruct(int depth,ValueTask<int> value,int tag){if(depth==0)return null;if(depth==1)return await value;return await RecursiveValueStruct(depth-1,value,tag);}
                public static object? Reference(bool flag,object value,int tag){if(flag)return value;return null;}
                public static int? Value(bool flag,int value,int tag){if(flag)return value;return null;}
                public static object[]? Array(bool flag,object[] value,int tag)=>flag?value:null;
                public static List<int>? List(bool flag,List<int> value,int tag){if(flag)return value;return default;}
                public static Func<object>? Delegate(bool flag,Func<object> value,int tag){if(flag)return value;return null;}
                public static (object left,int right)? Pair(bool flag,(object left,int right) value,int tag){if(flag)return value;return null;}
                public static object? CallerTarget(bool flag,object value,int tag){Stored=value;if(flag)return value;return null;}
                public static object? BoxedCaller(int value)=>CallerTarget(true,value,0);
                public static object? Typed(bool flag,object value,int tag){if(flag)return value;return(object?)null;}
                public static int DefaultValue(bool flag,int value,int tag){if(flag)return value;return default;}
                public static unsafe int* Pointer(bool flag,int* value,int tag){if(flag)return value;return null;}
                public static unsafe delegate*<int,int> FunctionPointer(bool flag,delegate*<int,int> value,int tag){if(flag)return value;return null;}
                public static async Task<object?> AsyncReference(bool flag,Task<object> value,int tag){if(flag)return await value;return null;}
                public static async Task<int?> AsyncValue(bool flag,Task<int> value,int tag){if(flag)return await value;return null;}
                public static async ValueTask<object?> ValueReference(bool flag,ValueTask<object> value,int tag){if(flag)return await value;return null;}
                public static async ValueTask<int?> ValueStruct(bool flag,ValueTask<int> value,int tag){if(flag)return await value;return null;}
            }
            public static class OriginalRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    object marker=new Uri("https://example.invalid/null-joins");
                    object[] array={marker};var list=new List<int>{7};Func<object> factory=()=>marker;
                    if(!ReferenceEquals(NullProbe.Reference(true,marker,0),marker)||NullProbe.Reference(false,marker,0)!=null
                        ||NullProbe.Value(true,7,0)!=7||NullProbe.Value(false,7,0)!=null
                        ||!ReferenceEquals(NullProbe.Array(true,array,0),array)||NullProbe.Array(false,array,0)!=null
                        ||!ReferenceEquals(NullProbe.List(true,list,0),list)||NullProbe.List(false,list,0)!=null
                        ||!ReferenceEquals(NullProbe.Delegate(true,factory,0),factory)||NullProbe.Delegate(false,factory,0)!=null)return false;
                    var pair=NullProbe.Pair(true,(marker,7),0);
                    if(!pair.HasValue||!ReferenceEquals(pair.Value.left,marker)||pair.Value.right!=7||NullProbe.Pair(false,(marker,7),0)!=null)return false;
                    foreach(var depth in new[]{0,1,4}){
                        var recursivePair=NullProbe.RecursivePair(depth,marker,7,0);
                        if(depth==0?(NullProbe.RecursiveReference(depth,marker,0)!=null||NullProbe.RecursiveValue(depth,7,0)!=null
                            ||NullProbe.RecursiveArray(depth,array,0)!=null||recursivePair.text!=null)
                            :(!ReferenceEquals(NullProbe.RecursiveReference(depth,marker,0),marker)||NullProbe.RecursiveValue(depth,7,0)!=7
                            ||!ReferenceEquals(NullProbe.RecursiveArray(depth,array,0),array)||!ReferenceEquals(recursivePair.text,marker)))return false;
                        if(recursivePair.number!=7||await NullProbe.RecursiveTaskValue(depth,Task.FromResult(7),0).WaitAsync(cancellationToken)!=(depth==0?(int?)null:7)
                            ||await NullProbe.RecursiveValueStruct(depth,new ValueTask<int>(7),0).AsTask().WaitAsync(cancellationToken)!=(depth==0?(int?)null:7))return false;
                    }
                    var boxed=NullProbe.BoxedCaller(42);if(!ReferenceEquals(boxed,NullProbe.Stored))return false;
                    if(NullProbe.Typed(false,marker,0)!=null||NullProbe.DefaultValue(false,7,0)!=0||!Native())return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Success","Fault","Cancel","Unselected"}){
                        var gate=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var failure=new InvalidOperationException("root-null-await");bool selected=outcome!="Unselected";
                        var first=NullProbe.AsyncReference(selected,gate.Task,0);
                        var second=NullProbe.ValueReference(selected,new ValueTask<object>(gate.Task),0).AsTask();
                        var recursiveFirst=NullProbe.RecursiveTaskReference(selected?4:0,gate.Task,0);
                        var recursiveSecond=NullProbe.RecursiveValueReference(selected?4:0,new ValueTask<object>(gate.Task),0).AsTask();
                        if(first.IsCompleted==selected||second.IsCompleted==selected||recursiveFirst.IsCompleted==selected||recursiveSecond.IsCompleted==selected)return false;
                        if(outcome=="Success")gate.SetResult(marker);else if(outcome=="Fault")gate.SetException(failure);else if(outcome=="Cancel")gate.SetCanceled(canceled.Token);
                        foreach(var pending in new[]{first,second,recursiveFirst,recursiveSecond}){
                            try{var value=await pending.WaitAsync(cancellationToken);if(outcome=="Success"?!ReferenceEquals(value,marker):outcome!="Unselected"||value!=null)return false;}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        }
                    }
                    if(await NullProbe.AsyncValue(true,Task.FromResult(7),0).WaitAsync(cancellationToken)!=7
                        ||await NullProbe.AsyncValue(false,Task.FromResult(7),0).WaitAsync(cancellationToken)!=null
                        ||await NullProbe.ValueStruct(true,new ValueTask<int>(7),0).AsTask().WaitAsync(cancellationToken)!=7
                        ||await NullProbe.ValueStruct(false,new ValueTask<int>(7),0).AsTask().WaitAsync(cancellationToken)!=null)return false;
                    return true;
                }
                static int Echo(int value)=>value+1;
                static unsafe bool Native(){int value=7;int* pointer=&value;var chosen=NullProbe.Pointer(true,pointer,0);
                    var callback=NullProbe.FunctionPointer(true,(delegate*<int,int>)&Echo,0);
                    return chosen==pointer&&*chosen==7&&NullProbe.Pointer(false,pointer,0)==null&&callback(7)==8
                        &&NullProbe.FunctionPointer(false,(delegate*<int,int>)&Echo,0)==null;}
            }
            """;
        const string newSource = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            public static class NewRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    var tag=new Uri("https://example.invalid/new-null-domains");var payload=new string('x',2);
                    decimal[] array={23m};var list=new List<decimal>{23m};Func<decimal> factory=()=>23m;
                    if(!ReferenceEquals(NullProbe.Reference(true,payload,tag),payload)||NullProbe.Reference(false,payload,tag)!=null
                        ||NullProbe.Value(true,23m,tag)!=23m||NullProbe.Value(false,23m,tag)!=null
                        ||!ReferenceEquals(NullProbe.Array(true,array,tag),array)||NullProbe.Array(false,array,tag)!=null
                        ||!ReferenceEquals(NullProbe.List(true,list,tag),list)||NullProbe.List(false,list,tag)!=null
                        ||!ReferenceEquals(NullProbe.Delegate(true,factory,tag),factory)||NullProbe.Delegate(false,factory,tag)!=null)return false;
                    var pair=NullProbe.Pair(true,(payload,23m),tag);
                    if(!pair.HasValue||!ReferenceEquals(pair.Value.left,payload)||pair.Value.right!=23m||NullProbe.Pair(false,(payload,23m),tag)!=null)return false;
                    if(await NullProbe.AsyncReference(true,Task.FromResult(payload),tag).WaitAsync(cancellationToken)!=payload
                        ||await NullProbe.AsyncReference(false,Task.FromResult(payload),tag).WaitAsync(cancellationToken)!=null
                        ||await NullProbe.AsyncValue(true,Task.FromResult(23m),tag).WaitAsync(cancellationToken)!=23m
                        ||await NullProbe.AsyncValue(false,Task.FromResult(23m),tag).WaitAsync(cancellationToken)!=null
                        ||await NullProbe.ValueReference(true,new ValueTask<string>(payload),tag).AsTask().WaitAsync(cancellationToken)!=payload
                        ||await NullProbe.ValueReference(false,new ValueTask<string>(payload),tag).AsTask().WaitAsync(cancellationToken)!=null
                        ||await NullProbe.ValueStruct(true,new ValueTask<decimal>(23m),tag).AsTask().WaitAsync(cancellationToken)!=23m
                        ||await NullProbe.ValueStruct(false,new ValueTask<decimal>(23m),tag).AsTask().WaitAsync(cancellationToken)!=null)return false;
                    foreach(var depth in new[]{0,1,4}){
                        var recursivePair=NullProbe.RecursivePair(depth,payload,23m,tag);
                        if(depth==0?(NullProbe.RecursiveReference(depth,payload,tag)!=null||NullProbe.RecursiveValue(depth,23m,tag)!=null
                            ||NullProbe.RecursiveArray(depth,array,tag)!=null||recursivePair.text!=null)
                            :(!ReferenceEquals(NullProbe.RecursiveReference(depth,payload,tag),payload)||NullProbe.RecursiveValue(depth,23m,tag)!=23m
                            ||!ReferenceEquals(NullProbe.RecursiveArray(depth,array,tag),array)||!ReferenceEquals(recursivePair.text,payload)))return false;
                        if(recursivePair.number!=23m||await NullProbe.RecursiveTaskReference(depth,Task.FromResult(payload),tag).WaitAsync(cancellationToken)!=(depth==0?null:payload)
                            ||await NullProbe.RecursiveValueReference(depth,new ValueTask<string>(payload),tag).AsTask().WaitAsync(cancellationToken)!=(depth==0?null:payload)
                            ||await NullProbe.RecursiveTaskValue(depth,Task.FromResult(23m),tag).WaitAsync(cancellationToken)!=(depth==0?(decimal?)null:23m)
                            ||await NullProbe.RecursiveValueStruct(depth,new ValueTask<decimal>(23m),tag).AsTask().WaitAsync(cancellationToken)!=(depth==0?(decimal?)null:23m))return false;
                    }
                    return Native(tag);
                }
                static string Echo(string value)=>value;
                static unsafe bool Native(Uri tag){byte value=7;byte* pointer=&value;
                    var selected=NullProbe.Pointer(true,pointer,tag);delegate*<string,string> callback=&Echo;
                    var chosen=NullProbe.FunctionPointer(true,callback,tag);
                    return selected==pointer&&*selected==7&&NullProbe.Pointer(false,pointer,tag)==null&&chosen("new")=="new"
                        &&NullProbe.FunctionPointer(false,callback,tag)==null;}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "NullJoins", "NullJoins", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Joins.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var symbol = changed.GetTypeByMetadataName("NullProbe")!;
        if (!symbol.GetMembers("Reference").OfType<IMethodSymbol>().Single().TypeParameters[0].HasReferenceTypeConstraint
            || !symbol.GetMembers("Value").OfType<IMethodSymbol>().Single().TypeParameters[0].HasValueTypeConstraint
            || symbol.GetMembers("Array").OfType<IMethodSymbol>().Single().TypeParameters[0].HasReferenceTypeConstraint
            || symbol.GetMembers("CallerTarget").OfType<IMethodSymbol>().Single().Parameters[1].Type.SpecialType != SpecialType.System_Object
            || symbol.GetMembers("Typed").OfType<IMethodSymbol>().Single().Parameters[1].Type.SpecialType != SpecialType.System_Object
            || symbol.GetMembers("DefaultValue").OfType<IMethodSymbol>().Single().Parameters[1].Type.SpecialType != SpecialType.System_Int32)
        { throw new InvalidOperationException("Null join Fix All lost constraints or fixed/caller endpoints."); }
        Program.Log.Info("PASS NullJoinFixAllConstraintsAndOriginalCallerTypedDefaultEndpoints");
        var beforeContext = new System.Runtime.Loader.AssemblyLoadContext("BeforeNullJoins", isCollectible: true);
        var afterContext = new System.Runtime.Loader.AssemblyLoadContext("AfterNullJoins", isCollectible: true);
        try
        {
            System.Reflection.Assembly Load(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Null join emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                return loadContext.LoadFromStream(image);
            }
            foreach (var (compilation, loadContext) in new[] { (original, beforeContext), (changed, afterContext) })
            {
                var assembly = Load(compilation, loadContext);
                var pending = (Task<bool>)assembly.GetType("OriginalRunner")!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
                if (!await pending.WaitAsync(cancellationToken).ConfigureAwait(false))
                { throw new InvalidOperationException("Null join changed original identities, values, native callbacks or async effects."); }
            }
            Program.Log.Info("PASS NullJoinOriginalIdentityBoxingValuesRecursionPointersCallbacksSuspensionFaultAndCancellation");
            var newer = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newSource, cancellationToken: cancellationToken)).WithAssemblyName("NewNullJoins");
            var newAssembly = Load(newer, afterContext);
            var newPending = (Task<bool>)newAssembly.GetType("NewRunner")!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
            if (!await newPending.WaitAsync(cancellationToken).ConfigureAwait(false))
            { throw new InvalidOperationException("Null join lost new generic null/value domains."); }
            Program.Log.Info("PASS NullJoinNewReferenceStructContainersTupleRecursiveNativeAndAsyncDomains");
            var invalid = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Bad{void Check(){_=NullProbe.Reference<int,int>(true,1,0);_=NullProbe.Value<string,int>(true,\"x\",0);}}", cancellationToken: cancellationToken));
            if (invalid.GetDiagnostics(cancellationToken).Count(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id is "CS0452" or "CS0453"; }) != 2)
            { throw new InvalidOperationException("Null join admitted invalid reference/value result domains."); }
            Program.Log.Info("PASS NullJoinRejectsInvalidReferenceAndStructDomains");
        }
        finally { beforeContext.Unload(); afterContext.Unload(); }
        return 4;
    }
}
