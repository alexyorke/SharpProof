using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractClauseSubstitutionRepresentativeTests
{
    [TestCase(-1L)]
    [TestCase(1L)]
    public void CompilerBoundClausesRebindTogetherWithoutChangingProvenance(long value)
    {
        var (factory, contracts) = CreateContracts();
        var parameter = contracts.Variables.Single(variable => variable.Role == BoundContractVariableRole.Parameter).Variable;
        var result = contracts.Variables.Single(variable => variable.Role == BoundContractVariableRole.Result).Variable;
        var substitutions = new Dictionary<IrVarId, IrTerm>
        {
            [parameter] = factory.Integer(value),
            [result] = factory.Integer(value)
        };
        var bound = ContractClauseSubstitution.Substitute(factory, contracts.Clauses, substitutions);
        Assert.That(bound.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(bound.Clauses, Has.Length.EqualTo(2));
        Assert.That(bound.Clauses.Select(clause => clause.Kind),
            Is.EqualTo(new[] { BoundContractKind.Requires, BoundContractKind.Ensures }));
        var interpreter = new IrInterpreter(factory);
        Assert.That(interpreter.Evaluate(bound.Clauses[0].Condition, new Dictionary<IrVarId, IrValue>()).Value!.Boolean,
            Is.EqualTo(value > 0));
        Assert.That(interpreter.Evaluate(bound.Clauses[1].Condition, new Dictionary<IrVarId, IrValue>()).Value!.Boolean, Is.True);
        for (var index = 0; index < contracts.Clauses.Length; index++)
        {
            var original = contracts.Clauses[index];
            var rewritten = bound.Clauses[index];
            Assert.That(rewritten.SourceOperation, Is.EqualTo(original.SourceOperation));
            Assert.That(rewritten.Evidence, Is.EqualTo(original.Evidence));
            Assert.That(rewritten.DiagnosticText, Is.EqualTo(original.DiagnosticText));
            Assert.That(rewritten.SourceSyntax, Is.SameAs(original.SourceSyntax));
            Assert.That(IrTermAnalysis.CollectVariables(rewritten.Condition), Is.Empty);
            Assert.That(IrTermAnalysis.CollectVariables(original.Condition), Is.Not.Empty);
        }
    }

    [Test]
    public void IncompatiblePublicIrReplacementRejectsTheWholeClauseSet()
    {
        var (factory, contracts) = CreateContracts();
        var parameter = contracts.Variables.Single(variable => variable.Role == BoundContractVariableRole.Parameter).Variable;
        var wrongType = factory.Boolean(true);
        Assert.That(factory.GetVariableInfo(parameter).Type, Is.EqualTo(factory.IntegerType));
        Assert.That(wrongType.Type, Is.EqualTo(factory.BooleanType));
        var substitutions = new Dictionary<IrVarId, IrTerm> { [parameter] = wrongType };
        var originalConditions = contracts.Clauses.Select(clause => clause.Condition).ToArray();
        Assert.Throws<ArgumentException>(new Action(() => IrSubstitution.Substitute(factory, contracts.Clauses[0].Condition, substitutions)));
        var bound = ContractClauseSubstitution.Substitute(factory, contracts.Clauses, substitutions);
        Assert.That(bound.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(bound.Clauses, Is.Empty);
        Assert.That(contracts.Clauses.Select(clause => clause.Condition), Is.EqualTo(originalConditions));
    }

    private static (IrFactory Factory, BoundMethodContracts Contracts) CreateContracts()
    {
        var compilation = TestCompilation.Create("ClauseSubstitutionRepresentative", """
            using SharpProof.Attributes;
            public static class Target {
                public static int Read(int value) {
                    Contract.Requires(value > 0);
                    Contract.Ensures(Contract.Result<int>() >= value);
                    return value;
                }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly), Is.True);
        var factory = new IrFactory();
        var bound = new ContractBinder(compilation, factory).Bind(target);
        Assert.That(bound.IsSuccess, Is.True, bound.Failure.ToString());
        Assert.That(bound.Contracts, Is.Not.Null);
        Assert.That(bound.Contracts!.Clauses, Has.Length.EqualTo(2));
        Assert.That(bound.Contracts.Clauses.All(clause => clause.Evidence == BoundContractEvidence.CompilerBoundInvocation), Is.True);
        Assert.That(bound.Contracts.Clauses.All(clause => clause.SourceSyntax != null), Is.True);
        return (factory, bound.Contracts);
    }
}
