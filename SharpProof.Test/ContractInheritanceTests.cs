using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class ContractInheritanceTests
{
    [Test]
    public async Task InterfaceBehavioralContractsApplyToImplementationsAndCallSites()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using System;
            using SharpProof.Attributes;

            public interface IContract
            {
                [ZeroAllocations]
                object Allocate();

                [AllowedCapabilities(SharpProofCapability.None)]
                void Write();

                [ExpectedComplexity(ComplexityKind.Linear)]
                int Work(int n);

                [Ensures("result > 0")]
                int Positive();

                [Requires("value > 0")]
                void Consume(int value);
            }

            public sealed class Implementation : IContract
            {
                public object Allocate() => new object();

                public void Write() => Console.WriteLine();

                public int Work(int n)
                {
                    var total = 0;
                    for (var i = 0; i < n; i++)
                    for (var j = 0; j < n; j++) total += i + j;
                    return total;
                }

                public int Positive() => -1;

                public void Consume(int value)
                {
                }
            }

            public static class Caller
            {
                public static void Call(Implementation implementation)
                {
                    implementation.Consume(0);
                }
            }
            """,
            globalOptions: ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_suggest_missing_enforce_pure", "false"),
            concurrentAnalysis: true,
            compilationName: "InheritedInterfaceContracts");

        var ids = diagnostics.Select(static diagnostic => diagnostic.Id).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain("SP0013"));
            Assert.That(ids, Does.Contain("SP0015"));
            Assert.That(ids, Does.Contain("SP0021"));
            Assert.That(ids, Does.Contain("SP0018"));
            Assert.That(ids, Does.Contain("SP0027"));
        });
    }

    [Test]
    public async Task InheritedPurityContractMatchesOnlyItsInterfaceOverload()
    {
        const string source = """
            using System;
            using SharpProof.Attributes;

            public interface IContract
            {
                [EnforcePure]
                int Read();

                int Read(int value);
            }

            public sealed class Implementation : IContract
            {
                public int Read()
                {
                    Console.WriteLine();
                    return 0;
                }

                public int Read(int value)
                {
                    Console.WriteLine(value);
                    return value;
                }
            }
            """;

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            globalOptions: ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_suggest_missing_enforce_pure", "false"),
            concurrentAnalysis: true,
            compilationName: "InheritedPurityOverload");

        var purityDiagnostic = diagnostics.Single(
            static diagnostic => diagnostic.Id == "SP0002");
        var implementationStart = source.LastIndexOf("Read()", StringComparison.Ordinal);

        Assert.That(purityDiagnostic.Location.SourceSpan.Start, Is.EqualTo(implementationStart));
    }

    [Test]
    public async Task BasePostconditionAppliesToOverride()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public abstract class Base
            {
                [Ensures("result > 0")]
                public abstract int Value();
            }

            public sealed class Derived : Base
            {
                public override int Value() => -1;
            }
            """,
            globalOptions: ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_suggest_missing_enforce_pure", "false"),
            concurrentAnalysis: true,
            compilationName: "InheritedOverrideContracts");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SP0018"));
    }

    [Test]
    public async Task PureOverrideConflictsWithInheritedImpureContract()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public abstract class Base
            {
                [Impure]
                public abstract int Value();
            }

            public sealed class Derived : Base
            {
                [EnforcePure]
                public override int Value() => 1;
            }
            """,
            globalOptions: ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_suggest_missing_enforce_pure", "false"),
            concurrentAnalysis: true,
            compilationName: "InheritedPurityConflict");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SP0005"));
    }
}
