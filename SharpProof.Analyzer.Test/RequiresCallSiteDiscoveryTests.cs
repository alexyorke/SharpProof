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
    public void PotentialCallScreeningIncludesPropertyAndEventAccessors()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            public sealed class Subject {
                public int Value { get; set; }
                public event Action Changed { add { } remove { } }
                public void Call() { _ = Value; Value = 1; Changed += null!; Changed -= null!; }
            }
            """,
            []);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var caller = (IMethodSymbol)semanticModel.GetDeclaredSymbol(declaration)!;
        var discovery = new RequiresCallSiteDiscovery(
            caller, declaration, semanticModel, CancellationToken.None);
        var kinds = new HashSet<MethodKind>();

        var hasPotential = discovery.HasPotentialCallSite(target =>
        {
            kinds.Add(target.MethodKind);
            return true;
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hasPotential, Is.True);
            Assert.That(kinds, Does.Contain(MethodKind.PropertyGet));
            Assert.That(kinds, Does.Contain(MethodKind.PropertySet));
            Assert.That(kinds, Does.Contain(MethodKind.EventAdd));
            Assert.That(kinds, Does.Contain(MethodKind.EventRemove));
        }
    }

    [Test]
    public void AccessorOperationShapesProduceOneReplayCandidateEach()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            public sealed class Subject {
                public int Value { get; set; }
                public event Action Changed { add { } remove { } }
                public void Call() { _ = Value; Value = 1; Changed += null!; Changed -= null!; }
            }
            """,
            []);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var caller = (IMethodSymbol)semanticModel.GetDeclaredSymbol(declaration)!;
        var candidates = new RequiresCallSiteDiscovery(
                caller, declaration, semanticModel, CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidates!.Value, Has.Length.EqualTo(4));
            Assert.That(
                candidates.Value.Select(static candidate =>
                    candidate.TargetMethod.MethodKind),
                Is.EqualTo(new[] {
                    MethodKind.PropertyGet,
                    MethodKind.PropertySet,
                    MethodKind.EventAdd,
                    MethodKind.EventRemove
                }));
            Assert.That(
                string.Join(
                    ",",
                    candidates.Value.Select(static candidate =>
                        $"{candidate.TargetMethod.MethodKind}:" +
                        $"{candidate.CanReplay}:{candidate.Flow != null}:" +
                        $"{candidate.FlowStatus}")),
                Is.EqualTo(
                    "PropertyGet:True:True:Complete," +
                    "PropertySet:True:True:Complete," +
                    "EventAdd:True:True:Complete," +
                    "EventRemove:True:True:Complete"));
        }
    }

    [Test]
    public void AccessorRequiresArePotentialCallPreconditions()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;
            public sealed class Subject {
                public int Value { get { Contract.Requires(false); return 0; } set { Contract.Requires(value > 0); } }
                public event Action Changed { add { Contract.Requires(value != null); } remove { Contract.Requires(value != null); } }
            }
            """,
            []);
        var type = compilation.GetTypeByMetadataName("Subject")!;
        var property = type.GetMembers("Value").OfType<IPropertySymbol>().Single();
        var @event = type.GetMembers("Changed").OfType<IEventSymbol>().Single();
        var session = new AnalyzerSession(
            compilation,
            AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None);

        var accessors = new[] {
            property.GetMethod!, property.SetMethod!,
            @event.AddMethod!, @event.RemoveMethod!
        };
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                accessors.All(session.HasPotentialCallPreconditions),
                Is.True);
            Assert.That(
                string.Join(
                    ",",
                    accessors.Select(accessor =>
                    {
                        var binding = session.BindRequires(accessor);
                        return $"{accessor.MethodKind}:{binding.Failure}:" +
                            $"{binding.Contracts?.Clauses.Length ?? -1}";
                    })),
                Is.EqualTo(
                    "PropertyGet:None:1,PropertySet:None:1," +
                    "EventAdd:None:1,EventRemove:None:1"));
        }
    }

    [Test]
    public void ConditionalPropertyAccessProducesFailClosedCandidate()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            public sealed class Subject {
                public int Value => 0;
                public static void Call(Subject? subject) { _ = subject?.Value; }
            }
            """,
            []);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "Call");
        var semanticModel = compilation.GetSemanticModel(tree);
        var caller = (IMethodSymbol)semanticModel.GetDeclaredSymbol(declaration)!;
        var candidates = new RequiresCallSiteDiscovery(
                caller, declaration, semanticModel, CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        Assert.That(candidates!.Value, Has.Length.EqualTo(1));
        Assert.That(candidates.Value[0].CanReplay, Is.False);
    }

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
            using System;
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
                private static int state;

                public static int Read(
                    CompanionTarget receiver,
                    int value) {
                    Contract.Requires(value > 0);
                    Action unsupportedDummy = () => state++;
                    unsupportedDummy();
                    return value;
                }
            }
            """,
            []);
        var session = new AnalyzerSession(
            compilation,
            AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None);
        var companionTarget = GetMethod(
            compilation,
            "CompanionTarget",
            "Read");
        var companionBinding = session.BindRequires(companionTarget);

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
                    companionTarget),
                Is.True);
            Assert.That(companionBinding.Failure, Is.EqualTo(
                SharpProof.Contracts.ContractBindingFailure.None));
            Assert.That(companionBinding.Contracts?.Clauses, Has.Length.EqualTo(1));
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
