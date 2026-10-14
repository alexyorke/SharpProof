using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerSpecificationScalarDomainWorkflowTests
{
    [TestCase("bool", true, true, 0)]
    [TestCase("int", 17, true, 32)]
    [TestCase("long", 4294967297L, true, 64)]
    [TestCase("byte", (byte)7, false, 0)]
    [TestCase("uint", 17U, false, 0)]
    [TestCase("double", 1.25, false, 0)]
    [TestCase("string", "text", false, 0)]
    public void ActualEmittedSignaturesRetainTheSpecificationScalarDomain(
        string sourceType, object value, bool admitted, int width)
    {
        ArgumentNullException.ThrowIfNull(value);
        var expectedRuntimeType = value.GetType();
        var compilation = AnalyzerTestHost.CreateCompilation(
            $$"""
            public static class ScalarDomainProbe {
                public static {{sourceType}} Identity({{sourceType}} value) => value;
            }
            """, []).WithAssemblyName("ScalarDomain_" + sourceType);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var image = AnalyzerTestHost.EmitImage(compilation);
        Assert.That(image, Is.Not.Empty);
        var method = (IMethodSymbol)compilation.GetTypeByMetadataName("ScalarDomainProbe")!
            .GetMembers("Identity").Single();
        var runtimeMethod = System.Reflection.Assembly.Load(image)
            .GetType("ScalarDomainProbe", throwOnError: true)!.GetMethod("Identity")!;
        object? runtimeValue = null;
        Assert.DoesNotThrow((Action)(() => { runtimeValue = runtimeMethod.Invoke(null, [value]); }));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(runtimeMethod.ReturnType, Is.EqualTo(expectedRuntimeType));
            Assert.That(runtimeValue, Is.EqualTo(value));
            Assert.That(method.Parameters, Has.Length.EqualTo(1));
            Assert.That(SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, method.ReturnType), Is.True);
        }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var mapped = CompilerSpecificationScalarDomain.MapType(factory, method.ReturnType);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mapped.IsDefault, Is.EqualTo(!admitted));
            Assert.That(CompilerSpecificationScalarDomain.MatchesType(method.ReturnType, IrTypeKind.Boolean),
                Is.EqualTo(sourceType == "bool"));
            Assert.That(CompilerSpecificationScalarDomain.MatchesType(method.ReturnType, IrTypeKind.Integer),
                Is.EqualTo(sourceType is "int" or "long"));
            Assert.That(CompilerSpecificationScalarDomain.MatchesType(method.ReturnType, IrTypeKind.String), Is.False);
        }
        if (admitted)
        {
            var type = factory.GetTypeInfo(mapped);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(type.Kind, Is.EqualTo(sourceType == "bool" ? IrTypeKind.Boolean : IrTypeKind.Integer));
                Assert.That(type.Width, Is.EqualTo(width));
                Assert.That(type.Signed, Is.EqualTo(sourceType != "bool"));
                Assert.That(CompilerSpecificationScalarDomain.MapType(factory, method.Parameters[0].Type), Is.EqualTo(mapped));
            }
        }
    }
}
