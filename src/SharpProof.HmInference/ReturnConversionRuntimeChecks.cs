using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class ReturnConversionRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
            public class ReturnBase{}
            public sealed class ReturnDerived:ReturnBase{}
            public sealed class ReturnOtherDerived:ReturnBase{}
            public class ReturnBase<T>{public T Value;public ReturnBase(T value){Value=value;}}
            public sealed class ReturnDerived<T>:ReturnBase<T>{public ReturnDerived(T value):base(value){}}
            public static class ReturnProbe{
                public static object Box(int value)=>value;
                public static object FixedWidened(int value)=>value;
                public static Delegate Natural(int value,int tag){object callback=()=>value;return (Delegate)callback;}
                public static object? Optional(int value)=>value;
                public static object? Nullable(int? value)=>value;
                public static object? FixedNull(int? value)=>value;
                public static object? FixedValue(int? value)=>value;
                public static IComparable Interface(int value)=>value;
                public static IComparable? NullableInterface(int? value)=>value;
                public static ValueType Value(int value)=>value;
                public static Enum Enum(DayOfWeek value)=>value;
                public static ReturnBase Base(ReturnDerived value)=>value;
                public static ReturnBase<int> Generic(ReturnDerived<int> value)=>value;
                public static IEnumerable<int> Array(int[] value)=>value;
                public static IEnumerable<int> List(List<int> value)=>value;
                public static object[] ArrayCovariance(string[] value)=>value;
                public static IEnumerable<object> InterfaceCovariance(IEnumerable<string> value)=>value;
                public static Func<object> DelegateCovariance(Func<string> value)=>value;
                public static long Numeric(int value,int tag)=>value;
                public static long? NullableNumeric(int? value,int tag)=>value;
                public static (object value,long number) Tuple(int value,int number)=>(value,number);
                public static object Local(int value,int tag){object Read()=>value;return Read();}
                public static Func<object> Lambda(int value,int tag)=>()=>value;
                public static object FixedLambda(int value,int tag){Func<object> read=()=>value;return read();}
                public static IEnumerable<object> Iterator(int value,int tag){yield return value;}
                public static async IAsyncEnumerable<object> AsyncIterator(int value,int tag){await Task.Yield();yield return value;}
                public static async Task<object> AsyncBox(Task<int> value)=>await value;
                public static async ValueTask<object?> AsyncNullable(ValueTask<int?> value)=>await value;
            }
            public static class OriginalReturnRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    var natural=ReturnProbe.Natural(7,0);if(natural.GetType()!=typeof(Func<int>)||natural.DynamicInvoke() is not int naturalValue||naturalValue!=7)return false;
                    short widened=7;if(ReturnProbe.FixedWidened(widened) is not int widenedBox||widenedBox!=7)return false;
                    object first=ReturnProbe.Box(7),second=ReturnProbe.Box(7);
                    if(first is not int firstValue||firstValue!=7||second is not int secondValue||secondValue!=7||ReferenceEquals(first,second))return false;
                    if(ReturnProbe.Optional(7) is not int optional||optional!=7||ReturnProbe.Nullable((int?)null)!=null||ReturnProbe.FixedNull(null)!=null||ReturnProbe.FixedValue(7) is not int fixedValue||fixedValue!=7
                        ||ReturnProbe.Nullable((int?)7) is not int nullable||nullable!=7||ReturnProbe.Interface(7).CompareTo(7)!=0
                        ||ReturnProbe.NullableInterface((int?)null)!=null||ReturnProbe.NullableInterface((int?)7)!.CompareTo(7)!=0
                        ||ReturnProbe.Value(7) is not int value||value!=7||ReturnProbe.Enum(DayOfWeek.Friday) is not DayOfWeek.Friday)return false;
                    var derived=new ReturnDerived();var generic=new ReturnDerived<int>(7);int[] array={7};var list=new List<int>{7};string[] labels={"x"};Func<string> factory=()=>labels[0];
                    if(!ReferenceEquals(ReturnProbe.Base(derived),derived)||!ReferenceEquals(ReturnProbe.Generic(generic),generic)
                        ||!ReferenceEquals(ReturnProbe.Array(array),array)||!ReferenceEquals(ReturnProbe.List(list),list)
                        ||!ReferenceEquals(ReturnProbe.ArrayCovariance(labels),labels)||!ReferenceEquals(ReturnProbe.InterfaceCovariance(labels),labels)
                        ||!ReferenceEquals(ReturnProbe.DelegateCovariance(factory),factory)||!ReferenceEquals(ReturnProbe.DelegateCovariance(factory)(),labels[0]))return false;
                    var tuple=ReturnProbe.Tuple(7,int.MinValue);
                    if(tuple.value is not int boxed||boxed!=7||tuple.number!=(long)int.MinValue||ReturnProbe.Numeric(int.MinValue,0)!=(long)int.MinValue
                        ||ReturnProbe.NullableNumeric(null,0)!=null||ReturnProbe.NullableNumeric(int.MaxValue,0)!=(long)int.MaxValue)return false;
                    foreach(var captured in new[]{ReturnProbe.Local(7,0),ReturnProbe.Lambda(7,0)(),ReturnProbe.FixedLambda(7,0)})
                        if(captured is not int item||item!=7)return false;
                    var iterator=ReturnProbe.Iterator(7,0);var one=iterator.Single();var two=iterator.Single();
                    if(one is not int element||element!=7||ReferenceEquals(one,two))return false;
                    int count=0;await foreach(var entry in ReturnProbe.AsyncIterator(7,0).WithCancellation(cancellationToken)){if(entry is not int item||item!=7)return false;count++;}if(count!=1)return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Null","Fault","Cancel"}){
                        var gate=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var optionalGate=new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var pending=ReturnProbe.AsyncBox(gate.Task);var optionalPending=ReturnProbe.AsyncNullable(new ValueTask<int?>(optionalGate.Task)).AsTask();
                        if(pending.IsCompleted||optionalPending.IsCompleted)return false;
                        var failure=new InvalidOperationException("return-conversion-await");
                        if(outcome=="Fault"){gate.SetException(failure);optionalGate.SetException(failure);}
                        else if(outcome=="Cancel"){gate.SetCanceled(canceled.Token);optionalGate.SetCanceled(canceled.Token);}
                        else{gate.SetResult(7);optionalGate.SetResult(outcome=="Null"?null:7);}
                        try{var result=await pending.WaitAsync(cancellationToken);if(outcome is "Fault" or "Cancel"||result is not int item||item!=7)return false;}
                        catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                        catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        try{var result=await optionalPending.WaitAsync(cancellationToken);if(outcome is "Fault" or "Cancel"||(outcome=="Null"?result!=null:result is not int item||item!=7))return false;}
                        catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                        catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                    }
                    return true;
                }
            }
            """;
        const string newSource = """
            #nullable enable
            using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
            public static class NewReturnRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    var marker=new Uri("https://example.invalid/new-returns");var text=new string('x',2);
                    var natural=ReturnProbe.Natural(7,marker);if(natural.GetType()!=typeof(Func<int>)||natural.DynamicInvoke() is not int naturalValue||naturalValue!=7)return false;
                    if(ReturnProbe.Box(23m) is not decimal boxed||boxed!=23m||!ReferenceEquals(ReturnProbe.Box(text),text)
                        ||ReturnProbe.Optional((decimal?)null)!=null||ReturnProbe.Nullable((decimal?)null)!=null
                        ||ReturnProbe.Nullable((decimal?)23m) is not decimal nullable||nullable!=23m||ReturnProbe.Interface(23m).CompareTo(23m)!=0
                        ||ReturnProbe.NullableInterface((decimal?)null)!=null||ReturnProbe.NullableInterface((decimal?)23m)!.CompareTo(23m)!=0
                        ||ReturnProbe.Value(23m) is not decimal value||value!=23m||ReturnProbe.Enum(ConsoleColor.Green) is not ConsoleColor.Green)return false;
                    var derived=new ReturnOtherDerived();var generic=new ReturnDerived<decimal>(23m);decimal[] array={23m};var list=new List<decimal>{23m};Uri[] references={marker};Func<Uri> factory=()=>marker;
                    if(!ReferenceEquals(ReturnProbe.Base(derived),derived)||!ReferenceEquals(ReturnProbe.Generic(generic),generic)||ReturnProbe.Generic(generic).Value!=23m
                        ||!ReferenceEquals(ReturnProbe.Array(array),array)||!ReferenceEquals(ReturnProbe.List(list),list)
                        ||!ReferenceEquals(ReturnProbe.ArrayCovariance(references),references)||!ReferenceEquals(ReturnProbe.InterfaceCovariance(references),references)
                        ||!ReferenceEquals(ReturnProbe.DelegateCovariance(factory),factory)||!ReferenceEquals(ReturnProbe.DelegateCovariance(factory)(),marker))return false;
                    var tuple=ReturnProbe.Tuple(23m,int.MinValue);
                    if(tuple.value is not decimal payload||payload!=23m||tuple.number!=(long)int.MinValue||ReturnProbe.Numeric(int.MaxValue,marker)!=(long)int.MaxValue
                        ||ReturnProbe.NullableNumeric(null,marker)!=null)return false;
                    foreach(var captured in new[]{ReturnProbe.Local(23m,marker),ReturnProbe.Lambda(23m,marker)(),ReturnProbe.FixedLambda(23m,marker)})
                        if(captured is not decimal item||item!=23m)return false;
                    if(ReturnProbe.Iterator(23m,marker).Single() is not decimal element||element!=23m)return false;
                    int count=0;await foreach(var entry in ReturnProbe.AsyncIterator(23m,marker).WithCancellation(cancellationToken)){if(entry is not decimal item||item!=23m)return false;count++;}if(count!=1)return false;
                    if(!ReferenceEquals(await ReturnProbe.AsyncBox(Task.FromResult(text)).WaitAsync(cancellationToken),text)
                        ||await ReturnProbe.AsyncBox(Task.FromResult(23m)).WaitAsync(cancellationToken) is not decimal asyncBox||asyncBox!=23m
                        ||await ReturnProbe.AsyncNullable(new ValueTask<decimal?>((decimal?)null)).AsTask().WaitAsync(cancellationToken)!=null
                        ||await ReturnProbe.AsyncNullable(new ValueTask<decimal?>((decimal?)23m)).AsTask().WaitAsync(cancellationToken) is not decimal asyncOptional||asyncOptional!=23m)return false;
                    return true;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "ReturnConversions", "ReturnConversions", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Returns.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var fixtureErrors = original.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).Take(6).ToArray();
        if (fixtureErrors.Length != 0)
        {
            throw new InvalidOperationException("Return conversion fixture: " + string.Join("\n", fixtureErrors.Select(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.ToString(); })));
        }
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var symbol = changed.GetTypeByMetadataName("ReturnProbe")!;
        if (symbol.GetMembers("Box").OfType<IMethodSymbol>().Single().TypeParameters is not [{ HasNotNullConstraint: true }]
            || symbol.GetMembers("Nullable").OfType<IMethodSymbol>().Single().TypeParameters is not [{ HasValueTypeConstraint: true }]
            || symbol.GetMembers("Numeric").OfType<IMethodSymbol>().Single().Parameters[0].Type.SpecialType != SpecialType.System_Int32
            || symbol.GetMembers("FixedWidened").OfType<IMethodSymbol>().Single().Parameters[0].Type.SpecialType != SpecialType.System_Int32
            || symbol.GetMembers("Interface").OfType<IMethodSymbol>().Single().TypeParameters is not [{ ConstraintTypes: [{ Name: "IComparable" }] }]
            || symbol.GetMembers("FixedNull").OfType<IMethodSymbol>().Single().Parameters[0].Type is not INamedTypeSymbol { TypeArguments: [{ SpecialType: SpecialType.System_Int32 }] }
            || symbol.GetMembers("FixedValue").OfType<IMethodSymbol>().Single().Parameters[0].Type is not INamedTypeSymbol { TypeArguments: [{ SpecialType: SpecialType.System_Int32 }] })
        {
            var details = Program.Results((CSharpCompilation)original, cancellationToken).Where(result =>
                { ArgumentNullException.ThrowIfNull(result); return result.Method is "Box" or "Nullable" or "Interface" or "Numeric" or "FixedNull"; }).Select(result =>
                { ArgumentNullException.ThrowIfNull(result); return result.Method + ": " + result.Status + ": " + (result.Signature ?? result.Message); });
            var declarations = symbol.GetMembers().OfType<IMethodSymbol>().Where(method =>
                { ArgumentNullException.ThrowIfNull(method); return method.Name is "Box" or "Nullable" or "Interface" or "Numeric" or "FixedNull"; }).Select(method =>
                { ArgumentNullException.ThrowIfNull(method); return method.ToDisplayString(); });
            throw new InvalidOperationException("Return Fix All constraints: " + string.Join("\n", details.Concat(declarations)));
        }
        Program.Log.Info("PASS ReturnConversionFixAllConstraintsAndNumericEndpoints");
        var beforeContext = new System.Runtime.Loader.AssemblyLoadContext("BeforeReturnConversions", isCollectible: true);
        var afterContext = new System.Runtime.Loader.AssemblyLoadContext("AfterReturnConversions", isCollectible: true);
        try
        {
            System.Reflection.Assembly Load(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Return conversion emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                return loadContext.LoadFromStream(image);
            }
            foreach (var (compilation, loadContext) in new[] { (original, beforeContext), (changed, afterContext) })
            {
                var assembly = Load(compilation, loadContext);
                var pending = (Task<bool>)assembly.GetType("OriginalReturnRunner")!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
                if (!await pending.WaitAsync(cancellationToken).ConfigureAwait(false))
                { throw new InvalidOperationException("Return conversion changed original identity, boxing, numeric or async effects."); }
            }
            Program.Log.Info("PASS ReturnConversionOriginalIdentityBoxingNumericIteratorsSuspensionFaultAndCancellation");
            var newer = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newSource, cancellationToken: cancellationToken)).WithAssemblyName("NewReturnConversions");
            var newAssembly = Load(newer, afterContext);
            var newPending = (Task<bool>)newAssembly.GetType("NewReturnRunner")!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
            if (!await newPending.WaitAsync(cancellationToken).ConfigureAwait(false))
            { throw new InvalidOperationException("Return conversion lost new reference/value/container/async domains."); }
            Program.Log.Info("PASS ReturnConversionNewReferenceStructBaseEnumContainersCallbacksAndAsyncDomains");
            var invalid = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Bad{void Check(){_=ReturnProbe.Value(\"x\");_=ReturnProbe.Interface(new object());_=ReturnProbe.ArrayCovariance(new[]{1});_=ReturnProbe.Enum(1);}}", cancellationToken: cancellationToken));
            if (invalid.GetDiagnostics(cancellationToken).Count(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id is "CS0452" or "CS0453" or "CS0311" or "CS0315"; }) != 4)
            { throw new InvalidOperationException("Return conversions admitted invalid interface/value/enum/covariance domains."); }
            Program.Log.Info("PASS ReturnConversionRejectsInvalidInterfaceValueEnumAndCovarianceDomains");
        }
        finally { beforeContext.Unload(); afterContext.Unload(); }
        return 4;
    }
}
