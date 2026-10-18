using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;

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

    private static bool CheckDictionaryStateIntrinsic(CSharpCompilation references, bool wrongClaim)
    {
        ArgumentNullException.ThrowIfNull(references);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target() { " +
            "var data = new System.Collections.Generic.Dictionary<int, int>(); var alias = data; " +
            "alias.Add(1, 23); data[2] = 29; alias[1] = 31; " +
            "return data.Count * 1000 + data[1] * 10 + data[2] + (data.ContainsKey(1) ? 1 : 0) + (data.ContainsKey(3) ? 100 : 0); } }");
        var compilation = references.AddSyntaxTrees(tree);
        if (compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error))
        { return false; }
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
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
        var data = new Dictionary<int, int>();
        var alias = data;
        alias.Add(1, 23);
        data[2] = 29;
        alias[1] = 31;
        var actual = data.Count * 1000 + data[1] * 10 + data[2] + (data.ContainsKey(1) ? 1 : 0) + (data.ContainsKey(3) ? 100 : 0);
        return result.Status == IrProgramExecutionStatus.Returned && result.ReturnValue!.Integer == actual + (wrongClaim ? 1 : 0);
    }
}
