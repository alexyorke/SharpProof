using System.Collections.Immutable;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    [Test]
    public void DictionaryStateIntrinsicRejectsSourceDefinedFrameworkLookalikes()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace System.Collections.Generic { public class Dictionary<K,V> { public Dictionary() {} public void Add(K key, V value) {} public bool ContainsKey(K key) => false; public int Count => -1; public V this[K key] { get => default(V); set {} } } }");
        var compilation = CSharpCompilation.Create("UserDictionary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var type = compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2")!;
        foreach (var member in type.GetMembers())
        { Assert.That(CSharpOperationSemantics.IsDictionaryStateDeclaration(member), Is.False); }
    }

    private static bool CheckDictionaryStateIntrinsic(CSharpCompilation references, bool wrongClaim, bool referenceValues = false, bool opaqueValues = false, bool genericKeys = false, bool sourceParameters = false, bool capacity = false)
    {
        ArgumentNullException.ThrowIfNull(references);
        var body = genericKeys
            ? "var data = new System.Collections.Generic.Dictionary<T, int>(); var alias = data; return data.Count + alias.Count;"
            : opaqueValues
            ? "var data = new System.Collections.Generic.Dictionary<int, T>(); var alias = data; return data.Count + alias.Count + (alias.ContainsKey(17) ? 100 : 0);"
            : referenceValues
            ? "var data = new System.Collections.Generic.Dictionary<int, object>(); var alias = data; var first = new object(); var second = new object(); " +
                "alias.Add(1, first); data[2] = null; alias[1] = second; " +
                "return data.Count * 1000 + (object.ReferenceEquals(data[1], second) ? 310 : 0) + (data[2] == null ? 29 : 0) + (data.ContainsKey(1) ? 1 : 0) + (data.ContainsKey(3) ? 100 : 0);"
            : "var data = new System.Collections.Generic.Dictionary<int, int>(); var alias = data; " +
                "alias.Add(1, 23); data[2] = 29; alias[1] = 31; " +
                "return data.Count * 1000 + data[1] * 10 + data[2] + (data.ContainsKey(1) ? 1 : 0) + (data.ContainsKey(3) ? 100 : 0);";
        var helpers = "";
        if (sourceParameters)
        {
            body = referenceValues
                ? "var data = new System.Collections.Generic.Dictionary<int, object>(); var first = new object(); var second = new object(); " +
                    "Append<int, object>(data, 1, first); Append<int, object>(data, 2, null); Replace<int, object>(data, 1, second); " +
                    "return Size<int, object>(data) * 1000 + (object.ReferenceEquals(Read<int, object>(data, 1), second) ? 310 : 0) + (Read<int, object>(data, 2) == null ? 29 : 0) + (Has<int, object>(data, 1) ? 1 : 0) + (Has<int, object>(data, 3) ? 100 : 0);"
                : "var data = new System.Collections.Generic.Dictionary<int, int>(); Append<int, int>(data, 1, 23); " +
                    "Append<int, int>(data, 2, 29); Replace<int, int>(data, 1, 31); " +
                    "return Size<int, int>(data) * 1000 + Read<int, int>(data, 1) * 10 + Read<int, int>(data, 2) + (Has<int, int>(data, 1) ? 1 : 0) + (Has<int, int>(data, 3) ? 100 : 0);";
            helpers = "static V Read<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key) => data[key]; " +
                "static void Replace<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key, V value) { data[key] = value; } " +
                "static void Append<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key, V value) { data.Add(key, value); } " +
                "static bool Has<K, V>(System.Collections.Generic.Dictionary<K, V> data, K key) => data.ContainsKey(key); " +
                "static int Size<K, V>(System.Collections.Generic.Dictionary<K, V> data) => data.Count;";
        }
        if (capacity)
        { body = body.Replace(">();", ">(3);", StringComparison.Ordinal); }
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int " + (opaqueValues || genericKeys ? "Target<T>()" : "Target()") + " { " + body + " } " + helpers + " }");
        var compilation = references.AddSyntaxTrees(tree);
        if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error))
        { return false; }
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        if (!lowered.IsExact)
        { return false; }
        // This bounded evidence probe selects normal framework return. It grants
        // no native replay authority and claims no exception/allocation freedom.
        var normal = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        var result = new IrProgramInterpreter(factory).Execute(lowered.Program, null, 5000, normal);
        if (sourceParameters)
        {
            using var image = new MemoryStream();
            if (!compilation.Emit(image).Success)
            { return false; }
            var runtime = new AssemblyLoadContext("DictionarySourceInventoryOracle", isCollectible: true);
            try
            {
                image.Position = 0;
                var actualSource = (int)runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, null)!;
                return result.Status == IrProgramExecutionStatus.Returned && result.ConsumedApproximation &&
                    result.ReturnValue!.Integer == actualSource + (wrongClaim ? 1 : 0);
            }
            finally
            { runtime.Unload(); }
        }
        int actual;
        if (genericKeys)
        {
            var data = new Dictionary<string, int>();
            var alias = data;
            actual = data.Count + alias.Count;
        }
        else if (opaqueValues)
        {
            var data = new Dictionary<int, decimal>();
            var alias = data;
            actual = data.Count + alias.Count + (alias.ContainsKey(17) ? 100 : 0);
        }
        else if (referenceValues)
        {
            var data = new Dictionary<int, object?>();
            var alias = data;
            var first = new object();
            var second = new object();
            alias.Add(1, first);
            data[2] = null;
            alias[1] = second;
            actual = data.Count * 1000 + (ReferenceEquals(data[1], second) ? 310 : 0) + (data[2] == null ? 29 : 0) + (data.ContainsKey(1) ? 1 : 0) + (data.ContainsKey(3) ? 100 : 0);
        }
        else
        {
            var data = new Dictionary<int, int>();
            var alias = data;
            alias.Add(1, 23);
            data[2] = 29;
            alias[1] = 31;
            actual = data.Count * 1000 + data[1] * 10 + data[2] + (data.ContainsKey(1) ? 1 : 0) + (data.ContainsKey(3) ? 100 : 0);
        }
        return result.Status == IrProgramExecutionStatus.Returned && result.ReturnValue!.Integer == actual + (wrongClaim ? 1 : 0);
    }

    private static bool CheckOpaqueDictionaryStorageIntrinsic(CSharpCompilation references, bool wrongClaim, bool direct = false)
    {
        ArgumentNullException.ThrowIfNull(references);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static T Target<T>(T first, T second) { var data = new System.Collections.Generic.Dictionary<int, T>(); " +
            "data.Add(1, first); data.Add(3, second); " +
            (direct ? "data[1] = data[3]; return data[1];" : "Replace<T>(data, 1, Read<T>(data, 3)); return Read<T>(data, 1);") +
            " } static T Read<T>(System.Collections.Generic.Dictionary<int, T> data, int key) => data[key]; " +
            "static void Replace<T>(System.Collections.Generic.Dictionary<int, T> data, int key, T value) { data[key] = value; } }");
        var compilation = references.AddSyntaxTrees(tree);
        if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error))
        { return false; }
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        if (!lowered.IsExact)
        { return false; }
        var inputBuilder = ImmutableDictionary.CreateBuilder<ScopedIrId<IrVariableTag>, IrValue>();
        foreach (var parameter in context.Parameters)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            var key = parameter.Entry;
            var value = factory.CreateReferenceValue(factory.GetVariableInfo(key).Type, new IrObjectState());
            if (inputBuilder.ContainsKey(key))
            { throw new ArgumentException("An item with the same key has already been added. Key: " + key); }
            inputBuilder.Add(key, value);
        }
        var inputs = inputBuilder.ToImmutable();
        // Select an initialized normal return only for this bounded semantic oracle.
        // This does not make framework initialization or faults exact for native replay.
        var normal = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        var result = new IrProgramInterpreter(factory).Execute(lowered.Program, inputs, 10000, normal);
        using var image = new MemoryStream();
        if (!compilation.Emit(image).Success)
        { return false; }
        var runtime = new AssemblyLoadContext("OpaqueDictionaryInventoryOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.MakeGenericMethod(typeof(decimal));
            var expected = new decimal(12300, 0, 0, false, 4);
            var actual = (decimal)target.Invoke(null, new object[] { decimal.MinValue, expected })!;
            return decimal.GetBits(actual).SequenceEqual(decimal.GetBits(expected)) && result.Status == IrProgramExecutionStatus.Returned &&
                result.ConsumedApproximation && result.ReturnValue!.Kind == IrValueKind.Reference &&
                ReferenceEquals(result.ReturnValue.Reference, inputs[context.Parameters[wrongClaim ? 0 : 1].Entry].Reference);
        }
        finally
        { runtime.Unload(); }
    }
}
