using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerSpecificationSignatureDomainWorkflowTests
{
    [TestCase("BooleanConstant", true, 32)]
    [TestCase("BooleanIdentity", true, 32)]
    [TestCase("Int32Sum", true, 32)]
    [TestCase("Int64Sum", true, 64)]
    [TestCase("WidenInt32", false, 0)]
    [TestCase("MixedComparison", false, 0)]
    [TestCase("StringResult", false, 0)]
    [TestCase("StringArgument", false, 0)]
    public void ActualEmittedMethodsRetainTheSingleIntegerWidthSignatureDomain(
        string scenario, bool admitted, int integerWidth)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var (declaration, arguments, expected) = scenario switch
        {
            "BooleanConstant" => ("bool Probe() => true", Array.Empty<object>(), (object)true),
            "BooleanIdentity" => ("bool Probe(bool value) => value", new object[] { true }, (object)true),
            "Int32Sum" => ("int Probe(int left, int right) => left + right", new object[] { 17, 3 }, (object)20),
            "Int64Sum" => ("long Probe(long left, long right) => left + right", new object[] { 4294967297L, 3L }, (object)4294967300L),
            "WidenInt32" => ("long Probe(int value) => value", new object[] { 17 }, (object)17L),
            "MixedComparison" => ("bool Probe(int left, long right) => left < right", new object[] { 17, 4294967297L }, (object)true),
            "StringResult" => ("string Probe(string value) => value", new object[] { "text" }, (object)"text"),
            "StringArgument" => ("bool Probe(string value) => value.Length > 0", new object[] { "text" }, (object)true),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var compilation = AnalyzerTestHost.CreateCompilation(
            "public static class SignatureProbe { public static " + declaration + "; }", [])
            .WithAssemblyName("SignatureDomain_" + scenario);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var image = AnalyzerTestHost.EmitImage(compilation);
        Assert.That(image, Is.Not.Empty);
        var method = (IMethodSymbol)compilation.GetTypeByMetadataName("SignatureProbe")!
            .GetMembers("Probe").Single();
        var runtimeMethod = System.Reflection.Assembly.Load(image)
            .GetType("SignatureProbe", throwOnError: true)!.GetMethod("Probe")!;
        object? result = null;
        Assert.DoesNotThrow((Action)(() => { result = runtimeMethod.Invoke(null, arguments); }));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(runtimeMethod.ReturnType, Is.EqualTo(expected.GetType()));
            Assert.That(method.Parameters, Has.Length.EqualTo(arguments.Length));
            Assert.That(runtimeMethod.GetParameters().Select(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.ParameterType)),
                Is.EqualTo(arguments.Select(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (argument.GetType()))));
        }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var mapped = CompilerSpecificationScalarDomain.TryMapSignature(factory, method,
            out var parameters, out var resultType, out var integerType);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapped, Is.EqualTo(admitted));
            Assert.That(parameters, Has.Length.EqualTo(method.Parameters.Length));
            Assert.That(resultType, Is.EqualTo(CompilerSpecificationScalarDomain.MapType(factory, method.ReturnType)));
            Assert.That(parameters, Is.EqualTo(method.Parameters.Select(parameter =>
                parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (CompilerSpecificationScalarDomain.MapType(factory, parameter.Type)))));
            Assert.That(integerType.IsDefault, Is.EqualTo(!admitted));
        }
        if (admitted)
        {
            Assert.That(factory.GetTypeInfo(integerType).Width, Is.EqualTo(integerWidth));
            Assert.That(factory.GetTypeInfo(integerType).Signed, Is.True);
        }
    }
}
