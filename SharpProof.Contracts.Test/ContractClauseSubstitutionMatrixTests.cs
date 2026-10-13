using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractClauseSubstitutionMatrixTests
{
    public static IEnumerable<TestCaseData> CheckedLongCases()
    {
        for (var transformation = 0; transformation < 3; transformation++)
        {
            for (var value = -2L; value <= 3; value++)
            {
                yield return new TestCaseData(transformation, value);
            }
        }
    }

    [TestCaseSource(nameof(CheckedLongCases))]
    public void DistinctCheckedLongArithmeticClausesPreserveBoundedMathematicalMeaning(int transformation, long value)
    {
        var (factory, contracts) = CreateContracts("long", "checked(value + 1L) > 0L");
        var supplied = contracts.Variables.ToDictionary(variable => variable.Variable,
            variable => factory.CreateIntegerValue(factory.GetVariableInfo(variable.Variable).Type,
                variable.Role == BoundContractVariableRole.Result ? -value : value));
        var rewritten = Transform(factory, contracts, supplied, transformation);
        AssertSemanticsAndProvenance(factory, contracts, rewritten.Clauses, supplied,
            rewritten.Values, value + 1 > 0, -value >= value);
    }

    public static IEnumerable<TestCaseData> IntegerCases()
    {
        foreach (var expression in new[] { 0, 1, 2, 4, 5 })
        {
            for (var transformation = 0; transformation < 3; transformation++)
            {
                for (var value = -2L; value <= 3; value++)
                {
                    yield return new TestCaseData(expression, transformation, value);
                }
            }
        }
    }

    [TestCaseSource(nameof(IntegerCases))]
    public void IntegerClausesPreserveMeaningAndProvenanceAcrossOwnedTransformations(
        int expression, int transformation, long value)
    {
        var condition = expression switch
        {
            0 => "value > 0",
            1 => "value == 0",
            2 => "value != 0",
            4 => "value > 0 && value < 3",
            _ => "value > 0 ? true : false"
        };
        var expected = expression switch
        {
            0 => value > 0,
            1 => value == 0,
            2 => value != 0,
            4 => value > 0 && value < 3,
            _ => value > 0
        };
        var (factory, contracts) = CreateContracts("int", condition);
        var supplied = contracts.Variables.ToDictionary(variable => variable.Variable,
            variable => factory.CreateIntegerValue(factory.GetVariableInfo(variable.Variable).Type,
                variable.Role == BoundContractVariableRole.Result ? -value : value));
        var rewritten = Transform(factory, contracts, supplied, transformation);
        AssertSemanticsAndProvenance(factory, contracts, rewritten.Clauses, supplied,
            rewritten.Values, expected, -value >= value);
    }

    [TestCase(0, 0, false)]
    [TestCase(0, 0, true)]
    [TestCase(0, 1, false)]
    [TestCase(0, 1, true)]
    [TestCase(0, 2, false)]
    [TestCase(0, 2, true)]
    [TestCase(1, 0, false)]
    [TestCase(1, 0, true)]
    [TestCase(1, 1, false)]
    [TestCase(1, 1, true)]
    [TestCase(1, 2, false)]
    [TestCase(1, 2, true)]
    [TestCase(2, 0, false)]
    [TestCase(2, 0, true)]
    [TestCase(2, 1, false)]
    [TestCase(2, 1, true)]
    [TestCase(2, 2, false)]
    [TestCase(2, 2, true)]
    public void BooleanClausesPreserveMeaningAcrossOwnedTransformations(int expression, int transformation, bool value)
    {
        var condition = expression switch
        {
            0 => "value",
            1 => "!value",
            _ => "value == true"
        };
        var (factory, contracts) = CreateContracts("bool", condition);
        var supplied = contracts.Variables.ToDictionary(variable => variable.Variable,
            variable => factory.CreateBooleanValue(variable.Role == BoundContractVariableRole.Result || value));
        var rewritten = Transform(factory, contracts, supplied, transformation);
        AssertSemanticsAndProvenance(factory, contracts, rewritten.Clauses, supplied,
            rewritten.Values, expression == 1 ? !value : value, value);
    }

    [TestCase("int", BoundContractVariableRole.Parameter)]
    [TestCase("int", BoundContractVariableRole.Result)]
    [TestCase("int", BoundContractVariableRole.PreState)]
    [TestCase("bool", BoundContractVariableRole.Parameter)]
    [TestCase("bool", BoundContractVariableRole.Result)]
    [TestCase("bool", BoundContractVariableRole.PreState)]
    public void EveryCompilerDerivedRoleRejectsAnIncompatibleOwnedReplacement(string typeName, BoundContractVariableRole role)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        var (factory, contracts) = CreateContracts(typeName, typeName == "int" ? "value > 0" : "value");
        var variable = contracts.Variables.Single(binding => binding.Role == role).Variable;
        IrTerm replacement = typeName == "int" ? factory.Boolean(true) : factory.Integer(1);
        Assert.That(factory.GetVariableInfo(variable).Type,
            Is.EqualTo(typeName == "int" ? factory.IntegerType : factory.BooleanType));
        Assert.That(replacement.Type, Is.Not.EqualTo(factory.GetVariableInfo(variable).Type));
        var substitutions = new Dictionary<IrVarId, IrTerm> { [variable] = replacement };
        Assert.Throws<ArgumentException>(new Action(() =>
            IrSubstitution.Substitute(factory, contracts.Clauses[0].Condition, substitutions)));
        var originalConditions = contracts.Clauses.Select(clause => clause.Condition).ToArray();
        var rejected = ContractClauseSubstitution.Substitute(factory, contracts.Clauses, substitutions);
        Assert.That(rejected.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(rejected.Clauses, Is.Empty);
        Assert.That(contracts.Clauses.Select(clause => clause.Condition), Is.EqualTo(originalConditions));
    }

    [Test]
    public void NoOpAndGenuineEmptyClauseBatchesRemainSuccessful()
    {
        var (factory, contracts) = CreateContracts("int", "value > 0");
        var rewritten = ContractClauseSubstitution.Substitute(factory, contracts.Clauses, new Dictionary<IrVarId, IrTerm>());
        Assert.That(rewritten.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(rewritten.Clauses.Select(clause => clause.Condition),
            Is.EqualTo(contracts.Clauses.Select(clause => clause.Condition)));
        var (emptyFactory, emptyContracts) = CreateContracts("int", condition: null);
        Assert.That(emptyContracts.Clauses, Is.Empty);
        var empty = ContractClauseSubstitution.Substitute(emptyFactory, emptyContracts.Clauses, new Dictionary<IrVarId, IrTerm>());
        Assert.That(empty.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(empty.Clauses, Is.Empty);
    }

    private static (System.Collections.Immutable.ImmutableArray<BoundContractClause> Clauses, Dictionary<IrVarId, IrValue> Values)
        Transform(IrFactory factory, BoundMethodContracts contracts, Dictionary<IrVarId, IrValue> supplied, int transformation)
    {
        var substitutions = new Dictionary<IrVarId, IrTerm>();
        var values = new Dictionary<IrVarId, IrValue>();
        foreach (var variable in contracts.Variables)
        {
            var original = variable.Variable;
            var value = supplied[original];
            if (transformation == 1)
            {
                var renamed = factory.CreateVariable("renamed:" + variable.Role, factory.GetVariableInfo(original).Type);
                substitutions.Add(original, factory.Variable(renamed));
                values.Add(renamed, value);
            }
            else if (transformation == 0 || variable.Role == BoundContractVariableRole.Parameter)
            {
                substitutions.Add(original, value.Type == factory.BooleanType
                    ? factory.Boolean(value.Boolean)
                    : factory.Integer((long)value.Integer));
            }
            else
            {
                values.Add(original, value);
            }
        }
        var rewritten = ContractClauseSubstitution.Substitute(factory, contracts.Clauses, substitutions);
        Assert.That(rewritten.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(rewritten.Clauses, Has.Length.EqualTo(2));
        Assert.That(rewritten.Clauses.SelectMany(clause => IrTermAnalysis.CollectVariables(clause.Condition)).Distinct(),
            Is.EquivalentTo(values.Keys));
        return (rewritten.Clauses, values);
    }

    private static void AssertSemanticsAndProvenance(IrFactory factory, BoundMethodContracts original,
        System.Collections.Immutable.ImmutableArray<BoundContractClause> rewritten, Dictionary<IrVarId, IrValue> originalValues,
        Dictionary<IrVarId, IrValue> rewrittenValues, bool expectedRequires, bool expectedEnsures)
    {
        var interpreter = new IrInterpreter(factory);
        for (var index = 0; index < original.Clauses.Length; index++)
        {
            var source = original.Clauses[index];
            var transformed = rewritten[index];
            var expected = index == 0 ? expectedRequires : expectedEnsures;
            Assert.That(interpreter.Evaluate(source.Condition, originalValues).Value!.Boolean, Is.EqualTo(expected));
            Assert.That(interpreter.Evaluate(transformed.Condition, rewrittenValues).Value!.Boolean, Is.EqualTo(expected));
            Assert.That(transformed.Kind, Is.EqualTo(source.Kind));
            Assert.That(transformed.SourceOperation, Is.EqualTo(source.SourceOperation));
            Assert.That(transformed.Evidence, Is.EqualTo(source.Evidence));
            Assert.That(transformed.DiagnosticText, Is.EqualTo(source.DiagnosticText));
            Assert.That(transformed.SourceSyntax, Is.SameAs(source.SourceSyntax));
            Assert.That(IrTermAnalysis.CollectVariables(source.Condition), Is.Not.Empty);
        }
    }

    private static (IrFactory Factory, BoundMethodContracts Contracts) CreateContracts(string typeName, string? condition)
    {
        var clauses = condition == null ? "" : $$"""
            Contract.Requires({{condition}});
            Contract.Ensures(Contract.Result<{{typeName}}>() {{(typeName == "bool" ? "==" : ">=")}} Contract.Old(value));
            """;
        var compilation = TestCompilation.Create("ClauseSubstitutionMatrix", $$"""
            using SharpProof.Attributes;
            public static class Target {
                public static {{typeName}} Read({{typeName}} value) {
                    {{clauses}}
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
        var additionSyntax = syntax.DescendantNodes().OfType<BinaryExpressionSyntax>()
            .SingleOrDefault(binary => binary.OperatorToken.Text == "+");
        if (additionSyntax != null)
        {
            var addition = (IBinaryOperation)model.GetOperation(additionSyntax)!;
            Assert.That(addition.IsChecked, Is.True);
            Assert.That(addition.Type!.SpecialType, Is.EqualTo(SpecialType.System_Int64));
            Assert.That(SymbolEqualityComparer.Default.Equals(addition.SemanticModel!.Compilation.Assembly, compilation.Assembly), Is.True);
        }
        var factory = new IrFactory();
        var bound = new ContractBinder(compilation, factory).Bind(target);
        Assert.That(bound.IsSuccess, Is.True, bound.Failure.ToString());
        Assert.That(bound.Contracts, Is.Not.Null);
        var contracts = bound.Contracts!;
        Assert.That(contracts.Clauses, Has.Length.EqualTo(condition == null ? 0 : 2));
        Assert.That(contracts.Clauses.All(clause => clause.Evidence == BoundContractEvidence.CompilerBoundInvocation), Is.True);
        Assert.That(contracts.Clauses.All(clause => clause.SourceSyntax != null), Is.True);
        return (factory, contracts);
    }
}
