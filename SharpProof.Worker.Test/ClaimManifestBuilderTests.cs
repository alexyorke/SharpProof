using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ClaimManifestBuilderTests
{
    private static readonly int[] DenseOrdinals = [0, 1];
    private static readonly int[] FailingShadowInputs = [0, 10];
    private static readonly string[] PotentialOwnerGapReasons =
        ["UnsupportedOwner", "UnsupportedSignature", "IncompleteCalls"];
    private static readonly WorkerClaimEvidence[] CompanionEvidence = [
        WorkerClaimEvidence.CompanionClause,
        WorkerClaimEvidence.ReturnAttribute
    ];

    [Test]
    public void ShadowCallerPreparationRetainsDirectAndTransitiveRequiresWithoutPublishingCallers()
    {
        var compilation = GetCompilation(("Subject.cs", """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static int Positive(int value) { Contract.Requires(value > 0); Contract.Requires(value < 10); return value + 1; }
                public static int Wrapper(int value) => Positive(value);
                public static int Root(int value) => Wrapper(value);
            }
            """));
        var baseline = new ClaimManifestBuilder(compilation).Build();
        var batch = PrepareShadowBatch(compilation);
        Assert.That(batch.Gaps, Is.Empty);
        Assert.That(batch.Callers, Has.Length.EqualTo(2));
        foreach (var caller in batch.Callers)
        {
            Assert.That(caller.OwnerId, Is.EqualTo(caller.Body.CallableId));
            Assert.That(caller.Body.CallPreconditions, Has.Length.EqualTo(2));
            Assert.That(caller.Body.CallPreconditions.Select(static call => call.ClauseOrdinal), Is.EqualTo(DenseOrdinals));
            var factory = caller.Body.Program.Factory;
            var entry = caller.Body.Parameters.Single().Entry;
            foreach (var input in FailingShadowInputs)
            {
                var execution = new IrProgramInterpreter(factory).Execute(caller.Body.Program,
                    new Dictionary<IrVarId, IrValue>
                    { [entry] = factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, (long)input) });
                Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(execution.ConsumedApproximation, Is.False);
                Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(input + 1)));
                var failed = caller.Body.CallPreconditions[input == 0 ? 0 : 1];
                var marker = caller.Body.Program.Blocks.SelectMany(static block => block.Instructions)
                    .OfType<IrAssignInstruction>().Single(instruction => instruction.Id == failed.Instruction);
                Assert.That(execution.GetCurrentValue(marker.Target)!.Boolean, Is.False);
            }
            Assert.That(caller.Body.Clauses, Is.Empty);
            Assert.That(caller.Body.ValidEffectClaimIds, Is.Empty);
            Assert.That(caller.Body.ExceptionConstraints, Is.Empty);
            Assert.That(caller.Body.IsBodyAbstraction, Is.False);
            Assert.That(baseline.Manifest.Callables.Any(entry => entry.CallableId == caller.OwnerId), Is.False);
        }
        Assert.That(System.Text.Json.JsonSerializer.Serialize(new ClaimManifestBuilder(compilation).Build().Manifest),
            Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(baseline.Manifest)));
    }

    [TestCase("static Subject() { throw new System.InvalidOperationException(); }", "", "UnsupportedEntryInitialization")]
    [TestCase("", "static Helper() { throw new System.InvalidOperationException(); }", "UnsupportedBody")]
    [TestCase("", "static int State = 1;", "UnsupportedBody")]
    [TestCase("", "", "UnsupportedEntryInitialization", true)]
    public void ShadowCallerPreparationRejectsUnmodeledInitialization(string ownerInitialization,
        string calleeInitialization, string expectedReason, bool moduleInitializer = false)
    {
        var module = moduleInitializer ? """
            static class Bootstrap {
                [System.Runtime.CompilerServices.ModuleInitializer]
                public static void Initialize() => throw new System.InvalidOperationException();
            }
            """ : "";
        var compilation = GetCompilation(("Subject.cs", $$"""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Helper {
                {{calleeInitialization}}
                public static int Positive(int value) { Contract.Requires(value > 0); return value; }
            }
            public static class Subject {
                {{ownerInitialization}}
                public static int Root(int value) => Helper.Positive(value);
            }
            {{module}}
            """));
        var batch = PrepareShadowBatch(compilation);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps.Select(static gap => gap.Reason), Has.Some.EqualTo(expectedReason));
    }

    [Test]
    public void ShadowCallerPreparationGuardsSelectedDeepCalleeBeforeBinding()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "using SharpProof.Attributes; public static class Subject { public static bool Positive(bool value) { " +
            "Contract.Requires(" + new string('!', 256) + "value); return value; } public static bool Root(bool value) => Positive(value); }",
            new CSharpParseOptions(LanguageVersion.CSharp12), "Deep.cs");
        var compilation = CSharpCompilation.Create("ManifestTests", [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var batch = PrepareShadowBatch(compilation);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("SyntaxBudget"));
    }

    [Test]
    public void ShadowCallerPreparationRejectsMismatchedSourceAndPropagatesCancellation()
    {
        var first = GetCompilation(("Subject.cs", "public static class Subject { public static int Root() => 1; }"));
        var second = GetCompilation(("Subject.cs", "public static class Subject { public static int Root() => 2; }"));
        var trees = CompilerCompilationCapture.CaptureTrees(first, CancellationToken.None);
        var authority = CompilerSpecificationPackProvider.ResolveConfiguration([]);
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(second, WorkerFeatureSet.All,
            trees, null, authority, CancellationToken.None);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("SourceSnapshotMismatch"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() =>
            CompilerTotalCallableLowerer.PrepareShadowCallers(first, WorkerFeatureSet.All,
                trees, null, authority, cancellation.Token)));
    }

    [Test]
    public void ShadowCallerPreparationRetainsUnreachableMarkersWithoutExecutingThem()
    {
        var compilation = GetCompilation(("Subject.cs", """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static int Positive(int value) { Contract.Requires(value > 0); return value; }
                public static int Root() { if (false) return Positive(-1); return 0; }
            }
            """));
        var batch = PrepareShadowBatch(compilation);
        Assert.That(batch.Gaps, Is.Empty);
        var body = batch.Callers.Single().Body;
        Assert.That(body.CallPreconditions, Has.Length.EqualTo(1));
        var execution = new IrProgramInterpreter(body.Program.Factory).Execute(body.Program);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.Zero));
        var marker = body.Program.Blocks.SelectMany(static block => block.Instructions)
            .OfType<IrAssignInstruction>().Single(instruction => instruction.Id == body.CallPreconditions.Single().Instruction);
        Assert.That(execution.GetCurrentValue(marker.Target), Is.Null);
    }

    [Test]
    public void ShadowCallerPreparationRejectsDeepAttributesBeforeSemanticInventoryConstruction()
    {
        var tree = CSharpSyntaxTree.ParseText(
            "[System.Obsolete(" + new string('(', 256) + "\"message\"" + new string(')', 256) +
            ")] public static class Subject { public static int Root() => 1; }",
            new CSharpParseOptions(LanguageVersion.CSharp12), "DeepAttribute.cs");
        var compilation = CSharpCompilation.Create("ManifestTests", [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var batch = PrepareShadowBatch(compilation);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("SyntaxBudget"));
    }

    [Test]
    public void ShadowCallerPreparationRejectsOwnContractsWhenFeaturesExcludePublicMembership()
    {
        var compilation = GetCompilation(("Subject.cs", """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static int Positive(int value) { Contract.Requires(value > 0); return value; }
                public static int Root([Positive] int value) => Positive(value);
            }
            """));
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.Effects,
            CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None), null,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps.Select(static gap => gap.Reason), Has.Some.EqualTo("UnsupportedOwnContracts"));
    }

    [Test]
    public void ShadowCallerPreparationBoundsSourceCommentsBeforeHashing()
    {
        var tree = CSharpSyntaxTree.ParseText("/*" + new string('x', 4_194_304) +
            "*/ public static class Subject { public static int Root() => 1; }",
            new CSharpParseOptions(LanguageVersion.CSharp12), "WideComment.cs");
        var compilation = CSharpCompilation.Create("ManifestTests", [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        // Do not capture/hash this source before invoking the production guard.
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            [new CompilerSyntaxTreeSnapshot()], null,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("SourceSnapshotBudget"));
    }

    private static CompilerShadowPreparationBatch PrepareShadowBatch(CSharpCompilation compilation)
    {
        return CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None), null,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None);
    }

    [Test]
    public void PotentialCallShadowFindsPlainSeparateFileCallersWithoutChangingManifest()
    {
        var compilation = GetCompilation(
            ("Helper.cs", """
                using SharpProof.Attributes;
                public static class Helper {
                    public static int Positive(int value) { Contract.Requires(value > 0); return value; }
                }
                """),
            ("Caller.cs", "public static class Caller { public static int Root(int value) => Helper.Positive(value); }"));
        var baseline = new ClaimManifestBuilder(compilation).Build();
        var shadow = new ClaimManifestBuilder(compilation).Build(includePotentialCallShadow: true);
        Assert.That(baseline.PotentialCalls, Is.Null);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(shadow.Manifest),
            Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(baseline.Manifest)));
        var caller = shadow.PotentialCalls!.Owners.Single(static owner => owner.Method.Name == "Root");
        Assert.That(caller.DiscoveryComplete, Is.True);
        Assert.That(caller.Calls, Has.Length.EqualTo(1));
        Assert.That(caller.Calls.Single().Target.Name, Is.EqualTo("Positive"));
        Assert.That(caller.CallableId, Is.EqualTo(SemanticClaimIdentity.CreateCallableId(caller.Method)));
        Assert.That(shadow.Manifest.Callables.Any(entry => entry.CallableId == caller.CallableId), Is.False);
        Assert.That(shadow.PotentialCalls.Gaps, Is.Empty);
    }

    [Test]
    public void PotentialCallShadowRetainsIncompleteAndUnsupportedOwnerGaps()
    {
        var compilation = GetCompilation(("Subject.cs", """
            public static class Subject {
                public static void Root() { System.Action callback = () => { }; }
                public static void Dynamic(dynamic value) { value.Invoke(); }
                public static int Incomplete() => new Buffer()[^1];
                public class Buffer { public int Length => 2; public int this[int index] => index; }
            }
            """));
        var shadow = new ClaimManifestBuilder(compilation).Build(includePotentialCallShadow: true).PotentialCalls!;
        Assert.That(shadow.Gaps.Select(static gap => gap.Reason),
            Is.EquivalentTo(PotentialOwnerGapReasons));
        Assert.That(shadow.Owners.Single().Method.Name, Is.EqualTo("Incomplete"));
        Assert.That(shadow.Owners.Single().DiscoveryComplete, Is.False);
    }

    [Test]
    public void PotentialCallShadowRejectsDeepPlainSyntaxBeforeSemanticBinding()
    {
        var expression = new string('!', 256) + "value";
        var tree = CSharpSyntaxTree.ParseText(
            "public static class Subject { public static bool Root(bool value) => " + expression + "; }",
            new CSharpParseOptions(LanguageVersion.CSharp12), "Plain.cs");
        // Avoid TestCompilation.Create/GetDiagnostics: those bind before the guard.
        var compilation = CSharpCompilation.Create("ManifestTests", [tree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var shadow = new ClaimManifestBuilder(compilation).Build(includePotentialCallShadow: true).PotentialCalls!;
        Assert.That(shadow.Owners, Is.Empty);
        Assert.That(shadow.Gaps, Has.Length.EqualTo(1));
        Assert.That(shadow.Gaps.Single().Reason, Is.EqualTo("SyntaxBudget"));
    }

    [Test]
    public void PotentialCallShadowGuardsCrossTreeCalleesBeforeContractScreening()
    {
        var expression = new string('!', 256) + "value";
        var caller = CSharpSyntaxTree.ParseText(
            "public static class Caller { public static bool Root(bool value) => Deep.Callee(value); }",
            new CSharpParseOptions(LanguageVersion.CSharp12), "Caller.cs");
        var callee = CSharpSyntaxTree.ParseText(
            "public static class Deep { public static bool Callee(bool value) => " + expression + "; }",
            new CSharpParseOptions(LanguageVersion.CSharp12), "Deep.cs");
        var compilation = CSharpCompilation.Create("ManifestTests", [caller, callee], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var shadow = new ClaimManifestBuilder(compilation).Build(includePotentialCallShadow: true).PotentialCalls!;
        Assert.That(shadow.Owners, Is.Empty);
        Assert.That(shadow.Gaps.Single().TreeOrdinal, Is.EqualTo(1));
        Assert.That(shadow.Gaps.Single().Reason, Is.EqualTo("SyntaxBudget"));
    }
    [TestCase(8, true)]
    [TestCase(256, false)]
    public void PotentialCallShadowGuardsReferencedSourceBeforeContractScreening(int depth, bool expectedComplete)
    {
        var expression = new string('!', depth) + "value";
        var calleeTree = CSharpSyntaxTree.ParseText(
            "using SharpProof.Attributes; public static class Helper { public static bool Positive(bool value) " +
            "{ Contract.Requires(value); return " + expression + "; } }",
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: [Contract.ConditionalSymbol]), "Helper.cs");
        var dependency = CSharpCompilation.Create("HelperLibrary", [calleeTree], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var callerTree = CSharpSyntaxTree.ParseText(
            "public static class Caller { public static bool Root(bool value) => Helper.Positive(value); }",
            new CSharpParseOptions(LanguageVersion.CSharp12), "Caller.cs");
        var compilation = CSharpCompilation.Create("ManifestTests", [callerTree],
            TestMetadataReferences.WithSharpProof.Add(dependency.ToMetadataReference()),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var shadow = new ClaimManifestBuilder(compilation).Build(includePotentialCallShadow: true).PotentialCalls!;
        if (expectedComplete)
        {
            Assert.That(shadow.Owners.Single().DiscoveryComplete, Is.True);
            Assert.That(shadow.Owners.Single().Calls, Has.Length.EqualTo(1));
            Assert.That(shadow.Gaps, Is.Empty);
        }
        else
        {
            Assert.That(shadow.Owners, Is.Empty);
            Assert.That(shadow.Gaps.Single().Reason, Is.EqualTo("ReferenceSyntaxBudget"));
            Assert.That(shadow.Gaps.Single().ReferenceAssemblyName, Is.EqualTo("HelperLibrary"));
        }
    }
    [Test]
    public void PotentialCallShadowRejectsAmbiguousReferencedTreeOwnership()
    {
        var shared = CSharpSyntaxTree.ParseText("""
            using SharpProof.Attributes;
            public static class Helper {
                public static int Positive(int value) { Contract.Requires(value > 0); return value; }
            }
            """, new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: [Contract.ConditionalSymbol]), "Helper.cs");
        var first = CSharpCompilation.Create("FirstLibrary", [shared], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var second = CSharpCompilation.Create("SecondLibrary", [shared], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var caller = CSharpSyntaxTree.ParseText(
            "extern alias first; public static class Caller { public static int Root(int value) => first::Helper.Positive(value); }",
            new CSharpParseOptions(LanguageVersion.CSharp12), "Caller.cs");
        var compilation = CSharpCompilation.Create("ManifestTests", [caller], TestMetadataReferences.WithSharpProof
            .Add(first.ToMetadataReference(aliases: ["first"]))
            .Add(second.ToMetadataReference(aliases: ["second"])),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var shadow = new ClaimManifestBuilder(compilation).Build(includePotentialCallShadow: true).PotentialCalls!;
        Assert.That(shadow.Owners, Is.Empty);
        Assert.That(shadow.Gaps.Single().Reason, Is.EqualTo("ReferenceOwnership"));
    }
    [Test]
    public void SelectedDescendantsOfPlainSiblingLambdasHaveDistinctIdentities()
    {
        var result = BuildIdentityArtifact(("Subject.cs", """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static void Outer() {
                    Func<Func<int,int>> first = () => value => {
                        Contract.Ensures(Contract.Result<int>() == value);
                        return value;
                    };
                    Func<Func<int,int>> second = () => value => {
                        Contract.Ensures(Contract.Result<int>() == value);
                        return value;
                    };
                    _ = first()(second()(1));
                }
            }
            """));
        Assert.That(result.Manifest.Callables, Has.Length.EqualTo(2));
        Assert.That(result.Manifest.Claims, Has.Length.EqualTo(2));
        Assert.That(result.Manifest.Callables.Select(static entry => entry.CallableId).Distinct().Count(),
            Is.EqualTo(2));
        Assert.That(result.Manifest.Claims.Select(static entry => entry.ClaimId).Distinct().Count(),
            Is.EqualTo(2));
        Assert.That(WorkerProtocolJson.ValidateManifest(result.Manifest).IsValid, Is.True);
    }

    [Test]
    public void InheritedTrustKeepsPlainSiblingCallableIdentitiesDistinct()
    {
        var result = BuildIdentityArtifact(("Subject.cs", """
            using System;
            using SharpProof.Attributes;
            [SharpProofTrusted("Reviewed boundary")]
            public static class Subject {
                public static int Outer(int value) {
                    Func<int,int> first = item => item;
                    Func<int,int> second = item => item;
                    return first(second(value));
                }
            }
            """));
        Assert.That(result.Manifest.Callables, Has.Length.EqualTo(3));
        Assert.That(result.Manifest.Callables.Select(static entry => entry.CallableId).Distinct().Count(),
            Is.EqualTo(3));
        Assert.That(result.Manifest.Claims, Is.Empty);
        Assert.That(result.Manifest.Callables.All(static entry => entry.Assumptions.Length == 1 &&
            entry.Assumptions[0].Kind == WorkerAssumptionKind.TrustedBoundary), Is.True);
        Assert.That(WorkerProtocolJson.ValidateManifest(result.Manifest).IsValid, Is.True);
    }

    [Test]
    public void FileLocalMethodNamesDistinguishCallableIdentities()
    {
        var result = BuildIdentityArtifact(("Subject.cs", """
            using SharpProof.Attributes;
            file static class Subject {
                public static int First(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
                public static int Second(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """));
        Assert.That(result.Manifest.Callables, Has.Length.EqualTo(2));
        Assert.That(result.Manifest.Callables.Select(static entry => entry.CallableId).Distinct().Count(),
            Is.EqualTo(2));
        Assert.That(WorkerProtocolJson.ValidateManifest(result.Manifest).IsValid, Is.True);
    }

    [Test]
    public void FunctionPointerMethodNamesDistinguishCallableIdentities()
    {
        var compilation = TestCompilation.Create("ManifestTests", [("Subject.cs", """
            using SharpProof.Attributes;
            public static unsafe class Subject {
                public static int First(delegate*<int,int> pointer) {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    return 1;
                }
                public static int Second(delegate*<int,int> pointer) {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    return 1;
                }
            }
            """)], allowUnsafe: true);
        var result = new ClaimManifestBuilder(compilation).Build();
        var prepared = AssertIdentityArtifactRoundTrip(compilation, result);
        Assert.That(result.Manifest.Callables, Has.Length.EqualTo(2));
        Assert.That(result.Manifest.Claims, Has.Length.EqualTo(2));
        Assert.That(prepared.All(static entry => entry.Total == null &&
            entry.FailureReason == WorkerClaimReason.UnsupportedCallable), Is.True);
        Assert.That(result.Manifest.Callables.Select(static entry => entry.CallableId).Distinct().Count(),
            Is.EqualTo(2));
        Assert.That(WorkerProtocolJson.ValidateManifest(result.Manifest).IsValid, Is.True);
    }

    [Test]
    public void ChangingPredicateCallTargetChangesClaimIdentity()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                static int First(int value) => value;
                static int Second(int value) => value + 1;
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == CALLEE(value));
                    return value;
                }
            }
            """;
        var first = BuildIdentityArtifact(("Subject.cs", source.Replace("CALLEE", "First", StringComparison.Ordinal)));
        var second = BuildIdentityArtifact(("Subject.cs", source.Replace("CALLEE", "Second", StringComparison.Ordinal)));
        Assert.That(first.Manifest.Callables.Single().CallableId,
            Is.EqualTo(second.Manifest.Callables.Single().CallableId));
        Assert.That(first.Manifest.Claims.Single().ClaimId, Is.Not.EqualTo(second.Manifest.Claims.Single().ClaimId));
    }

    [Test]
    public void LocalPredicateTargetsHaveDistinctRenameStableIdentities()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    bool Positive(int input) => input > 0;
                    bool Negative(int input) => input < 0;
                    Contract.Ensures(CALLEE(value));
                    return value;
                }
            }
            """;
        var first = BuildIdentityArtifact(("Subject.cs", source.Replace("CALLEE", "Positive", StringComparison.Ordinal)));
        var second = BuildIdentityArtifact(("Subject.cs", source.Replace("CALLEE", "Negative", StringComparison.Ordinal)));
        var renamed = BuildIdentityArtifact(("Subject.cs", source.Replace("CALLEE", "Positive", StringComparison.Ordinal)
            .Replace("Positive", "Renamed", StringComparison.Ordinal)));
        Assert.That(first.Manifest.Claims.Single().ClaimId, Is.Not.EqualTo(second.Manifest.Claims.Single().ClaimId));
        Assert.That(first.Manifest.Claims.Single().ClaimId, Is.EqualTo(renamed.Manifest.Claims.Single().ClaimId));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LocalPredicateHelperInsideNestedCallableKeepsRenameStableIdentity(bool useLambda)
    {
        var parent = useLambda ? "System.Func<int,int> Parent = value =>" : "int Parent(int value)";
        var suffix = useLambda ? ";" : "";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static void Outer() {
                    {{parent}} {
                        Contract.Ensures(Contract.Result<int>() == Helper(value));
                        int Helper(int item) => item;
                        return value;
                    }{{suffix}}
                    _ = Parent(1);
                }
            }
            """;
        var first = BuildIdentityArtifact(("Subject.cs", source));
        var renamed = BuildIdentityArtifact(("Subject.cs", source.Replace("Helper", "Renamed", StringComparison.Ordinal)));
        Assert.That(first.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.That(first.Manifest.Claims.Single().ClaimId, Is.EqualTo(renamed.Manifest.Claims.Single().ClaimId));
        Assert.That(WorkerProtocolJson.ValidateManifest(first.Manifest).IsValid, Is.True);
    }

    [Test]
    public void GenericLocalAncestorRenamesPreserveNestedIdentities()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static void Outer() {
                    bool Parent<T>(T value) {
                        bool Child(T item) {
                            Contract.Ensures(Helper(item));
                            return true;
                        }
                        bool Helper(T item) => true;
                        return Child(value);
                    }
                    _ = Parent<int>(1);
                }
            }
            """;
        var before = BuildIdentityArtifact(("Subject.cs", source));
        var after = BuildIdentityArtifact(("Subject.cs", source.Replace("Parent", "Renamed", StringComparison.Ordinal)));
        var beforeTarget = before.Targets.Values.Single(static target => target.Method.Name == "Child");
        var afterTarget = after.Targets.Values.Single(static target => target.Method.Name == "Child");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterTarget.Entry.CallableId, Is.EqualTo(beforeTarget.Entry.CallableId));
            Assert.That(afterTarget.Claims.Single().Entry.ClaimId, Is.EqualTo(beforeTarget.Claims.Single().Entry.ClaimId));
        }
    }
    [Test]
    public void CheckedResultAndOldPreserveEstablishedClaimIdentities()
    {
        var result = BuildIdentityArtifact(("Subject.cs", """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() == Contract.Old(value));
                    return value;
                }
            }
            """));
        Assert.That(result.Manifest.Callables.Single().CallableId,
            Is.EqualTo("M:Subject.Target(System.Int32)~System.Int32"));
        Assert.That(result.Manifest.Claims.Single().ClaimId,
            Is.EqualTo("spc1:f48e52a492d1b8b11171c02d137abd51cd41c19f4eaced15e92e145067335f3e"));
        Assert.That(result.Manifest.Callables.Single().Assumptions.Single().Id,
            Is.EqualTo("spa1:3ddb2cbf9ccbb834015a267d7d8cdb7f937809a4deecbd7c032dcb1d38003373"));
    }
    [TestCase(false)]
    [TestCase(true)]
    public void AliasedAssemblyCallTargetsHaveDistinctClaimIdentities(bool useCompilationReference)
    {
        MetadataReference Library(string assemblyName, string alias, string body)
        {
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText("namespace Shared { public static class Api { public static int Helper(int value) => " + body + "; } }")],
                TestMetadataReferences.Platform,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            if (useCompilationReference)
            {
                return compilation.ToMetadataReference(aliases: [alias]);
            }
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            Assert.That(emitted.Success, Is.True,
                string.Join(Environment.NewLine, emitted.Diagnostics));
            return MetadataReference.CreateFromImage(stream.ToArray().ToImmutableArray(),
                new MetadataReferenceProperties(aliases: [alias]));
        }
        var references = TestMetadataReferences.WithSharpProof
            .Add(Library("FirstLibrary", "first", "value"))
            .Add(Library("SecondLibrary", "second", "value + 1"));
        const string source = """
            extern alias first;
            extern alias second;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == CALLEE::Shared.Api.Helper(value));
                    return value;
                }
            }
            """;
        ClaimManifestBuildResult Compile(string alias)
        {
            var tree = CSharpSyntaxTree.ParseText(source.Replace("CALLEE", alias, StringComparison.Ordinal),
                new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: [Contract.ConditionalSymbol]), "Subject.cs");
            var compilation = CSharpCompilation.Create("ManifestTests", [tree], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(static invocation => invocation.Expression.ToString().Contains("::", StringComparison.Ordinal));
            var callee = (IMethodSymbol)compilation.GetSemanticModel(tree).GetSymbolInfo(syntax).Symbol!;
            Assert.That(callee.ContainingAssembly.Identity.Name,
                Is.EqualTo(alias == "first" ? "FirstLibrary" : "SecondLibrary"));
            return new ClaimManifestBuilder(compilation).Build();
        }
        var first = Compile("first");
        var second = Compile("second");
        Assert.That(first.Manifest.Callables.Single().CallableId, Is.EqualTo(second.Manifest.Callables.Single().CallableId));
        Assert.That(first.Manifest.Claims.Single().ClaimId, Is.Not.EqualTo(second.Manifest.Claims.Single().ClaimId));
    }
    [TestCase(false)]
    [TestCase(true)]
    public void PropertyAndFieldCallTargetsHaveDistinctClaimIdentities(bool useField)
    {
        var declarations = useField
            ? "static int First = 0; static int Second = 1;"
            : "static int First => 0; static int Second => 1;";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                {{declarations}}
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == MEMBER);
                    return value;
                }
            }
            """;
        ClaimManifestBuildResult Compile(string name)
        {
            var tree = CSharpSyntaxTree.ParseText(source.Replace("MEMBER", name, StringComparison.Ordinal),
                new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: [Contract.ConditionalSymbol]), "Subject.cs");
            var compilation = CSharpCompilation.Create("ManifestTests", [tree], TestMetadataReferences.WithSharpProof,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var syntax = tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>()
                .Single(identifier => identifier.Identifier.ValueText == name);
            var member = compilation.GetSemanticModel(tree).GetSymbolInfo(syntax).Symbol;
            Assert.That(member, Is.Not.Null);
            Assert.That(member!.MetadataName, Is.EqualTo(name));
            Assert.That(member.Kind, Is.EqualTo(useField ? SymbolKind.Field : SymbolKind.Property));
            if (member is IFieldSymbol field)
            {
                Assert.That(field.IsConst, Is.False);
            }
            return new ClaimManifestBuilder(compilation).Build();
        }
        var first = Compile("First");
        var second = Compile("Second");
        Assert.That(first.Manifest.Callables.Single().CallableId, Is.EqualTo(second.Manifest.Callables.Single().CallableId));
        Assert.That(first.Manifest.Claims.Single().ClaimId, Is.Not.EqualTo(second.Manifest.Claims.Single().ClaimId));
    }
    [Test]
    public void EffectWireMappingsAreNamedAndExhaustive()
    {
        var effects = Enum.GetValues<SharpProof.Effects.EffectContractKind>();
        foreach (var effect in effects)
        {
            Assert.That(
                ClaimManifestBuilder.ToWorkerEffects(effect).ToString(),
                Is.EqualTo(effect.ToString()));
        }

        var capabilities =
            Enum.GetValues<SharpProof.Effects.EffectContractCapabilityKind>();
        foreach (var capability in capabilities)
        {
            Assert.That(
                ClaimManifestBuilder.ToWorkerCapabilities(capability).ToString(),
                Is.EqualTo(capability.ToString()));
        }

        var effectCombinations =
            new Dictionary<SharpProof.Effects.EffectContractKind, WorkerEffectSet>
            {
                [SharpProof.Effects.EffectContractKind.None] =
                    WorkerEffectSet.None
            };
        foreach (var effect in effects.Where(static effect =>
                     effect != SharpProof.Effects.EffectContractKind.None))
        {
            var mapped = ClaimManifestBuilder.ToWorkerEffects(effect);
            foreach (var combination in effectCombinations.ToArray())
            {
                effectCombinations[combination.Key | effect] =
                    combination.Value | mapped;
            }
        }
        foreach (var combination in effectCombinations)
        {
            Assert.That(
                ClaimManifestBuilder.ToWorkerEffects(combination.Key),
                Is.EqualTo(combination.Value),
                combination.Key.ToString());
        }

        var capabilityCombinations =
            new Dictionary<
                SharpProof.Effects.EffectContractCapabilityKind,
                WorkerEffectCapabilitySet>
            {
                [SharpProof.Effects.EffectContractCapabilityKind.None] =
                    WorkerEffectCapabilitySet.None
            };
        foreach (var capability in capabilities.Where(static capability =>
                     capability !=
                     SharpProof.Effects.EffectContractCapabilityKind.None))
        {
            var mapped =
                ClaimManifestBuilder.ToWorkerCapabilities(capability);
            foreach (var combination in capabilityCombinations.ToArray())
            {
                capabilityCombinations[combination.Key | capability] =
                    combination.Value | mapped;
            }
        }
        foreach (var combination in capabilityCombinations)
        {
            Assert.That(
                ClaimManifestBuilder.ToWorkerCapabilities(combination.Key),
                Is.EqualTo(combination.Value),
                combination.Key.ToString());
        }
        Action invalidEffect = () => _ = ClaimManifestBuilder.ToWorkerEffects(
            (SharpProof.Effects.EffectContractKind)(1L << 30));
        Action invalidCapability = () => _ = ClaimManifestBuilder.ToWorkerCapabilities(
            (SharpProof.Effects.EffectContractCapabilityKind)(1 << 20));
        Assert.That(invalidEffect, Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(invalidCapability, Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void CancellationStopsCompanionDiscovery()
    {
        var compilation = GetCompilation((
            "Subject.cs", "internal sealed class Subject { }"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Action action = () => _ = new ClaimManifestBuilder(
            compilation, WorkerFeatureSet.All, cancellation.Token);
        Assert.That(action, Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void CancellationStopsMethodDiscovery()
    {
        var compilation = GetCompilation((
            "Subject.cs", "internal sealed class Subject { }"));
        using var cancellation = new CancellationTokenSource();
        var builder = new ClaimManifestBuilder(
            compilation, WorkerFeatureSet.All, cancellation.Token);
        cancellation.Cancel();

        Action action = () => builder.Build();
        Assert.That(action, Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void ClaimIdentityIgnoresTriviaNamesAndPaths()
    {
        var first = Build((
            "First.cs",
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Identity(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
            }
            """));
        var second = Build((
            "Renamed.cs",
            """
            using SharpProof.Attributes;
            public static class Subject {
                // Formatting and parameter names are not semantic identity.
                public static long Identity(long renamed)
                {
                    Contract.Ensures(
                        Contract.Result<long>() == renamed);
                    return renamed;
                }
            }
            """));

        Assert.That(
            second.Manifest.Claims.Single().ClaimId,
            Is.EqualTo(first.Manifest.Claims.Single().ClaimId));
        Assert.That(
            second.Manifest.Hash,
            Is.Not.EqualTo(first.Manifest.Hash));
    }

    [Test]
    public void AttributeClaimOrderingIsCultureInvariant()
    {
        const string definition =
            "using SharpProof.Attributes;\n" +
            "public static partial class Subject {\n" +
            "    [return: Positive]\n" +
            "    public static partial int Value();\n" +
            "}";
        const string implementation =
            "using SharpProof.Attributes;\n" +
            "public static partial class Subject {\n" +
            "    [return: InRange(-1, 1)]\n" +
            "    public static partial int Value() => 0;\n" +
            "}";
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var enUs = Build(("ä.cs", definition), ("z.cs", implementation));
            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("sv-SE");
            var svSe = Build(("ä.cs", definition), ("z.cs", implementation));

            Assert.That(
                svSe.Manifest.Claims.Select(static claim => claim.ClaimId),
                Is.EqualTo(enUs.Manifest.Claims.Select(static claim => claim.ClaimId)));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [TestCase("global using Z = SharpProof.Attributes.ZeroAllocationsAttribute;", "Z")]
    [TestCase("global using Z = SharpProof.Attributes;", "Z.ZeroAllocations")]
    [TestCase("global using Z = SharpProof.Attributes.ZeroAllocationsAttribute;", "\\u005A")]
    public void GlobalAttributeAliasSelectsASeparateTokenFreeTree(string alias, string attribute)
    {
        var result = Build(
            ("Aliases.cs", alias),
            ("Subject.cs", $"public static class Subject {{ [{attribute}] public static int Target() => 0; }}"));
        Assert.That(result.Targets.Values.Single().Method.Name, Is.EqualTo("Target"));
        Assert.That(result.Manifest.Claims.Single().EffectContractKind,
            Is.EqualTo(WorkerEffectContractKind.ZeroAllocations));
    }

    [Test]
    public void TrustedPartialScopeSelectsASeparateTokenFreeTree()
    {
        var result = Build(
            ("Boundary.cs", "using SharpProof.Attributes; [SharpProofTrusted(\"Reviewed boundary\")] public static partial class Subject { }"),
            ("Implementation.cs", "public static partial class Subject { public static int Target() => 0; }"));
        var target = result.Targets.Values.Single();
        Assert.That(target.Method.Name, Is.EqualTo("Target"));
        Assert.That(target.Entry.Assumptions.Select(static assumption => assumption.Kind),
            Does.Contain(WorkerAssumptionKind.TrustedBoundary));
    }

    [Test]
    public void TrustedAssemblySelectsASeparateTokenFreeTree()
    {
        var result = Build(
            ("Boundary.cs", "using SharpProof.Attributes; [assembly: SharpProofTrusted(\"Reviewed boundary\")]"),
            ("Implementation.cs", "public static class Subject { public static int Target() => 0; }"));
        Assert.That(result.Targets.Values.Single().Entry.Assumptions.Select(static assumption => assumption.Kind),
            Does.Contain(WorkerAssumptionKind.TrustedBoundary));
    }

    [Test]
    public void TrustedOuterScopeSelectsNestedPartialDeclarations()
    {
        var result = Build(
            ("Boundary.cs", "using SharpProof.Attributes; [SharpProofTrusted(\"Reviewed boundary\")] public static partial class Subject { public static partial class Nested { } }"),
            ("Implementation.cs", "public static partial class Subject { public static partial class Nested { public static int Target() => 0; } }"));
        var target = result.Targets.Values.Single();
        Assert.That(target.Method.ContainingType.Name, Is.EqualTo("Nested"));
        Assert.That(target.Entry.Assumptions.Select(static assumption => assumption.Kind),
            Does.Contain(WorkerAssumptionKind.TrustedBoundary));
    }

    [TestCase("SharpProofTrustedAttribute", "type")]
    [TestCase("SharpProofSuppressAttribute", "type")]
    [TestCase("SharpProofTrustedAttribute", "assembly")]
    [TestCase("SharpProofSuppressAttribute", "assembly")]
    [TestCase("SharpProofTrustedAttribute", "nested")]
    [TestCase("SharpProofSuppressAttribute", "nested")]
    public void RejectedControlScopesSelectTokenFreeDeclarationsWithoutGrantingTrust(string attribute, string scope)
    {
        var shadow = $$"""
            namespace SharpProof.Attributes {
                public sealed class {{attribute}} : System.Attribute {
                    public {{attribute}}(string reason) { }
                }
            }
            """;
        var control = $"SharpProof.Attributes.{attribute}(\"reason\")";
        var boundary = scope == "assembly" ? $"[assembly: {control}]"
            : $"[{control}] public static partial class Subject {{ }}";
        var implementation = scope == "nested"
            ? "public static partial class Subject { public static class Nested { public static int Target() => 0; } }"
            : "public static partial class Subject { public static int Target() => 0; }";
        var compilation = GetCompilation(("Shadow.cs", shadow), ("Boundary.cs", boundary), ("Implementation.cs", implementation));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var result = new ClaimManifestBuilder(compilation).Build();
        var target = result.Targets.Values.Single(static target => target.Method.Name == "Target");
        Assert.That(target.Entry.SelectedFeatures, Does.Contain(WorkerSelectedFeature.Contracts));
        Assert.That(target.Entry.SelectedFeatures, Does.Contain(WorkerSelectedFeature.Effects));
        Assert.That(target.Entry.Assumptions.Select(static assumption => assumption.Kind),
            Does.Not.Contain(WorkerAssumptionKind.TrustedBoundary));
        Assert.That(target.Entry.ClaimIds, Is.Empty);
    }

    [Test]
    public void AssumptionIdentityIncludesCallableScopeAndUsesGeneratedGrammar()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;
            public static class Subject {
                [SharpProofTrusted("reviewed boundary")]
                [DoesNotThrow]
                public static long First(long value) => value;

                [SharpProofTrusted("reviewed boundary")]
                [DoesNotThrow]
                public static long Second(long value) => value;
            }
            """));

        var assumptions = result.Manifest.Callables
            .SelectMany(static callable => callable.Assumptions)
            .Where(static assumption =>
                assumption.Kind == WorkerAssumptionKind.TrustedBoundary)
            .ToArray();

        Assert.That(assumptions, Has.Length.EqualTo(2));
        Assert.That(
            assumptions.Select(static assumption => assumption.Id),
            Is.All.Matches("^spa1:[0-9a-f]{64}$"));
        Assert.That(
            assumptions.Select(static assumption => assumption.Id).Distinct().ToArray(),
            Has.Length.EqualTo(2));
    }

    [Test]
    public void PredicateChangeChangesOnlyThatClaimIdentity()
    {
        var first = Build(("Subject.cs", TwoClaims("==", ">=")));
        var changed = Build(("Subject.cs", TwoClaims("==", ">")));

        Assert.That(
            changed.Manifest.Claims.Select(static claim => claim.ClaimId)
                .Intersect(first.Manifest.Claims.Select(static claim =>
                    claim.ClaimId)),
            Has.Exactly(1).Items);
    }

    [Test]
    public void SurrogateCharacterConstantsHaveDistinctStableClaimIdentities()
    {
        string Source(int codeUnit)
        {
            return $$"""
                using SharpProof.Attributes;
                public static class Subject {
                    public static char Target(char value) {
                        Contract.Ensures(Contract.Result<char>() == '\u{{codeUnit.ToString("X4", System.Globalization.CultureInfo.InvariantCulture)}}');
                        return value;
                    }
                }
                """;
        }
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var codeUnit in new[] { 0xD800, 0xD801, 0xDC00, 0xDFFF })
        {
            var first = Build(("First.cs", Source(codeUnit))).Manifest.Claims.Single().ClaimId;
            var repeated = Build(("Renamed.cs", Source(codeUnit))).Manifest.Claims.Single().ClaimId;
            Assert.That(first, Is.EqualTo(repeated));
            Assert.That(identities.Add(first), Is.True);
        }
    }

    [Test]
    public void RichPredicateOperationKindsHaveStableSemanticIdentity()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;
            public sealed class Subject<T>
            {
                public event Action? Changed;
                public int this[int index] => index;
                private static int Echo(int value) => value;

                public object Check<U>(object value)
                {
                    Contract.Ensures(
                        this != null &&
                        new object() != null &&
                        ((Func<int, int>)Echo) != null &&
                        value is string &&
                        new int[1].Length == 1 &&
                        this[0] == 0 &&
                        Changed == null &&
                        typeof(U) != typeof(T) &&
                        1.25f < 2.5f &&
                        1.25d < 2.5d &&
                        1.25m < 2.5m);
                    Contract.Ensures(
                        Contract.Result<object>() == value);
                    return value;
                }
            }
            """;
        var first = Build(("First.cs", source));
        var renamed = Build(("Renamed.cs", source));
        var changed = Build((
            "Changed.cs",
            source.Replace("1.25m < 2.5m", "1.5m < 2.5m",
                StringComparison.Ordinal)));
        var firstIds = first.Manifest.Claims
            .Select(static claim => claim.ClaimId)
            .ToArray();
        var renamedIds = renamed.Manifest.Claims
            .Select(static claim => claim.ClaimId)
            .ToArray();
        var changedIds = changed.Manifest.Claims
            .Select(static claim => claim.ClaimId)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstIds, Has.Length.EqualTo(2));
            Assert.That(firstIds, Is.Unique);
            Assert.That(renamedIds, Is.EqualTo(firstIds));
            Assert.That(changedIds.Intersect(firstIds), Has.Exactly(1).Items);
        }
    }

    [Test]
    public void ReorderingDistinctClaimsPreservesTheirIdentitySet()
    {
        var first = Build(("Subject.cs", TwoClaims("==", ">=")));
        var reordered = Build(("Subject.cs", TwoClaims(">=", "==")));

        Assert.That(
            reordered.Manifest.Claims.Select(static claim => claim.ClaimId),
            Is.EquivalentTo(first.Manifest.Claims.Select(static claim =>
                claim.ClaimId)));
        Assert.That(
            reordered.Manifest.Claims.Select(static claim => claim.Ordinal),
            Is.EqualTo(DenseOrdinals));
    }

    [Test]
    public void DuplicatePredicatesReceiveDeterministicDistinctIds()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Identity(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
            }
            """;
        var first = Build(("Subject.cs", source));
        var second = Build(("Other.cs", source));

        Assert.That(
            first.Manifest.Claims.Select(static claim => claim.ClaimId),
            Is.Unique);
        Assert.That(
            second.Manifest.Claims.Select(static claim => claim.ClaimId),
            Is.EqualTo(first.Manifest.Claims.Select(static claim =>
                claim.ClaimId)));
    }

    [Test]
    public void PartialMethodUsesItsImplementationExactlyOnce()
    {
        var result = Build(
            (
                "Definition.cs",
                """
                public static partial class Subject {
                    public static partial long Identity(long value);
                }
                """),
            (
                "Implementation.cs",
                """
                using SharpProof.Attributes;
                public static partial class Subject {
                    public static partial long Identity(long value) {
                        Contract.Ensures(
                            Contract.Result<long>() == value);
                        return value;
                    }
                }
                """));

        var target = result.Targets.Values.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Manifest.Callables, Has.Length.EqualTo(1));
            Assert.That(result.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(target.Method.PartialDefinitionPart, Is.Not.Null);
            Assert.That(
                Path.GetFileName(target.Declaration!.SyntaxTree.FilePath),
                Is.EqualTo("Implementation.cs"));
            Assert.That(
                Path.GetFileName(target.Claims[0].Entry.Location.Path),
                Is.EqualTo("Implementation.cs"));
        }
    }

    [Test]
    public void CompanionAndReturnAttributeClaimsBelongToTarget()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;
            public class Subject {
                [return: Positive]
                public long Identity(long value) => value;
            }
            [ContractFor(typeof(Subject))]
            public static class SubjectContracts {
                public static long Identity(
                    Subject receiver,
                    long value) {
                    Contract.Ensures(
                        Contract.Result<long>() == value);
                    return value;
                }
            }
            """));

        var target = result.Targets.Values.Single();
        Assert.That(
            target.Claims.Select(static claim => claim.Entry.Evidence),
            Is.EqualTo(CompanionEvidence));
        Assert.That(
            target.Claims.Select(static claim => claim.Entry.CallableId),
            Is.All.EqualTo(target.Entry.CallableId));
        Assert.That(target.Claims[0].SourceOperation, Is.Not.Null);
        Assert.That(target.Claims[1].SourceAttribute, Is.Not.Null);
    }

    [Test]
    public void NestedCallableClausesDoNotHideTargetCompanionClaims()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Identity(long value) {
                    long Local(long item) {
                        Contract.Ensures(
                            Contract.Result<long>() == item);
                        return item;
                    }
                    return Local(value);
                }
            }
            [ContractFor(typeof(Subject))]
            public static class SubjectContracts {
                public static long Identity(long value) {
                    Contract.Ensures(
                        Contract.Result<long>() == value);
                    return value;
                }
            }
            """));

        var identity = result.Targets.Values.Single(static target =>
            target.Method.Name == "Identity");
        var local = result.Targets.Values.Single(static target =>
            target.Method.MethodKind == MethodKind.LocalFunction);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Manifest.Callables, Has.Length.EqualTo(2));
            Assert.That(result.Manifest.Claims, Has.Length.EqualTo(2));
            Assert.That(
                identity.Claims.Single().Entry.Evidence,
                Is.EqualTo(WorkerClaimEvidence.CompanionClause));
            Assert.That(
                local.Claims.Single().Entry.Evidence,
                Is.EqualTo(WorkerClaimEvidence.DirectClause));
        }
    }

    [Test]
    public void NestedOnlyClausesDoNotSelectTheirContainingMethod()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Outer(long value) {
                    long Local(long item) {
                        Contract.Ensures(
                            Contract.Result<long>() == item);
                        return item;
                    }
                    return Local(value);
                }
            }
            """));

        var target = result.Targets.Values.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                target.Method.MethodKind,
                Is.EqualTo(MethodKind.LocalFunction));
            Assert.That(result.Manifest.Callables, Has.Length.EqualTo(1));
            Assert.That(result.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(
                result.Manifest.Callables.Any(static callable =>
                    callable.CallableId.Contains(
                        "Outer",
                        StringComparison.Ordinal) &&
                    !callable.CallableId.Contains(
                        "Local",
                        StringComparison.Ordinal)),
                Is.False);
        }
    }

    [Test]
    public void MalformedCompanionSelectionFailsClosed()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public sealed class Subject {
                public long Identity(long value) => value;
            }
            [ContractFor(typeof(Subject))]
            public static class SubjectContracts {
                public static long Identity(
                    Subject receiver,
                    string unexpected) {
                    Contract.Ensures(true);
                    return unexpected.Length;
                }
            }
            """;
        var compilation = GetCompilation(("Subject.cs", source));
        var result = new ClaimManifestBuilder(compilation).Build();
        var target = result.Targets.Values.Single();
        var binding = new ContractBinder(
            compilation,
            new SharpProof.Ir.IrFactory()).Bind(
                target.Method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                target.Entry.SelectedFeatures,
                Is.EqualTo([WorkerSelectedFeature.Contracts]));
            Assert.That(target.Claims, Is.Empty);
            Assert.That(target.Entry.Assumptions, Is.Empty);
            Assert.That(
                binding.Failure,
                Is.EqualTo(
                    ContractBindingFailure.CompanionSignatureMismatch));
        }
    }

    [Test]
    public void ExplicitInterfaceImplementationCanBeVerifierSupported()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;
            public interface ISubject { int Read(int value); }
            public sealed class Subject : ISubject {
                [EnforcePure]
                int ISubject.Read(int value) => value;
            }
            """));

        var target = result.Targets.Values.Single();
        Assert.That(target.Method.MethodKind,
            Is.EqualTo(MethodKind.ExplicitInterfaceImplementation));
        Assert.That(target.IsVerifierSupported, Is.True);
    }

    [Test]
    public void DirectClausesOwnTheEntireContractSource()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Identity(long value) {
                    Contract.Ensures(
                        Contract.Result<long>() == value);
                    return value;
                }
            }
            [ContractFor(typeof(Subject))]
            public static class SubjectContracts {
                public static long Identity(long value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """));

        var target = result.Targets.Values.Single();
        Assert.That(target.Claims, Has.Length.EqualTo(1));
        Assert.That(
            target.Claims[0].Entry.Evidence,
            Is.EqualTo(WorkerClaimEvidence.DirectClause));
        Assert.That(target.Entry.Assumptions, Is.Empty);
    }

    [Test]
    public void InvalidPlacementDoesNotHideAnyPostcondition()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Identity(long value) {
                    Contract.Ensures(
                        Contract.Result<long>() == value);
                    if (value > 0) {
                        Contract.Ensures(
                            Contract.Result<long>() >= value);
                    }
                    return value;
                }
            }
            """;
        var compilation = GetCompilation(("Subject.cs", source));
        var result = new ClaimManifestBuilder(compilation).Build();
        var target = result.Targets.Values.Single();
        var binding = new ContractBinder(
            compilation,
            new SharpProof.Ir.IrFactory()).Bind(
                target.Method);

        Assert.That(target.Claims, Has.Length.EqualTo(2));
        Assert.That(
            target.Claims.Select(static claim => claim.Placement),
            Is.EqualTo(new ContractClausePlacement?[] {
                ContractClausePlacement.ValidPrologue,
                ContractClausePlacement.Conditional
            }));
        Assert.That(
            binding.Failure,
            Is.EqualTo(ContractBindingFailure.InvalidClausePlacement));
    }

    [Test]
    public void UnsupportedAccessorAndLocalFunctionRemainSelected()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;
            public sealed class Subject {
                [DoesNotThrow]
                public static long Value => 1;
                public static Subject operator +(
                    Subject left,
                    Subject right) {
                    Contract.Ensures(
                        Contract.Result<Subject>() != null);
                    return left;
                }
                public static void Outer() {
                    long Local(long value) {
                        Contract.Ensures(
                            Contract.Result<long>() == value);
                        return value;
                    }
                    _ = Local(1);
                }
            }
            """));

        Assert.That(
            result.Manifest.Callables,
            Has.Length.EqualTo(3),
            string.Join(
                ", ",
                result.Manifest.Callables.Select(static callable =>
                    callable.CallableId)));
        Assert.That(
            result.Targets.Values.All(static target =>
                !target.IsVerifierSupported),
            Is.True);
        Assert.That(
            result.Manifest.Claims,
            Has.Length.EqualTo(3));
        Assert.That(
            result.Manifest.Callables.Single(callable =>
                callable.SelectedFeatures.Contains(
                WorkerSelectedFeature.Effects)).SelectedFeatures,
            Does.Contain(WorkerSelectedFeature.Effects));
    }

    [Test]
    public void FieldLikeEventMethodAttributesDiscoverBothAccessorsOnce()
    {
        var result = Build((
            "Subject.cs",
            """
            using System;
            using SharpProof.Attributes;

            public sealed class Subject {
                [method: DoesNotThrow]
                public event Action? FieldLike, SecondFieldLike;

                public event Action? Custom {
                    [DoesNotThrow]
                    add { }
                    [DoesNotThrow]
                    remove { }
                }

                public event Action? Unselected;

                [DoesNotThrow]
                public int Value => 1;

                [DoesNotThrow]
                public void Method() { }
            }
            """));
        var targets = result.Targets.Values.ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(targets, Has.Length.EqualTo(8));
            Assert.That(
                targets.Count(static target =>
                    target.Method.AssociatedSymbol?.Name == "FieldLike" &&
                    target.Method.MethodKind == MethodKind.EventAdd),
                Is.EqualTo(1));
            Assert.That(
                targets.Count(static target =>
                    target.Method.AssociatedSymbol?.Name == "FieldLike" &&
                    target.Method.MethodKind == MethodKind.EventRemove),
                Is.EqualTo(1));
            Assert.That(
                targets.Count(static target =>
                    target.Method.AssociatedSymbol?.Name == "SecondFieldLike" &&
                    target.Method.MethodKind == MethodKind.EventAdd),
                Is.EqualTo(1));
            Assert.That(
                targets.Count(static target =>
                    target.Method.AssociatedSymbol?.Name == "SecondFieldLike" &&
                    target.Method.MethodKind == MethodKind.EventRemove),
                Is.EqualTo(1));
            Assert.That(
                targets.Count(static target =>
                    target.Method.AssociatedSymbol?.Name == "Custom" &&
                    target.Method.MethodKind == MethodKind.EventAdd),
                Is.EqualTo(1));
            Assert.That(
                targets.Count(static target =>
                    target.Method.AssociatedSymbol?.Name == "Custom" &&
                    target.Method.MethodKind == MethodKind.EventRemove),
                Is.EqualTo(1));
            Assert.That(
                targets.Count(static target =>
                    target.Method.AssociatedSymbol?.Name == "Value" &&
                    target.Method.MethodKind == MethodKind.PropertyGet),
                Is.EqualTo(1));
            Assert.That(
                targets.Count(static target =>
                    target.Method.Name == "Method" &&
                    target.Method.MethodKind == MethodKind.Ordinary),
                Is.EqualTo(1));
            Assert.That(
                targets.Any(static target =>
                    target.Method.AssociatedSymbol?.Name == "Unselected"),
                Is.False);
            Assert.That(
                result.Manifest.Callables.Select(static callable =>
                    callable.CallableId).Distinct(StringComparer.Ordinal).ToArray(),
                Has.Length.EqualTo(8));
            Assert.That(
                result.Manifest.Claims.Select(static claim =>
                    claim.ClaimId).Distinct(StringComparer.Ordinal).ToArray(),
                Has.Length.EqualTo(8));
            Assert.That(
                targets.SelectMany(static target => target.EffectClaims).ToArray(),
                Has.Length.EqualTo(8));
        }
    }

    [Test]
    public void UnsupportedEffectCallablesCannotCarryConcreteEvidence()
    {
        var result = Build((
            "Subject.cs",
            WorkerTestSources.UnsupportedEffectCallables));
        var targets = result.Targets.Values.ToDictionary(
            static target => target.Method.Name,
            StringComparer.Ordinal);

        AssertUnsupportedEffectTargets(
            targets,
            "Async",
            "DelegateCall",
            "Generic");
    }

    [Test]
    public void UnsupportedContractCallablesUseTheSharedSubsetGate()
    {
        var result = Build((
            "Subject.cs",
            WorkerTestSources.UnsupportedContractCallables));
        var targets = result.Targets.Values.ToDictionary(
            static target => target.Method.Name,
            StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(targets, Has.Count.EqualTo(2));
            Assert.That(targets, Does.ContainKey("Async"));
            Assert.That(targets, Does.ContainKey("Generic"));
            Assert.That(
                targets.Values.All(static target =>
                    !target.IsVerifierSupported),
                Is.True);
            Assert.That(
                targets.Values.SelectMany(static target =>
                    target.Claims).Count(),
                Is.EqualTo(2));
        }
    }

    [Test]
    public void UnsupportedEffectCallableShapesCannotCarryReplayEvidence()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;

            public sealed class Subject {
                private static object _value = null!;

                [ZeroAllocations]
                static Subject() =>
                    _value = new object();

                public static object Value {
                    [ZeroAllocations]
                    get => new object();
                }
            }
            """));
        var targets = result.Targets.Values.ToDictionary(
            static target => target.Method.Name,
            StringComparer.Ordinal);

        AssertUnsupportedEffectTargets(
            targets,
            ".cctor",
            "get_Value");
    }

    [Test]
    public void AnonymousCallablesHaveUniqueStableIdsAndRemainUnsupported()
    {
        var first = Build((
            "First.cs",
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static void Outer() {
                    Func<long, long> first = value => {
                        Contract.Ensures(Contract.Result<long>() == value);
                        return value;
                    };
                    Func<long, long> second = delegate(long other) {
                        Contract.Ensures(Contract.Result<long>() >= other);
                        return other;
                    };
                    _ = first(second(1));
                }
            }
            """));
        var renamed = Build((
            "Renamed.cs",
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static void Outer()
                {
                    // Paths, trivia, and parameter names do not identify callables.
                    Func<long, long> first = renamedValue =>
                    {
                        Contract.Ensures(
                            Contract.Result<long>() == renamedValue);
                        return renamedValue;
                    };
                    Func<long, long> second = delegate(long renamedOther)
                    {
                        Contract.Ensures(
                            Contract.Result<long>() >= renamedOther);
                        return renamedOther;
                    };
                    _ = first(second(1));
                }
            }
            """));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Manifest.Callables, Has.Length.EqualTo(2));
            Assert.That(first.Manifest.Claims, Has.Length.EqualTo(2));
            Assert.That(first.Manifest.Callables.Select(
                static callable => callable.CallableId), Is.Unique);
            Assert.That(first.Targets.Values.All(static target =>
                target.Method.MethodKind == MethodKind.AnonymousFunction &&
                !target.IsVerifierSupported), Is.True);
            Assert.That(renamed.Manifest.Callables.Select(
                    static callable => callable.CallableId),
                Is.EqualTo(first.Manifest.Callables.Select(
                    static callable => callable.CallableId)));
            Assert.That(renamed.Manifest.Claims.Select(static claim => claim.ClaimId),
                Is.EqualTo(first.Manifest.Claims.Select(static claim => claim.ClaimId)));
        }
    }

    [Test]
    public void UnrelatedEarlierNestedCallableDoesNotRenumberClaimedCallable()
    {
        static string Source(bool includeUnrelated)
        {
            return """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static void Outer() {
                    PLACEHOLDER
                    Func<long, long> selected = value => {
                        Contract.Ensures(Contract.Result<long>() == value);
                        return value;
                    };
                    _ = selected(1);
                }
            }
            """.Replace("PLACEHOLDER", includeUnrelated
                ? "Func<long, long> unrelated = value => value;"
                : string.Empty, StringComparison.Ordinal);
        }

        var without = Build(("Subject.cs", Source(false)));
        var with = Build(("Subject.cs", Source(true)));
        var withoutId = without.Manifest.Callables.Single(static callable =>
            callable.CallableId != "M:Subject.Outer()").CallableId;
        var withId = with.Manifest.Callables.Single(static callable =>
            callable.CallableId != "M:Subject.Outer()").CallableId;

        Assert.That(withId, Is.EqualTo(withoutId));
    }

    [Test]
    public void NestedCallableClaimsAppearExactlyOnceWithoutIdentityCollisions()
    {
        var result = Build((
            "Subject.cs",
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static void Outer() {
                    Func<long, long> first = value => {
                        Contract.Ensures(Contract.Result<long>() == value);
                        long Local(long item) {
                            Contract.Ensures(Contract.Result<long>() == item);
                            return item;
                        }
                        return Local(value);
                    };
                    Func<long, long> second = value => {
                        Contract.Ensures(Contract.Result<long>() >= value);
                        long Local(long item) {
                            Contract.Ensures(Contract.Result<long>() >= item);
                            return item;
                        }
                        return Local(value);
                    };
                    _ = first(second(1));
                }
            }
            """));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Manifest.Callables, Has.Length.EqualTo(4));
            Assert.That(result.Manifest.Claims, Has.Length.EqualTo(4));
            Assert.That(result.Manifest.Callables.Select(
                static callable => callable.CallableId), Is.Unique);
            Assert.That(result.Manifest.Claims.Select(
                static claim => claim.Location.Start), Is.Unique);
            Assert.That(result.Targets.Values.Count(static target =>
                target.Method.MethodKind == MethodKind.AnonymousFunction), Is.EqualTo(2));
            Assert.That(result.Targets.Values.Count(static target =>
                target.Method.MethodKind == MethodKind.LocalFunction), Is.EqualTo(2));
            Assert.That(result.Targets.Values.All(static target =>
                !target.IsVerifierSupported), Is.True);
        }
    }

    [Test]
    public void TopLevelAndNestedClaimsAreAccountedForExactlyOnce()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;
            Contract.Ensures(Contract.Result<int>() == 0);
            long Local(long value) {
                Contract.Ensures(Contract.Result<long>() == value);
                return value;
            }
            Func<long, long> lambda = value => {
                Contract.Ensures(Contract.Result<long>() == value);
                return value;
            };
            _ = Local(lambda(1));
            return 0;
            """;
        var result = new ClaimManifestBuilder(GetCompilation(
            OutputKind.ConsoleApplication, ("Program.cs", source))).Build();
        var topLevel = result.Targets.Values.Single(static target =>
            target.Declaration is CompilationUnitSyntax);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Manifest.Callables, Has.Length.EqualTo(3));
            Assert.That(result.Manifest.Claims, Has.Length.EqualTo(3));
            Assert.That(result.Manifest.Claims.Select(
                static claim => claim.Location.Start), Is.Unique);
            Assert.That(result.Targets.Values.All(static target =>
                !target.IsVerifierSupported), Is.True);
            Assert.That(topLevel.Method.MethodKind, Is.EqualTo(MethodKind.Ordinary));
            Assert.That(topLevel.Claims.Single().Placement,
                Is.EqualTo(ContractClausePlacement.ValidPrologue));
        }
    }

    [Test]
    public void SameTypedLocalReferencesHaveStableDistinctIdentity()
    {
        var first = Build(("First.cs", LocalReferenceSource("first", "first")));
        var renamed = Build(("Renamed.cs", LocalReferenceSource("renamed", "renamed")));
        var second = Build(("Second.cs", LocalReferenceSource("first", "second")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(renamed.Manifest.Claims.Single().ClaimId,
                Is.EqualTo(first.Manifest.Claims.Single().ClaimId));
            Assert.That(second.Manifest.Claims.Single().ClaimId,
                Is.Not.EqualTo(first.Manifest.Claims.Single().ClaimId));
        }
    }

    [Test]
    public void UserAndTrustedAssumptionsAreStableAndVisible()
    {
        const string source =
            """
            using SharpProof.Attributes;
            [SharpProofTrusted("reviewed boundary")]
            public static class Subject {
                public static long Identity(long value) {
                    Contract.Assume(value >= 0);
                    Contract.Ensures(
                        Contract.Result<long>() == value);
                    return value;
                }
            }
            """;
        var first = Build(("First.cs", source)).Targets.Values.Single();
        var second = Build(("Second.cs", source)).Targets.Values.Single();

        Assert.That(
            first.Entry.Assumptions.Select(static value => value.Kind),
            Is.EqualTo(WorkerTestData.UserAndTrustedAssumptions));
        Assert.That(
            first.Entry.Assumptions.Select(static value => value.Id),
            Is.EqualTo(second.Entry.Assumptions.Select(static value => value.Id)));
        Assert.That(
            first.Entry.Assumptions.All(static value => !value.Used),
            Is.True);
    }

    [Test]
    public void NestedTypeParameterRolesHaveDistinctSemanticIdentity()
    {
        const string template =
            """
            using SharpProof.Attributes;
            public sealed class Outer<T> {
                public sealed class Inner<U> {
                    public static object Identity(object value) {
                        Contract.Ensures(typeof(REPLACE) != null);
                        return value;
                    }
                }
            }
            """;
        var outer = Build(("Subject.cs", template.Replace(
            "REPLACE", "T", StringComparison.Ordinal)));
        var inner = Build(("Subject.cs", template.Replace(
            "REPLACE", "U", StringComparison.Ordinal)));

        Assert.That(
            inner.Manifest.Claims.Single().ClaimId,
            Is.Not.EqualTo(outer.Manifest.Claims.Single().ClaimId));
    }

    [Test]
    public void SameNamedForeignAttributeDoesNotSelectCallable()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;
            namespace Foreign {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class DoesNotThrowAttribute :
                    System.Attribute { }
            }
            public static class Subject {
                [Foreign.DoesNotThrow]
                public static long Identity(long value) => value;
            }
            """));

        Assert.That(result.Manifest.Callables, Is.Empty);
    }

    [Test]
    public void RejectedReturnAttributeCannotProveManifestEffectTransitively()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;

            namespace SharpProof.Attributes {
                [System.AttributeUsage(
                    System.AttributeTargets.ReturnValue)]
                public sealed class NotNullAttribute :
                    System.Attribute {
                }
            }

            public static class Subject {
                [return: SharpProof.Attributes.NotNull]
                private static string MaybeNull(bool condition) {
                    return condition ? "" : null!;
                }

                [DoesNotThrow]
                public static int Call(bool condition) {
                    return MaybeNull(condition).Length;
                }
            }
            """));

        var target = result.Targets.Values.Single(static candidate =>
            candidate.Method.Name == "Call");
        var evidence = target.EffectClaims.Single().Evidence;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Method.Name, Is.EqualTo("Call"));
            Assert.That(evidence.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence.Reason,
                Is.EqualTo(WorkerClaimReason.EffectSummaryIncomplete));
        }
    }

    [Test]
    public void SuppressionAloneDoesNotSelectCallable()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;
            public static class Subject {
                [SharpProofSuppress("reporting only")]
                public static long Identity(long value) => value;
            }
            """));

        Assert.That(result.Manifest.Callables, Is.Empty);
    }

    [TestCase("method")]
    [TestCase("type")]
    [TestCase("assembly")]
    public void SuppressionScopesRetainSelectedClaimsInTheManifest(
        string scope)
    {
        const string template =
            """
            using SharpProof.Attributes;
            ASSEMBLY_SUPPRESSION
            TYPE_SUPPRESSION
            public static class Subject {
                METHOD_SUPPRESSION
                [DoesNotThrow]
                public static void Throwing() =>
                    throw new System.InvalidOperationException();

                METHOD_SUPPRESSION
                public static long Identity(long value) {
                    Contract.Ensures(
                        Contract.Result<long>() == value + 1L);
                    return value;
                }
            }
            """;
        var controlSource = template
            .Replace("ASSEMBLY_SUPPRESSION", string.Empty, StringComparison.Ordinal)
            .Replace("TYPE_SUPPRESSION", string.Empty, StringComparison.Ordinal)
            .Replace("METHOD_SUPPRESSION", string.Empty, StringComparison.Ordinal);
        var suppression = scope switch
        {
            "method" => "[SharpProofSuppress(\"reviewed method\")]",
            "type" => "[SharpProofSuppress(\"reviewed type\")]",
            "assembly" => "[assembly: SharpProofSuppress(\"reviewed assembly\")]",
            _ => throw new InvalidOperationException(
                $"Unknown suppression scope '{scope}'.")
        };
        var suppressedSource = template
            .Replace(
                "ASSEMBLY_SUPPRESSION",
                scope == "assembly" ? suppression : string.Empty,
                StringComparison.Ordinal)
            .Replace(
                "TYPE_SUPPRESSION",
                scope == "type" ? suppression : string.Empty,
                StringComparison.Ordinal)
            .Replace(
                "METHOD_SUPPRESSION",
                scope == "method" ? suppression : string.Empty,
                StringComparison.Ordinal);

        var control = Build(("Control.cs", controlSource));
        var suppressed = Build(("Suppressed.cs", suppressedSource));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                control.Manifest.Claims.Select(static claim => claim.Kind),
                Is.EquivalentTo([
                    WorkerClaimKind.Postcondition,
                    WorkerClaimKind.Effect
                ]));
            Assert.That(suppressed.Manifest.Callables, Has.Length.EqualTo(2));
            Assert.That(
                suppressed.Manifest.Claims.Select(static claim => claim.Kind),
                Is.EquivalentTo(control.Manifest.Claims.Select(
                    static claim => claim.Kind)));
            Assert.That(
                control.Targets.Values
                    .SelectMany(static target => target.EffectClaims)
                    .Single().Evidence.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                suppressed.Targets.Values
                    .SelectMany(static target => target.EffectClaims)
                    .Single().Evidence.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
        }
    }

    [Test]
    public void FeatureSelectionFiltersTheManifest()
    {
        var compilation = GetCompilation((
            "Subject.cs",
            """
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                public static long Identity(long value) {
                    Contract.Ensures(
                        Contract.Result<long>() == value);
                    return value;
                }
            }
            """));

        var contracts = new ClaimManifestBuilder(
            compilation,
            WorkerFeatureSet.Contracts).Build().Manifest;
        var effects = new ClaimManifestBuilder(
            compilation,
            WorkerFeatureSet.Effects).Build().Manifest;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(contracts.Claims, Has.Length.EqualTo(1));
            Assert.That(
                contracts.Callables.Single().SelectedFeatures,
                Is.EqualTo([WorkerSelectedFeature.Contracts]));
            Assert.That(effects.Claims, Has.Length.EqualTo(1));
            Assert.That(effects.Claims.Single().Kind,
                Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(effects.Claims.Single().EffectContractKind,
                Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
            Assert.That(
                effects.Callables.Single().SelectedFeatures,
                Is.EqualTo([WorkerSelectedFeature.Effects]));
        }
    }

    [Test]
    public void EffectsSelectionRetainsTrustedEvidence()
    {
        var compilation = GetCompilation((
            "Subject.cs",
            """
            using SharpProof.Attributes;
            public static class Subject {
                [SharpProofTrusted("reviewed boundary")]
                [DoesNotThrow]
                public static long Identity(long value) => value;
            }
            """));

        var target = new ClaimManifestBuilder(
            compilation,
            WorkerFeatureSet.Effects).Build().Targets.Values.Single();

        Assert.That(
            target.Entry.Assumptions.Select(static evidence => evidence.Kind),
            Is.EqualTo([WorkerAssumptionKind.TrustedBoundary]));
    }

    [Test]
    public void EffectContractsProduceStableTypedClaimsAndSealedEvidence()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                [EffectContract(
                    SharpProofEffect.Throws | SharpProofEffect.Allocates,
                    ThrownExceptions = new[] { typeof(Exception) },
                    Complete = true)]
                public static void ThrowDerived() =>
                    throw new InvalidOperationException();
            }
            """;
        var first = Build(("First.cs", source));
        var second = Build(("Second.cs", source));
        var claim = first.Manifest.Claims.Single();
        var evidence = first.Targets.Values.Single().EffectClaims.Single().Evidence;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(claim.Evidence, Is.EqualTo(WorkerClaimEvidence.Attribute));
            Assert.That(claim.EffectContractKind,
                Is.EqualTo(WorkerEffectContractKind.EffectContract));
            Assert.That(evidence.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown),
                evidence.Evidence);
            Assert.That(evidence.EvidenceSha256, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(second.Manifest.Claims.Single().ClaimId,
                Is.EqualTo(claim.ClaimId));
        }
    }

    [Test]
    public void ConstructedGenericExceptionClaimsRemainExact()
    {
        var discovery = Build((
            "Subject.cs",
            """
            using System;
            using SharpProof.Attributes;

            public class GenericException<T> : Exception {
            }

            public sealed class DerivedStringException
                : GenericException<string> {
            }

            public static class Subject {
                [AllowedExceptions(typeof(GenericException<int>))]
                public static void WrongAllowed(
                    [NotNull] GenericException<string> exception) =>
                    throw exception;

                [AllowedExceptions(typeof(GenericException<string>))]
                public static void ExactAllowed(
                    [NotNull] GenericException<string> exception) =>
                    throw exception;

                [AllowedExceptions(typeof(GenericException<string>))]
                public static void DerivedAllowed(
                    [NotNull] DerivedStringException exception) =>
                    throw exception;

                [DoesNotThrow]
                public static void WrongCatch(
                    [NotNull] GenericException<string> exception) {
                    try {
                        throw exception;
                    }
                    catch (GenericException<int>) {
                    }
                }

                [DoesNotThrow]
                public static void ExactCatch(
                    [NotNull] GenericException<string> exception) {
                    try {
                        throw exception;
                    }
                    catch (GenericException<string>) {
                    }
                }
            }
            """));
        var targets = discovery.Targets.Values.ToDictionary(
            static target => target.Method.Name,
            StringComparer.Ordinal);
        var wrongAllowed = targets["WrongAllowed"].EffectClaims.Single().Evidence;
        var exactAllowed = targets["ExactAllowed"].EffectClaims.Single().Evidence;
        var derivedAllowed = targets["DerivedAllowed"].EffectClaims.Single().Evidence;
        var wrongCatch = targets["WrongCatch"].EffectClaims.Single().Evidence;
        var exactCatch = targets["ExactCatch"].EffectClaims.Single().Evidence;
        var thrownStringType = (INamedTypeSymbol)targets["WrongAllowed"]
            .Method.Parameters[0].Type;
        var integerIdentity = wrongAllowed.Constraint
            .AllowedExceptionTypes.Single();
        var stringIdentity = CompilerExceptionTypeIdentity.Encode(
            thrownStringType);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(integerIdentity, Is.Not.EqualTo(stringIdentity));
            Assert.That(
                wrongAllowed.Constraint.AllowedExceptionTypes,
                Is.EqualTo([integerIdentity]));
            // The compiler only declares these claims; Z3 decides them.
            Assert.That(new[] { wrongAllowed, exactAllowed, derivedAllowed, wrongCatch, exactCatch }
                .Select(static evidence => evidence.Outcome), Is.All.EqualTo(WorkerClaimOutcome.Unknown));
        }
    }

    [Test]
    public void RepeatableEffectAttributesEachReceiveAStableClaim()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                [AllowedExceptions(typeof(Exception))]
                [AllowedExceptions(typeof(InvalidOperationException))]
                [EffectContract(
                    SharpProofEffect.Throws | SharpProofEffect.Allocates,
                    ThrownExceptions = new[] { typeof(Exception) },
                    Complete = true)]
                [EffectContract(
                    SharpProofEffect.Throws | SharpProofEffect.Allocates,
                    ThrownExceptions = new[] { typeof(Exception) },
                    Complete = true)]
                public static void Throw() =>
                    throw new InvalidOperationException();
            }
            """;
        var first = Build(("First.cs", source));
        var second = Build(("Second.cs", source));
        var claims = first.Manifest.Claims;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(claims, Has.Length.EqualTo(3));
            Assert.That(
                claims.Select(static claim => claim.EffectContractKind),
                Is.EqualTo([
                    WorkerEffectContractKind.AllowedExceptions,
                    WorkerEffectContractKind.EffectContract,
                    WorkerEffectContractKind.EffectContract
                ]));
            Assert.That(
                claims.Select(static claim => claim.Ordinal),
                Is.EqualTo([0, 1, 2]));
            Assert.That(
                claims.Select(static claim => claim.ClaimId).Distinct().ToArray(),
                Has.Length.EqualTo(3));
            Assert.That(
                second.Manifest.Claims.Select(static claim =>
                    claim.ClaimId),
                Is.EqualTo(claims.Select(static claim =>
                    claim.ClaimId)));
            Assert.That(
                first.Targets.Values.Single().EffectClaims.Select(
                    static claim => claim.Evidence.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Unknown));
        }
    }

    private static string TwoClaims(string first, string second)
    {
        return $$"""
        using SharpProof.Attributes;
        public static class Subject {
            public static long Identity(long value) {
                Contract.Ensures(
                    Contract.Result<long>() {{first}} value);
                Contract.Ensures(
                    Contract.Result<long>() {{second}} value);
                return value;
            }
        }
        """;
    }

    private static void AssertUnsupportedEffectTargets(
        IReadOnlyDictionary<string, ManifestCallableTarget> targets,
        params string[] expectedNames)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(targets, Has.Count.EqualTo(expectedNames.Length));
            foreach (var expectedName in expectedNames)
            {
                Assert.That(targets, Does.ContainKey(expectedName));
            }
            Assert.That(
                targets.Values.All(static target =>
                    !target.IsVerifierSupported),
                Is.True);
            Assert.That(
                targets.Values.SelectMany(static target =>
                    target.EffectClaims).Select(static claim =>
                    claim.Evidence.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                targets.Values.SelectMany(static target =>
                    target.EffectClaims).Select(static claim =>
                    claim.Evidence.Reason),
                Is.All.EqualTo(
                    WorkerClaimReason.UnsupportedContract));
        }
    }

    private static string LocalReferenceSource(string firstName, string predicateName)
    {
        return $$"""
        using SharpProof.Attributes;
        public static class Subject {
            public static long Identity(long value) {
                long {{firstName}} = value;
                long second = value + 1;
                Contract.Ensures({{predicateName}} >= 0);
                return value;
            }
        }
        """;
    }

    [Test]
    public void DeeplyNestedPredicatesStillProduceAClaimIdentity()
    {
        // The claim fingerprint walks the operation tree recursively. Beyond its
        // depth budget it truncates rather than recursing, because
        // StackOverflowException is uncatchable and would take the compiler down.
        // Truncation is safe: identity also carries a duplicate rank, so claims
        // that fingerprint alike still receive distinct ids.
        var predicate = string.Join(" + ", Enumerable.Repeat("value", 400));
        var source = $$"""
            using SharpProof.Attributes;

            public static class Subject {
                public static long Deep(long value) {
                    Contract.Ensures({{predicate}} >= 0);
                    return value;
                }
            }
            """;

        var result = Build(("Subject.cs", source));

        var claims = result.Manifest.Claims.Where(static claim =>
            claim.Kind == WorkerClaimKind.Postcondition).ToArray();
        Assert.That(claims, Has.Length.EqualTo(1));
        Assert.That(claims[0].ClaimId, Is.Not.Empty);
    }

    [Test]
    public async Task DeeplyNestedUnselectedCallablesDoNotOverflowManifestDiscovery()
    {
        const string childVariable =
            "SHARPPROOF_NESTED_CALLABLE_STACK_CHILD";
        const string markerVariable =
            "SHARPPROOF_NESTED_CALLABLE_STACK_MARKER";
        if (!string.Equals(
                Environment.GetEnvironmentVariable(childVariable),
                "1",
                StringComparison.Ordinal))
        {
            var startInfo = ProcessRunner.CreateStartInfo(
                Environment.CurrentDirectory,
                "dotnet",
                new[]
                {
                    "vstest",
                    typeof(ClaimManifestBuilderTests).Assembly.Location,
                    "/TestCaseFilter:FullyQualifiedName=" +
                    typeof(ClaimManifestBuilderTests).FullName + "." +
                    nameof(
                        DeeplyNestedUnselectedCallablesDoNotOverflowManifestDiscovery)
            });
            startInfo.Environment[childVariable] = "1";
            using var temporary = new TempDirectory("nested-callable-stack-");
            var marker = Path.Combine(temporary.FullName, "marker");
            startInfo.Environment[markerVariable] = marker;
            var result = await ProcessRunner.RunCapturedAsync(
                startInfo,
                CancellationToken.None);
            var output = result.CombinedOutput;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero, output);
                Assert.That(File.Exists(marker), Is.True, output);
            }
            return;
        }

        const int depth = 2048;
        var source = new System.Text.StringBuilder(
            "public static class Subject { public static void Outer() {");
        for (var index = 0; index < depth; index++)
        {
            source.Append("void Local").Append(index).Append("() {");
        }
        source.Append("_ = 0;");
        for (var index = 0; index < depth; index++)
        {
            source.Append('}');
        }
        source.Append("} }");

        var compilation = GetCompilation(("Subject.cs", source.ToString()));
        var builder = new ClaimManifestBuilder(compilation);
        const BindingFlags privateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        var builderType = typeof(ClaimManifestBuilder);
        var discover = builderType.GetMethod(
            "DiscoverMethods",
            privateInstance)!;
        var createSeed = builderType.GetMethod(
            "CreateSeed",
            privateInstance)!;
        var createIds = builderType.GetMethod(
            "CreateCallableIds",
            privateInstance)!;
        var methods = (ImmutableArray<IMethodSymbol>)discover.Invoke(
            builder,
            null)!;
        var seedType = createSeed.ReturnType;
        var seedArray = Array.CreateInstance(seedType, methods.Length);
        for (var index = 0; index < methods.Length; index++)
        {
            seedArray.SetValue(
                createSeed.Invoke(builder, [methods[index]]),
                index);
        }
        var createRange = typeof(ImmutableArray)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(static method =>
                method.Name == nameof(ImmutableArray.CreateRange) &&
                method.IsGenericMethodDefinition &&
                method.GetParameters() is [{ ParameterType: { } parameter }] &&
                parameter.IsGenericType &&
                parameter.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .MakeGenericMethod(seedType);
        var seeds = createRange.Invoke(null, [seedArray])!;
        ImmutableDictionary<IMethodSymbol, string>? ids = null;
        var thread = new Thread(
            () => ids =
                (ImmutableDictionary<IMethodSymbol, string>)createIds.Invoke(
                    builder,
                    [seeds])!,
            128 * 1024);
        thread.Start();
        thread.Join();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ids, Is.Not.Null);
            // The tree has no SharpProof selection syntax, so discovery skips
            // semantic binding entirely. No manifest identities are needed for
            // its unrelated local functions.
            Assert.That(ids, Is.Empty);
        }
        var mixed = Build(
            ("Deep.cs", source.ToString()),
            ("Aliases.cs", "global using Z = SharpProof.Attributes.ZeroAllocationsAttribute;"),
            ("Selected.cs", "public static class Selected { [Z] public static int Target() => 0; }"));
        Assert.That(mixed.Targets.Values.Single().Method.Name, Is.EqualTo("Target"));
        Assert.That(mixed.Manifest.Claims.Single().EffectContractKind,
            Is.EqualTo(WorkerEffectContractKind.ZeroAllocations));
        await File.WriteAllTextAsync(
            Environment.GetEnvironmentVariable(markerVariable)!,
            "complete");
    }

    private static ClaimManifestBuildResult Build(
        params (string FileName, string Source)[] sources)
    {
        return new ClaimManifestBuilder(GetCompilation(sources)).Build();
    }

    private static ClaimManifestBuildResult BuildIdentityArtifact(
        params (string FileName, string Source)[] sources)
    {
        var compilation = GetCompilation(sources);
        var result = new ClaimManifestBuilder(compilation).Build();
        AssertIdentityArtifactRoundTrip(compilation, result);
        return result;
    }

    private static ImmutableArray<CompilerCallablePreparation> AssertIdentityArtifactRoundTrip(
        CSharpCompilation compilation, ClaimManifestBuildResult discovery)
    {
        TestCompilation.AssertNoErrors(compilation);
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
            WorkerFeatureSet.All, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var decoded = CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var prepared);
        Assert.That(decoded.Manifest.Callables.Select(static entry => entry.CallableId),
            Is.EqualTo(discovery.Manifest.Callables.Select(static entry => entry.CallableId)));
        Assert.That(decoded.Manifest.Claims.Select(static entry => entry.ClaimId),
            Is.EqualTo(discovery.Manifest.Claims.Select(static entry => entry.ClaimId)));
        Assert.That(prepared.Select(static entry => entry.Entry.CallableId),
            Is.EqualTo(decoded.Manifest.Callables.Select(static entry => entry.CallableId)));
        foreach (var entry in decoded.Manifest.Callables)
        {
            Assert.That(entry.ClaimIds, Is.EqualTo(decoded.Manifest.Claims
                .Where(claim => claim.CallableId == entry.CallableId).Select(static claim => claim.ClaimId)));
            Assert.That(entry.Assumptions.Select(static assumption => assumption.Id),
                Is.EqualTo(discovery.Manifest.Callables.Single(original => original.CallableId == entry.CallableId)
                    .Assumptions.Select(static assumption => assumption.Id)));
        }
        return prepared;
    }

    private static CSharpCompilation GetCompilation(
        params (string FileName, string Source)[] sources)
    {
        return GetCompilation(OutputKind.DynamicallyLinkedLibrary, sources);
    }

    private static CSharpCompilation GetCompilation(
        OutputKind outputKind,
        params (string FileName, string Source)[] sources)
    {
        return TestCompilation.Create(
            "ManifestTests",
            outputKind,
            sources);
    }

}
