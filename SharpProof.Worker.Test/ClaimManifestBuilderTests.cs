using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ClaimManifestBuilderTests
{
    private static readonly int[] DenseOrdinals = [0, 1];
    private static readonly WorkerClaimEvidence[] CompanionEvidence = [
        WorkerClaimEvidence.CompanionClause,
        WorkerClaimEvidence.ReturnAttribute
    ];
    private static readonly WorkerAssumptionKind[] UserAndTrusted = [
        WorkerAssumptionKind.UserAssume,
        WorkerAssumptionKind.TrustedBoundary
    ];

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
            """
            using System;
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public static class Subject {
                [ZeroAllocations]
                public static object Generic<T>() =>
                    new object();

                [ZeroAllocations]
                public static async Task<object> Async() {
                    await Task.Yield();
                    return new object();
                }

                [ZeroAllocations]
                public static object DelegateCall(
                    Func<object> factory) =>
                    new object();
            }
            """));
        var targets = result.Targets.Values.ToDictionary(
            static target => target.Method.Name,
            StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(targets, Has.Count.EqualTo(3));
            Assert.That(targets, Does.ContainKey("Async"));
            Assert.That(targets, Does.ContainKey("DelegateCall"));
            Assert.That(targets, Does.ContainKey("Generic"));
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
            Assert.That(
                targets.Values.SelectMany(static target =>
                    target.EffectClaims).All(static claim =>
                    claim.Evidence.Witness == null &&
                    claim.Evidence.Replay == null),
                Is.True);
        }
    }

    [Test]
    public void UnsupportedContractCallablesUseTheSharedSubsetGate()
    {
        var result = Build((
            "Subject.cs",
            """
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public static class Subject {
                public static int Generic<T>() {
                    Contract.Ensures(
                        Contract.Result<int>() == 1);
                    return 1;
                }

                public static async Task<int> Async() {
                    Contract.Ensures(true);
                    await Task.Yield();
                    return 1;
                }
            }
            """));
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(targets, Has.Count.EqualTo(2));
            Assert.That(targets, Does.ContainKey(".cctor"));
            Assert.That(targets, Does.ContainKey("get_Value"));
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
            Assert.That(
                targets.Values.SelectMany(static target =>
                    target.EffectClaims).All(static claim =>
                    claim.Evidence.Witness == null &&
                    claim.Evidence.Replay == null),
                Is.True);
        }
    }

    [Test]
    public void BodylessEffectAdmissionMatchesTheAnalyzerException()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;

            public static class Subject {
                [SharpProofTrusted("Reviewed native implementation.")]
                [EffectContract(
                    SharpProofEffect.None,
                    Complete = true)]
                public static extern int Accepted();

                [return: Positive]
                [SharpProofTrusted("Reviewed native implementation.")]
                [EffectContract(
                    SharpProofEffect.None,
                    Complete = true)]
                public static extern int ContractSelected();
            }
            """));
        var targets = result.Targets.Values.ToDictionary(
            static target => target.Method.Name,
            StringComparer.Ordinal);
        var accepted = targets["Accepted"];
        var rejected = targets["ContractSelected"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(accepted.IsVerifierSupported, Is.True);
            Assert.That(
                accepted.EffectClaims.Single().Evidence.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                accepted.EffectClaims.Single().Evidence.Certainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty
                        .TrustedCompleteBoundary));
            Assert.That(rejected.IsVerifierSupported, Is.False);
            Assert.That(
                rejected.EffectClaims.Single().Evidence.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                rejected.EffectClaims.Single().Evidence.Reason,
                Is.EqualTo(
                    WorkerClaimReason.UnsupportedContract));
            Assert.That(
                rejected.EffectClaims.Single().Evidence.Replay,
                Is.Null);
        }
    }

    [Test]
    public void InterfaceTrustedAttributeCreatesAnImplementationBoundaryAssumption()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;

            [SharpProofTrusted("reviewed interface boundary")]
            public interface IService
            {
                int Map(int value);
            }

            public sealed class Service : IService
            {
                public int Map(int value) => value;
            }
            """));

        var implementation = result.Targets.Values.Single(target =>
            target.Method.ContainingType.Name == "Service" &&
            target.Method.Name == "Map");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(implementation.Entry.SelectedFeatures,
                Does.Contain(WorkerSelectedFeature.Contracts));
            Assert.That(
                implementation.Entry.Assumptions,
                Has.One.Matches<WorkerAssumptionEvidence>(assumption =>
                    assumption.Kind == WorkerAssumptionKind.TrustedBoundary));
        }
    }

    [Test]
    public void ExplicitInterfaceImplementationUsesTheSupportedCallableSet()
    {
        var result = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;

            public interface IService
            {
                int Map(int value);
            }

            public sealed class Service : IService
            {
                [EnforcePure]
                int IService.Map(int value) => value;
            }
            """));

        var implementation = result.Targets.Values.Single(target =>
            target.Method.MethodKind == MethodKind.ExplicitInterfaceImplementation);

        Assert.That(implementation.IsVerifierSupported, Is.True);
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
            Is.EqualTo(UserAndTrusted));
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
                Is.EqualTo(WorkerClaimReason.EffectContractNotEstablished));
            Assert.That(evidence.Evidence, Does.Contain("NullReferenceException"));
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
            Assert.That(evidence.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
                evidence.Evidence);
            Assert.That(evidence.EvidenceSha256, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(second.Manifest.Claims.Single().ClaimId,
                Is.EqualTo(claim.ClaimId));
        }
    }

    [Test]
    public void EffectEvidenceAccountsForCalleePreconditions()
    {
        var discovery = Build((
            "Subject.cs",
            """
            using SharpProof.Attributes;

            public static class Subject {
                private static void Restricted(int value) {
                    Contract.Requires(value > 0);
                }

                [DoesNotThrow]
                public static void Proven(int value) {
                    Contract.Requires(value > 0);
                    Restricted(value);
                }

                [DoesNotThrow]
                public static void Unknown(int value) =>
                    Restricted(value);

                private static int Divide(
                    int denominator,
                    int ignored) {
                    Contract.Requires(denominator > 0);
                    return 1 / denominator;
                }

                [DoesNotThrow]
                public static int MutatingArgument() {
                    var value = 0;
                    return Divide(value, value = 1);
                }

                private static void RequireZero(
                    int value) {
                    Contract.Requires(value == 0);
                    if (value != 0) {
                        throw new System.InvalidOperationException();
                    }
                }

                [DoesNotThrow]
                public static void SelfMutatingArgument() {
                    var value = 1;
                    RequireZero(value + (value = 0));
                }

                private static void RequireNull(
                    params string?[] values) {
                    Contract.Requires(values == null);
                    if (values != null) {
                        throw new System.InvalidOperationException();
                    }
                }

                [DoesNotThrow]
                public static void ExpandedParamsArgument() =>
                    RequireNull((string?)null);

                private static void FreeParams(
                    params string?[] values) {
                }

                [ZeroAllocations]
                public static void ExpandedParamsAllocation() =>
                    FreeParams((string?)null);
            }
            """));
        var evidence = discovery.Targets.Values
            .Where(static target =>
                !target.EffectClaims.IsDefaultOrEmpty)
            .ToDictionary(
                static target => target.Method.Name,
                static target =>
                    target.EffectClaims.Single().Evidence,
                StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evidence["Proven"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                evidence["Proven"].Reason,
                Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                evidence["Unknown"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence["Unknown"].Reason,
                Is.EqualTo(
                    WorkerClaimReason
                        .EffectSummaryIncomplete));
            Assert.That(
                evidence["Unknown"].Certainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty
                        .IncompleteMayEffectSummary));
            Assert.That(
                evidence["Unknown"].Evidence,
                Does.Contain(
                    "CallPreconditionNotProven"));
            Assert.That(
                evidence["MutatingArgument"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence["MutatingArgument"].Reason,
                Is.EqualTo(
                    WorkerClaimReason
                        .EffectSummaryIncomplete));
            Assert.That(
                evidence["MutatingArgument"].Evidence,
                Does.Contain(
                    "CallPreconditionNotProven"));
            Assert.That(
                evidence["SelfMutatingArgument"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence["SelfMutatingArgument"].Reason,
                Is.EqualTo(
                    WorkerClaimReason
                        .EffectSummaryIncomplete));
            Assert.That(
                evidence["SelfMutatingArgument"].Evidence,
                Does.Contain(
                    "CallPreconditionNotProven"));
            Assert.That(
                evidence["ExpandedParamsArgument"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence["ExpandedParamsArgument"].Reason,
                Is.EqualTo(
                    WorkerClaimReason
                        .EffectSummaryIncomplete));
            Assert.That(
                evidence["ExpandedParamsAllocation"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence["ExpandedParamsAllocation"].Reason,
                Is.EqualTo(
                    WorkerClaimReason
                        .EffectSummaryIncomplete));
        }
    }

    [Test]
    public void TypeInitializationSuppressesOnlyTheDefiniteAllocationWitness()
    {
        var discovery = Build((
            "Subject.cs",
            """
            using System;
            using SharpProof.Attributes;

            public sealed class PlainAllocation {
                public PlainAllocation() {
                }
            }

            public sealed class ThrowingInitialization {
                static ThrowingInitialization() {
                    throw new InvalidOperationException();
                }

                public ThrowingInitialization() {
                }
            }

            public static class Subject {
                [ZeroAllocations]
                public static object FrameworkObject() =>
                    new object();

                [ZeroAllocations]
                public static PlainAllocation PlainSourceType() =>
                    new PlainAllocation();

                [ZeroAllocations]
                public static ThrowingInitialization BlockedByTypeInitializer() =>
                    new ThrowingInitialization();
            }
            """));
        var evidence = discovery.Targets.Values
            .Where(static target => !target.EffectClaims.IsDefaultOrEmpty)
            .ToDictionary(
                static target => target.Method.Name,
                static target => target.EffectClaims.Single().Evidence,
                StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evidence["FrameworkObject"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                evidence["FrameworkObject"].Certainty,
                Is.EqualTo(WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(
                evidence["FrameworkObject"].Witness?.Kind,
                Is.EqualTo("managed-allocation"));
            Assert.That(
                evidence["PlainSourceType"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                evidence["PlainSourceType"].Certainty,
                Is.EqualTo(WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(
                evidence["PlainSourceType"].Witness?.Kind,
                Is.EqualTo("managed-allocation"));
            Assert.That(
                evidence["BlockedByTypeInitializer"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence["BlockedByTypeInitializer"].Reason,
                Is.EqualTo(WorkerClaimReason.EffectSummaryIncomplete));
            Assert.That(
                evidence["BlockedByTypeInitializer"].Certainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary));
            Assert.That(
                evidence["BlockedByTypeInitializer"].Witness,
                Is.Null);
            Assert.That(
                evidence["BlockedByTypeInitializer"].Evidence,
                Does.Contain("actual.allocation=Unknown")
                    .And.Contain("UnmodeledCall"));
        }
    }

    [Test]
    public void AllocationViolationsCarrySealedUnconditionalReplayEvidence()
    {
        const string source =
            """
            using SharpProof.Attributes;

            public sealed class Box {
            }

            public class InitializedBase {
                static InitializedBase() {
                    throw new System.InvalidOperationException();
                }
            }

            public sealed class DerivedBox : InitializedBase {
            }

            public static class InitializedSubject {
                static InitializedSubject() {
                    throw new System.InvalidOperationException();
                }

                [ZeroAllocations]
                public static object CallerInitializationCanPreemptAllocation() =>
                    new object();
            }

            public sealed class ConstructorSubject {
                private object _value = null!;

                [ZeroAllocations]
                public ConstructorSubject() =>
                    _value = new object();
            }

            public static class Subject {
                [ZeroAllocations]
                public static object ObjectAllocation() =>
                    new object();

                [ZeroAllocations]
                public static object[] ArrayAllocation() =>
                    new object[1];

                [EffectContract(
                    SharpProofEffect.None,
                    Complete = true)]
                public static Box EffectAllocation() =>
                    new Box();

                [ZeroAllocations]
                public static DerivedBox BaseInitializationCanPreemptAllocation() =>
                    new DerivedBox();
            }
            """;
        var compilation = GetCompilation(("Allocations.cs", source));
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var evidence = discovery.Targets.Values.ToDictionary(
            static target => target.Method.Name,
            static target => target.EffectClaims.Single().Evidence,
            StringComparer.Ordinal);
        var treeSha256 = WorkerProtocolJson.ComputeSha256(
            Encoding.UTF8.GetBytes(source));
        var capturedTree = CompilerCompilationCapture.CaptureTree(
            compilation.SyntaxTrees[0],
            CancellationToken.None);

        AssertAllocation(
            evidence["ObjectAllocation"],
            CompilerEffectReplayEventKind.ManagedObjectAllocation,
            "ObjectAllocation",
            "new object()",
            expectMember: true);
        AssertAllocation(
            evidence["ArrayAllocation"],
            CompilerEffectReplayEventKind.ManagedArrayAllocation,
            "ArrayAllocation",
            "new object[1]",
            expectMember: false);
        AssertAllocation(
            evidence["EffectAllocation"],
            CompilerEffectReplayEventKind.ManagedObjectAllocation,
            "EffectAllocation",
            "new Box()",
            expectMember: true);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evidence["BaseInitializationCanPreemptAllocation"]
                    .Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence["BaseInitializationCanPreemptAllocation"]
                    .Witness,
                Is.Null);
            Assert.That(
                evidence["BaseInitializationCanPreemptAllocation"]
                    .Replay,
                Is.Null);
            Assert.That(
                evidence["CallerInitializationCanPreemptAllocation"]
                    .Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence["CallerInitializationCanPreemptAllocation"]
                    .Replay,
                Is.Null);
            Assert.That(
                evidence[".ctor"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(evidence[".ctor"].Replay, Is.Null);
        }
        return;

        void AssertAllocation(
            CompilerEffectClaimArtifact value,
            CompilerEffectReplayEventKind expectedKind,
            string methodName,
            string expression,
            bool expectMember)
        {
            var replay = value.Replay;
            var @event = replay?.Events.Single();
            var start = source.IndexOf(
                expression,
                source.IndexOf(methodName, StringComparison.Ordinal),
                StringComparison.Ordinal);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    value.Outcome,
                    Is.EqualTo(WorkerClaimOutcome.Refuted));
                Assert.That(
                    value.Reason,
                    Is.EqualTo(WorkerClaimReason.None));
                Assert.That(
                    value.Certainty,
                    Is.EqualTo(
                        WorkerEffectEvidenceCertainty
                            .DefiniteViolation));
                Assert.That(value.Witness, Is.Not.Null);
                Assert.That(value.Replay, Is.Not.Null);
                Assert.That(
                    value.EvidenceSha256,
                    Does.Match("^[0-9a-f]{64}$"));
                Assert.That(
                    replay?.ConstraintSha256,
                    Is.EqualTo(
                        CompilerEffectClaimArtifactCodec
                            .ComputeConstraintSha256(
                                value.ContractKind,
                                value.Constraint)));
                Assert.That(
                    replay?.PathKind,
                    Is.EqualTo(
                        CompilerEffectReplayPathKind
                            .Unconditional));
                Assert.That(replay?.Events, Has.Length.EqualTo(1));
                Assert.That(@event?.Ordinal, Is.Zero);
                Assert.That(@event?.Kind, Is.EqualTo(expectedKind));
                Assert.That(@event?.SyntaxTreeOrdinal, Is.Zero);
                Assert.That(
                    @event?.SyntaxTreeSha256,
                    Is.EqualTo(treeSha256));
                Assert.That(
                    @event?.SyntaxTreeLineMapSha256,
                    Is.EqualTo(capturedTree.LineMapSha256));
                Assert.That(
                    @event?.SourceTreeOrdinal,
                    Is.Zero);
                Assert.That(
                    @event?.SourceTreePath,
                    Is.EqualTo(capturedTree.Path));
                Assert.That(
                    @event?.SourceTreeSha256,
                    Is.EqualTo(capturedTree.Sha256));
                Assert.That(
                    @event?.SourceLineMapSha256,
                    Is.EqualTo(capturedTree.LineMapSha256));
                Assert.That(@event?.SyntaxStart, Is.EqualTo(start));
                Assert.That(
                    @event?.SyntaxLength,
                    Is.EqualTo(expression.Length));
                Assert.That(
                    @event?.OperationIdentitySha256,
                    Is.EqualTo(
                        CompilerEffectClaimArtifactCodec
                            .ComputeReplayOperationSha256(
                                @event!)));
                Assert.That(@event?.TypeIdentity, Is.Not.Empty);
                Assert.That(
                    @event?.TypeDocumentationId,
                    Is.Not.Null.And.Not.Empty);
                Assert.That(@event?.SpecWitnessIdentifier, Is.Null);
                Assert.That(@event?.ScalarOperands, Is.Empty);
                Assert.That(
                    @event?.ExactExceptionTypeHierarchy,
                    Is.Empty);
                Assert.That(
                    @event?.Location.Path,
                    Is.EqualTo("Allocations.cs"));
                Assert.That(@event?.Location.Start, Is.EqualTo(start));
                Assert.That(
                    @event?.Location.Length,
                    Is.EqualTo(expression.Length));
                Assert.That(
                    value.Witness?.Location.Start,
                    Is.EqualTo(@event?.Location.Start));
                Assert.That(
                    value.Witness?.Location.Length,
                    Is.EqualTo(@event?.Location.Length));
            }

            if (expectMember)
            {
                Assert.That(@event!.MemberIdentity, Is.Not.Empty);
                Assert.That(
                    @event.MemberDocumentationId,
                    Is.Not.Null.And.Not.Empty);
                Assert.That(
                    value.Witness!.Detail,
                    Is.EqualTo(@event.MemberDocumentationId));
            }
            else
            {
                Assert.That(@event!.MemberIdentity, Is.Empty);
                Assert.That(@event.MemberDocumentationId, Is.Null);
                Assert.That(
                    value.Witness!.Detail,
                    Is.EqualTo(@event.TypeDocumentationId));
            }

            Assert.DoesNotThrow(
                (Action)(() =>
                    CompilerEffectClaimArtifactCodec.Validate(
                        value)));
        }
    }

    [Test]
    public void MalformedBaseTypesCannotProduceAllocationReplayEvidence()
    {
        const string source =
            """
            using SharpProof.Attributes;

            public sealed class Subject : MissingBase {
            }

            public static class Factory {
                [ZeroAllocations]
                public static Subject Allocate() =>
                    new Subject();
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(
                LanguageVersion.CSharp12,
                preprocessorSymbols: [
                    Contract.ConditionalSymbol
                ]),
            "Subject.cs");
        var compilation = CSharpCompilation.Create(
            "MalformedBaseTypeTests",
            [tree],
            GetReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions:
                    NullableContextOptions.Enable));
        Assert.That(
            compilation.GetDiagnostics().Any(static diagnostic =>
                diagnostic.Severity ==
                DiagnosticSeverity.Error),
            Is.True);

        var discovery = new ClaimManifestBuilder(
            compilation).Build();
        var evidence = discovery.Targets.Values.Single(
            static target =>
                target.Method.Name == "Allocate")
            .EffectClaims.Single().Evidence;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evidence.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(evidence.Witness, Is.Null);
            Assert.That(evidence.Replay, Is.Null);
        }
    }

    [Test]
    public void DirectLockReceiverCompletionControlsEffectEvidence()
    {
        var discovery = Build((
            "Subject.cs",
            """
            using System;
            using SharpProof.Attributes;

            public sealed class ThrowingGate {
                public ThrowingGate() {
                    throw new InvalidOperationException();
                }
            }

            public static class Subject {
                [AllowedCapabilities(SharpProofCapability.None)]
                public static void SafeObject() {
                    lock ((object)new object()) {
                    }
                }

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void SafeArray() {
                    lock (new object[1]) {
                    }
                }

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void ThrowingConstructor() {
                    lock (new ThrowingGate()) {
                    }
                }

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void WrappedThrowingConstructor() {
                    lock ((object)(new ThrowingGate())) {
                    }
                }

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void DynamicArrayLength(int length) {
                    lock (new object[length]) {
                    }
                }
            }
            """));
        var evidence = discovery.Targets.Values
            .Where(static target => !target.EffectClaims.IsDefaultOrEmpty)
            .ToDictionary(
                static target => target.Method.Name,
                static target => target.EffectClaims.Single().Evidence,
                StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            AssertUnsupportedDirectCandidate(evidence["SafeObject"]);
            AssertUnsupportedDirectCandidate(evidence["SafeArray"]);
            Assert.That(
                evidence["ThrowingConstructor"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                evidence["WrappedThrowingConstructor"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            AssertUnknownWithoutWitness(evidence["DynamicArrayLength"]);
        }
        return;

        static void AssertUnsupportedDirectCandidate(
            CompilerEffectClaimArtifact value)
        {
            Assert.That(value.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                value.Reason,
                Is.EqualTo(
                    WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(
                value.Certainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(value.Witness, Is.Null);
            Assert.That(value.Replay, Is.Null);
        }

        static void AssertUnknownWithoutWitness(
            CompilerEffectClaimArtifact value)
        {
            Assert.That(value.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(value.Witness, Is.Null);
        }
    }

    [Test]
    public void ExceptionConstructorEvidenceRequiresAnExactApprovedSpec()
    {
        var discovery = Build((
            "Subject.cs",
            """
            using System;
            using System.Collections.Generic;
            using SharpProof.Attributes;

            public static class Subject {
                [DoesNotThrow]
                public static InvalidOperationException SafeConstruction() =>
                    new InvalidOperationException("message");

                [DoesNotThrow]
                public static AggregateException UnmodeledConstruction() =>
                    new AggregateException(
                        (IEnumerable<Exception>)null!);

                [AllowedExceptions(typeof(ArgumentException))]
                public static void DefiniteWrongThrow() =>
                    throw new InvalidOperationException();

                [AllowedExceptions(typeof(ArgumentException))]
                public static void UnmodeledThrow() =>
                    throw new AggregateException(
                        (IEnumerable<Exception>)null!);
            }
            """));
        var evidence = discovery.Targets.Values.ToDictionary(
            static target => target.Method.Name,
            static target => target.EffectClaims.Single().Evidence,
            StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evidence["SafeConstruction"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                evidence["UnmodeledConstruction"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence["UnmodeledConstruction"].Reason,
                Is.EqualTo(WorkerClaimReason.EffectSummaryIncomplete));
            Assert.That(
                evidence["UnmodeledConstruction"].Evidence,
                Does.Contain("UnmodeledCall"));
            Assert.That(
                evidence["UnmodeledConstruction"].Witness,
                Is.Null);
            Assert.That(
                evidence["DefiniteWrongThrow"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence["DefiniteWrongThrow"].Reason,
                Is.EqualTo(
                    WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(
                evidence["DefiniteWrongThrow"].Certainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(
                evidence["DefiniteWrongThrow"].Witness,
                Is.Null);
            Assert.That(
                evidence["DefiniteWrongThrow"].Replay,
                Is.Null);
            Assert.That(
                evidence["UnmodeledThrow"].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(evidence["UnmodeledThrow"].Witness, Is.Null);
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
            Assert.That(
                wrongAllowed.Evidence,
                Does.Contain(integerIdentity).And.Contain(stringIdentity));
            Assert.That(
                wrongAllowed.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                wrongAllowed.Reason,
                Is.EqualTo(WorkerClaimReason.EffectContractNotEstablished));
            Assert.That(
                exactAllowed.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                derivedAllowed.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                wrongCatch.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                wrongCatch.Reason,
                Is.EqualTo(WorkerClaimReason.EffectContractNotEstablished));
            Assert.That(
                exactCatch.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
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
            Assert.That(claims, Has.Length.EqualTo(4));
            Assert.That(
                claims.Select(static claim => claim.EffectContractKind),
                Is.EqualTo([
                    WorkerEffectContractKind.AllowedExceptions,
                    WorkerEffectContractKind.AllowedExceptions,
                    WorkerEffectContractKind.EffectContract,
                    WorkerEffectContractKind.EffectContract
                ]));
            Assert.That(
                claims.Select(static claim => claim.Ordinal),
                Is.EqualTo([0, 1, 2, 3]));
            Assert.That(
                claims.Select(static claim => claim.ClaimId).Distinct().ToArray(),
                Has.Length.EqualTo(4));
            Assert.That(
                second.Manifest.Claims.Select(static claim =>
                    claim.ClaimId),
                Is.EqualTo(claims.Select(static claim =>
                    claim.ClaimId)));
            Assert.That(
                first.Targets.Values.Single().EffectClaims.Select(
                    static claim => claim.Evidence.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Proven));
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

    private static ClaimManifestBuildResult Build(
        params (string FileName, string Source)[] sources)
    {
        return new ClaimManifestBuilder(GetCompilation(sources)).Build();
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
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.CSharp12,
            preprocessorSymbols: [Contract.ConditionalSymbol]);
        var compilation = CSharpCompilation.Create(
            "ManifestTests",
            sources.Select(source => CSharpSyntaxTree.ParseText(
                source.Source,
                parseOptions,
                source.FileName)),
            GetReferences(),
            new CSharpCompilationOptions(
                outputKind,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic =>
                    diagnostic.ToString())));
        return compilation;
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var paths = ((string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Contract).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return [.. paths.Select(static path =>
            MetadataReference.CreateFromFile(path))];
    }
}
