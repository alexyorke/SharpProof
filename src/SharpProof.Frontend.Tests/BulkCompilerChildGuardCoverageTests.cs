using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkCompilerChildGuardCoverageTests
{
    [Test]
    public void ActualNullableCfgNullCheckRetainsLegacyChildTypeClosure()
    {
        using var subject = TypedProgramSubject.Create("int Target(int? value) => value ?? 7;");
        Assert.That(subject.Invoke([17]), Is.EqualTo(17));
        Assert.That(subject.Invoke([null!]), Is.EqualTo(7));
        var operation = subject.Graph.Blocks.Select(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.BranchValue)).OfType<IIsNullOperation>().Single();
        var result = new RoslynOperationLowerer(new IrFactory()).Lower(operation);
        Assert.That(result.IsExact, Is.False);
        Assert.That(result.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        using var supported = TypedProgramSubject.Create("string Target(string value) => value ?? \"fallback\";");
        Assert.That(supported.Invoke(["word"]), Is.EqualTo("word"));
        Assert.That(supported.Invoke([null!]), Is.EqualTo("fallback"));
        var supportedOperation = supported.Graph.Blocks.Select(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.BranchValue)).OfType<IIsNullOperation>().Single();
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var exact = lowerer.Lower(supportedOperation);
        Assert.That(exact.IsExact, Is.True);
        var capture = lowerer.CreateCaptureBindings().Single();
        var environment = new Dictionary<IrVarId, IrValue> { [capture] = factory.CreateStringValue("word") };
        Assert.That(new IrInterpreter(factory).Evaluate(exact.Term, environment).Value!.Boolean, Is.False);
        environment[capture] = factory.CreateNullValue(factory.StringType);
        Assert.That(new IrInterpreter(factory).Evaluate(exact.Term, environment).Value!.Boolean, Is.True);
    }

    [Test]
    public void RealGenericTypeTestPreservesUnsupportedUnboxingChild()
    {
        using var subject = TypedProgramSubject.Create("bool Target(object value) => Helper<int>(value); static bool Helper<T>(object value) => ((T)value) is int;");
        Assert.That(subject.Invoke([17]), Is.True);
        Assert.That(subject.Invoke(["word"]), Is.InstanceOf<InvalidCastException>());
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Helper"));
        var model = subject.Compilation.GetSemanticModel(tree);
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var operation = (IIsTypeOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestOperand(operation), Is.SameAs(operation.ValueOperand));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var exit = builder.CreateBlock("exceptional-exit");
        builder.SetEntry(entry);
        var body = new RoslynTotalExpressionLowerer(context, builder, exit).LowerBodyValue(operation, entry);
        Assert.That(body.Classification.IsExact, Is.False);
        Assert.That(body.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedType));
        using var supported = TypedProgramSubject.Create("bool Target(int value) => value == 17;");
        Assert.That(supported.Invoke([17]), Is.True);
        var lowering = supported.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = supported.Execute(lowering, [17]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Boolean, Is.True);
    }
}
