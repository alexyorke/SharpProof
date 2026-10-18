using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ClosedContractAttributeBindingComponentTests
{
    private static readonly IrExecutionSemantics[] Modes = [IrExecutionSemantics.Legacy, IrExecutionSemantics.Total];
    private static readonly string[] ExpectedDiagnostics = ["[Positive] count", "[InRange(1L, 3L)] count"];
    private static readonly long[] Inputs = [-1L, 2L, 4L];
    [Test]
    public void ValidCompilerAttributesProjectInSourceOrderAndKeepPredicateMeaning()
    {
        var compilation = CreateCompilation();
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var method = GetMethod(compilation, "Read");
        var parameter = method.Parameters.Single();

        foreach (var semantics in Modes)
        {
            var factory = new IrFactory(semantics);
            var variable = factory.CreateVariable("count", factory.IntegerType);
            var value = factory.Variable(variable);
            var valueRequests = 0;
            var operationSites = new List<SyntaxNode?>();
            var result = ClosedContractAttributeBinding.BindValue(
                factory,
                api.Selections,
                parameter.GetAttributes(),
                parameter.Type,
                parameter.RefKind,
                () =>
                {
                    valueRequests++;
                    return value;
                },
                parameter.Name,
                BoundContractKind.Requires,
                total: semantics == IrExecutionSemantics.Total,
                syntax =>
                {
                    operationSites.Add(syntax);
                    return factory.CreateOperation("held-attribute");
                });

            Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
            Assert.That(result.Clauses, Has.Length.EqualTo(2));
            Assert.That(result.Clauses.Select(static clause => clause.DiagnosticText),
                Is.EqualTo(ExpectedDiagnostics));
            Assert.That(result.Clauses.All(static clause => clause.Kind == BoundContractKind.Requires), Is.True);
            Assert.That(result.Clauses.All(static clause => clause.SourceSyntax is not null), Is.True);
            Assert.That(valueRequests, Is.EqualTo(2));
            Assert.That(operationSites, Has.Count.EqualTo(2));
            Assert.That(operationSites.All(static syntax => syntax is not null), Is.EqualTo(semantics == IrExecutionSemantics.Total));

            var interpreter = new IrInterpreter(factory);
            foreach (var input in Inputs)
            {
                var values = new Dictionary<IrVarId, IrValue>
                {
                    [variable] = factory.CreateIntegerValue(factory.IntegerType, input)
                };
                Assert.That(interpreter.Evaluate(result.Clauses[0].Condition, values).Value!.Boolean,
                    Is.EqualTo(input > 0));
                Assert.That(interpreter.Evaluate(result.Clauses[1].Condition, values).Value!.Boolean,
                    Is.EqualTo(input is >= 1 and <= 3));
            }

            if (semantics == IrExecutionSemantics.Total)
            {
                Assert.That(result.Clauses.All(clause => clause.SafeCondition is not null), Is.True);
                Assert.That(result.Clauses.All(clause =>
                    interpreter.Evaluate(clause.SafeCondition!).Value!.Boolean), Is.True);
            }
            else
            {
                Assert.That(result.Clauses.All(static clause => clause.SafeCondition is null), Is.True);
            }
        }

        var returnMethod = GetMethod(compilation, "Name");
        var returnSite = returnMethod.GetReturnTypeAttributes();
        Assert.That(returnSite, Has.Length.EqualTo(1));
        foreach (var semantics in Modes)
        {
            var factory = new IrFactory(semantics);
            var variable = factory.CreateVariable("result", factory.StringType);
            var value = factory.Variable(variable);
            var result = ClosedContractAttributeBinding.BindValue(
                factory,
                api.Selections,
                returnSite,
                returnMethod.ReturnType,
                RefKind.None,
                () => value,
                "result",
                BoundContractKind.Ensures,
                total: semantics == IrExecutionSemantics.Total,
                syntax => factory.CreateOperation("held-return-attribute"));

            Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
            Assert.That(result.Clauses, Has.Length.EqualTo(1));
            Assert.That(result.Clauses[0].Kind, Is.EqualTo(BoundContractKind.Ensures));
            Assert.That(result.Clauses[0].DiagnosticText, Does.Contain("NotNull"));
            Assert.That(result.Clauses[0].SourceSyntax, Is.Not.Null);
            var interpreter = new IrInterpreter(factory);
            Assert.That(interpreter.Evaluate(result.Clauses[0].Condition,
                new Dictionary<IrVarId, IrValue> { [variable] = factory.CreateNullValue(factory.StringType) }).Value!.Boolean,
                Is.False);
            Assert.That(interpreter.Evaluate(result.Clauses[0].Condition,
                new Dictionary<IrVarId, IrValue> { [variable] = factory.CreateStringValue("present") }).Value!.Boolean,
                Is.True);
        }
    }

    [Test]
    public void LegalButIncompatibleSameFactoryValueKeepsModeSpecificFailure()
    {
        var compilation = CreateCompilation();
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var method = GetMethod(compilation, "TypeMismatch");
        var parameter = method.Parameters.Single();

        foreach (var semantics in Modes)
        {
            var factory = new IrFactory(semantics);
            var variable = factory.CreateVariable("incompatible", factory.BooleanType);
            var value = factory.Variable(variable);
            var valueRequests = 0;
            var operationCalls = 0;
            var result = ClosedContractAttributeBinding.BindValue(
                factory,
                api.Selections,
                parameter.GetAttributes(),
                parameter.Type,
                parameter.RefKind,
                () =>
                {
                    valueRequests++;
                    return value;
                },
                parameter.Name,
                BoundContractKind.Requires,
                total: semantics == IrExecutionSemantics.Total,
                syntax =>
                {
                    operationCalls++;
                    return factory.CreateOperation("held-incompatible");
                });

            Assert.That(factory.GetTypeInfo(value.Type).Kind, Is.EqualTo(IrTypeKind.Boolean));
            Assert.That(result.Failure, Is.EqualTo(semantics == IrExecutionSemantics.Total
                ? ContractBindingFailure.UnsupportedExpression
                : ContractBindingFailure.InvalidClosedAttribute));
            Assert.That(result.Clauses, Is.Empty);
            Assert.That(valueRequests, Is.EqualTo(1));
            Assert.That(operationCalls, Is.Zero);
        }
    }

    [Test]
    public void ValidPrefixIsDiscardedWhenLaterRealAttributeIsInvalid()
    {
        var compilation = CreateCompilation();
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var method = GetMethod(compilation, "InvalidAfterPrefix");
        var parameter = method.Parameters.Single();

        foreach (var semantics in Modes)
        {
            var factory = new IrFactory(semantics);
            var variable = factory.CreateVariable("value", factory.IntegerType);
            var value = factory.Variable(variable);
            var result = ClosedContractAttributeBinding.BindValue(
                factory,
                api.Selections,
                parameter.GetAttributes(),
                parameter.Type,
                parameter.RefKind,
                () => value,
                parameter.Name,
                BoundContractKind.Requires,
                total: semantics == IrExecutionSemantics.Total,
                syntax => factory.CreateOperation("held-prefix"));

            Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
            Assert.That(result.Clauses, Is.Empty);
        }
    }

    private static CSharpCompilation CreateCompilation()
    {
        var compilation = TestCompilation.Create("ClosedAttributeBindingHeldProbe", """
            #nullable enable
            using SharpProof.Attributes;
            public static class Target {
                public static void Read([Positive, InRange(1L, 3L)] int count) { }
                [return: NotNull]
                public static string? Name() => null;
                public static void TypeMismatch([Positive] int value) { }
                public static void InvalidAfterPrefix([Positive, NotNull] int value) { }
            }
            """);
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.That(emit.Success, Is.True, string.Join(System.Environment.NewLine,
            emit.Diagnostics.Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))));
        return compilation;
    }

    private static IMethodSymbol GetMethod(Compilation compilation, string name)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(name);
        return compilation.GetTypeByMetadataName("Target")!
            .GetMembers(name).OfType<IMethodSymbol>().Single();
    }
}
