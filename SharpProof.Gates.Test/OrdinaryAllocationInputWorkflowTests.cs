using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryAllocationInputWorkflowTests
{
    [TestCase("""
        public static class C {
            public static string Target(string value) {
                SharpProof.Attributes.Contract.Requires(value == "ordinary");
                return value;
            }
        }
        """)]
    public async Task ActualSourcePreconditionSelectsAConcreteStringInput(string source)
    {
        var report = await ObserveSourceAsync(source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.NativeProven, Is.EqualTo(1), report.Rows.Single().NativeReason.ToString());
            Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("Confirmed"));
            Assert.That(report.Rows.Single().RuntimeChecks, Is.EqualTo(1));
            Assert.That(report.Rows.Single().AllocatedBytes, Is.Zero);
            Assert.That(report.RuntimeContradictions, Is.Zero);
            Assert.That(report.Passed, Is.True);
        }
    }

    [TestCase("""
        public sealed class Node { }
        public static class C {
            public static bool Target(Node value) {
                SharpProof.Attributes.Contract.Requires(value != null);
                return true;
            }
        }
        """)]
    [TestCase("""
        public sealed class Node { }
        public static class C {
            public static bool Target(Node first, Node alias) {
                SharpProof.Attributes.Contract.Requires(first != null);
                SharpProof.Attributes.Contract.Requires(first == alias);
                return first == alias;
            }
        }
        """)]
    public async Task ActualSameTypeSourcePreconditionsSelectConcreteNominalAndSharedInputs(string source)
    {
        var report = await ObserveSourceAsync(source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.NativeProven, Is.EqualTo(1), report.Rows.Single().NativeReason.ToString());
            Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("Confirmed"));
            Assert.That(report.Rows.Single().RuntimeChecks, Is.EqualTo(1));
            Assert.That(report.Rows.Single().AllocatedBytes, Is.Zero);
            Assert.That(report.RuntimeContradictions, Is.Zero);
            Assert.That(report.Passed, Is.True);
        }
    }

    [TestCase("public static class C<T> where T : class { public static int Target(int value) => value; }")]
    [TestCase("public static class C { public static int Target<T>(int value) where T : class => value; }")]
    public async Task ActualGenericConstraintsSelectOnlyTheAdmissibleReferenceInstantiation(string source)
    {
        var report = await ObserveSourceAsync(source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.NativeProven, Is.EqualTo(1));
            Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("Confirmed"));
            Assert.That(report.Rows.Single().RuntimeChecks, Is.EqualTo(1));
            Assert.That(report.Rows.Single().AllocatedBytes, Is.Zero);
            Assert.That(report.RuntimeContradictions, Is.Zero);
            Assert.That(report.Passed, Is.True);
        }
    }

    [Test]
    public async Task TwoActualScalarProofsReuseTheOriginalReleaseAssembly()
    {
        var report = await ObserveSourceAsync("""
            public static class C {
                public static int First(int value) => value;
                public static int Second(int value) => value + 1;
            }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.NativeProven, Is.EqualTo(2));
            Assert.That(report.Rows, Has.Length.EqualTo(2));
            Assert.That(report.Rows.Select(static row => row.NativeOutcome), Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(report.Rows.Select(static row => row.RuntimeOracle), Is.All.EqualTo("Confirmed"));
            Assert.That(report.Rows.Select(static row => row.RuntimeChecks), Is.All.EqualTo(1));
            Assert.That(report.Rows.Select(static row => row.AllocatedBytes), Is.All.EqualTo(0L));
            Assert.That(report.RuntimeContradictions, Is.Zero);
            Assert.That(report.Passed, Is.True);
        }
    }

    private static Task<NativeEffectOracleReport> ObserveSourceAsync(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-allocation-inputs.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(declaration =>
        {
            var span = declaration.GetLocation().GetLineSpan();
            return new OpenSourceCorpusMethod("ordinary-" + declaration.Identifier.ValueText, "fixture", file.Path,
                span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
                OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
                declaration.Identifier.ValueText, "effects", CorpusVerdict.Proven, CorpusSupport.Supported);
        }).ToImmutableArray();
        var document = new OpenSourceCorpusDocument(2, [], [file], methods);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, TestContext.CurrentContext.CancellationToken, allocations: true);
        return NativeEffectOracleGate.ObserveAsync(compilation, methods.Select(static method => method.Id).ToImmutableArray(),
            RepositoryLayout.FindRoot(), "ordinary-allocation-inputs", methods.Length, allocations: true, cancellationToken: TestContext.CurrentContext.CancellationToken);
    }
}
