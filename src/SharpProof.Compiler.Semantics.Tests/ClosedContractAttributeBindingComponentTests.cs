using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ClosedContractAttributeBindingComponentTests
{
    private static readonly ImmutableArray<string> ExpectedDiagnostics = ["[Positive] count", "[InRange(1L, 3L)] count"];
    private static readonly ImmutableArray<long> Inputs = [-1L, 2L, 4L];
    [Test]
    public void ValidCompilerAttributesProjectInSourceOrderAndKeepPredicateMeaning()
    {
        var compilation = CreateCompilation();
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var method = GetMethod(compilation, "Read");
        var parameter = method.Parameters.Single();

        var factory = new IrFactory();
        var variable = factory.CreateVariable("count", factory.IntegerType);
        var value = factory.Variable(variable);
        var valueRequests = 0;
        var operationSiteBuilder = ImmutableArray.CreateBuilder<SyntaxNode?>();
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
            syntax =>
            {
                operationSiteBuilder.Add(syntax);
                return factory.CreateOperation("held-attribute");
            });
        var operationSites = operationSiteBuilder.ToImmutable();

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Clauses, Has.Length.EqualTo(2));
        Assert.That(result.Clauses.Select(static clause => clause.DiagnosticText),
            Is.EqualTo(ExpectedDiagnostics));
        Assert.That(result.Clauses.All(static clause => clause.Kind == BoundContractKind.Requires), Is.True);
        Assert.That(result.Clauses.All(static clause => clause.SourceSyntax is not null), Is.True);
        Assert.That(valueRequests, Is.EqualTo(2));
        Assert.That(operationSites, Has.Length.EqualTo(2));
        Assert.That(operationSites.All(static syntax => syntax is not null), Is.True);

        var interpreter = new IrInterpreter(factory);
        foreach (var input in Inputs)
        {
            var values = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(variable, factory.CreateIntegerValue(factory.IntegerType, input));
            Assert.That(interpreter.Evaluate(result.Clauses[0].Condition, values).Value!.Boolean,
                Is.EqualTo(input > 0));
            Assert.That(interpreter.Evaluate(result.Clauses[1].Condition, values).Value!.Boolean,
                Is.EqualTo(input is >= 1 and <= 3));
        }

        Assert.That(result.Clauses.All(clause => clause.SafeCondition is not null), Is.True);
        Assert.That(result.Clauses.All(clause =>
            interpreter.Evaluate(clause.SafeCondition!).Value!.Boolean), Is.True);

        var returnMethod = GetMethod(compilation, "Name");
        var returnSite = returnMethod.GetReturnTypeAttributes();
        Assert.That(returnSite, Has.Length.EqualTo(1));
        var returnFactory = new IrFactory();
        var returnVariable = returnFactory.CreateVariable("result", returnFactory.StringType);
        var returnValue = returnFactory.Variable(returnVariable);
        var returnResult = ClosedContractAttributeBinding.BindValue(
            returnFactory,
            api.Selections,
            returnSite,
            returnMethod.ReturnType,
            RefKind.None,
            () => returnValue,
            "result",
            BoundContractKind.Ensures,
            syntax => returnFactory.CreateOperation("held-return-attribute"));

        Assert.That(returnResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(returnResult.Clauses, Has.Length.EqualTo(1));
        Assert.That(returnResult.Clauses[0].Kind, Is.EqualTo(BoundContractKind.Ensures));
        Assert.That(returnResult.Clauses[0].DiagnosticText, Does.Contain("NonNull"));
        Assert.That(returnResult.Clauses[0].SourceSyntax, Is.Not.Null);
        var returnInterpreter = new IrInterpreter(returnFactory);
        Assert.That(returnInterpreter.Evaluate(returnResult.Clauses[0].Condition,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(returnVariable, returnFactory.CreateNullValue(returnFactory.StringType))).Value!.Boolean,
            Is.False);
        Assert.That(returnInterpreter.Evaluate(returnResult.Clauses[0].Condition,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(returnVariable, returnFactory.CreateStringValue("present"))).Value!.Boolean,
            Is.True);
    }

    [Test]
    public void LegalButIncompatibleSameFactoryValueKeepsModeSpecificFailure()
    {
        var compilation = CreateCompilation();
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var method = GetMethod(compilation, "TypeMismatch");
        var parameter = method.Parameters.Single();

        var factory = new IrFactory();
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
            syntax =>
            {
                operationCalls++;
                return factory.CreateOperation("held-incompatible");
            });

        Assert.That(factory.GetTypeInfo(value.Type).Kind, Is.EqualTo(IrTypeKind.Boolean));
        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(result.Clauses, Is.Empty);
        Assert.That(valueRequests, Is.EqualTo(1));
        Assert.That(operationCalls, Is.Zero);
    }

    [Test]
    public void ValidPrefixIsDiscardedWhenLaterRealAttributeIsInvalid()
    {
        var compilation = CreateCompilation();
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var method = GetMethod(compilation, "InvalidAfterPrefix");
        var parameter = method.Parameters.Single();

        var factory = new IrFactory();
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
            syntax => factory.CreateOperation("held-prefix"));

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
        Assert.That(result.Clauses, Is.Empty);
    }

    private static CSharpCompilation CreateCompilation()
    {
        var compilation = TestCompilation.Create("ClosedAttributeBindingHeldProbe", """
            #nullable enable
            using SharpProof.Attributes;
            public static class Target {
                public static void Read([Positive, InRange(1L, 3L)] int count) { }
                [return: NonNull]
                public static string? Name() => null;
                public static void TypeMismatch([Positive] int value) { }
                public static void InvalidAfterPrefix([Positive, NonNull] int value) { }
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
