using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TotalInputBindingTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void InputProjectionPreservesParameterStorageAndReceiverOrder(bool isStatic)
    {
        var tree = CSharpSyntaxTree.ParseText(
            $"public sealed class Subject {{ public {(isStatic ? "static " : "")}int Target(int value) => value; }}");
        var compilation = CSharpCompilation.Create("InputBindings", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var target = (IMethodSymbol)compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").Single();
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target);
        context.ModelReceiver();
        var parameter = context.Parameters.Single();
        var inputs = context.Inputs;

        Assert.That(inputs, Has.Length.EqualTo(isStatic ? 1 : 2));
        Assert.That(inputs[0], Is.SameAs(parameter.Input));
        Assert.That(inputs[0].Entry, Is.EqualTo(parameter.Entry));
        Assert.That(inputs[0].Current, Is.EqualTo(parameter.Current));
        Assert.That(inputs[0].PreState, Is.EqualTo(parameter.PreState));
        Assert.That(context.EntryVariables[0], Is.EqualTo(parameter.Entry));
        Assert.That(parameter.Parameter, Is.SameAs(target.Parameters[0]));
        if (!isStatic)
        {
            Assert.That(inputs[1], Is.SameAs(context.Receiver));
            Assert.That(context.EntryVariables[1], Is.EqualTo(context.Receiver!.Entry));
        }
    }
}
