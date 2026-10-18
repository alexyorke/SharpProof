using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    [Test]
    public void ListStateIntrinsicRejectsSourceDefinedFrameworkLookalikes()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace System.Collections.Generic { public class List<T> { public List() {} public void Add(T value) {} public int Count => -1; public T this[int key] { get => default(T); set {} } } }");
        var compilation = CSharpCompilation.Create("UserList", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var type = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1")!;
        foreach (var member in type.GetMembers())
        { Assert.That(CSharpOperationSemantics.IsListStateDeclaration(member), Is.False); }
    }

    private static bool CheckListStateIntrinsic(CSharpCompilation references, bool wrongClaim, bool referenceValues = false, bool opaqueValues = false, bool incoming = false)
    {
        ArgumentNullException.ThrowIfNull(references);
        var body = opaqueValues ? "var data = new System.Collections.Generic.List<T>(); System.Collections.Generic.ICollection<T> alias = data; return data.Count + alias.Count;"
            : referenceValues ? "var data = new System.Collections.Generic.List<object>(); System.Collections.Generic.IList<object> alias = data; var first = new object(); var second = new object(); " +
                "alias.Add(first); data.Add(null); alias[0] = second; return data.Count * 100 + (object.ReferenceEquals(data[0], second) ? 17 : 0) + (alias[1] == null ? 23 : 0);"
            : "var data = new System.Collections.Generic.List<int>(); System.Collections.Generic.IList<int> alias = data; " +
                "alias.Add(17); data.Add(23); alias[0] = 31; return data.Count * 1000 + alias[0] * 10 + data[1];";
        if (incoming)
        { body = "var before = data.Count; data[0] = 31; data.Add(23); return data.Count * 1000 + data[0] * 10 + data[before];"; }
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int " + (incoming ? "Target(System.Collections.Generic.List<int> data)" : opaqueValues ? "Target<T>()" : "Target()") + " { " + body + " } }");
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
        var normal = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            return factory.GetVariableInfo(request.Variable).Type == factory.BooleanType ? factory.CreateBooleanValue(false) : null;
        });
        Dictionary<ScopedIrId<IrVariableTag>, IrValue>? inputs = null;
        if (incoming)
        {
            var count = lowered.Program.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
                .OfType<IrAssignInstruction>().Select(instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) : instruction.Value)
                .OfType<IrBinaryTerm>().Where(term => term is null ? throw new ArgumentNullException(nameof(term)) : term.Operator == IrBinaryOperator.BitwiseAnd)
                .Select(term => term is null ? throw new ArgumentNullException(nameof(term)) : term.Left).OfType<IrOpaqueTerm>().First();
            var parameter = context.Parameters.Single().Entry;
            inputs = new()
            {
                [parameter] = factory.CreateReferenceValue(factory.GetVariableInfo(parameter).Type,
                new IrObjectState().WithField(count.Member, factory.CreateIntegerValue(1)))
            };
        }
        var result = new IrProgramInterpreter(factory).Execute(lowered.Program, inputs, 5000, normal);
        using var image = new MemoryStream();
        if (!compilation.Emit(image).Success)
        { return false; }
        var runtime = new AssemblyLoadContext("ListInventoryOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var target = runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!;
            if (opaqueValues)
            { target = target.MakeGenericMethod(typeof(decimal)); }
            var actual = (int)target.Invoke(null, incoming ? [new List<int> { 17 }] : null)!;
            return result.Status == IrProgramExecutionStatus.Returned && result.ConsumedApproximation && result.ReturnValue!.Integer == actual + (wrongClaim ? 1 : 0);
        }
        finally
        { runtime.Unload(); }
    }
}
