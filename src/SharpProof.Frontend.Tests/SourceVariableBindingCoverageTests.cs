using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceVariableBindingCoverageTests
{
    [TestCase("long Target(long value) => checked(value + 1L);", 9L, 10L)]
    [TestCase("long Target(long input) { long value = input; return checked(value + 1L); }", 9L, 10L)]
    public void SourceSymbolBindingsReplayTheCompiledExpression(string members, long input, long expected)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([input]), Is.EqualTo(expected));
        var operation = TargetExpression(subject);
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var result = lowerer.Lower(operation);
        Assert.That(result.IsExact, Is.True);
        Assert.That(result.Variables, Has.Length.EqualTo(1));
        var reference = operation.DescendantsAndSelf().Single(candidate =>
            candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate is IParameterReferenceOperation or ILocalReferenceOperation));
        var variable = lowerer.GetReferencedVariable(reference);
        Assert.That(variable, Is.EqualTo(result.Variables[0].Variable));
        var ids = lowerer.CreateVariableIds();
        Assert.That(ids, Has.Length.EqualTo(1));
        var bare = lowerer.LowerTerm(operation);
        Assert.That(bare.Classification.IsExact, Is.True);
        var environment = ImmutableDictionaryCapture.Create(ids, id => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (id), _ => factory.CreateIntegerValue(input));
        var evaluated = new IrInterpreter(factory).Evaluate(bare.Term, environment);
        Assert.That(evaluated.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(evaluated.Value!.Integer, Is.EqualTo(expected));
    }

    [Test]
    public void DistinctParameterBindingsCannotSwapTheirCompiledValues()
    {
        using var subject = TypedProgramSubject.Create(
            "long Target(long left, long right) => checked(left * 2L + right);");
        Assert.That(subject.Invoke([3L, 7L]), Is.EqualTo(13L));
        var operation = TargetExpression(subject);
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var result = lowerer.Lower(operation);
        Assert.That(result.IsExact, Is.True);
        Assert.That(result.Variables, Has.Length.EqualTo(2));
        var inputs = ImmutableDictionaryCapture.Create(result.Variables, binding => binding.Variable.IsDefault ? throw new System.ArgumentException("Value must be initialized.", nameof(binding)) : binding.Variable,
            binding => ((binding.Variable.IsDefault) ? throw new System.ArgumentException("Value must be initialized.", nameof(binding)) : (factory.CreateIntegerValue(binding.Symbol.Name == "left" ? 3L : 7L))));
        var ids = lowerer.CreateVariableIds();
        Assert.That(ids, Is.EquivalentTo(inputs.Keys));
        var bare = lowerer.LowerTerm(operation);
        Assert.That(bare.Classification.IsExact, Is.True);
        var evaluated = new IrInterpreter(factory).Evaluate(bare.Term, inputs);
        Assert.That(evaluated.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(evaluated.Value!.Integer, Is.EqualTo(13L));
    }

    [Test]
    public void ConversionUnwrappingPreservesTheOriginalParameterBinding()
    {
        using var subject = TypedProgramSubject.Create("long Target(int value) => checked((long)value);");
        var operation = TargetExpression(subject);
        Assert.That(operation, Is.InstanceOf<IConversionOperation>());
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var result = lowerer.Lower(operation);
        Assert.That(result.IsExact, Is.True);
        Assert.That(lowerer.GetReferencedVariable(operation), Is.EqualTo(result.Variables.Single().Variable));
        Assert.That(lowerer.GetReferencedVariable(operation, unwrapConversions: false), Is.Null);
        Assert.That(lowerer.GetReferencedVariable(((IConversionOperation)operation).Operand),
            Is.EqualTo(result.Variables.Single().Variable));
    }

    [Test]
    public void ActualCfgCaptureReferencesKeepTheirAllocatedIdentity()
    {
        using var subject = TypedProgramSubject.Create(
            "long Target(bool select, long left, long right) => select ? left : right;");
        var lowerer = new RoslynOperationLowerer(new IrFactory());
        var roots = subject.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations
            .Concat(block.BranchValue == null ? Array.Empty<IOperation>() : [block.BranchValue]))).ToImmutableArray();
        foreach (var operation in roots)
        {
            Assert.That(lowerer.Lower(operation).IsExact, Is.True);
        }
        var captures = roots.SelectMany(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.DescendantsAndSelf()))
            .OfType<IFlowCaptureReferenceOperation>().ToImmutableArray();
        Assert.That(captures, Is.Not.Empty);
        foreach (var capture in captures)
        {
            var result = lowerer.Lower(capture);
            Assert.That(result.Term, Is.InstanceOf<IrVariableTerm>());
            var expected = ((IrVariableTerm)result.Term).Variable;
            Assert.That(lowerer.GetReferencedVariable(capture), Is.EqualTo(expected));
            Assert.That(lowerer.CreateCaptureBindings(), Does.Contain(expected));
        }
    }

    private static IOperation TargetExpression(TypedProgramSubject subject)
    {
        System.ArgumentNullException.ThrowIfNull(subject);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(declaration => declaration is null ? throw new System.ArgumentNullException(nameof(declaration)) : (declaration.Identifier.ValueText == "Target"));
        var expression = method.ExpressionBody?.Expression ??
            method.Body!.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!;
        return FrontendTestHelpers.TryGetExpressionOperation(subject.Compilation.GetSemanticModel(tree), expression)!;
    }
}
