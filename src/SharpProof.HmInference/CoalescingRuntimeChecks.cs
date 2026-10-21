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
            public readonly struct ConvertedBox<T>{
                public static int Conversions;public T Value{get;}
                public ConvertedBox(T value){Value=value;}
                public static implicit operator ConvertedBox<T>(T value){Conversions++;return new(value);}
            }
            public static class CoalesceProbe{
                public static object ConvertedReference(object? value,string fallback)=>value??fallback;
                public static object ConvertedSelection(string? value,object fallback)=>value??fallback;
                public static object ConvertedBoxing(int? value,object fallback)=>value??fallback;
                public static object ConvertedFallback(object? value,int fallback)=>value??fallback;
                public static object ConvertedAssignment(object? value,int fallback)=>value??=fallback;
                public static IEnumerable<int> ConvertedArray(int[]? value,IEnumerable<int> fallback)=>value??fallback;
                public static IEnumerable<int> ConvertedList(List<int>? value,IEnumerable<int> fallback)=>value??fallback;
                public static IEnumerable<object> ConvertedCovariance(IEnumerable<object>? value,string[] fallback)=>value??fallback;
                public static object ConvertedFactory(object? value,Func<int> fallback)=>value??fallback();
                public static long ConvertedNumeric(long? value,int fallback,int tag)=>value??fallback;
                public static ConvertedBox<int> ConvertedUser(ConvertedBox<int>? value,int fallback)=>value??fallback;
                public static ConvertedBox<int> ConvertedUserAssign(ConvertedBox<int>? value,int fallback)=>value??=fallback;
                public static async Task<object> ConvertedTask(Task<int?> value,object fallback)=>(await value)??fallback;
                public static async ValueTask<object> ConvertedValueTask(ValueTask<int?> value,object fallback)=>(await value)??fallback;
                public static async Task<object> ConvertedAssignTask(object? value,Task<int> fallback)=>value??=await fallback;
                public static async ValueTask<object> ConvertedAssignValueTask(object? value,ValueTask<int> fallback)=>value??=await fallback;
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
                        const string fallback="fallback";
                        if(!ReferenceEquals(CoalesceProbe.ConvertedReference(reference,fallback),missing?fallback:reference)
                            ||!ReferenceEquals(CoalesceProbe.ConvertedSelection(missing?null:fallback,marker),missing?marker:fallback))return false;
                        var selectedBox=CoalesceProbe.ConvertedBoxing(number,marker);
                        if(missing?!ReferenceEquals(selectedBox,marker):selectedBox is not int selectedInt||selectedInt!=7)return false;
                        foreach(var boxed in new[]{CoalesceProbe.ConvertedFallback(reference,7),CoalesceProbe.ConvertedAssignment(reference,7)})
                            if(missing?boxed is not int fallbackInt||fallbackInt!=7:!ReferenceEquals(boxed,marker))return false;
                        int calls=0;var fromFactory=CoalesceProbe.ConvertedFactory(reference,()=>{calls++;return 7;});
                        if(calls!=(missing?1:0)||(missing?fromFactory is not int factoryInt||factoryInt!=7:!ReferenceEquals(fromFactory,marker)))return false;
                        var factoryError=new InvalidOperationException("converted-fallback");Func<int> fail=()=>throw factoryError;
                        try{var result=CoalesceProbe.ConvertedFactory(reference,fail);if(missing||!ReferenceEquals(result,marker))return false;}
                        catch(InvalidOperationException error)when(missing&&ReferenceEquals(error,factoryError)){}
                        int[]? integers=missing?null:new[]{7};IEnumerable<int> integersFallback=new List<int>{8};
                        if(!ReferenceEquals(CoalesceProbe.ConvertedArray(integers,integersFallback),missing?integersFallback:integers)
                            ||!ReferenceEquals(CoalesceProbe.ConvertedList(list,integersFallback),missing?integersFallback:list))return false;
                        IEnumerable<object>? objects=missing?null:array;string[] labels={fallback};
                        if(!ReferenceEquals(CoalesceProbe.ConvertedCovariance(objects,labels),missing?labels:objects))return false;
                        foreach(bool assignment in new[]{false,true}){
                            ConvertedBox<int>.Conversions=0;ConvertedBox<int>? optional=missing?null:new ConvertedBox<int>(5);
                            var wrapped=assignment?CoalesceProbe.ConvertedUserAssign(optional,7):CoalesceProbe.ConvertedUser(optional,7);
                            if(wrapped.Value!=(missing?7:5)||ConvertedBox<int>.Conversions!=(missing?1:0))return false;
                        }
                        if(CoalesceProbe.ConvertedNumeric(null,-1,0)!=-1L||CoalesceProbe.ConvertedNumeric(long.MaxValue,-1,0)!=long.MaxValue)return false;
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
                        var convertedTasks=new[]{CoalesceProbe.ConvertedTask(valueGate.Task,marker),CoalesceProbe.ConvertedValueTask(new ValueTask<int?>(valueGate.Task),marker).AsTask()};
                        foreach(var task in convertedTasks)if(task.IsCompleted)return false;
                        foreach(var task in referenceTasks)if(task.IsCompleted)return false;
                        foreach(var task in valueTasks)if(task.IsCompleted)return false;
                        var failure=new InvalidOperationException("coalesce-await");
                        if(outcome=="Fault"){referenceGate.SetException(failure);valueGate.SetException(failure);}
                        else if(outcome=="Cancel"){referenceGate.SetCanceled(canceled.Token);valueGate.SetCanceled(canceled.Token);}
                        else{referenceGate.SetResult(outcome=="Value"?marker:null);valueGate.SetResult(outcome=="Value"?7:null);}
                        foreach(var task in convertedTasks){
                            try{var result=await task.WaitAsync(cancellationToken);if(outcome is "Fault" or "Cancel"||(outcome=="Value"?result is not int item||item!=7:!ReferenceEquals(result,marker)))return false;}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        }
                        foreach(bool selected in new[]{false,true}){
                            var gate=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                            var pendingTasks=new[]{CoalesceProbe.ConvertedAssignTask(selected?marker:null,gate.Task),CoalesceProbe.ConvertedAssignValueTask(selected?marker:null,new ValueTask<int>(gate.Task)).AsTask()};
                            foreach(var task in pendingTasks)if(task.IsCompleted!=selected)return false;
                            if(outcome=="Fault")gate.SetException(failure);else if(outcome=="Cancel")gate.SetCanceled(canceled.Token);else gate.SetResult(7);
                            foreach(var task in pendingTasks){
                                try{var result=await task.WaitAsync(cancellationToken);if(selected?!ReferenceEquals(result,marker):outcome is "Fault" or "Cancel"||result is not int item||item!=7)return false;}
                                catch(InvalidOperationException error)when(!selected&&outcome=="Fault"&&ReferenceEquals(error,failure)){}
                                catch(OperationCanceledException error)when(!selected&&outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                            }
                        }
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
                        var fallback=new string('y',3);object? objectValue=reference;
                        if(!ReferenceEquals(CoalesceProbe.ConvertedReference(reference,fallback),missing?fallback:reference))return false;
                        IComparable other=fallback;var selectedBox=CoalesceProbe.ConvertedBoxing(number,other);
                        if(missing?!ReferenceEquals(selectedBox,other):selectedBox is not decimal selectedDecimal||selectedDecimal!=23m)return false;
                        foreach(var boxed in new[]{CoalesceProbe.ConvertedFallback(objectValue,23m),CoalesceProbe.ConvertedAssignment(objectValue,23m),CoalesceProbe.ConvertedFactory(objectValue,()=>23m)})
                            if(missing?boxed is not decimal fallbackDecimal||fallbackDecimal!=23m:!ReferenceEquals(boxed,reference))return false;
                        IEnumerable<decimal> fallbackNumbers=new List<decimal>{29m};
                        if(!ReferenceEquals(CoalesceProbe.ConvertedArray(array,fallbackNumbers),missing?fallbackNumbers:array)
                            ||!ReferenceEquals(CoalesceProbe.ConvertedList(list,fallbackNumbers),missing?fallbackNumbers:list))return false;
                        Uri[] references={tag};IEnumerable<object>? existing=missing?null:references;string[] labels={fallback};
                        if(!ReferenceEquals(CoalesceProbe.ConvertedCovariance(existing,labels),missing?labels:existing)
                            ||!ReferenceEquals(CoalesceProbe.ConvertedCovariance((IEnumerable<object>?)null,references),references))return false;
                        foreach(bool assignment in new[]{false,true}){
                            ConvertedBox<decimal>.Conversions=0;ConvertedBox<decimal>? optional=missing?null:new ConvertedBox<decimal>(29m);
                            var wrapped=assignment?CoalesceProbe.ConvertedUserAssign(optional,23m):CoalesceProbe.ConvertedUser(optional,23m);
                            if(wrapped.Value!=(missing?23m:29m)||ConvertedBox<decimal>.Conversions!=(missing?1:0))return false;
                        }
                        foreach(var pending in new[]{CoalesceProbe.ConvertedTask(Task.FromResult(number),other),CoalesceProbe.ConvertedValueTask(new ValueTask<decimal?>(number),other).AsTask()}){
                            var selected=await pending.WaitAsync(cancellationToken);
                            if(missing?!ReferenceEquals(selected,other):selected is not decimal item||item!=23m)return false;
                        }
                        foreach(var pending in new[]{CoalesceProbe.ConvertedAssignTask(objectValue,Task.FromResult(23m)),CoalesceProbe.ConvertedAssignValueTask(objectValue,new ValueTask<decimal>(23m)).AsTask()}){
                            var selected=await pending.WaitAsync(cancellationToken);
                            if(missing?selected is not decimal item||item!=23m:!ReferenceEquals(selected,reference))return false;
                        }
                        if(CoalesceProbe.ConvertedNumeric(null,-1,tag)!=-1L)return false;
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
        var fixtureErrors = original.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).Take(6).ToArray();
        if (fixtureErrors.Length != 0)
        {
            throw new InvalidOperationException("Coalescing fixture: " + string.Join("\n", fixtureErrors.Select(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.ToString(); })));
        }
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
            || symbol.GetMembers("ConvertedReference").OfType<IMethodSymbol>().Single().TypeParameters.Length != 2
            || !symbol.GetMembers("ConvertedBoxing").OfType<IMethodSymbol>().Single().TypeParameters[0].HasValueTypeConstraint
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
            Program.Log.Info("PASS CoalescingOriginalConversionsIdentityPropertyEvaluationSuspensionFaultAndCancellation");
            var newer = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newSource, cancellationToken: cancellationToken)).WithAssemblyName("NewCoalescing");
            var newAssembly = Load(newer, afterContext);
            var newPending = (Task<bool>)newAssembly.GetType("NewCoalesceRunner")!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
            if (!await newPending.WaitAsync(cancellationToken).ConfigureAwait(false))
            { throw new InvalidOperationException("Coalescing lost new reference/value/container/async domains."); }
            Program.Log.Info("PASS CoalescingNewDependentReferenceStructContainerUserConversionAndAsyncDomains");
            var invalid = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Bad{void Check(){_=CoalesceProbe.Reference<int,int>(1,0);_=CoalesceProbe.Value<string,int>(\"x\",0);_=CoalesceProbe.ConvertedCovariance<object,int>(null,new[]{1});_=CoalesceProbe.ConvertedReference<Uri,string>(null,\"x\");}}", cancellationToken: cancellationToken));
            if (invalid.GetDiagnostics(cancellationToken).Count(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id is "CS0452" or "CS0453" or "CS0311"; }) != 4)
            { throw new InvalidOperationException("Coalescing admitted invalid null domains."); }
            Program.Log.Info("PASS CoalescingRejectsInvalidReferenceAndStructDomains");
        }
        finally { beforeContext.Unload(); afterContext.Unload(); }
        return 4;
    }
}
