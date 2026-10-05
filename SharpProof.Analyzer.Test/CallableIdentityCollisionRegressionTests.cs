using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Testing;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CallableIdentityCollisionRegressionTests
{
    [Test]
    public void FileLocalTypesWithTheSameNameHaveDistinctManifestIds()
    {
        var first = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            file class Helper
            {
                [EnforcePure]
                public static int Check(long value) => (int)value;
            }
            """,
            []);
        var secondTree = CSharpSyntaxTree.ParseText(
            """
            using SharpProof.Attributes;
            file class Helper
            {
                [EnforcePure]
                public static int Check(long value) => (int)value;
            }
            """,
            (CSharpParseOptions)first.SyntaxTrees.Single().Options,
            "B.cs");
        var compilation = first.AddSyntaxTrees(secondTree);

        var result = new ClaimManifestBuilder(compilation).Build();
        Assert.That(result.Manifest.Callables, Has.Length.EqualTo(2));
        Assert.That(
            result.Manifest.Callables.Select(static callable => callable.CallableId)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            Is.EqualTo(2));
    }

    [Test]
    public void ExtensionBlocksAndFunctionPointerOverloadsHaveDistinctManifestIds()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public static class Extensions
            {
                extension(int value)
                {
                    [EnforcePure]
                    public long Twice() => value * 2L;
                }

                extension(long value)
                {
                    [EnforcePure]
                    public long Twice() => value * 2L;
                }
            }

            public static unsafe class FunctionPointers
            {
                [EnforcePure]
                public static int Invoke(delegate* managed<int> callback) => callback();

                [EnforcePure]
                public static int Invoke(delegate* unmanaged[Cdecl]<int> callback) => 0;
            }
            """,
            []);
        compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(true));

        var result = new ClaimManifestBuilder(compilation).Build();
        var callables = result.Manifest.Callables;
        Assert.That(callables, Has.Length.EqualTo(4));
        Assert.That(
            callables.Select(static callable => callable.CallableId)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            Is.EqualTo(callables.Length));
    }
}
