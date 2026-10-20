using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class CallerConstraintRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public static partial class ConstraintProbe{
                public static readonly List<string> Events=new();
                public static TaskCompletionSource<bool> Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public class Base{}
                public class ComparableBase:IComparable{public int CompareTo(object? other)=>0;}
                public struct Value{public int Number;}
                public class ReferenceConversion{
                    public static implicit operator Base(ReferenceConversion value){Events.Add("convert-base");return new Base();}
                }
                public struct InterfaceConversion{
                    public static implicit operator ComparableBase(InterfaceConversion value){Events.Add("convert-interface");return new ComparableBase();}
                }
                public class StructConversion{public static implicit operator Value(StructConversion value){Events.Add("convert-struct");return new Value{Number=9};}}
                public struct ManagedConversion{
                    public object Payload;
                    public static implicit operator Value(ManagedConversion value){Events.Add("convert-value");return new Value{Number=7};}
                }
                static void ClassBound<T>(T value)where T:class{Events.Add("class:"+typeof(T).Name);}
                static void NewBound<T>(T value)where T:new(){Events.Add("new:"+typeof(T).Name);_ = new T();}
                static void BaseBound<T>(T value)where T:Base{Events.Add("base:"+typeof(T).Name);}
                static void InterfaceBound<T>(T value)where T:IComparable{Events.Add("interface:"+typeof(T).Name);}
                static void StructBound<T>(T value)where T:struct{Events.Add("struct:"+typeof(T).Name);}
                static void UnmanagedBound<T>(T value)where T:unmanaged{Events.Add("unmanaged:"+typeof(T).Name);}
                public static object Reference(object value,int tag){ClassBound(value);return value;}
                public static object Constructor(object value,int tag){NewBound(value);return value;}
                public static Base ConvertBase(Base value,int tag){BaseBound(value);return value;}
                public static ComparableBase ConvertInterface(ComparableBase value,int tag){InterfaceBound(value);return value;}
                public static Value ConvertStruct(Value value,int tag){StructBound(value);return value;}
                public static Value ConvertValue(Value value,int tag){UnmanagedBound(value);return value;}
                public static (object,int) Sibling(object value,int sibling,int tag){ClassBound(value);return(value,sibling);}
                public static (object left,int right) Tuple((object left,int right) pair,int tag){ClassBound(pair.left);return pair;}
                public static object Extension(this object value,int tag){ClassBound(value);return value;}
                public static object? UsePartial()=>Partial(42,1);
                public static Task<object?> UseAsync()=>PartialAsync(42,1);
                public static object UseReference()=>Reference(42,1);
                public static object UseConstructor()=>Constructor("payload",1);
                public static Base UseBase()=>ConvertBase(new ReferenceConversion(),1);
                public static ComparableBase UseInterface()=>ConvertInterface(new InterfaceConversion(),1);
                public static Value UseStruct()=>ConvertStruct(new StructConversion(),1);
                public static Value UseValue()=>ConvertValue(new ManagedConversion{Payload=new object()},1);
                public static (object,int) UseSibling()=>Sibling(42,7,1);
                public static (object,int) UseTuple()=>Tuple((42,7),1);
                public static object UseExtension()=>(42).Extension(1);
                public static object UseOpen<T>(T value)=>Reference(value!,1);
            }
            """;
        const string definitions = """
            #nullable enable
            public static partial class ConstraintProbe{
                public static partial object? Partial(object? value,int tag);
                public static partial System.Threading.Tasks.Task<object?> PartialAsync(object? value,int tag);
            }
            """;
        const string implementations = """
            #nullable enable
            public static partial class ConstraintProbe{
                public static partial object Partial(object value,int tag)=>value;
                public static async partial System.Threading.Tasks.Task<object> PartialAsync(object value,int tag){await Gate.Task;return value;}
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "CallerConstraintRuntime", "CallerConstraintRuntime", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var main = project.AddDocument("Main.cs", source);
        var definition = main.Project.AddDocument("Definition.cs", definitions);
        var implementation = definition.Project.AddDocument("Implementation.cs", implementations);
        main = implementation.Project.GetDocument(main.Id)!;
        var original = (await main.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(main, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds,
            new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(main.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        foreach (var name in new[] { "Reference", "Constructor", "ConvertBase", "ConvertInterface", "ConvertStruct", "ConvertValue", "Sibling", "Tuple", "Extension", "Partial", "PartialAsync" })
        {
            var method = changed.GetTypeByMetadataName("ConstraintProbe")!.GetMembers(name).OfType<IMethodSymbol>().Single();
            if (!method.IsGenericMethod)
            { throw new InvalidOperationException("Caller constraint Fix All missed " + name); }
        }
        var originalContext = new System.Runtime.Loader.AssemblyLoadContext("OriginalCallerConstraints", isCollectible: true);
        var changedContext = new System.Runtime.Loader.AssemblyLoadContext("ChangedCallerConstraints", isCollectible: true);
        try
        {
            Type Load(Compilation compilation, System.Runtime.Loader.AssemblyLoadContext loadContext)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentNullException.ThrowIfNull(loadContext);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Caller constraint emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                return loadContext.LoadFromStream(image).GetType("ConstraintProbe")!;
            }
            var before = Load(original, originalContext);
            var after = Load(changed, changedContext);
            var beforeEvents = (List<string>)before.GetField("Events")!.GetValue(null)!;
            var afterEvents = (List<string>)after.GetField("Events")!.GetValue(null)!;
            foreach (var name in new[] { "UsePartial", "UseReference", "UseConstructor", "UseBase", "UseInterface", "UseStruct", "UseValue", "UseSibling", "UseTuple", "UseExtension" })
            {
                beforeEvents.Clear();
                afterEvents.Clear();
                var oldValue = before.GetMethod(name)!.Invoke(null, null);
                var newValue = after.GetMethod(name)!.Invoke(null, null);
                var oldDescription = Describe(oldValue);
                var newDescription = Describe(newValue);
                if (oldDescription != newDescription || !beforeEvents.SequenceEqual(afterEvents))
                { throw new InvalidOperationException(name + " changed values or conversion/helper order."); }
            }
            if (!Equals(after.GetMethod("UseOpen")!.MakeGenericMethod(typeof(int)).Invoke(null, [42]), 42))
            { throw new InvalidOperationException("Open generic caller lost its boxed value."); }
            Program.Log.Info("PASS CallerConstraintOriginalCallsBoxingConversionsAndEffects");

            var marker = new Uri("https://example.invalid/caller-constraint");
            if (!ReferenceEquals(after.GetMethod("Reference")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [marker, marker]), marker)
                || !ReferenceEquals(after.GetMethod("Partial")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [marker, marker]), marker)
                || after.GetMethod("Partial")!.MakeGenericMethod(typeof(Uri)).Invoke(null, [null, marker]) is not null)
            { throw new InvalidOperationException("Fixed object slots lost identity or nullable contracts."); }
            var sibling = ((object, decimal))after.GetMethod("Sibling")!.MakeGenericMethod(typeof(decimal), typeof(Uri)).Invoke(null, [marker, 3.5m, marker])!;
            var tuple = ((object, decimal))after.GetMethod("Tuple")!.MakeGenericMethod(typeof(decimal), typeof(Uri)).Invoke(null, [((object)marker, 3.5m), marker])!;
            if (!ReferenceEquals(sibling.Item1, marker) || sibling.Item2 != 3.5m || !ReferenceEquals(tuple.Item1, marker) || tuple.Item2 != 3.5m)
            { throw new InvalidOperationException("Refinement froze or changed an independent tuple component."); }
            Program.Log.Info("PASS CallerConstraintIndependentDomainsAndTupleComponents");

            var gateField = after.GetField("Gate")!;
            var gate = (TaskCompletionSource<bool>)gateField.GetValue(null)!;
            var pending = (Task<object?>)after.GetMethod("UseAsync")!.Invoke(null, null)!;
            if (pending.IsCompleted)
            { throw new InvalidOperationException("Refined partial async call failed to suspend."); }
            gate.SetResult(true);
            if (!Equals(await pending.WaitAsync(cancellationToken).ConfigureAwait(false), 42))
            { throw new InvalidOperationException("Refined partial async call changed boxing."); }
            var methodInfo = after.GetMethod("PartialAsync")!.MakeGenericMethod(typeof(Uri));
            gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            gateField.SetValue(null, gate);
            var newPending = (Task<object?>)methodInfo.Invoke(null, [marker, marker])!;
            gate.SetResult(true);
            if (!ReferenceEquals(await newPending.WaitAsync(cancellationToken).ConfigureAwait(false), marker))
            { throw new InvalidOperationException("Refined async method lost reference identity in a new domain."); }
            gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            gateField.SetValue(null, gate);
            var failure = new InvalidOperationException("caller-constraint");
            var fault = (Task<object?>)methodInfo.Invoke(null, [marker, marker])!;
            gate.SetException(failure);
            try
            { await fault.WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Missing refined async fault."); }
            catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure)) { }
            gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            gateField.SetValue(null, gate);
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(false);
            var cancellation = (Task<object?>)methodInfo.Invoke(null, [marker, marker])!;
#pragma warning disable SPINT003 // Deliberately canceled fixture token, independent of test cancellation.
            gate.SetCanceled(canceled.Token);
#pragma warning restore SPINT003
            try
            { await cancellation.WaitAsync(cancellationToken).ConfigureAwait(false); throw new InvalidOperationException("Missing refined async cancellation."); }
            catch (OperationCanceledException exception) when (exception.CancellationToken == canceled.Token) { }
            Program.Log.Info("PASS CallerConstraintPartialAsyncSuspensionFaultAndCancellation");
            return 3;
        }
        finally { originalContext.Unload(); changedContext.Unload(); }
    }

    private static string Describe(object? value)
    {
        if (value is null)
        { return "null"; }
        if (value is ValueTuple<object, int> pair)
        { return pair.Item1 + ":" + pair.Item2; }
        var type = value.GetType();
        return type.GetField("Number")?.GetValue(value)?.ToString()
            ?? (type == typeof(int) || type == typeof(string) ? value.ToString() : type.FullName) ?? "";
    }
}
