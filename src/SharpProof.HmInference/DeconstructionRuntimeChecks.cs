using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class DeconstructionRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "DeconstructionFixes", "DeconstructionFixes", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary), metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Probe.cs", """
            #nullable enable
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public sealed class Pair<T> {
                public T Value;
                public Pair(T value){Value=value;}
                public void Deconstruct(out T first,out bool flag){DeconstructionProbe.Trace+="D";first=Value;flag=true;}
            }
            public sealed class Box<T> {public T Value;public Box(T value){Value=value;}}
            public class Base<T> {public T Value;public Base(T value){Value=value;}}
            public sealed class Derived<T>:Base<T[]> {public Derived(T[] value):base(value){}}
            public interface IPair<T> {T Value{get;}}
            public struct StructPair<T>:IPair<T> {public T Item;public T Value=>Item;}
            public sealed class RefPair<T> where T:class {
                public T? Value;
                public void Deconstruct(out T? first,out bool flag){first=Value;flag=true;}
            }
            public static class Extensions {
                public static void Deconstruct<T>(this Box<T> pair,out T first,out bool flag){first=pair.Value;flag=true;}
                public static void Deconstruct<T>(this Base<T> pair,out T first,out bool flag){first=pair.Value;flag=true;}
                public static void Deconstruct<T>(this IPair<T> pair,out T first,out bool flag){first=pair.Value;flag=true;}
                public static void Deconstruct<T>(this T? pair,out T first,out bool flag) where T:struct {first=pair.GetValueOrDefault();flag=pair.HasValue;}
            }
            public sealed class Target {
                private int value;
                public Target Touch(){DeconstructionProbe.Trace+="R";return this;}
                public int Value {get=>value;set{DeconstructionProbe.Trace+="S";this.value=value;}}
            }
            public static class DeconstructionProbe {
                public static string Trace="";
                public static int Head(KeyValuePair<int,bool> pair){var(first,flag)=pair;return first;}
                public static string Nested(KeyValuePair<int,KeyValuePair<string,bool>> pair){var(first,(second,flag))=pair;return second;}
                public static (int,bool) Assigned(KeyValuePair<int,bool> pair,int first,bool flag)=>(first,flag)=pair;
                public static (long,bool) NativeWiden((int,bool) pair,long first,bool flag)=>(first,flag)=pair;
                public static int Custom(Pair<int> pair){var(first,flag)=pair;return first;}
                public static int Extension(Box<int> pair){var(first,flag)=pair;return first;}
                public static int[] BaseExtension(Derived<int> pair){var(first,flag)=pair;return first;}
                public static int InterfaceExtension(StructPair<int> pair){var(first,flag)=pair;return first;}
                public static int Foreach(IEnumerable<KeyValuePair<int,bool>> pairs,int seed){foreach(var(first,flag) in pairs)seed=first;return seed;}
                public static async Task<int> AsyncForeach(IAsyncEnumerable<KeyValuePair<int,bool>> pairs,int seed){await foreach(var(first,flag) in pairs)seed=first;return seed;}
                public static int Stored(Pair<int> pair,Target target,int ignored){(target.Touch().Value,_)=pair;return target.Value;}
                public static int NullableValue(int? pair){var(first,flag)=pair;return first;}
                public static string? NullableReference(RefPair<string> pair){var(first,flag)=pair;return first;}
            }
            """);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        using var originalImage = new MemoryStream();
        var originalEmit = original.Emit(originalImage, cancellationToken: cancellationToken);
        if (!originalEmit.Success)
        { throw new InvalidOperationException(string.Join("\n", originalEmit.Diagnostics)); }
        var originalAssembly = System.Reflection.Assembly.Load(originalImage.ToArray());
        var provider = new HmSignatureCodeFixProvider();
        var applied = 0;
        for (var round = 0; round < 18; round++)
        {
            var compilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Deconstruction analyzer crashed."); }
            var diagnostic = diagnostics.FirstOrDefault(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "HM0001"; });
            if (diagnostic is null)
            { break; }
            var actions = new List<CodeAction>();
            await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => { ArgumentNullException.ThrowIfNull(action); actions.Add(action); }, cancellationToken)).ConfigureAwait(false);
            var operations = await actions.Single().GetOperationsAsync(cancellationToken).ConfigureAwait(false);
            document = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
            applied++;
        }
        if (applied != 13)
        { throw new InvalidOperationException($"Expected thirteen deconstruction code fixes; applied {applied}."); }
        var finalCompilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var problems = finalCompilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning; }).ToArray();
        if (problems.Length != 0)
        { throw new InvalidOperationException(string.Join("\n", problems.Select(d => { ArgumentNullException.ThrowIfNull(d); return d.ToString(); }))); }
        using var image = new MemoryStream();
        var emitted = finalCompilation.Emit(image, cancellationToken: cancellationToken);
        if (!emitted.Success)
        { throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics)); }
        var assembly = System.Reflection.Assembly.Load(image.ToArray());
        var probe = assembly.GetType("DeconstructionProbe")!;
        var marker = Guid.NewGuid();
        var exception = new InvalidOperationException("marker");
        var pair = new KeyValuePair<Guid, string>(marker, "flag");
        if (!Equals(probe.GetMethod("Head")!.MakeGenericMethod(typeof(Guid), typeof(string)).Invoke(null, new object[] { pair }), marker)
            || !Equals(probe.GetMethod("Nested")!.MakeGenericMethod(typeof(short), typeof(Guid), typeof(InvalidOperationException)).Invoke(null, new object[] { new KeyValuePair<short, KeyValuePair<Guid, InvalidOperationException>>(1, new(marker, exception)) }), marker))
        { throw new InvalidOperationException("Instance or nested deconstruction changed."); }
        var assigned = probe.GetMethod("Assigned")!.MakeGenericMethod(typeof(Guid), typeof(string)).Invoke(null, new object[] { pair, Guid.Empty, "old" });
        var widened = probe.GetMethod("NativeWiden")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { (int.MaxValue, marker), 0L, Guid.Empty });
        if (assigned is not ValueTuple<Guid, string> { Item1: var first, Item2: "flag" } || first != marker || widened is not ValueTuple<long, Guid> { Item1: int.MaxValue, Item2: var second } || second != marker)
        { throw new InvalidOperationException("Deconstruction assignment result or conversion changed."); }
        Program.Log.Info("PASS NestedDeconstructionAssignmentValuesRuntime");
        var customType = assembly.GetType("Pair`1")!.MakeGenericType(typeof(Guid));
        var boxType = assembly.GetType("Box`1")!.MakeGenericType(typeof(Guid));
        var derivedType = assembly.GetType("Derived`1")!.MakeGenericType(typeof(Guid));
        var structType = assembly.GetType("StructPair`1")!.MakeGenericType(typeof(Guid));
        var structure = Activator.CreateInstance(structType)!;
        structType.GetField("Item")!.SetValue(structure, marker);
        var values = new[] { marker, Guid.Empty };
        if (!Equals(probe.GetMethod("Custom")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new[] { Activator.CreateInstance(customType, new object[] { marker })! }), marker)
            || !Equals(probe.GetMethod("Extension")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new[] { Activator.CreateInstance(boxType, new object[] { marker })! }), marker)
            || !ReferenceEquals(probe.GetMethod("BaseExtension")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new[] { Activator.CreateInstance(derivedType, new object[] { values })! }), values)
            || !Equals(probe.GetMethod("InterfaceExtension")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new[] { structure }), marker))
        { throw new InvalidOperationException("Custom, extension or projected receiver deconstruction changed."); }
        Program.Log.Info("PASS CustomExtensionReceiverProjectionRuntime");
        var pairs = new[] { new KeyValuePair<Guid, string>(Guid.Empty, "a"), pair };
        if (!Equals(probe.GetMethod("Foreach")!.MakeGenericMethod(typeof(Guid), typeof(string)).Invoke(null, new object[] { pairs, Guid.Empty }), marker)
            || await ((Task<Guid>)probe.GetMethod("AsyncForeach")!.MakeGenericMethod(typeof(Guid), typeof(string)).Invoke(null, new object[] { AsyncPairs(pairs, cancellationToken), Guid.Empty })!).WaitAsync(cancellationToken).ConfigureAwait(false) != marker)
        { throw new InvalidOperationException("Sync or async foreach deconstruction changed."); }
        Program.Log.Info("PASS SyncAsyncForeachDeconstructionRuntime");
        var originalProbe = originalAssembly.GetType("DeconstructionProbe")!;
        var originalPairType = originalAssembly.GetType("Pair`1")!.MakeGenericType(typeof(int));
        var originalTarget = Activator.CreateInstance(originalAssembly.GetType("Target")!)!;
        originalProbe.GetField("Trace")!.SetValue(null, "");
        var oldResult = originalProbe.GetMethod("Stored")!.Invoke(null, new object[] { Activator.CreateInstance(originalPairType, new object[] { 42 })!, originalTarget, 0 });
        var target = Activator.CreateInstance(assembly.GetType("Target")!)!;
        probe.GetField("Trace")!.SetValue(null, "");
        var stored = probe.GetMethod("Stored")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object[] { Activator.CreateInstance(assembly.GetType("Pair`1")!.MakeGenericType(typeof(int)), new object[] { 42 })!, target, marker });
        if (!Equals(oldResult, stored) || !Equals(stored, 42) || !Equals(probe.GetField("Trace")!.GetValue(null), originalProbe.GetField("Trace")!.GetValue(null)))
        { throw new InvalidOperationException("Deconstruction receiver evaluation, call or setter order changed."); }
        Program.Log.Info("PASS DeconstructionEvaluationAndSetterOrderRuntime");
        if (!Equals(probe.GetMethod("NullableValue")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object?[] { marker }), marker)
            || !Equals(probe.GetMethod("NullableValue")!.MakeGenericMethod(typeof(Guid)).Invoke(null, new object?[] { null }), Guid.Empty))
        { throw new InvalidOperationException("Nullable value extension deconstruction changed."); }
        var nullableType = assembly.GetType("RefPair`1")!.MakeGenericType(typeof(InvalidOperationException));
        var nullable = Activator.CreateInstance(nullableType)!;
        var nullableMethod = probe.GetMethod("NullableReference")!.MakeGenericMethod(typeof(InvalidOperationException));
        if (nullableMethod.Invoke(null, new[] { nullable }) is not null)
        { throw new InvalidOperationException("Nullable deconstruction lost null."); }
        nullableType.GetField("Value")!.SetValue(nullable, exception);
        if (!ReferenceEquals(nullableMethod.Invoke(null, new[] { nullable }), exception))
        { throw new InvalidOperationException("Nullable deconstruction lost identity."); }
        Program.Log.Info("PASS NullableDeconstructionRuntime");
        return 5;
    }

    private static async IAsyncEnumerable<KeyValuePair<Guid, string>> AsyncPairs(IEnumerable<KeyValuePair<Guid, string>> pairs, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        foreach (var pair in pairs)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return pair;
        }
    }
}
