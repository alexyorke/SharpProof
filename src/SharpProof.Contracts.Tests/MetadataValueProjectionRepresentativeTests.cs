using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class MetadataValueProjectionRepresentativeTests
{
    private static readonly ImmutableArray<string> SingleAttributeNames = ["PositiveAttribute", "InRangeAttribute"];
    private static readonly ImmutableArray<int> SingleParameterOrdinals = [0, 0];
    private static readonly ImmutableArray<int> PairParameterOrdinals = [0, 1];

    [Test]
    public void RealMetadataAnnotationsRetainFacadeOrderAndIndependentPredicateTruth()
    {
        var (compilation, target) = Import("Single");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var selections = ContractSelectionInventory.ForCompilation(compilation);
        var parameter = target.Parameters.Single();
        var variable = context.Parameters.Single().Entry;
        var value = factory.Variable(variable);
        var publicResult = new ContractBinder(compilation, factory).BindTotalMetadataRequires(context, TestContext.CurrentContext.CancellationToken);
        var result = ClosedContractAttributeBinding.BindMetadataValueSites(factory, selections,
            [new ContractMetadataValueSite(parameter, () => value)], TestContext.CurrentContext.CancellationToken);
        Assert.That(publicResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Attribute.AttributeClass!.Name)),
            Is.EqualTo(SingleAttributeNames));
        Assert.That(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.ParameterOrdinal)), Is.EqualTo(SingleParameterOrdinals));
        Assert.That(publicResult.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Validation.Kind)),
            Is.EqualTo(result.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Validation.Kind))));
        var interpreter = new IrInterpreter(factory);
        foreach (var input in ImmutableArray.Create(-3, -2, 0, 1, 2, 3))
        {
            var values = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(variable, factory.CreateIntegerValue(value.Type, (long)input));
            var expected = ImmutableArray.Create(input > 0, input >= -2 && input <= 2);
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.That(interpreter.Evaluate(result.Clauses[index].Value, values).Value!.Boolean, Is.EqualTo(expected[index]));
                Assert.That(interpreter.Evaluate(publicResult.Clauses[index].Value, values).Value!.Boolean, Is.EqualTo(expected[index]));
                Assert.That(interpreter.Evaluate(result.Clauses[index].SafeCondition, values).Value!.Boolean, Is.True);
            }
        }
    }

    [Test]
    public void ActualIntegerAnnotationRejectsIncompatibleOwnedBooleanValue()
    {
        var (compilation, target) = Import("Single");
        var parameter = target.Parameters.Single();
        Assert.That(parameter.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var selections = ContractSelectionInventory.ForCompilation(compilation);
        var publicResult = new ContractBinder(compilation, factory).BindTotalMetadataRequires(context, TestContext.CurrentContext.CancellationToken);
        Assert.That(publicResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(publicResult.Clauses, Has.Length.EqualTo(2));
        var wrongValue = factory.Boolean(true);
        Assert.That(factory.GetTypeInfo(wrongValue.Type).Kind, Is.EqualTo(IrTypeKind.Boolean));
        var result = ClosedContractAttributeBinding.BindMetadataValueSites(factory, selections,
            [new ContractMetadataValueSite(parameter, () => wrongValue)], TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(result.Clauses, Is.Empty);
    }

    [Test]
    public void LateIncompatibleValuePublishesNoValidMetadataPrefix()
    {
        var (compilation, target) = Import("Pair");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var selections = ContractSelectionInventory.ForCompilation(compilation);
        var publicResult = new ContractBinder(compilation, factory).BindTotalMetadataRequires(context, TestContext.CurrentContext.CancellationToken);
        Assert.That(publicResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(publicResult.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.ParameterOrdinal)), Is.EqualTo(PairParameterOrdinals));
        var prefix = new ContractMetadataValueSite(target.Parameters[0],
            () => factory.Variable(context.Parameters[0].Entry));
        var validPrefix = ClosedContractAttributeBinding.BindMetadataValueSites(factory, selections, [prefix], TestContext.CurrentContext.CancellationToken);
        Assert.That(validPrefix.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(validPrefix.Clauses, Has.Length.EqualTo(1));
        var result = ClosedContractAttributeBinding.BindMetadataValueSites(factory, selections,
            [prefix, new ContractMetadataValueSite(target.Parameters[1], () => factory.Boolean(false))], TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(result.Clauses, Is.Empty);
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Target) Import(string methodName)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        var library = TestCompilation.Create("MetadataValueProjectionLibrary", """
            using SharpProof.Attributes;
            public static class MetadataValueSource {
                public static bool Single([Positive, InRange(-2L, 2L)] int value) => value > 0;
                public static bool Pair([InRange(-2L, 2L)] int first, [InRange(-2L, 2L)] int second) => first == second;
            }
            """);
        TestCompilation.AssertNoErrors(library);
        using var image = new MemoryStream();
        var emit = library.Emit(image);
        Assert.That(emit.Success, Is.True, string.Join(Environment.NewLine, emit.Diagnostics));
        var compilation = TestCompilation.Create("MetadataValueProjectionConsumer", "public static class Consumer {}")
            .AddReferences(MetadataReference.CreateFromImage(ImmutableArray.Create(image.GetBuffer(), 0, checked((int)image.Length))));
        TestCompilation.AssertNoErrors(compilation);
        using var consumerImage = new MemoryStream();
        var consumerEmit = compilation.Emit(consumerImage);
        Assert.That(consumerEmit.Success, Is.True, string.Join(Environment.NewLine, consumerEmit.Diagnostics));
        var target = compilation.GetTypeByMetadataName("MetadataValueSource")!.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        Assert.That(target.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(target.Parameters.Select(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.Ordinal)), Is.EqualTo(Enumerable.Range(0, target.Parameters.Length)));
        Assert.That(target.Parameters.All(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.Type.SpecialType == SpecialType.System_Int32 && parameter.RefKind == RefKind.None)), Is.True);
        return (compilation, target);
    }
}
