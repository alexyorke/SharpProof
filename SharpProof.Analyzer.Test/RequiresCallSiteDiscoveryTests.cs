using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class RequiresCallSiteDiscoveryTests
{
    private static readonly string[] RequiresNotProvenDiagnosticIds =
        ["SP0027"];
    private static readonly bool[] ReplayableCandidate = [true];
    private static readonly CSharpCompilation AccessorCompilation =
        CreateAccessorCompilation();

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
        var discovery = CreateDiscovery(compilation, declaration);
        var candidates = discovery
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
        var discovery = CreateDiscovery(compilation, declaration);
        var candidates = discovery
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
            []);
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

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027", expectedDiagnostics);
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
        var discovery = CreateDiscovery(compilation, declaration);
        var candidates = discovery
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
            []);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027", expectedDiagnostics);
    }

    [Test]
    public void PotentialCallScreeningIncludesPropertyAndEventAccessors()
    {
        var compilation = AccessorCompilation;
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single();
        var discovery = CreateDiscovery(compilation, declaration);
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
    public async Task NestedNameOfPropertiesDoNotExecuteAccessors()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            public sealed class Node {
                public Node Child {
                    get {
                        Contract.Requires(false);
                        return this;
                    }
                }
                public int Value => 0;
            }
            public static class Subject {
                private static readonly Node Root = new();
                private static readonly string Name =
                    nameof(Root.Child.Value);
                public static string Read() => Name;
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void AccessorOperationShapesProduceOneReplayCandidateEach()
    {
        var compilation = AccessorCompilation;
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single();
        var discovery = CreateDiscovery(compilation, declaration);
        var candidates = discovery
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
        var discovery = CreateDiscovery(compilation, declaration);
        var candidates = discovery
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

    [TestCase("getter", TestName =
        "CoalesceAssignmentSkipsSetterAfterNonreturningGetter")]
    [TestCase("receiver", TestName =
        "CoalesceAssignmentSkipsSetterAfterNonreturningReceiver")]
    [TestCase("index", TestName =
        "CoalesceAssignmentSkipsSetterAfterNonreturningIndex")]
    [TestCase("value", TestName =
        "CoalesceAssignmentSkipsSetterAfterNonreturningValue")]
    public void CoalesceAssignmentSkipsSetterAfterNonreturningExpression(
        string scenario)
    {
        var (source, expected) = scenario switch
        {
            "getter" => (
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
                new[] { MethodKind.PropertyGet }),
            "receiver" => (
                """
                #nullable enable
                using System;
                public sealed class Box { public string? Value { get; set; } }
                public static class Subject {
                    private static Box Fail() => throw new InvalidOperationException();
                    public static void Call() { Fail().Value ??= null; }
                }
                """,
                new[] { MethodKind.PropertyGet, MethodKind.Ordinary }),
            "index" => (
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
                new[] { MethodKind.PropertyGet, MethodKind.Ordinary }),
            "value" => (
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
                new[] { MethodKind.PropertyGet, MethodKind.Ordinary }),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "Call");
        var discovery = CreateDiscovery(compilation, declaration);
        var candidates = discovery
            .Get(callerContracts: null);

        Assert.That(candidates, Is.Not.Null);
        Assert.That(
            candidates!.Value.Select(static candidate =>
                candidate.TargetMethod.MethodKind),
            Is.EqualTo(expected));
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
        var discovery = CreateDiscovery(compilation, declaration);
        var candidates = discovery
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
        var discovery = CreateDiscovery(compilation, declaration);
        var candidates = discovery
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
        var discovery = CreateDiscovery(compilation, declaration);
        var candidates = discovery
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
            var discovery = CreateDiscovery(compilation, declaration);
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
            []);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027", 3);
    }

    [Test]
    public async Task ListPatternImplicitArgumentsHonorPreconditions()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;
            public sealed class IndexArgumentContractList {
                public int Length => 3;
                public int this[int index] {
                    get {
                        Contract.Requires(index != 2);
                        return 0;
                    }
                }
            }
            public sealed class SliceArgumentContractList {
                public int Length => 3;
                public int this[int index] => 0;
                public SliceArgumentContractList Slice(int start, int length) {
                    Contract.Requires(start != 1);
                    Contract.Requires(length != 1);
                    return this;
                }
            }
            public static class Subject {
                public static bool Index() =>
                    new IndexArgumentContractList() is [_, _, _];
                public static bool Slice() =>
                    new SliceArgumentContractList() is [_, .. var rest, _];
            }
            """,
            []);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        AnalyzerTestHost.AssertIds(diagnostics, "SP0027", 3);
    }

    [Test]
    public async Task ExecutedImplicitCallShapesHonorRequiresPreconditions()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class Number {
                public static Number operator +(Number left, Number right) {
                    Contract.Requires(false);
                    return left;
                }
                public static implicit operator int(Number value) {
                    Contract.Requires(false);
                    return 0;
                }
            }

            public sealed class Sequence {
                public Enumerator GetEnumerator() {
                    Contract.Requires(false);
                    return new Enumerator();
                }
            }
            public struct Enumerator {
                public int Current => 0;
                public bool MoveNext() => false;
            }

            public sealed class Resource : IDisposable {
                public void Dispose() {
                    Contract.Requires(false);
                }
            }

            public sealed class Point {
                public void Deconstruct(out int value) {
                    Contract.Requires(false);
                    value = 0;
                }
            }

            public static class Subject {
                private static void Target() {
                    Contract.Requires(false);
                }

                public static void OperatorCall() {
                    var number = new Number();
                    _ = number + number;
                }
                public static void ConversionCall() {
                    var number = new Number();
                    int converted = number;
                    _ = converted;
                }
                public static void ForEachCall() {
                    foreach (var item in new Sequence()) { _ = item; }
                }
                public static void UsingCall() {
                    using (var resource = new Resource()) { }
                }
                public static void DeconstructCall() {
                    var point = new Point();
                    if (point is Point(var coordinate)) {
                        _ = coordinate;
                    }
                }
                public static void DelegateCall() {
                    Action callback = Target;
                    callback();
                }
            }
            """,
            ["SP0027"]);
        var session = new AnalyzerSession(
            compilation,
            AnalyzerConfiguration.AdvisoryAll,
            CancellationToken.None);
        foreach (var (callerName, targetName) in new[]
                 {
                     ("OperatorCall", "op_Addition"),
                     ("ConversionCall", "op_Implicit"),
                     ("ForEachCall", "GetEnumerator"),
                     ("UsingCall", "Dispose"),
                     ("DeconstructCall", "Deconstruct"),
                     ("DelegateCall", "Target")
                 })
        {
            var caller = GetMethod(
                compilation,
                "Subject",
                callerName);
            var declaration = await caller.DeclaringSyntaxReferences.Single()
                .GetSyntaxAsync();
            var discovery = CreateDiscovery(compilation, declaration);
            var owners = discovery.GetPotentialCallOwners(
                session.HasPotentialCallPreconditions);
            var candidates = discovery.Get(callerContracts: null);
            var relevant = candidates?.Where(candidate =>
                    candidate.TargetMethod.Name == targetName)
                .ToArray() ?? [];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(owners, Does.Contain(caller), callerName);
                Assert.That(relevant, Is.Not.Empty, callerName);
                Assert.That(
                    relevant,
                    Has.Length.EqualTo(1),
                    callerName + ": " + string.Join(
                        ", ",
                        relevant.Select(static candidate =>
                            candidate.Operation?.Kind + "@" +
                            candidate.Syntax.Span)));
                Assert.That(
                    relevant.All(static candidate => candidate.CanReplay),
                    Is.True,
                    callerName + ": " + string.Join(
                        ", ",
                        candidates?.Select(static candidate =>
                            candidate.TargetMethod.Name + ":" +
                            candidate.CanReplay + ":" +
                            candidate.FlowStatus) ?? []));
            }
        }

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0027", 6)),
            string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic =>
                    diagnostic.Id + ": " + diagnostic.GetMessage(
                        System.Globalization.CultureInfo.InvariantCulture))));
        var messages = diagnostics
            .Select(static diagnostic => diagnostic.GetMessage(
                System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages, Has.Count.EqualTo(6));
            Assert.That(messages, Does.Contain(
                "Call to 'op_Addition' violates precondition 'false'"));
            Assert.That(messages, Does.Contain(
                "Call to 'op_Implicit' violates precondition 'false'"));
            Assert.That(messages, Does.Contain(
                "Call to 'GetEnumerator' violates precondition 'false'"));
            Assert.That(messages, Does.Contain(
                "Call to 'Dispose' violates precondition 'false'"));
            Assert.That(messages, Does.Contain(
                "Call to 'Deconstruct' violates precondition 'false'"));
            Assert.That(messages, Does.Contain(
                "Call to 'Target' violates precondition 'false'"));
        }
    }

    [Test]
    public async Task DirectDelegateTargetsUseInvocationArguments()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public static class Subject {
                private static void Target([Positive] int value) { }

                public static void Call() {
                    Action<int> callback = Target;
                    callback(-1);
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics[0].Id, Is.EqualTo("SP0027"));
            Assert.That(
                diagnostics[0].GetMessage(
                    System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(
                    "Call to 'Target' violates precondition 'false'"));
        }
    }

    [Test]
    public async Task DelegateReferencesWithoutOneStableInvocationTargetAreIgnored()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public static class Subject {
                private static void Target([Positive] int value) { }
                private static void Safe(int value) { }
                private static void Replace(ref Action<int> callback) {
                    callback = Safe;
                }

                public static void NeverInvoked() {
                    Action<int> callback = Target;
                    _ = callback;
                }

                public static void Reassigned() {
                    Action<int> callback = Target;
                    callback = Safe;
                    callback(-1);
                }

                public static void PassedByReference() {
                    Action<int> callback = Target;
                    Replace(ref callback);
                    callback(-1);
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task DelegateTargetRemainsKnownUntilItsFirstReassignment()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public static class Subject {
                private static void Target([Positive] int value) { }
                private static void Safe(int value) { }

                public static void Call() {
                    Action<int> callback = Target;
                    callback(-1);
                    callback = Safe;
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(RequiresNotProvenDiagnosticIds));
    }

    [Test]
    public async Task DelegateRefAliasMutationInvalidatesTheKnownTarget()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public static class Subject {
                private static void Target() { Contract.Requires(false); }
                private static void Safe() { }

                public static void Call() {
                    Action callback = Target;
                    ref Action alias = ref callback;
                    alias = Safe;
                    callback();
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task NestedPositionalPatternsCheckEachDeconstructPrecondition()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public sealed class Outer {
                public void Deconstruct(out Inner value) {
                    value = new Inner();
                }
            }

            public sealed class Inner {
                public void Deconstruct(out int value) {
                    Contract.Requires(false);
                    value = 0;
                }
            }

            public static class Subject {
                public static void Call(Outer outer) {
                    if (outer is Outer(Inner(var value))) {
                        _ = value;
                    }
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(RequiresNotProvenDiagnosticIds));
    }

    [Test]
    public async Task LiftedNullConversionDoesNotInvokeItsOperator()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public readonly struct Number {
                public static implicit operator int(Number value) {
                    Contract.Requires(false);
                    return 0;
                }
            }

            public static class Subject {
                public static void Call() {
                    Number? number = null;
                    int? converted = number;
                    _ = converted;
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task LiftedNonNullConversionInvokesItsOperator()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public readonly struct Number {
                public static implicit operator int(Number value) {
                    Contract.Requires(false);
                    return 0;
                }
            }

            public static class Subject {
                public static void Call() {
                    Number? number = new Number();
                    int? converted = number;
                    _ = converted;
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(RequiresNotProvenDiagnosticIds));
    }

    [Test]
    public async Task LiftedNullOperatorsDoNotInvokeUnderlyingMethods()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public readonly struct Number {
                public static Number operator +(Number left, Number right) {
                    Contract.Requires(false);
                    return left;
                }
                public static Number operator -(Number value) {
                    Contract.Requires(false);
                    return value;
                }
                public static Number operator ++(Number value) {
                    Contract.Requires(false);
                    return value;
                }
            }

            public static class Subject {
                public static void Call() {
                    Number? left = null;
                    Number? right = new Number();
                    _ = left + right;
                    _ = -left;
                    left++;
                    left += right;
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task NestedUserDefinedConversionsCheckRequiresPreconditions()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public sealed class Number {
                public static implicit operator int(Number value) {
                    Contract.Requires(false);
                    return 0;
                }
            }

            public static class Subject {
                private static void Sink(int value) { }

                public static void Call() {
                    Sink(new Number());
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(RequiresNotProvenDiagnosticIds));
    }

    [Test]
    public async Task DefinitelyNullConditionalImplicitCallsAreIgnored()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            #nullable enable
            using System;
            using SharpProof.Attributes;

            public sealed class Resource : IDisposable {
                public void Dispose() { Contract.Requires(false); }
            }

            public sealed class Point {
                public void Deconstruct(out int value) {
                    Contract.Requires(false);
                    value = 0;
                }
            }

            public static class Subject {
                public static void Call() {
                    Resource? resource = null;
                    using (resource) { }

                    Point? point = null;
                    if (point is Point(var coordinate)) {
                        _ = coordinate;
                    }
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task NullableDisposableStructUsesItsUnderlyingDisposeMethod()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public struct Resource : IDisposable {
                public void Dispose() { Contract.Requires(false); }
            }

            public static class Subject {
                public static void Call() {
                    Resource? resource = new Resource();
                    using (resource) { }
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(RequiresNotProvenDiagnosticIds));
    }

    [Test]
    public async Task NullNullableDisposableStructSkipsDispose()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public struct Resource : IDisposable {
                public void Dispose() { Contract.Requires(false); }
            }

            public static class Subject {
                public static void Call() {
                    Resource? resource = null;
                    using (resource) { }
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task ConstrainedDisposableTypeParameterUsesInterfaceContract()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            [ContractFor(typeof(IDisposable))]
            public static class DisposableContracts {
                public static void Dispose(IDisposable receiver) {
                    Contract.Requires(false);
                }
            }

            public static class Subject {
                public static void Call<T>(T resource)
                    where T : IDisposable {
                    using (resource) { }
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(RequiresNotProvenDiagnosticIds));
    }

    [Test]
    public async Task AwaitForeachDoesNotUseSynchronousDispose()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public sealed class Sequence {
                public Enumerator GetAsyncEnumerator(
                    CancellationToken cancellationToken = default) => new();
            }

            public sealed class Enumerator : IDisposable, IAsyncDisposable {
                public int Current => 0;
                public ValueTask<bool> MoveNextAsync() => new(false);
                public void Dispose() { Contract.Requires(false); }
                public ValueTask DisposeAsync() => default;
            }

            public static class Subject {
                public static async Task Call() {
                    await foreach (var item in new Sequence()) {
                        _ = item;
                    }
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task AwaitForeachUsesAsynchronousDispose()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public sealed class Sequence {
                public Enumerator GetAsyncEnumerator(
                    CancellationToken cancellationToken = default) => new();
            }

            public sealed class Enumerator : IDisposable, IAsyncDisposable {
                public int Current => 0;
                public ValueTask<bool> MoveNextAsync() => new(false);
                public void Dispose() { }
                public ValueTask DisposeAsync() {
                    Contract.Requires(false);
                    return default;
                }
            }

            public static class Subject {
                public static async Task Call() {
                    await foreach (var item in new Sequence()) {
                        _ = item;
                    }
                }
            }
            """,
            ["SP0027"]);

        var caller = GetMethod(compilation, "Subject", "Call");
        var declaration = await caller.DeclaringSyntaxReferences.Single()
            .GetSyntaxAsync();
        var discovery = CreateDiscovery(compilation, declaration);
        var candidates = discovery
            .Get(callerContracts: null);
        Assert.That(
            candidates?.Where(static candidate =>
                candidate.TargetMethod.Name == "DisposeAsync")
                .Select(static candidate => candidate.CanReplay),
            Is.EqualTo(ReplayableCandidate),
            string.Join(
                ", ",
                candidates?.Select(static candidate =>
                    candidate.TargetMethod.Name + ":" +
                    candidate.CanReplay) ?? []));

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics[0].Id, Is.EqualTo("SP0027"));
            Assert.That(
                diagnostics[0].GetMessage(
                    System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(
                    "Call to 'DisposeAsync' violates precondition 'false'"));
        }
    }

    [Test]
    public async Task ForeachDisposesAfterMoveNextFailsToComplete()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class Sequence {
                public Enumerator GetEnumerator() => new Enumerator();
            }

            public sealed class Enumerator : IDisposable {
                public int Current => 0;
                public bool MoveNext() => throw new Exception();
                public void Dispose() { Contract.Requires(false); }
            }

            public static class Subject {
                public static void Call() {
                    foreach (var item in new Sequence()) { _ = item; }
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics[0].Id, Is.EqualTo("SP0027"));
            Assert.That(
                diagnostics[0].GetMessage(
                    System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(
                    "Call to 'Dispose' violates precondition 'false'"));
        }
    }

    [Test]
    public async Task ForeachSkipsCurrentWhenMoveNextIsConstantFalse()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public sealed class Sequence {
                public Enumerator GetEnumerator() => new Enumerator();
            }

            public sealed class Enumerator {
                public int Current {
                    get {
                        Contract.Requires(false);
                        return 0;
                    }
                }
                public bool MoveNext() => false;
            }

            public static class Subject {
                public static void Call() {
                    foreach (var item in new Sequence()) { _ = item; }
                }
            }
            """,
            ["SP0027"]);

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: "CONTRACTS");

        Assert.That(diagnostics, Is.Empty);
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

    private static RequiresCallSiteDiscovery CreateDiscovery(
        Compilation compilation,
        SyntaxNode declaration)
    {
        var semanticModel = compilation.GetSemanticModel(
            declaration.SyntaxTree);
        var caller = (IMethodSymbol)semanticModel.GetDeclaredSymbol(declaration)!;
        return new RequiresCallSiteDiscovery(
            caller,
            declaration,
            semanticModel,
            CancellationToken.None);
    }

    private static CSharpCompilation CreateAccessorCompilation()
    {
        return AnalyzerTestHost.CreateCompilation(
            """
            using System;
            public sealed class Subject {
                public int Value { get; set; }
                public event Action Changed { add { } remove { } }
                public void Call() { _ = Value; Value = 1; Changed += null!; Changed -= null!; }
            }
            """,
            []);
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
