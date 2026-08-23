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
    public void ImplicitBaseConstructorProducesOneReplayCandidate()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            public class Base { protected Base() { } }
            public sealed class Derived : Base {
                public Derived() { }
            }
            """,
            []);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<ConstructorDeclarationSyntax>()
            .Single(static constructor =>
                constructor.Identifier.ValueText == "Derived");
        var semanticModel = compilation.GetSemanticModel(tree);
        var caller = (IMethodSymbol)semanticModel.GetDeclaredSymbol(declaration)!;

        var candidates = new RequiresCallSiteDiscovery(
                caller,
                declaration,
                semanticModel,
                CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        Assert.That(candidates!.Value, Has.Length.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidates.Value[0].TargetMethod.Name, Is.EqualTo(".ctor"));
            Assert.That(
                candidates.Value[0].TargetMethod.ContainingType.Name,
                Is.EqualTo("Base"));
            Assert.That(candidates.Value[0].Arguments, Is.Empty);
            Assert.That(candidates.Value[0].CanReplay, Is.True);
        }
    }

    [Test]
    public void ImplicitMetadataBaseConstructorProducesOneReplayCandidate()
    {
        var external = AnalyzerTestHost.EmitReference(
            """
            public class MetadataBase {
                public MetadataBase() { }
            }
            """,
            "ImplicitMetadataBase");
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            public sealed class Derived : MetadataBase {
                public Derived() { }
            }
            """,
            [],
            [external]);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<ConstructorDeclarationSyntax>()
            .Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var caller = (IMethodSymbol)semanticModel.GetDeclaredSymbol(declaration)!;

        var candidates = new RequiresCallSiteDiscovery(
                caller,
                declaration,
                semanticModel,
                CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        Assert.That(candidates!.Value, Has.Length.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                candidates.Value[0].TargetMethod.ContainingType.Name,
                Is.EqualTo("MetadataBase"));
            Assert.That(
                SymbolEqualityComparer.Default.Equals(
                    candidates.Value[0].TargetMethod.ContainingAssembly,
                    compilation.Assembly),
                Is.False);
        }
    }


    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task SourceConditionalInvocationAndArgumentsFollowEmission(
        bool defineFirstSymbol,
        int expectedDiagnostics)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System.Diagnostics;
            using SharpProof.Attributes;
            public static class Subject {
                [Conditional("FIRST")]
                [Conditional("SECOND")]
                private static void Trace(int value) {
                    Contract.Requires(value > 0);
                }
                public static void Call() { Trace(-1); }
            }
            """,
            ["SP0027"]);
        if (defineFirstSymbol)
        {
            var tree = compilation.SyntaxTrees.Single();
            compilation = compilation.ReplaceSyntaxTree(
                tree,
                tree.WithRootAndOptions(
                    await tree.GetRootAsync(),
                    ((CSharpParseOptions)tree.Options)
                    .WithPreprocessorSymbols("FIRST")));
        }

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0027", expectedDiagnostics)));
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task MetadataConditionalInvocationArgumentsFollowEmission(
        bool defineDebug,
        int expectedDiagnostics)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System.Diagnostics;
            using SharpProof.Attributes;
            public static class Subject {
                private static bool Positive(int value) {
                    Contract.Requires(value > 0);
                    return true;
                }
                public static void Call() { Debug.Assert(Positive(-1)); }
            }
            """,
            ["SP0027"]);
        if (defineDebug)
        {
            var originalTree = compilation.SyntaxTrees.Single();
            compilation = compilation.ReplaceSyntaxTree(
                originalTree,
                originalTree.WithRootAndOptions(
                    await originalTree.GetRootAsync(),
                    ((CSharpParseOptions)originalTree.Options)
                    .WithPreprocessorSymbols("DEBUG")));
        }

        var tree = compilation.SyntaxTrees.Single();
        var declaration = (await tree.GetRootAsync()).DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "Call");
        var model = compilation.GetSemanticModel(tree);
        var caller = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var candidates = new RequiresCallSiteDiscovery(
                caller,
                declaration,
                model,
                CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(
            candidates!.Value.Count(static candidate =>
                candidate.TargetMethod.Name == "Positive"),
            Is.EqualTo(expectedDiagnostics));
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task PartialInvocationFollowsCompilerEmission(
        bool implemented,
        int expectedDiagnostics)
    {
        var implementation = implemented
            ? "static partial void Target(int value) { }"
            : string.Empty;
        var compilation = AnalyzerTestHost.CreateCompilation(
            $$"""
            using SharpProof.Attributes;
            public static partial class Subject {
                static partial void Target([Positive] int value);
                {{implementation}}
                public static void Call() { Target(-1); }
            }
            """,
            ["SP0027", "SP0047"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0027", expectedDiagnostics)));
    }

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
    public void CoalesceAssignmentDiscoversConditionalPropertySetter()
    {
        bool[] expectedReplay = [true, false];
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            #nullable enable
            public sealed class Subject {
                public string? Value { get; set; }
                public void Call() { Value ??= null; }
            }
            """,
            []);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var caller = (IMethodSymbol)semanticModel.GetDeclaredSymbol(declaration)!;
        var candidates = new RequiresCallSiteDiscovery(
                caller,
                declaration,
                semanticModel,
                CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                candidates!.Value.Select(static candidate =>
                    candidate.TargetMethod.MethodKind),
                Is.EqualTo(new[] {
                    MethodKind.PropertyGet,
                    MethodKind.PropertySet
                }));
            Assert.That(
                candidates.Value.Select(static candidate =>
                    candidate.CanReplay),
                Is.EqualTo(expectedReplay));
        }
    }

    [Test]
    public void CoalesceAssignmentSkipsSetterAfterNonreturningGetter()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            #nullable enable
            using System;
            public sealed class Subject {
                public string? Value {
                    get => throw new InvalidOperationException();
                    set { }
                }
                public void Call() { Value ??= null; }
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
                caller,
                declaration,
                semanticModel,
                CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        Assert.That(
            candidates!.Value.Select(static candidate =>
                candidate.TargetMethod.MethodKind),
            Is.EqualTo([MethodKind.PropertyGet]));
    }

    [Test]
    public void CoalesceAssignmentSkipsSetterAfterNonreturningReceiver()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            #nullable enable
            using System;
            public sealed class Box { public string? Value { get; set; } }
            public static class Subject {
                private static Box Fail() => throw new InvalidOperationException();
                public static void Call() { Fail().Value ??= null; }
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
                caller,
                declaration,
                semanticModel,
                CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        Assert.That(
            candidates!.Value.Select(static candidate =>
                candidate.TargetMethod.MethodKind),
            Is.EqualTo([MethodKind.PropertyGet, MethodKind.Ordinary]));
    }

    [Test]
    public void CoalesceAssignmentSkipsSetterAfterNonreturningIndex()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            #nullable enable
            using System;
            public sealed class Box {
                public string? this[int index] { get => null; set { } }
            }
            public static class Subject {
                private static int Fail() => throw new InvalidOperationException();
                public static void Call(Box box) { box[Fail()] ??= null; }
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
                caller,
                declaration,
                semanticModel,
                CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        Assert.That(
            candidates!.Value.Select(static candidate =>
                candidate.TargetMethod.MethodKind),
            Is.EqualTo([MethodKind.PropertyGet, MethodKind.Ordinary]));
    }

    [Test]
    public void CoalesceAssignmentSkipsSetterAfterNonreturningValue()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            #nullable enable
            using System;
            public sealed class Subject {
                public string? Value { get; set; }
                private static string Fail() =>
                    throw new InvalidOperationException();
                public void Call() { Value ??= Fail(); }
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
                caller,
                declaration,
                semanticModel,
                CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        Assert.That(
            candidates!.Value.Select(static candidate =>
                candidate.TargetMethod.MethodKind),
            Is.EqualTo([MethodKind.PropertyGet, MethodKind.Ordinary]));
    }

    [Test]
    public void CoalesceSetterReconciliationStaysInsideTheCaller()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            #nullable enable
            public sealed class Box {
                public string? Value { get; set; }
            }
            public static class Subject {
                public static void Call(Box box) {
                    void NeverCalled() { box.Value ??= null; }
                }
            }
            """,
            []);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        var semanticModel = compilation.GetSemanticModel(tree);
        var caller = (IMethodSymbol)semanticModel.GetDeclaredSymbol(declaration)!;
        var candidates = new RequiresCallSiteDiscovery(
                caller,
                declaration,
                semanticModel,
                CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        Assert.That(candidates!.Value, Is.Empty);
    }

    [Test]
    public void CoalesceSetterReconciliationSkipsUnreachableBlocks()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            #nullable enable
            public sealed class Box {
                public string? Value { get; set; }
            }
            public static class Subject {
                public static void Call(Box box) {
                    if (false) {
                        box.Value ??= null;
                    }
                }
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
                caller,
                declaration,
                semanticModel,
                CancellationToken.None)
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        Assert.That(candidates!.Value, Is.Empty);
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
    public async Task ListPatternImplicitAccessorsHonorPreconditionsAndOrder()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            public sealed class LengthContractList {
                public int Length {
                    get { Contract.Requires(false); return 0; }
                }
                public int this[int index] => 0;
            }
            public sealed class EmptyIndexerContractList {
                public int Length => 0;
                public int this[int index] {
                    get { Contract.Requires(false); return 0; }
                }
            }
            public sealed class OneIndexerContractList {
                public int Length => 1;
                public int this[int index] {
                    get { Contract.Requires(false); return 0; }
                }
            }
            public sealed class SliceContractList {
                public int Length => 1;
                public int this[int index] => 0;
                public SliceContractList Slice(int start, int length) {
                    Contract.Requires(false);
                    return this;
                }
            }
            public static class Subject {
                public static bool EmptyLength(LengthContractList value) =>
                    value is [];
                public static bool LengthMismatchSkipsIndexer() =>
                    new EmptyIndexerContractList() is [0];
                public static bool ReachableIndexer() =>
                    new OneIndexerContractList() is [0];
                public static bool ReachableSlice() =>
                    new SliceContractList() is [.. var rest];
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0027", 3)));
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
