using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.ContractForValidation;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ContractForCycleAnalyzerTests
{
    private static readonly string[] DiagnosticIds =
        ["SP0047", "SPCF0009", "SPCF0010"];

    [Test]
    public void ContractForAttributesFromExcludedPartialTreesAreIgnored()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("""
            using SharpProof.Attributes;
            public interface ITarget { int Map(int value); }
            public static partial class Contracts
            {
                [ContractFor(typeof(ITarget))]
                public static int Map(ITarget receiver, int value) => value;
            }
            """, []);
        var excluded = CSharpSyntaxTree.ParseText("""
            using SharpProof.Attributes;
            public static partial class Contracts
            {
                [ContractFor(typeof(ITarget), "malformed")] 
                public static int Extra(ITarget receiver, int value) => value;
            }
            """, (CSharpParseOptions)compilation.SyntaxTrees[0].Options, path: "excluded.cs");
        compilation = compilation.AddSyntaxTrees(excluded);
        var candidates = ContractForValidationEngine.FindCandidates(
            compilation,
            tree => tree.FilePath != "excluded.cs",
            CancellationToken.None);

        var diagnostics = ContractForValidationEngine.Validate(
            compilation,
            candidates,
            CancellationToken.None,
            tree => tree.FilePath != "excluded.cs");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task SelfTargetIsRejectedAndItsBodyIsAnalyzedAsImplementation()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;
            using SharpProof.Attributes;

            [ContractFor(typeof(SelfContracts))]
            public static class SelfContracts
            {
                public static int Map(int value)
                {
                    Contract.Ensures(true);
                    Func<int> unsupported = () => value;
                    return unsupported();
                }
            }
            """);

        AnalyzerTestHost.AssertIds(diagnostics, "SPCF0009", "SP0047");
        Assert.That(
            diagnostics[0].GetMessage(
                System.Globalization.CultureInfo.InvariantCulture),
            Is.EqualTo(
                "Contract companion 'SelfContracts' cannot target itself"));
    }

    [Test]
    public async Task MutualCycleRejectsEachEdgeAndAnalyzesBothImplementations()
    {
        var diagnostics = await AnalyzeAsync("""
            using System;
            using SharpProof.Attributes;

            [ContractFor(typeof(RightContracts))]
            public static class LeftContracts
            {
                public static int Map(int value)
                {
                    Contract.Ensures(true);
                    Func<int> unsupported = () => value;
                    return unsupported();
                }
            }

            [ContractFor(typeof(LeftContracts))]
            public static class RightContracts
            {
                public static int Map(int value)
                {
                    Contract.Ensures(true);
                    Func<int> unsupported = () => value;
                    return unsupported();
                }
            }
            """);

        var cycleDiagnostics = diagnostics
            .Where(static diagnostic => diagnostic.Id == "SPCF0010")
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(
                    ["SPCF0010", "SP0047", "SPCF0010", "SP0047"]));
            Assert.That(
                cycleDiagnostics.Select(static diagnostic =>
                    diagnostic.GetMessage(
                        System.Globalization.CultureInfo.InvariantCulture)),
                Is.EqualTo(
                    [
                        "Contract companion 'LeftContracts' targets " +
                        "'RightContracts' in a ContractFor cycle",
                        "Contract companion 'RightContracts' targets " +
                        "'LeftContracts' in a ContractFor cycle"
                    ]));
        }
    }

    [Test]
    public async Task AcyclicCompanionChainRemainsValid()
    {
        var diagnostics = await AnalyzeAsync("""
            using SharpProof.Attributes;

            [ContractFor(typeof(MiddleContracts))]
            public static class OuterContracts
            {
                public static int Map(int value) => value;
            }

            [ContractFor(typeof(Target))]
            public static class MiddleContracts
            {
                public static int Map(int value) => value;
            }

            public static class Target
            {
                public static int Map(int value) => value;
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void MutualCycleCannotSupplyContractsThroughTheBinder()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            [ContractFor(typeof(RightContracts))]
            public static class LeftContracts
            {
                public static int Map(int value)
                {
                    Contract.Requires(value > 0);
                    return value;
                }
            }

            [ContractFor(typeof(LeftContracts))]
            public static class RightContracts
            {
                public static int Map(int value) => value;
            }
            """,
            []);
        var target = compilation.GetTypeByMetadataName("RightContracts")!
            .GetMembers("Map")
            .OfType<IMethodSymbol>()
            .Single();

        var binding = new SharpProof.Contracts.ContractBinder(
            compilation,
            new SharpProof.Ir.IrFactory()).Bind(target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(binding.IsSuccess, Is.True);
            Assert.That(binding.Contracts, Is.Not.Null);
            Assert.That(binding.Contracts!.UsesCompanion, Is.False);
            Assert.That(binding.Contracts.Clauses, Is.Empty);
            Assert.That(
                SymbolEqualityComparer.Default.Equals(
                    binding.Contracts.Source,
                    target),
                Is.True);
        }
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>>
        AnalyzeAsync(string source)
    {
        return AnalyzerTestHost.AnalyzeAsync(
            source,
            mode: "CONTRACTS",
            enabledIds: DiagnosticIds);
    }
}
