using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ClosedContractAttributeBindingBoundaryTests
{
    [Test]
    public void MetadataProjectionRejectsAnInvalidCompilerAttributeConstructorBeforeCreatingAValue()
    {
        var admitted = TestCompilation.Create("InvalidAttributeConstructorProjection",
            "using SharpProof.Attributes; public static class Target { public static int Read([Positive] int value) => value; }");
        var original = admitted.SyntaxTrees.Single();
        var invalid = original.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(
            original.GetText().ToString().Replace("[Positive]", "[Positive(0)]", StringComparison.Ordinal)));
        var compilation = admitted.ReplaceSyntaxTree(original, invalid);
        Assert.That(compilation.GetDiagnostics().Any(static diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.True);
        var target = compilation.GetTypeByMetadataName("Target")!.GetMembers("Read").OfType<IMethodSymbol>().Single();
        var parameter = target.Parameters.Single();
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var attribute = parameter.GetAttributes().Single();
        Assert.That(ClosedContractAttributeValidator.Validate(attribute, parameter.Type, parameter.RefKind,
            api.Selections, includeUnsigned64: true).IsValid, Is.True);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var bound = ClosedContractAttributeBinding.BindMetadataValueSites(factory, api.Selections,
            [new ContractMetadataValueSite(parameter, static () =>
                throw new InvalidOperationException("Invalid attributes must not create an IR value."))],
            TestContext.CurrentContext.CancellationToken);
        Assert.That(bound.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
    }

}
