using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    private static bool CheckListRangeIntrinsic(CSharpCompilation references, string id, bool wrongClaim, bool referenceValues = false)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(id);
        var body = referenceValues
            ? "var data = new System.Collections.Generic.List<object>(); var first = new object(); var second = new object(); data.Add(first); " +
                (id.Contains("IList", StringComparison.Ordinal) ? "System.Collections.Generic.IList<object> alias = data; alias.Insert(0, second); alias.Insert(2, null);" : "data.Insert(0, second); data.Insert(2, null);") +
                "var copy = data.GetRange(0, 3); data[0] = first; copy[1] = null; return !object.ReferenceEquals(data, copy) && object.ReferenceEquals(copy[0], second) && data[1] != null && copy[1] == null && copy[2] == null ? 1 : 0;"
            : "var data = new System.Collections.Generic.List<int>(); data.Add(11); data.Add(23); " +
                (id.Contains("IList", StringComparison.Ordinal) ? "System.Collections.Generic.IList<int> alias = data; alias.Insert(1, 17);" : "data.Insert(1, 17);") +
                "var copy = data.GetRange(0, 3); copy[0] = 29; data[2] = 31; return copy.Count * 1000 + data[0] * 100 + copy[1] * 10 + copy[2];";
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target() { " + body + " } }");
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
        var result = new IrProgramInterpreter(factory).Execute(lowered.Program, null, 10000, normal);
        using var image = new MemoryStream();
        if (!compilation.Emit(image).Success)
        { return false; }
        var runtime = new AssemblyLoadContext("ListRangeInventoryOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = (int)runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, null)!;
            return result.Status == IrProgramExecutionStatus.Returned && result.ConsumedApproximation && result.ReturnValue!.Integer == actual + (wrongClaim ? 1 : 0);
        }
        finally
        { runtime.Unload(); }
    }
}
