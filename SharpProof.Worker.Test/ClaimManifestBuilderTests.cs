using System.Collections.Immutable;
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

        Assert.That(
            ClaimManifestBuilder.ToWorkerEffects(effects.Aggregate(
                static (left, right) => left | right)),
            Is.EqualTo(WorkerEffectSet.AllKnown));
        Assert.That(
            ClaimManifestBuilder.ToWorkerCapabilities(capabilities.Aggregate(
                static (left, right) => left | right)),
            Is.EqualTo(WorkerEffectCapabilitySet.AllKnown));
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
