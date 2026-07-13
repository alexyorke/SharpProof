using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicInputWitnessTests
{
    [Test]
    public void InputDomainSynthesis_ProjectsRolesRangesStringsCollectionsAndIndexes()
    {
        const string source = """
                              class C
                              {
                                  private int _state;

                                  void M(int value, string text, int[] values, int index)
                                  {
                                      int local = value;
                                      _state = local;
                                  }
                              }
                              """;
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "WitnessDomains",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var valueName = GetSymbolicName(semanticModel, method.ParameterList.Parameters[0]);
        var textName = GetSymbolicName(semanticModel, method.ParameterList.Parameters[1]);
        var valuesName = GetSymbolicName(semanticModel, method.ParameterList.Parameters[2]);
        var indexName = GetSymbolicName(semanticModel, method.ParameterList.Parameters[3]);
        var localDeclarator = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Single(declarator => declarator.Identifier.ValueText == "local");
        var localName = SymbolicFactFactory.GetSmtVariableName(
            semanticModel.GetDeclaredSymbol(localDeclarator)!);
        var value = new SmtVariable(valueName, SmtValueKind.Int);
        var textReference = new SmtVariable(textName, SmtValueKind.Reference);
        var text = new SmtVariable(textName + ".String", SmtValueKind.String);
        var values = new SmtVariable(valuesName, SmtValueKind.Reference);
        var valuesLength = new SmtVariable(valuesName + ".Length", SmtValueKind.Int);
        var index = new SmtVariable(indexName, SmtValueKind.Int);
        var local = new SmtVariable(localName, SmtValueKind.Int);
        var receiverState = new SmtVariable("this._state", SmtValueKind.Int);
        var formulas = new SmtFormula[]
        {
            Compare(SmtBinaryOperator.GreaterThanOrEqual, value, new SmtIntegerConstant(2)),
            Compare(SmtBinaryOperator.LessThanOrEqual, value, new SmtIntegerConstant(9)),
            Compare(SmtBinaryOperator.NotEqual, textReference, new SmtNullConstant()),
            Compare(
                SmtBinaryOperator.GreaterThanOrEqual,
                new SmtStringLengthTerm(text),
                new SmtIntegerConstant(3)),
            new SmtStringStartsWithFormula(text, new SmtStringConstant("pre")),
            new SmtStringEndsWithFormula(text, new SmtStringConstant("end")),
            new SmtStringContainsFormula(text, new SmtStringConstant("mid")),
            new SmtRegexMatchFormula(text, "^pre.*end$"),
            Compare(SmtBinaryOperator.GreaterThanOrEqual, valuesLength, new SmtIntegerConstant(1)),
            Compare(SmtBinaryOperator.GreaterThanOrEqual, index, new SmtIntegerConstant(0)),
            Compare(SmtBinaryOperator.LessThan, index, valuesLength),
            Compare(SmtBinaryOperator.Equal, local, new SmtIntegerConstant(4)),
            Compare(SmtBinaryOperator.GreaterThanOrEqual, receiverState, new SmtIntegerConstant(0))
        };
        var witness = new SmtSatisfyingWitness(
            SmtWitnessStatus.Exact,
            "satisfying_model",
            new SmtModelAssignment[]
            {
                new(valueName, SmtValueKind.Int, "4", IntegerValue: 4),
                new(textName, SmtValueKind.Reference, "ref!0", IsNull: false,
                    Status: SmtWitnessStatus.Approximate),
                new(textName + ".String", SmtValueKind.String, "premidend", StringValue: "premidend"),
                new(valuesName, SmtValueKind.Reference, "ref!1", IsNull: false,
                    Status: SmtWitnessStatus.Approximate),
                new(valuesName + ".Length", SmtValueKind.Int, "5", IntegerValue: 5),
                new(indexName, SmtValueKind.Int, "0", IntegerValue: 0),
                new(localName, SmtValueKind.Int, "4", IntegerValue: 4),
                new("this._state", SmtValueKind.Int, "4", IntegerValue: 4)
            });

        var result = SymbolicInputWitnessFactory.Create(
            witness,
            formulas,
            semanticModel,
            method.Body!.Statements.Last().SpanStart,
            SymbolicWitnessStatus.Unsupported,
            "missing");

        var valueDomain = result.DomainSummary.Domains.Single(domain => domain.Name == "value");
        var textDomain = result.DomainSummary.Domains.Single(domain => domain.Name == "text");
        var valuesDomain = result.DomainSummary.Domains.Single(domain => domain.Name == "values");
        var indexDomain = result.DomainSummary.Domains.Single(domain => domain.Name == "index");
        var localDomain = result.DomainSummary.Domains.Single(domain => domain.Name == "local");
        var receiverDomain = result.DomainSummary.Domains.Single(domain => domain.Name == "this._state");
        Assert.Multiple(() =>
        {
            Assert.That(valueDomain.Role, Is.EqualTo(SymbolicInputRole.Parameter));
            Assert.That(valueDomain.IntegerRange?.Minimum, Is.EqualTo(2));
            Assert.That(valueDomain.IntegerRange?.Maximum, Is.EqualTo(9));
            Assert.That(textDomain.Role, Is.EqualTo(SymbolicInputRole.Parameter));
            Assert.That(textDomain.Nullness, Is.EqualTo(SymbolicNullness.NotNull));
            Assert.That(textDomain.StringLengthRange?.Minimum, Is.EqualTo(3));
            Assert.That(textDomain.RequiredPrefixes, Does.Contain("pre"));
            Assert.That(textDomain.RequiredSuffixes, Does.Contain("end"));
            Assert.That(textDomain.RequiredSubstrings, Does.Contain("mid"));
            Assert.That(textDomain.RegularExpressions, Does.Contain("^pre.*end$"));
            Assert.That(textDomain.Status, Is.EqualTo(SymbolicWitnessStatus.Approximate));
            Assert.That(valuesDomain.DomainKind, Is.EqualTo(SymbolicInputDomainKind.Collection));
            Assert.That(valuesDomain.CollectionLengthRange?.Minimum, Is.EqualTo(1));
            Assert.That(indexDomain.DomainKind, Is.EqualTo(SymbolicInputDomainKind.Index));
            Assert.That(indexDomain.IntegerRange?.Minimum, Is.EqualTo(0));
            Assert.That(indexDomain.RelatedCollection, Is.EqualTo("values"));
            Assert.That(localDomain.Role, Is.EqualTo(SymbolicInputRole.Local));
            Assert.That(receiverDomain.Role, Is.EqualTo(SymbolicInputRole.ReceiverState));
        });
    }

    [Test]
    public void InputDomainSynthesis_MarksDisjunctionAndUnsupportedTermShapes()
    {
        var value = new SmtVariable("value", SmtValueKind.Int);
        var flag = new SmtVariable("flag", SmtValueKind.Bool);
        var formulas = new SmtFormula[]
        {
            new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                Compare(SmtBinaryOperator.LessThan, value, new SmtIntegerConstant(0)),
                Compare(SmtBinaryOperator.GreaterThan, value, new SmtIntegerConstant(10))),
            new SmtConditionalFormula(flag, value, new SmtIntegerConstant(0), SmtValueKind.Int)
        };

        var result = SymbolicInputWitnessFactory.Create(
            null,
            formulas,
            null,
            0,
            SymbolicWitnessStatus.Unsupported,
            "model_unavailable");

        Assert.That(result.Status, Is.EqualTo(SymbolicWitnessStatus.Unsupported));
        Assert.That(result.DomainSummary.HasApproximation, Is.True);
        Assert.That(result.DomainSummary.HasUnsupportedDomains, Is.True);
        Assert.That(result.DomainSummary.Domains.SelectMany(domain => domain.Predicates)
            .Any(predicate => predicate.Kind == SymbolicDomainPredicateKind.Alternative), Is.True);
        Assert.That(result.DomainSummary.Domains.SelectMany(domain => domain.Predicates)
            .Any(predicate => predicate.Kind == SymbolicDomainPredicateKind.Unsupported), Is.True);
    }

    private static string GetSymbolicName(SemanticModel semanticModel, ParameterSyntax parameter)
    {
        return SymbolicFactFactory.GetSmtVariableName(semanticModel.GetDeclaredSymbol(parameter)!);
    }

    private static SmtBinaryFormula Compare(
        SmtBinaryOperator op,
        SmtFormula left,
        SmtFormula right)
    {
        return new SmtBinaryFormula(op, left, right);
    }
}
