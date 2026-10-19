using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend.Host;

namespace SharpProof.Frontend.Test;

public sealed class ArrayClearAdmissionTests
{
    [TestCase("int[] data", "data", true)]
    [TestCase("int[] data", "(System.Array)data", true)]
    [TestCase("int[] data", "(System.Array)(object)data", false)]
    [TestCase("System.Array data", "data", false)]
    [TestCase("int[,] data", "data", false)]
    [TestCase("object data", "(int[])data", true)]
    [TestCase("decimal[] data", "data", true)]
    public void ClearRequiresTypedSzStorageAndRetainsCheckedDowncasts(string parameters, string value, bool admitted)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(value);
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static void Target(" + parameters + ") { System.Array.Clear(" + value + ", 0, 1); } }");
        var compilation = CSharpCompilation.Create("ClearAdmission", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var invocation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        var operand = CSharpOperationSemantics.TypedArrayClearReceiver(invocation);
        Assert.That(operand != null, Is.EqualTo(admitted));
        if (value == "(int[])data")
        {
            Assert.That(operand, Is.InstanceOf<IConversionOperation>());
        }
    }
}
