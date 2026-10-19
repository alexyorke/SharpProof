using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreFunctionPointerDispatchDomainTests
{
    private static readonly ImmutableArray<(string TypeName, int Expected)> DispatchControls = [("Conflicting", 1), ("Matching", 0)];

    private const string Source = """
        using SharpProof.Attributes;
        public unsafe static class SignatureHolder {
            public static int Shape(delegate*<int, int> callback) => 0;
        }
        public interface Open { void Run(int value); }
        public interface Strict { void Run([Positive] int value); }
        public class Conflicting : Open, Strict {
            public void Run([Positive] int value) { }
        }
        public class Matching : Strict {
            public void Run([Positive] int value) { }
        }
        """;

    [Test]
    public async Task ActualSignatureAndDispatchMethodsRetainWholeValidationDomain()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(Source, ["SP0024"]);
        compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));

        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts", analyzer: new SharpProofAnalyzer(factory));
        AnalyzerTestHost.AssertIds(diagnostics, ["SP0024"]);
        Assert.That(factory.CreateCount, Is.EqualTo(1));
        Assert.That(factory.Session, Is.Not.Null);
        var session = factory.Session!;
        Assert.That(session.Configuration.ContractsEnabled, Is.True);
        var actual = session.Compilation;
        var owner = actual.GetTypeByMetadataName("SignatureHolder")!.GetMembers("Shape").OfType<IMethodSymbol>().Single();
        Assert.That(owner.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(owner.ContainingAssembly, actual.Assembly), Is.True);
        var pointer = owner.Parameters.Single().Type as IFunctionPointerTypeSymbol;
        Assert.That(pointer, Is.Not.Null);
        var signature = pointer!.Signature;
        Assert.That(signature.MethodKind, Is.EqualTo(MethodKind.FunctionPointerSignature));
        Assert.That(signature.OverriddenMethod, Is.Null);
        Assert.That(signature.ContainingType, Is.Null);
        Assert.That(signature.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(signature.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));

        var reportsBuilder = ImmutableArray.CreateBuilder<Diagnostic>();
        OverridePreconditionDiagnostics.Validate(signature, session, reportsBuilder.Add, TestContext.CurrentContext.CancellationToken);
        Assert.That(reportsBuilder.Count, Is.Zero);
        foreach (var (typeName, expected) in DispatchControls)
        {
            var method = actual.GetTypeByMetadataName(typeName)!.GetMembers("Run").OfType<IMethodSymbol>().Single();
            Assert.That(method.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
            Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, actual.Assembly), Is.True);
            var bound = session.BindRequires(method);
            Assert.That(bound.IsSuccess, Is.True);
            Assert.That(bound.Contracts, Is.Not.Null);
            Assert.That(bound.Contracts!.Clauses, Is.Not.Empty);
            var resultBuilder = ImmutableArray.CreateBuilder<Diagnostic>();
            OverridePreconditionDiagnostics.Validate(method, session, resultBuilder.Add, TestContext.CurrentContext.CancellationToken);
            var result = resultBuilder.ToImmutable();
            Assert.That(result, Has.Length.EqualTo(expected));
            if (expected != 0)
            {
                Assert.That(result.Single().Id, Is.EqualTo("SP0024"));
                AnalyzerTestHost.AssertMessageContains(result.Single(), "not visible through base or interface dispatch");
            }
        }
        var reports = reportsBuilder.ToImmutable();
        Assert.That(reports, Is.Empty);
    }
}
