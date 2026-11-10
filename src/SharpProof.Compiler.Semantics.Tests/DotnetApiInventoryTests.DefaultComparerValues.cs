using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;
using System.Runtime.Loader;

namespace SharpProof.Effects.Test;

public sealed partial class DotnetApiInventoryTests
{
    [Test]
    public void DefaultComparerValueDeclarationsRejectSourceLookalikes()
    {
        var tree = CSharpSyntaxTree.ParseText("namespace System.Collections.Generic { public abstract class Comparer<T> { public static Comparer<T> Default => null; } }");
        var compilation = CSharpCompilation.Create("UserComparer", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var property = compilation.GetTypeByMetadataName("System.Collections.Generic.Comparer`1")!.GetMembers("Default").Single();
        Assert.That(CSharpOperationSemantics.IsDefaultComparerValueDeclaration(property), Is.False);
    }

    private static bool CheckDefaultComparerValueIntrinsic(CSharpCompilation references, bool wrongClaim)
    {
        ArgumentNullException.ThrowIfNull(references);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target() { var first = System.Collections.Generic.Comparer<int>.Default; var second = System.Collections.Generic.Comparer<int>.Default; return first != null && object.ReferenceEquals(first, second) ? 1 : 0; } }");
        var compilation = references.AddSyntaxTrees(tree);
        using var image = new MemoryStream();
        if (!compilation.Emit(image).Success)
        { return false; }
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        if (!lowered.IsExact)
        { return false; }
        var options = new IrProgramReplayOptions(request =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var type = factory.GetVariableInfo(request.Variable).Type;
            return type == factory.BooleanType ? factory.CreateBooleanValue(false) : factory.CreateReferenceValue(type, Comparer<int>.Default);
        })
        { ConcreteOpaqueValueProvider = Observe };
        IrValue? Observe(IrOpaqueTerm term)
        {
            ArgumentNullException.ThrowIfNull(term);
            return IrOpaqueCallSite.DefaultComparerValuePrimitive(factory, term) == "System_Int32"
                ? factory.CreateReferenceValue(term.Type, Comparer<int>.Default) : null;
        }
        var replay = new IrProgramInterpreter(factory).Execute(lowered.Program, null, 5000, options);
        var runtime = new AssemblyLoadContext("DefaultComparerValueOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = (int)runtime.LoadFromStream(image).GetType("Probe")!.GetMethod("Target")!.Invoke(null, null)!;
            return replay.Status == IrProgramExecutionStatus.Returned && replay.ConsumedApproximation && replay.ReturnValue!.Integer == actual + (wrongClaim ? 1 : 0);
        }
        finally { runtime.Unload(); }
    }
}
