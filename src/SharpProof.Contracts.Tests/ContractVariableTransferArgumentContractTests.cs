using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractVariableTransferArgumentContractTests
{
    [TestCase("factory")]
    [TestCase("source")]
    [TestCase("createPreState")]
    public void NullRequiredArgumentIsRejectedWithItsParameterName(string parameterName)
    {
        System.ArgumentNullException.ThrowIfNull(parameterName);
        const string sourceText = "public static class Frames { public static void Source() {} public static void Destination() {} }";
        var compilation = TestCompilation.Create("ContractVariableTransferArgumentContract", sourceText);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var factory = new IrFactory();
        var type = compilation.GetTypeByMetadataName("Frames")!;
        var source = type.GetMembers("Source").OfType<IMethodSymbol>().Single();
        var destination = type.GetMembers("Destination").OfType<IMethodSymbol>().Single();
        var binder = new ContractBinder(compilation, factory);
        var sourceFrame = binder.Bind(source);
        var destinationFrame = binder.Bind(destination);
        Assert.That(sourceFrame.IsSuccess, Is.True, sourceFrame.Failure.ToString());
        Assert.That(destinationFrame.IsSuccess, Is.True, destinationFrame.Failure.ToString());
        Assert.That(sourceFrame.Contracts!.Variables, Is.Empty);
        Assert.That(destinationFrame.Contracts!.Variables, Is.Empty);
        Assert.That(SymbolEqualityComparer.Default.Equals(source.ContainingAssembly, compilation.Assembly), Is.True);
        Func<IrVarId, IrVarId> createPreState = current => current.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(current)) : (factory.CreateVariable(
            "argument-contract-prestate", factory.GetVariableInfo(current).Type));

        var exception = Assert.Throws<ArgumentNullException>((Action)(() => ContractVariableTransfer.CreateSubstitutions(
            parameterName == "factory" ? null! : factory,
            parameterName == "source" ? null! : sourceFrame.Contracts.Source,
            destinationFrame.Contracts.UsesCompanion,
            [],
            null,
            [],
            [],
            destinationFrame.Contracts.Variables,
            parameterName == "createPreState" ? null! : createPreState)));

        Assert.That(exception!.ParamName, Is.EqualTo(parameterName));
    }
}
