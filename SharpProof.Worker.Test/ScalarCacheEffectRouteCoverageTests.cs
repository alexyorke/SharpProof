using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ScalarCacheEffectRouteCoverageTests
{
    [TestCase("int")]
    [TestCase("string")]
    public void RegisteredEmptyArrayRetainsItsCacheAllocationEffect(string elementType)
    {
        using var subject = new MetadataTestSubject("public static class Library { public static int Target(int value) => value; }",
            "public static class Subject { public static int Target() => System.Array.Empty<" + elementType + ">().Length; }");
        Assert.That(subject.RootMethod.Invoke(null, []), Is.EqualTo(0));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        var lowering = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static _ => true, null, default, ResolveArrayEmpty);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var call = lowering.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrCallInstruction>().Single();
        Assert.That(IrOpaqueCallSite.Effects(factory, call.Operation), Is.EqualTo(IrOpaqueCallEffects.Allocates));

        TotalScalarCallModel? ResolveArrayEmpty(IMethodSymbol target)
        {
            Assert.That(target.IsStatic, Is.True);
            Assert.That(target.ContainingType.SpecialType, Is.EqualTo(SpecialType.System_Array));
            Assert.That(target.Name, Is.EqualTo("Empty"));
            Assert.That(target.Parameters, Is.Empty);
            Assert.That(target.ReturnType, Is.InstanceOf<IArrayTypeSymbol>());
            return new(0, _ => CSharpOperationSemantics.ArrayEmpty(factory,
                new RoslynTypeMapper(factory).GetTypeId(target.ReturnType)), effects: IrOpaqueCallEffects.Allocates);
        }
    }
}
