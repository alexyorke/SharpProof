using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class ControlFlowExitFactsTests
{
    [Test]
    public void SharedExitFacts_DoesNotReturnCallInSingleStatementBlock_DefinitelyExits()
    {
        var fixture = RoslynTestFixture.CreateCompilation(
            """
            using System;
            using System.Diagnostics.CodeAnalysis;

            internal static class Fixture
            {
                [DoesNotReturn]
                private static void Fail() => throw new InvalidOperationException();

                internal static void M()
                {
                    try
                    {
                    }
                    finally
                    {
                        Fail();
                    }
                }
            }
            """,
            "ControlFlowExitFacts",
            AnalyzerTestHost.GetTrustedPlatformReferences());
        var finallyBlock = fixture.Root.DescendantNodes()
            .OfType<FinallyClauseSyntax>()
            .Single()
            .Block;

        Assert.That(
            SymbolicControlFlowFacts.StatementDefinitelyExits(
                finallyBlock,
                fixture.SemanticModel,
                CancellationToken.None),
            Is.True);
    }
}
