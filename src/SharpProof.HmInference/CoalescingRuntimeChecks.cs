using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class CoalescingRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            public sealed class PropertyStore{
                public object? Value;public int Gets,Sets,Receivers;
                public PropertyStore Self(){Receivers++;return this;}
                public object? Slot{get{Gets++;return Value;}set{Sets++;Value=value;}}
            }
            public static class CoalesceProbe{
                public static object? Reference(object? value,int tag)=>value??null;
                public static object? ReferenceDefault(object? value,int tag)=>value??default;
                public static int? Value(int? value,int tag)=>value??null;
                public static object? AssignReference(object? value,int tag)=>value??=null;
                public static int? AssignValue(int? value,int tag)=>value??=null;
                public static object[]? Array(object[]? value,int tag)=>value??null;
                public static List<int>? List(List<int>? value,int tag)=>value??=default;
                public static Func<object>? Delegate(Func<object>? value,int tag)=>value??null;
                public static (object text,int number)? Pair((object text,int number)? value,int tag)=>value??=null;
                public static object? Chain(object? value,object? fallback,int tag)=>value??fallback??null;
                public static object? InferredChain(object? value,object? fallback,int tag)=>value??fallback??null;
                public static int ScalarDefault(int? value,int tag)=>value??default;
                public static int? AssignScalarDefault(int? value,int tag)=>value??=default;
                public static object? Typed(object? value,int tag)=>value??(object?)null;
                public static object? Storage(PropertyStore store,int tag)=>store.Self().Slot??=null;
                public static async Task<object?> AwaitReference(Task<object?> value,int tag)=>(await value)??null;
                public static async Task<int?> AwaitValue(Task<int?> value,int tag)=>(await value)??null;
                public static async ValueTask<object?> AwaitValueReference(ValueTask<object?> value,int tag)=>(await value)??null;
                public static async ValueTask<int?> AwaitValueStruct(ValueTask<int?> value,int tag)=>(await value)??null;
                public static async Task<object?> AsyncAssignReference(object? value,int tag){await Task.Yield();return value??=null;}
                public static async ValueTask<int?> AsyncAssignValue(int? value,int tag){await Task.Yield();return value??=null;}
            }
            public static class OriginalCoalesceRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    object marker=new Uri("https://example.invalid/coalescing");
                    foreach(bool missing in new[]{false,true}){
                        object? reference=missing?null:marker;int? number=missing?null:7;
                        object[]? array=missing?null:new[]{marker};List<int>? list=missing?null:new(){7};Func<object>? factory=missing?null:()=>marker;
                        (object text,int number)? pair=missing?null:(marker,7);
                        if(!ReferenceEquals(CoalesceProbe.Reference(reference,0),reference)||!ReferenceEquals(CoalesceProbe.ReferenceDefault(reference,0),reference)
                            ||!ReferenceEquals(CoalesceProbe.AssignReference(reference,0),reference)||CoalesceProbe.Value(number,0)!=number||CoalesceProbe.AssignValue(number,0)!=number
                            ||!ReferenceEquals(CoalesceProbe.Array(array,0),array)||!ReferenceEquals(CoalesceProbe.List(list,0),list)||!ReferenceEquals(CoalesceProbe.Delegate(factory,0),factory)
                            ||!ReferenceEquals(CoalesceProbe.Chain(null,reference,0),reference)||CoalesceProbe.Chain(null,null,0)!=null||!ReferenceEquals(CoalesceProbe.InferredChain(reference,reference,0),reference))return false;
                        var returned=CoalesceProbe.Pair(pair,0);
                        if(returned.HasValue!=pair.HasValue||returned.HasValue&&(!ReferenceEquals(returned.Value.text,marker)||returned.Value.number!=7))return false;
                        var store=new PropertyStore{Value=reference};
                        if(!ReferenceEquals(CoalesceProbe.Storage(store,0),reference)||store.Receivers!=1||store.Gets!=1||store.Sets!=(missing?1:0)||!ReferenceEquals(store.Value,reference))return false;
                        if(CoalesceProbe.ScalarDefault(number,0)!=(missing?0:7)||CoalesceProbe.AssignScalarDefault(number,0)!=(missing?0:7)
                            ||!ReferenceEquals(CoalesceProbe.Typed(reference,0),reference)
                            ||!ReferenceEquals(await CoalesceProbe.AsyncAssignReference(reference,0).WaitAsync(cancellationToken),reference)
                            ||await CoalesceProbe.AsyncAssignValue(number,0).AsTask().WaitAsync(cancellationToken)!=number)return false;
                    }
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Null","Fault","Cancel"}){
                        var referenceGate=new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var valueGate=new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var referenceTasks=new[]{CoalesceProbe.AwaitReference(referenceGate.Task,0),CoalesceProbe.AwaitValueReference(new ValueTask<object?>(referenceGate.Task),0).AsTask()};
                        var valueTasks=new[]{CoalesceProbe.AwaitValue(valueGate.Task,0),CoalesceProbe.AwaitValueStruct(new ValueTask<int?>(valueGate.Task),0).AsTask()};
                        foreach(var task in referenceTasks)if(task.IsCompleted)return false;
                        foreach(var task in valueTasks)if(task.IsCompleted)return false;
                        var failure=new InvalidOperationException("coalesce-await");
                        if(outcome=="Fault"){referenceGate.SetException(failure);valueGate.SetException(failure);}
                        else if(outcome=="Cancel"){referenceGate.SetCanceled(canceled.Token);valueGate.SetCanceled(canceled.Token);}
                        else{referenceGate.SetResult(outcome=="Value"?marker:null);valueGate.SetResult(outcome=="Value"?7:null);}
                        foreach(var task in referenceTasks){
                            try{var result=await task.WaitAsync(cancellationToken);if(outcome is "Fault" or "Cancel"||!ReferenceEquals(result,outcome=="Value"?marker:null))return false;}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        }
                        foreach(var task in valueTasks){
                            try{var result=await task.WaitAsync(cancellationToken);if(outcome is "Fault" or "Cancel"||result!=(outcome=="Value"?7:(int?)null))return false;}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        }
                    }
                    return true;
                }
            }
            """;
        const string newSource = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            public static class NewCoalesceRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    var tag=new Uri("https://example.invalid/new-coalescing");var marker=new string('x',2);
                    foreach(bool missing in new[]{false,true}){
                        string? reference=missing?null:marker;decimal? number=missing?null:23m;
                        decimal[]? array=missing?null:new[]{23m};List<decimal>? list=missing?null:new(){23m};Func<decimal>? factory=missing?null:()=>23m;
                        (string text,decimal number)? pair=missing?null:(marker,23m);
                        if(!ReferenceEquals(CoalesceProbe.Reference(reference,tag),reference)||!ReferenceEquals(CoalesceProbe.ReferenceDefault(reference,tag),reference)
                            ||!ReferenceEquals(CoalesceProbe.AssignReference(reference,tag),reference)||CoalesceProbe.Value(number,tag)!=number||CoalesceProbe.AssignValue(number,tag)!=number
                            ||!ReferenceEquals(CoalesceProbe.Array(array,tag),array)||!ReferenceEquals(CoalesceProbe.List(list,tag),list)||!ReferenceEquals(CoalesceProbe.Delegate(factory,tag),factory)
                            ||!ReferenceEquals(CoalesceProbe.Chain(null,reference,tag),reference)||CoalesceProbe.Chain(null,null,tag)!=null||CoalesceProbe.InferredChain<string,Uri>(null,null,tag)!=null||!ReferenceEquals(CoalesceProbe.InferredChain(reference,reference,tag),reference))return false;
                        var returned=CoalesceProbe.Pair(pair,tag);
                        if(returned.HasValue!=pair.HasValue||returned.HasValue&&(!ReferenceEquals(returned.Value.text,marker)||returned.Value.number!=23m))return false;
                        if(!ReferenceEquals(await CoalesceProbe.AwaitReference(Task.FromResult(reference),tag).WaitAsync(cancellationToken),reference)
                            ||!ReferenceEquals(await CoalesceProbe.AwaitValueReference(new ValueTask<string?>(reference),tag).AsTask().WaitAsync(cancellationToken),reference)
                            ||await CoalesceProbe.AwaitValue(Task.FromResult(number),tag).WaitAsync(cancellationToken)!=number
                            ||await CoalesceProbe.AwaitValueStruct(new ValueTask<decimal?>(number),tag).AsTask().WaitAsync(cancellationToken)!=number
                            ||!ReferenceEquals(await CoalesceProbe.AsyncAssignReference(reference,tag).WaitAsync(cancellationToken),reference)
                            ||await CoalesceProbe.AsyncAssignValue(number,tag).AsTask().WaitAsync(cancellationToken)!=number)return false;
                    }
                    return true;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Coalescing", "Coalescing", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Coalescing.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var symbol = changed.GetTypeByMetadataName("CoalesceProbe")!;
        if (!symbol.GetMembers("Reference").OfType<IMethodSymbol>().Single().TypeParameters[0].HasReferenceTypeConstraint
            || !symbol.GetMembers("Value").OfType<IMethodSymbol>().Single().TypeParameters[0].HasValueTypeConstraint
            || symbol.GetMembers("Chain").OfType<IMethodSymbol>().Single().Parameters[0].Type.SpecialType != SpecialType.System_Object
            || !symbol.GetMembers("InferredChain").OfType<IMethodSymbol>().Single().TypeParameters[0].HasReferenceTypeConstraint
            || symbol.GetMembers("Array").OfType<IMethodSymbol>().Single().TypeParameters[0].HasReferenceTypeConstraint
            || symbol.GetMembers("AssignScalarDefault").OfType<IMethodSymbol>().Single().Parameters[0].Type is not INamedTypeSymbol { TypeArguments: [{ SpecialType: SpecialType.System_Int32 }] }
            || symbol.GetMembers("Typed").OfType<IMethodSymbol>().Single().Parameters[0].Type.SpecialType != SpecialType.System_Object)
        { throw new InvalidOperationException("Coalescing Fix All lost null constraints or scalar/typed endpoints."); }
        Program.Log.Info("PASS CoalescingFixAllConstraintsAndDefaultEndpoints");
        var beforeContext = new System.Runtime.Loader.AssemblyLoadContext("BeforeCoalescing", isCollectible: true);
        var afterContext = new System.Runtime.Loader.AssemblyLoadContext("AfterCoalescing", isCollectible: true);
        try
        {
            System.Reflection.Assembly Load(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Coalescing emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                return loadContext.LoadFromStream(image);
            }
            foreach (var (compilation, loadContext) in new[] { (original, beforeContext), (changed, afterContext) })
            {
                var assembly = Load(compilation, loadContext);
                var pending = (Task<bool>)assembly.GetType("OriginalCoalesceRunner")!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
                if (!await pending.WaitAsync(cancellationToken).ConfigureAwait(false))
                { throw new InvalidOperationException("Coalescing changed original identity, values, property access or async effects."); }
            }
            Program.Log.Info("PASS CoalescingOriginalIdentityValuesPropertyEvaluationSuspensionFaultAndCancellation");
            var newer = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newSource, cancellationToken: cancellationToken)).WithAssemblyName("NewCoalescing");
            var newAssembly = Load(newer, afterContext);
            var newPending = (Task<bool>)newAssembly.GetType("NewCoalesceRunner")!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
            if (!await newPending.WaitAsync(cancellationToken).ConfigureAwait(false))
            { throw new InvalidOperationException("Coalescing lost new reference/value/container/async domains."); }
            Program.Log.Info("PASS CoalescingNewReferenceStructContainerTupleAndAsyncDomains");
            var invalid = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Bad{void Check(){_=CoalesceProbe.Reference<int,int>(1,0);_=CoalesceProbe.Value<string,int>(\"x\",0);}}", cancellationToken: cancellationToken));
            if (invalid.GetDiagnostics(cancellationToken).Count(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id is "CS0452" or "CS0453"; }) != 2)
            { throw new InvalidOperationException("Coalescing admitted invalid null domains."); }
            Program.Log.Info("PASS CoalescingRejectsInvalidReferenceAndStructDomains");
        }
        finally { beforeContext.Unload(); afterContext.Unload(); }
        return 4;
    }
}
