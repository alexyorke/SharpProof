using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class RequiresCallSiteDiscoveryTests
{
    [Test]
    public void PotentialCallScreeningUsesOnlyCallsOwnedByTheCallable()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            public sealed class Subject {
                public Subject(int value) {
                }

                private static int Plain() => 0;

                private static int Contracted(int value) => value;

                public static int OnlyPlain() => Plain();

                public static int CallsContracted() => Contracted(-1);

                public static Subject Creates() => new Subject(-1);

                public static int NestedOnly() {
                    System.Func<int> nested = () => Contracted(-1);
                    return Plain();
                }
            }
            """,
            []);
        var tree = compilation.SyntaxTrees.Single();
        var root = tree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(tree);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(HasPotentialCall("OnlyPlain"), Is.False);
            Assert.That(HasPotentialCall("CallsContracted"), Is.True);
            Assert.That(HasPotentialCall("Creates"), Is.True);
            Assert.That(HasPotentialCall("NestedOnly"), Is.False);
        }

        bool HasPotentialCall(string methodName)
        {
            var declaration = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method =>
                    method.Identifier.ValueText == methodName);
            var caller = (IMethodSymbol)semanticModel
                .GetDeclaredSymbol(declaration)!;
            var discovery = new RequiresCallSiteDiscovery(
                caller,
                declaration,
                semanticModel,
                CancellationToken.None);
            return discovery.HasPotentialCallSite(
                static target =>
                    target.Name == "Contracted" ||
                    target.MethodKind == MethodKind.Constructor);
        }
    }

    [Test]
    public void PotentialPreconditionScreenUsesBindingAndFailsClosed()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public static class Targets {
                public static int Plain(int value) => value;

                public static int Direct(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static void InvalidOut(
                    [Positive] out int value) {
                    value = 1;
                }
            }

            public static class InitializedTarget {
                static InitializedTarget() {
                }

                public static int Plain(int value) => value;
            }

            public sealed class CompanionTarget {
                public int Read(int value) => value;
            }

            [ContractFor(typeof(CompanionTarget))]
            public static class CompanionTargetContracts {
                public static int Read(
                    CompanionTarget receiver,
                    int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """,
            []);
        var session = new AnalyzerSession(
            compilation,
            AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                session.HasPotentialCallPreconditions(
                    GetMethod(compilation, "Targets", "Plain")),
                Is.False);
            Assert.That(
                session.HasPotentialCallPreconditions(
                    GetMethod(compilation, "Targets", "Direct")),
                Is.True);
            Assert.That(
                session.HasPotentialCallPreconditions(
                    GetMethod(compilation, "Targets", "InvalidOut")),
                Is.True);
            Assert.That(
                session.HasPotentialCallPreconditions(
                    GetMethod(
                        compilation,
                        "InitializedTarget",
                        "Plain")),
                Is.True);
            Assert.That(
                session.HasPotentialCallPreconditions(
                    GetMethod(
                        compilation,
                        "CompanionTarget",
                        "Read")),
                Is.True);
        }
    }

    [Test]
    public void PotentialPreconditionScreenFailsClosedWithoutTrustedApiIdentity()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            namespace SharpProof.Attributes {
                public static class Contract {
                    public static void Requires(bool condition) {
                    }
                }
            }

            public static class Target {
                public static int Plain(int value) => value;
            }
            """,
            []);
        var session = new AnalyzerSession(
            compilation,
            AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None);

        Assert.That(
            session.HasPotentialCallPreconditions(
                GetMethod(compilation, "Target", "Plain")),
            Is.True);
    }

    [Test]
    public void PotentialCallScreenFailsOpenWhenOperationRootIsUnavailable()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            public static class Target {
                public static int Call() => 1;
            }
            """,
            []);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        var caller = (IMethodSymbol)compilation
            .GetSemanticModel(tree)
            .GetDeclaredSymbol(declaration)!;
        var foreignDeclaration = CSharpSyntaxTree.ParseText(
                "public static class Foreign { " +
                "public static int Call() => 1; }")
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        var discovery = new RequiresCallSiteDiscovery(
            caller,
            foreignDeclaration,
            compilation.GetSemanticModel(tree),
            CancellationToken.None);

        Assert.That(
            discovery.HasPotentialCallSite(static _ => false),
            Is.True);
    }

    private static IMethodSymbol GetMethod(
        Compilation compilation,
        string typeName,
        string methodName)
    {
        return compilation.GetTypeByMetadataName(typeName)!
            .GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();
    }
}
