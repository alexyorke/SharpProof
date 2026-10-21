using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class DefaultValueRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
            public static class DefaultProbe{
                public static int First(IEnumerable<int> values){using var e=values.GetEnumerator();return e.MoveNext()?e.Current:default;}
                public static int Choose(bool flag,int value)=>flag?value:default;
                public static int Branch(bool flag,int value){if(flag)return value;return default;}
                public static Func<int> Callback(bool flag,int value)=>()=>flag?value:default;
                public static (int left,int right) Tuple(bool flag,(int left,int right) value)=>flag?value:default;
                public static IEnumerable<int> Iterator(int value){yield return value;yield return default;}
                public static async IAsyncEnumerable<int> AsyncIterator(int value){await Task.Yield();yield return value;yield return default;}
                public static async Task<int> Async(bool flag,Task<int> value){if(flag)return await value;return default;}
                public static async ValueTask<int> ValueAsync(bool flag,ValueTask<int> value){if(flag)return await value;return default;}
                public static int Coalesce(int? value)=>value??default;
                public static int? Assign(int? value)=>value??=default;
                public static object Box(bool flag,int value)=>flag?value:default;
                public static int Typed(bool flag,int value,int tag)=>flag?value:default(int);
            }
            public static class OriginalDefaultRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    foreach(bool flag in new[]{false,true}){
                        int expected=flag?7:0;
                        if(DefaultProbe.Choose(flag,7)!=expected||DefaultProbe.Branch(flag,7)!=expected||DefaultProbe.Callback(flag,7)()!=expected
                            ||DefaultProbe.Tuple(flag,(7,8))!=(flag?(7,8):(0,0))||DefaultProbe.Box(flag,7) is not int boxed||boxed!=expected)return false;
                        if(await DefaultProbe.Async(flag,Task.FromResult(7)).WaitAsync(cancellationToken)!=expected
                            ||await DefaultProbe.ValueAsync(flag,new ValueTask<int>(7)).AsTask().WaitAsync(cancellationToken)!=expected)return false;
                    }
                    if(DefaultProbe.First(Array.Empty<int>())!=0||DefaultProbe.First(new[]{7})!=7||!DefaultProbe.Iterator(7).SequenceEqual(new[]{7,0})||DefaultProbe.Coalesce((int?)null)!=0
                        ||DefaultProbe.Assign((int?)null) is not { } assigned||assigned!=0)return false;
                    var items=new List<int>();await foreach(var item in DefaultProbe.AsyncIterator(7).WithCancellation(cancellationToken))items.Add(item);
                    if(!items.SequenceEqual(new[]{7,0}))return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel"}){
                        var gate=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var pending=DefaultProbe.Async(true,gate.Task);
                        var valuePending=DefaultProbe.ValueAsync(true,new ValueTask<int>(gate.Task)).AsTask();
                        if(pending.IsCompleted||valuePending.IsCompleted||!DefaultProbe.Async(false,gate.Task).IsCompletedSuccessfully
                            ||!DefaultProbe.ValueAsync(false,new ValueTask<int>(gate.Task)).IsCompletedSuccessfully)return false;
                        var failure=new InvalidOperationException("default-await");
                        if(outcome=="Fault")gate.SetException(failure);else if(outcome=="Cancel")gate.SetCanceled(canceled.Token);else gate.SetResult(7);
                        foreach(var task in new[]{pending,valuePending}){
                            try{if(await task.WaitAsync(cancellationToken)!=7||outcome!="Value")return false;}
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
            using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
            public struct ConstructorProbe{public static int Calls;public int Value;public ConstructorProbe(){Calls++;Value=99;}}
            public static class NewDefaultRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    string text=new string('x',2);
                    if(DefaultProbe.First(Array.Empty<string>())!=null||!ReferenceEquals(DefaultProbe.First(new[]{text}),text)||!ReferenceEquals(DefaultProbe.Choose(true,text),text)||DefaultProbe.Choose(false,text)!=null
                        ||DefaultProbe.Branch(false,text)!=null||DefaultProbe.Callback(false,text)()!=null
                        ||!DefaultProbe.Iterator(text).SequenceEqual(new string?[]{text,null}))return false;
                    if(DefaultProbe.Choose(false,23m)!=0m||DefaultProbe.Choose(true,23m)!=23m
                        ||DefaultProbe.Choose(false,(decimal?)23m)!=null||DefaultProbe.Choose(true,(decimal?)23m)!=23m)return false;
                    var original=new ConstructorProbe();ConstructorProbe.Calls=0;
                    if(DefaultProbe.Choose(false,original).Value!=0||ConstructorProbe.Calls!=0)return false;
                    if(DefaultProbe.Tuple(false,(text,23m))!=(null,0m)||DefaultProbe.Tuple(true,(text,23m))!=(text,23m))return false;
                    if(DefaultProbe.Coalesce((decimal?)null)!=0m||DefaultProbe.Assign((decimal?)null) is not { } zero||zero!=0m
                        ||DefaultProbe.Box(false,23m) is not decimal boxed||boxed!=0m)return false;
                    if(await DefaultProbe.Async(false,Task.FromResult(text)).WaitAsync(cancellationToken)!=null
                        ||await DefaultProbe.Async(true,Task.FromResult(text)).WaitAsync(cancellationToken)!=text
                        ||await DefaultProbe.ValueAsync(false,new ValueTask<decimal>(23m)).AsTask().WaitAsync(cancellationToken)!=0m
                        ||await DefaultProbe.Async(false,Task.FromResult((decimal?)23m)).WaitAsync(cancellationToken)!=null)return false;
                    var items=new List<string?>();await foreach(var item in DefaultProbe.AsyncIterator(text).WithCancellation(cancellationToken))items.Add(item);
                    return items.SequenceEqual(new string?[]{text,null});
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Defaults", "Defaults", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Defaults.cs", source);
        var originalCompilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var symbol = changed.GetTypeByMetadataName("DefaultProbe")!;
        var choose = symbol.GetMembers("Choose").OfType<IMethodSymbol>().Single();
        if (choose.Parameters[1].Type is not ITypeParameterSymbol { HasValueTypeConstraint: false, HasReferenceTypeConstraint: false }
            || choose.ReturnNullableAnnotation != NullableAnnotation.Annotated
            || symbol.GetMembers("Coalesce").OfType<IMethodSymbol>().Single().TypeParameters is not [{ HasValueTypeConstraint: true }]
            || symbol.GetMembers("Typed").OfType<IMethodSymbol>().Single().Parameters[1].Type.SpecialType != SpecialType.System_Int32)
        { throw new InvalidOperationException("Default Fix All lost annotations, struct constraints or typed endpoints: " + string.Join("; ", symbol.GetMembers().OfType<IMethodSymbol>().Select(method => { ArgumentNullException.ThrowIfNull(method); return method.ToDisplayString(); }))); }
        Program.Log.Info("PASS DefaultFixAllAnnotationsConstraintsAndTypedEndpoints");
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("DefaultValues", isCollectible: true);
        try
        {
            async Task Execute(Compilation compilation, string runner)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentException.ThrowIfNullOrEmpty(runner);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Default emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                var assembly = loadContext.LoadFromStream(image);
                var pending = (Task<bool>)assembly.GetType(runner)!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
                if (!await pending.WaitAsync(cancellationToken).ConfigureAwait(false))
                { throw new InvalidOperationException("Default runtime effects: " + runner); }
            }
            await Execute(originalCompilation.WithAssemblyName("BeforeDefaults"), "OriginalDefaultRunner").ConfigureAwait(false);
            await Execute(changed.WithAssemblyName("AfterDefaults"), "OriginalDefaultRunner").ConfigureAwait(false);
            Program.Log.Info("PASS DefaultOriginalValuesIteratorsSuspensionFaultAndCancellation");
            await Execute(changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newSource, cancellationToken: cancellationToken)).WithAssemblyName("NewDefaults"), "NewDefaultRunner").ConfigureAwait(false);
            Program.Log.Info("PASS DefaultNewReferenceNullableStructTupleIteratorAndAsyncDomains");
            var invalid = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Bad{void Check(){_=DefaultProbe.Coalesce<string>(null);_=DefaultProbe.Assign<string>(null);_=DefaultProbe.Box(false,\"x\");}}", cancellationToken: cancellationToken));
            if (invalid.GetDiagnostics(cancellationToken).Count(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS0453"; }) != 3)
            { throw new InvalidOperationException("Default conversion admitted references into struct-only coalescing or boxing."); }
            Program.Log.Info("PASS DefaultRejectsInvalidCoalesceAndBoxingDomains");
        }
        finally { loadContext.Unload(); }
        return 4;
    }
}
