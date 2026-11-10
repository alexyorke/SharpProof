using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Contracts.Test;

[TestFixture]
internal sealed class ContractBinderTests
{

    [TestCase(false)]
    [TestCase(true)]
    public void LongTupleCompanionPreservesLogicalElementCount(bool extraElement)
    {
        const string targetTuple = "(int, int, int, int, int, int, int, int)";
        var companionTuple = extraElement
            ? "(int, int, int, int, int, int, int, int, int)"
            : targetTuple;
        var subject = ContractSubject.Create(
            $$"""
            using SharpProof.Attributes;
            public interface Target {
                int Read({{targetTuple}} value, bool condition);
            }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static int Read(Target receiver, {{companionTuple}} value, bool condition) {
                    Contract.Requires(condition);
                    return 0;
                }
            }
            """);
        var result = subject.Bind("Target", "Read");

        Assert.That(result.Failure, Is.EqualTo(extraElement
            ? ContractBindingFailure.CompanionSignatureMismatch
            : ContractBindingFailure.None));
        Assert.That(result.IsSuccess, Is.EqualTo(!extraElement));
        if (!extraElement)
        {
            Assert.That(result.Contracts!.UsesCompanion, Is.True);
            Assert.That(result.Contracts.Clauses, Has.Length.EqualTo(1));
        }
        else
        {
            Assert.That(result.Contracts, Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CompanionContainerMustBeStaticEvenWhenItsMethodIsStatic(bool staticContainer)
    {
        var subject = ContractSubject.Create(
            $$"""
            using SharpProof.Attributes;
            public interface Target { int Read(int value); }
            [ContractFor(typeof(Target))]
            public {{(staticContainer ? "static " : "")}}class TargetContracts {
                public static int Read(Target receiver, int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """);
        var result = subject.Bind("Target", "Read");

        Assert.That(result.Failure, Is.EqualTo(staticContainer
            ? ContractBindingFailure.None
            : ContractBindingFailure.CompanionSignatureMismatch));
        Assert.That(result.IsSuccess, Is.EqualTo(staticContainer));
        if (staticContainer)
        {
            Assert.That(result.Contracts!.UsesCompanion, Is.True);
            Assert.That(result.Contracts.Clauses, Has.Length.EqualTo(1));
        }
        else
        {
            Assert.That(result.Contracts, Is.Null);
        }
    }

    [TestCase("int*")]
    [TestCase("delegate*<void>")]
    public void NotNullDoesNotTurnPointerSignaturesIntoReferenceContracts(string type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var subject = ContractSubject.Create(
            $$"""
            using SharpProof.Attributes;
            public static unsafe class Target {
                public static void Annotated([NotNull] {{type}} value) {}
                public static void Unannotated({{type}} value) {}
            }
            """,
            allowUnsafe: true);

        var rejected = subject.Bind("Target", "Annotated");
        var control = subject.Bind("Target", "Unannotated");

        Assert.That(rejected.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
        Assert.That(control.IsSuccess, Is.True, control.Failure.ToString());
        Assert.That(control.Contracts!.Clauses, Is.Empty);
    }

    [TestCase("-0.0", "0.0", false)]
    [TestCase("0.0", "-0.0", false)]
    [TestCase("-0.0", "-0.0", true)]
    public void DoubleDefaultBitsControlCompanionResolution(
        string targetDefault,
        string companionDefault,
        bool expectedSuccess)
    {
        System.ArgumentNullException.ThrowIfNull(companionDefault);
        System.ArgumentNullException.ThrowIfNull(targetDefault);
        var subject = ContractSubject.Create(
            $$"""
            using SharpProof.Attributes;
            public interface Target {
                void Read(double value = {{targetDefault}});
            }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static void Read(
                    Target receiver,
                    double value = {{companionDefault}}) {
                }
            }
            """);

        var result = subject.Bind("Target", "Read");

        Assert.That(result.IsSuccess, Is.EqualTo(expectedSuccess));
        Assert.That(
            result.Failure,
            Is.EqualTo(expectedSuccess
                ? ContractBindingFailure.None
                : ContractBindingFailure.CompanionSignatureMismatch));
    }

    [Test]
    public void FunctionPointerConventionOrderBindsExactCompanion()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public unsafe interface Target {
                delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int>
                    Map(delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int> value);
            }
            [ContractFor(typeof(Target))]
            public static unsafe class TargetContracts {
                public static delegate* unmanaged[SuppressGCTransition, Cdecl]<int, int>
                    Map(
                        Target receiver,
                        delegate* unmanaged[SuppressGCTransition, Cdecl]<int, int> value) {
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source, allowUnsafe: true);

        var result = subject.Bind("Target", "Map");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.True);
        Assert.That(result.Contracts.Clauses, Is.Empty);
    }

    [Test]
    public void RefReadonlyParameterBindsExactCompanionWithoutRefKindCollapse()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public interface Target {
                void Read(ref readonly int value);
            }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static void Read(
                    Target receiver,
                    ref readonly int value) {
                    Contract.Requires(value >= 0);
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.Bind("Target", "Read");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.True);
        Assert.That(result.Contracts.Clauses, Has.Length.EqualTo(1));
    }

    [Test]
    public void DirectCompilerBoundContractWinsAndSymbolIdentityRejectsShadows()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Shadow {
                public static class Contract {
                    public static void Requires(bool value) { }
                }
            }
            public static class Target {
                public static T @Select<T>(T value, bool ok) {
                    Contract.Requires(ok);
                    Shadow.Contract.Requires(false);
                    void Nested() { Shadow.Contract.Requires(false); }
                    Nested();
                    return value;
                }
            }
            [ContractFor(typeof(Target))]
            public static class BrokenCompanion {
                public static int Select(int wrong) => wrong;
            }
            """;
        var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Select");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.False);
        Assert.That(result.Contracts.Source.Arity, Is.EqualTo(1));
        Assert.That(result.Contracts.Clauses.Length, Is.EqualTo(1));
        Assert.That(
            result.Contracts.Clauses[0].Kind,
            Is.EqualTo(BoundContractKind.Requires));
    }

    [Test]
    public void DirectClauseSourceAlsoWinsForRequiresOnlyBinding()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Read(long value) {
                    Contract.Ensures(
                        Contract.Result<long>() == value);
                    return value;
                }
            }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static long Read(long value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.BindRequires("Target", "Read");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.False);
        Assert.That(result.Contracts.Clauses, Is.Empty);
    }

    [Test]
    public void RequiresBindingIgnoresUnrelatedClausePlacementFailures()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static void InvalidEnsures(int value) {
                    Contract.Requires(value > 0);
                    if (value > 0) Contract.Ensures(true);
                }

                public static void InvalidAssume(int value) {
                    Contract.Requires(value > 0);
                    value++;
                    Contract.Assume(true);
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        using (Assert.EnterMultipleScope())
        {
            foreach (var methodName in ImmutableArray.Create("InvalidEnsures",
                         "InvalidAssume"
))
            {
                var full = subject.Bind("Target", methodName);
                var requires = subject.BindRequires("Target", methodName);

                Assert.That(
                    full.Failure,
                    Is.EqualTo(
                        ContractBindingFailure.InvalidClausePlacement),
                    methodName);
                Assert.That(
                    requires.IsSuccess,
                    Is.True,
                    $"{methodName}: {requires.Failure}");
                if (requires.IsSuccess)
                {
                    Assert.That(
                        requires.Contracts!.Clauses.Select(
                            static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)),
                        Is.EqualTo([BoundContractKind.Requires]),
                        methodName);
                }
            }
        }
    }

    [Test]
    public void InvalidTargetPlacementCannotBeHiddenByAValidCompanion()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Read(long value) {
                    if (value > 0) {
                        Contract.Ensures(
                            Contract.Result<long>() == value);
                    }
                    return value;
                }
            }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static long Read(long value) {
                    Contract.Ensures(
                        Contract.Result<long>() == value);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.Bind("Target", "Read");

        Assert.That(
            result.Failure,
            Is.EqualTo(ContractBindingFailure.InvalidClausePlacement));
    }

    [Test]
    public void InvalidDirectIntrinsicCannotBeHiddenByACompanion()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public sealed class Target {
                public long Read(long value) {
                    _ = Contract.Result<long>();
                    return value;
                }
            }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static long Read(Target receiver, long value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.ResultOutsideEnsures));
    }

    [TestCase(
        "_ = Contract.Result<long>();",
        ContractBindingFailure.ResultOutsideEnsures)]
    [TestCase(
        "_ = Contract.Old(value);",
        ContractBindingFailure.OldOutsideEnsures)]
    public void StandaloneIntrinsicsFailClosed(
        string statement,
        ContractBindingFailure expected)
    {
        System.ArgumentNullException.ThrowIfNull(statement);
        var source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Read(long value) {
                    STATEMENT
                    return value;
                }
            }
            """.Replace("STATEMENT", statement, StringComparison.Ordinal);
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Target", "Read").Failure,
            Is.EqualTo(expected));
    }

    [Test]
    public void TypeCompanionDoesNotHijackConstructorClosedContracts()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public sealed class Target {
                public Target([Positive] long value) {
                }
                public long Read(long value) => value;
            }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static long Read(Target receiver, long value) => value;
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.BindConstructor("Target");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.False);
        Assert.That(result.Contracts.Clauses, Has.Length.EqualTo(1));
        Assert.That(
            result.Contracts.Clauses[0].Evidence,
            Is.EqualTo(BoundContractEvidence.ClosedAttribute));
    }

    [Test]
    public void StaticConstructorDirectContractsBind()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public sealed class Target {
                static Target() {
                    Contract.Ensures(true);
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.BindMethodKind(
            "Target",
            MethodKind.StaticConstructor);

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.Clauses, Has.Length.EqualTo(1));
        Assert.That(
            result.Contracts.Clauses[0].Kind,
            Is.EqualTo(BoundContractKind.Ensures));
    }

    [Test]
    public void ExplicitInterfaceImplementationDirectContractsBind()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public interface ITarget {
                int Read(int value);
            }
            public sealed class Target : ITarget {
                int ITarget.Read(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.BindMethodKind(
            "Target",
            MethodKind.ExplicitInterfaceImplementation);

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.Clauses, Has.Length.EqualTo(1));
        Assert.That(
            result.Contracts.Clauses[0].Kind,
            Is.EqualTo(BoundContractKind.Requires));
    }

    [Test]
    public void NestedCallableClausesDoNotPoisonContainingContracts()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Read(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    long Local(long candidate) {
                        Contract.Requires(candidate > 0);
                        return candidate;
                    }
                    _ = Local(value);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.Bind("Target", "Read");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.Clauses, Has.Length.EqualTo(1));
        Assert.That(
            result.Contracts.Clauses[0].Kind,
            Is.EqualTo(BoundContractKind.Ensures));
    }

    [Test]
    public void ResultWithUnsupportedNullableValueDomainBindsInTotal()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static int? Read() {
                    Contract.Ensures(
                        Contract.Result<int?>() == null);
                    return null;
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.BindTotal("Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.None));
    }

    [TestCase("int", "long")]
    [TestCase("uint", "int")]
    [TestCase("string?", "object?")]
    [TestCase("string?", "string")]
    public void ResultTypeMustExactlyMatchTheCallableReturnType(
        string returnType,
        string resultType)
    {
        System.ArgumentNullException.ThrowIfNull(resultType);
        System.ArgumentNullException.ThrowIfNull(returnType);
        var source =
            """
            #nullable enable
            using SharpProof.Attributes;
            public static class Target {
                public static RETURN Invalid(RETURN value) {
                    Contract.Ensures(
                        Contract.Result<RESULT>() ==
                        Contract.Result<RESULT>());
                    return value;
                }
            }
            """
            .Replace("RETURN", returnType, StringComparison.Ordinal)
            .Replace("RESULT", resultType, StringComparison.Ordinal);
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Target", "Invalid").Failure,
            Is.EqualTo(ContractBindingFailure.InvalidIntrinsicSignature));
    }

    [TestCase("float")]
    [TestCase("double")]
    public void EqualityWithAnUnmodeledValueDomainFailsClosed(
        string typeName)
    {
        System.ArgumentNullException.ThrowIfNull(typeName);
        var source =
            """
            using SharpProof.Attributes;
            public enum Choice { First, Second }
            public static class Target {
                public static TYPE Echo(TYPE value) {
                    Contract.Ensures(
                        Contract.Result<TYPE>() == value);
                    return value;
                }
            }
            """.Replace("TYPE", typeName, StringComparison.Ordinal);
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.BindTotal("Target", "Echo").Failure,
            Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
    }

    [TestCase("ulong")]
    [TestCase("nint")]
    [TestCase("nuint")]
    [TestCase("decimal")]
    [TestCase("Choice")]
    public void EqualityOverATotalModeledValueDomainBinds(
        string typeName)
    {
        System.ArgumentNullException.ThrowIfNull(typeName);
        var source =
            """
            using SharpProof.Attributes;
            public enum Choice { First, Second }
            public static class Target {
                public static TYPE Echo(TYPE value) {
                    Contract.Ensures(
                        Contract.Result<TYPE>() == value);
                    return value;
                }
            }
            """.Replace("TYPE", typeName, StringComparison.Ordinal);
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.BindTotal("Target", "Echo").Failure,
            Is.EqualTo(ContractBindingFailure.None));
    }

    [Test]
    public void EqualityRetainsTheAdmittedUnsignedIntegerDomain()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static uint Echo(uint value) {
                    Contract.Ensures(
                        Contract.Result<uint>() == value);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Target", "Echo").IsSuccess,
            Is.True);
    }

    [TestCase(
        "Contract.Result<long>() > 0L",
        ContractBindingFailure.ResultOutsideEnsures)]
    [TestCase(
        "Contract.Old(value) > 0L",
        ContractBindingFailure.OldOutsideEnsures)]
    public void ResultAndOldOutsideEnsuresFailClosed(
        string expression,
        ContractBindingFailure expected)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        var source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Invalid(long value) {
                    Contract.Requires(
            """ +
            expression +
            """
                    );
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Invalid");
        Assert.That(result.Failure, Is.EqualTo(expected));
    }

    [Test]
    public void NestedOldFailsClosed()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Invalid(long value) {
                    Contract.Ensures(Contract.Old(Contract.Old(value)) > 0L);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);
        Assert.That(
            subject.Bind("Target", "Invalid").Failure,
            Is.EqualTo(ContractBindingFailure.NestedOld));
    }

    [Test]
    public void AssumeIsExplicitEvidenceAndNotARequirement()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Read(long value, bool established) {
                    Contract.Assume(established);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Read");
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        var clause = result.Contracts!.Clauses.Single();
        Assert.That(clause.Kind, Is.EqualTo(BoundContractKind.Assume));
    }

    [Test]
    public void ConditionalAndUnaryPostconditionsBindExactly()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Read(long value, bool choose) {
                    Contract.Ensures(
                        choose
                            ? checked(-Contract.Result<long>()) <= 0
                            : !choose);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.Bind("Target", "Read");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.Clauses, Has.Length.EqualTo(1));
    }

    [Test]
    public void NarrowingConversionInAPostconditionBindsInTotal()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Read(long value) {
                    Contract.Ensures(
                        Contract.Result<long>() == (long)(int)value);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.BindTotal("Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.None));
    }

    [Test]
    public void PlacementFailuresAreTypedAndBindingsAreCompilationCached()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Valid(long value) {
                    Contract.Requires(value > 0);
                    return value;
                }
                public static long Conditional(long value) {
                    if (value > 0) Contract.Requires(true);
                    return value;
                }
                public static long Late(long value) {
                    value++;
                    Contract.Ensures(true);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        var first = subject.Bind("Target", "Valid");
        Assert.That(subject.Bind("Target", "Valid"), Is.SameAs(first));
        Assert.That(
            subject.Bind("Target", "Conditional").Failure,
            Is.EqualTo(ContractBindingFailure.InvalidClausePlacement));
        Assert.That(
            subject.Bind("Target", "Late").Failure,
            Is.EqualTo(ContractBindingFailure.InvalidClausePlacement));
    }

    [Test]
    public void ClosedAttributesProduceTypedConditions()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                [return: NotNull]
                public static string Read(
                    [NotNull] string text,
                    [Positive, InRange(1L, 10L)] long count) => text;
            }
            """;
        var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Read");
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.Clauses.Length, Is.EqualTo(4));
        Assert.That(
            result.Contracts.Clauses.Count(static clause =>
                clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Requires)),
            Is.EqualTo(3));
        Assert.That(
            result.Contracts.Clauses.Count(static clause =>
                clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Ensures)),
            Is.EqualTo(1));
        Assert.That(
            result.Contracts.Clauses.All(static clause =>
                clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Evidence == BoundContractEvidence.ClosedAttribute)),
            Is.True);
    }

    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    public void PositiveClosedContractAcceptsEveryCatalogInteger(
        string typeName)
    {
        System.ArgumentNullException.ThrowIfNull(typeName);
        var source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static TYPE Read([Positive] TYPE value) => value;
            }
            """.Replace("TYPE", typeName, StringComparison.Ordinal);
        var subject = ContractSubject.Create(source);

        var result = subject.Bind("Target", "Read");
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
    }

    [TestCase("ulong")]
    [TestCase("nint")]
    [TestCase("nuint")]
    public void PositiveClosedContractStillRejectsUnmodeledIntegers(
        string typeName)
    {
        System.ArgumentNullException.ThrowIfNull(typeName);
        var source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static TYPE Read([Positive] TYPE value) => value;
            }
            """.Replace("TYPE", typeName, StringComparison.Ordinal);
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
    }

    [TestCase("Value")]
    [TestCase("Choice")]
    [TestCase("int")]
    [TestCase("System.DateTime")]
    [TestCase("System.IntPtr")]
    public void NotNullRejectsNonReferenceDomains(string typeName)
    {
        System.ArgumentNullException.ThrowIfNull(typeName);
        var source =
            """
            using SharpProof.Attributes;
            public struct Value {
            }
            public enum Choice {
                First
            }
            public static class Target {
                public static void Read([NotNull] TYPE value) {
                }
            }
            """.Replace("TYPE", typeName, StringComparison.Ordinal);
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
    }

    [TestCase("[NotNull]", "string")]
    [TestCase("[Positive]", "long")]
    [TestCase("[InRange(1, 10)]", "long")]
    public void ClosedPreconditionRejectsOutParameters(
        string attribute,
        string type)
    {
        System.ArgumentNullException.ThrowIfNull(attribute);
        System.ArgumentNullException.ThrowIfNull(type);
        var source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static void Read(ATTRIBUTE out TYPE value) {
                    value = default!;
                }
            }
            """
            .Replace("ATTRIBUTE", attribute, StringComparison.Ordinal)
            .Replace("TYPE", type, StringComparison.Ordinal);
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
    }

    [Test]
    public void NotNullRejectsValueConstrainedTypeParameters()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static void Read<T>([NotNull] T value)
                    where T : struct {
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
    }

    [Test]
    public void NotNullAcceptsReferenceConstrainedTypeParameters()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static void Read<T>([NotNull] T value)
                    where T : class {
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.Bind("Target", "Read");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(
            result.Contracts!.Clauses.Single().Evidence,
            Is.EqualTo(BoundContractEvidence.ClosedAttribute));
    }

    [Test]
    public void InvalidClosedReturnContractFailsAtReturnBinding()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                [return: Positive]
                public static string Read() => string.Empty;
            }
            """;
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
    }

    [Test]
    public void VoidReturnClosedContractFailsAtReturnBinding()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                [return: NotNull]
                public static void Read() {
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        var full = subject.Bind("Target", "Read");
        var requires = subject.BindRequires("Target", "Read");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                full.Failure,
                Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
            Assert.That(requires.IsSuccess, Is.True, requires.Failure.ToString());
            Assert.That(requires.Contracts!.Clauses, Is.Empty);
        }
    }

    [Test]
    public void ExactGenericCompanionIsDiscoveredAndBound()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static T Select<T>(T value, bool ok) => value;
            }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static T Select<T>(T value, bool ok) {
                    Contract.Requires(ok);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);
        var result = subject.Bind("Target", "Select");
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.True);
        Assert.That(
            result.Contracts.Clauses.Single().Evidence,
            Is.EqualTo(BoundContractEvidence.Companion));
    }

    [Test]
    public void OpenGenericConstraintOrderIsSemanticallyMatched()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public interface IFirst {
            }
            public interface ISecond {
            }
            public interface IRepository<T>
                where T : IFirst, ISecond {
                T Select(T value, bool ok);
            }
            [ContractFor(typeof(IRepository<>))]
            public static class RepositoryContracts<T>
                where T : ISecond, IFirst {
                public static T Select(
                    IRepository<T> receiver,
                    T value,
                    bool ok) {
                    Contract.Requires(ok);
                    return value;
                }
            }
            """;
        var subject = ContractSubject.Create(source);
        var result = subject.Bind("IRepository`1", "Select");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.True);
        Assert.That(result.Contracts.Clauses, Has.Length.EqualTo(1));
    }

    [Test]
    public void ConstructedNestedGenericTargetSpecializesEveryCompanionLayer()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public sealed class Outer<TOuter> where TOuter : class {
                public interface ITarget<TInner> where TInner : struct {
                    void Read(TOuter outer, TInner inner);
                }
            }
            public static class CompanionOuter<TOuter> where TOuter : class {
                [ContractFor(typeof(Outer<>.ITarget<>))]
                public static class TargetContracts<TInner> where TInner : struct {
                    public static void Read(
                        Outer<TOuter>.ITarget<TInner> receiver,
                        TOuter outer,
                        TInner inner) {
                        Contract.Requires(outer != null);
                    }
                }
            }
            public static class Caller {
                public static void Call(
                    Outer<string>.ITarget<int> target,
                    string outer,
                    int inner) => target.Read(outer, inner);
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.BindCallRequires("Caller", "Call", "Read");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.True);
        Assert.That(result.Contracts.Clauses, Has.Length.EqualTo(1));
    }

    [Test]
    public void ConstructedNestedGenericTargetIgnoresNonGenericOwnerWrappers()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public sealed class Outer<TOuter> where TOuter : class {
                public sealed class Middle {
                    public interface ITarget<TInner> where TInner : struct {
                        void Read(TOuter outer, TInner inner);
                    }
                }
            }
            public static class CompanionOuter<TOuter> where TOuter : class {
                [ContractFor(typeof(Outer<>.Middle.ITarget<>))]
                public static class TargetContracts<TInner> where TInner : struct {
                    public static void Read(
                        Outer<TOuter>.Middle.ITarget<TInner> receiver,
                        TOuter outer,
                        TInner inner) {
                        Contract.Requires(outer != null);
                    }
                }
            }
            public static class Caller {
                public static void Call(
                    Outer<string>.Middle.ITarget<int> target,
                    string outer,
                    int inner) => target.Read(outer, inner);
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.BindCallRequires("Caller", "Call", "Read");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.True);
        Assert.That(result.Contracts.Clauses, Has.Length.EqualTo(1));
    }

    [Test]
    public void NonGenericTargetWrapperDoesNotRequireCompanionWrapper()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Outer {
                public interface ITarget<T> {
                    void Read(T value);
                }
            }
            [ContractFor(typeof(Outer.ITarget<>))]
            public static class TargetContracts<T> {
                public static void Read(
                    Outer.ITarget<T> receiver,
                    T value) {
                    Contract.Requires(receiver != null);
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        var result = subject.Bind("Outer+ITarget`1", "Read");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.True);
    }

    [TestCase(
        "public static class CompanionOuter<TOuter> where TOuter : struct { " +
        "[ContractFor(typeof(Outer<>.ITarget<>))] " +
        "public static class TargetContracts<TInner> { " +
        "public static void Read(Outer<TOuter>.ITarget<TInner> receiver, TOuter outer, TInner inner) { } } } ")]
    [TestCase(
        "[ContractFor(typeof(Outer<>.ITarget<>))] " +
        "public static class TargetContracts<TOuter, TInner> { " +
        "public static void Read(Outer<TOuter>.ITarget<TInner> receiver, TOuter outer, TInner inner) { } } ")]
    [TestCase(
        "public static class CompanionOuter<TOuter> { " +
        "[ContractFor(typeof(Outer<>.ITarget<>))] " +
        "public static class TargetContracts<TInner> { " +
        "public static void Read(Outer<TInner>.ITarget<TOuter> receiver, TOuter outer, TInner inner) { } } } ")]
    public void NestedGenericCompanionLayersMustMatchExactly(string companion)
    {
        System.ArgumentNullException.ThrowIfNull(companion);
        var source =
            """
            using SharpProof.Attributes;
            public sealed class Outer<TOuter> {
                public interface ITarget<TInner> {
                    void Read(TOuter outer, TInner inner);
                }
            }
            """ + companion;
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Outer`1+ITarget`1", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.CompanionSignatureMismatch));
    }

    [Test]
    public void ClosedNestedTargetRejectsOpenContainingCompanionLayer()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public sealed class Outer<TOuter> {
                public interface ITarget<TInner> {
                    void Read(TOuter outer, TInner inner);
                }
            }
            public static class CompanionOuter<TOuter> {
                [ContractFor(typeof(Outer<string>.ITarget<int>))]
                public static class TargetContracts {
                    public static void Read(
                        Outer<string>.ITarget<int> receiver,
                        string outer,
                        int inner) { }
                }
            }
            public static class Caller {
                public static void Call(
                    Outer<string>.ITarget<int> target,
                    string outer,
                    int inner) => target.Read(outer, inner);
            }
            """;
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.BindCallRequires("Caller", "Call", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.CompanionSignatureMismatch));
    }

    [TestCase("Left", ContractBindingFailure.None)]
    [TestCase("Other", ContractBindingFailure.CompanionSignatureMismatch)]
    public void TupleElementNamesAreMatchedExactly(
        string companionElementName,
        ContractBindingFailure expected)
    {
        System.ArgumentNullException.ThrowIfNull(companionElementName);
        var source =
            """
            #nullable enable
            using SharpProof.Attributes;
            public interface ITarget {
                (int Left, string? Right) Read(
                    (int Left, string? Right) value,
                    bool ok);
            }
            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static (int ELEMENT, string? Right) Read(
                    ITarget receiver,
                    (int ELEMENT, string? Right) value,
                    bool ok) {
                    Contract.Requires(ok);
                    return value;
                }
            }
            """.Replace(
                "ELEMENT",
                companionElementName,
                StringComparison.Ordinal);
        var subject = ContractSubject.Create(source);

        var result = subject.Bind("ITarget", "Read");

        Assert.That(result.Failure, Is.EqualTo(expected));
        Assert.That(
            result.IsSuccess,
            Is.EqualTo(expected == ContractBindingFailure.None));
    }

    [TestCase(
        """
        #nullable enable
        using SharpProof.Attributes;
        public interface ITarget {
            string? Read(string? value);
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static string? Read(
                ITarget receiver,
                string value) => value;
        }
        """)]
    [TestCase(
        """
        using SharpProof.Attributes;
        public interface ITarget {
            ref int Read(ref int value);
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static int Read(
                ITarget receiver,
                ref int value) => value;
        }
        """)]
    [TestCase(
        """
        using SharpProof.Attributes;
        public interface ITarget {
            void Read(int value = 1);
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static void Read(
                ITarget receiver,
                int value) {
            }
        }
        """)]
    [TestCase(
        """
        using SharpProof.Attributes;
        public sealed class Outer<T> {
            public sealed class Leaf {
            }
        }
        public interface ITarget {
            void Read(Outer<int>.Leaf value);
        }
        [ContractFor(typeof(ITarget))]
        public static class TargetContracts {
            public static void Read(
                ITarget receiver,
                Outer<string>.Leaf value) {
            }
        }
        """)]
    public void ExactMemberShapeMismatchesFailClosed(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("ITarget", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.CompanionSignatureMismatch));
    }

    [Test]
    public void NestedGenericOwnerScopesDoNotAliasByOrdinal()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public sealed class Outer<TOuter> {
                public interface ITarget<TInner> {
                    void Read(TOuter value);
                }
            }
            [ContractFor(typeof(Outer<>.ITarget<>))]
            public static class TargetContracts<TContract> {
                public static void Read(
                    Outer<TContract>.ITarget<TContract> receiver,
                    TContract value) {
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind("Outer`1+ITarget`1", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.CompanionSignatureMismatch));
    }

    [Test]
    public void StaticAndInstanceOverloadCollapseFailsAsAmbiguous()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public interface ITarget {
                void Act(int value);
                static abstract void Act(ITarget receiver, int value);
            }
            [ContractFor(typeof(ITarget))]
            public static class TargetContracts {
                public static void Act(ITarget receiver, int value) {
                }
            }
            """;
        var subject = ContractSubject.Create(source);

        Assert.That(
            subject.Bind(
                "ITarget",
                "Act",
                parameterCount: 1,
                isStatic: false).Failure,
            Is.EqualTo(ContractBindingFailure.AmbiguousCompanion));
    }

    [Test]
    public void LookalikeContractForAttributeIsIgnored()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;
            using ContractForAttribute = Lookalike.ContractForAttribute;
            public interface ITarget {
                void Act(bool ok);
            }
            [ContractFor(typeof(ITarget))]
            public static class NotACompanion {
                public static void Act(ITarget receiver, bool ok) {
                    Contract.Requires(ok);
                }
            }
            namespace Lookalike {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class ContractForAttribute : Attribute {
                    public ContractForAttribute(Type target) {
                    }
                }
            }
            """;
        var subject = ContractSubject.Create(source);
        var result = subject.Bind("ITarget", "Act");

        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Contracts!.UsesCompanion, Is.False);
        Assert.That(result.Contracts.Clauses, Is.Empty);
    }

    [Test]
    public void SourceShadowedRuntimeContractApiCannotBecomeProofEvidence()
    {
        var source = ContractIntrinsicValidationFixtures
            .SourceShadowedRuntimeContract("Target");
        var subject = ContractSubject.Create(source);

        var result = subject.Bind("Target", "Read");

        Assert.That(
            result.Failure,
            Is.EqualTo(ContractBindingFailure.ContractApiUnavailable));
    }

    [Test]
    public void ForeignCallableFailsClosedInsteadOfBindingEmptyContracts()
    {
        var owner = ContractSubject.Create(
            """
            public static class Owner {
                public static void Analyze() {
                }
            }
            """);
        var foreign = ContractSubject.Create(
            """
            using SharpProof.Attributes;
            public static class Foreign {
                public static void Analyze(bool condition) {
                    Contract.Requires(condition);
                }
            }
            """);

        var result = owner.Bind(
            foreign.GetMethod("Foreign", "Analyze"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                result.Failure,
                Is.EqualTo(ContractBindingFailure.UnsupportedTarget));
            Assert.That(result.Contracts, Is.Null);
        }
    }

    [TestCase(
        """
        [ContractFor(typeof(Target))]
        public static class TargetContracts {
            public static long Other(long value) => value;
        }
        """,
        ContractBindingFailure.MissingCompanion)]
    [TestCase(
        """
        [ContractFor(typeof(Target))]
        public static class TargetContracts {
            public static long Read(string value) => 0L;
        }
        """,
        ContractBindingFailure.CompanionSignatureMismatch)]
    [TestCase(
        """
        [ContractFor(typeof(Target))]
        public static class FirstContracts {
            public static long Read(long value) => value;
        }
        [ContractFor(typeof(Target))]
        public static class SecondContracts {
            public static long Read(long value) => value;
        }
        """,
        ContractBindingFailure.AmbiguousCompanion)]
    public void InvalidCompanionShapesFailClosed(
        string companion,
        ContractBindingFailure expected)
    {
        System.ArgumentNullException.ThrowIfNull(companion);
        var source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static long Read(long value) => value;
            }
            """ +
            companion;
        var subject = ContractSubject.Create(source);
        Assert.That(subject.Bind("Target", "Read").Failure, Is.EqualTo(expected));
    }

    [Test]
    public void ProductionBinderContainsNoTextualOrSpeculativeBindingEscapeHatches()
    {
        var root = TestRepository.FindRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "src/SharpProof.Compiler.Semantics/Contracts"),
            "*.cs",
            SearchOption.AllDirectories).ToImmutableArray();
        var forbidden = ImmutableArray.Create("SyntaxFactory.",
            "ParseExpression(",
            "ParseStatement(",
            "GetSpeculative",
            "ToDisplayString("
);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                Assert.That(text, Does.Not.Contain(token), file);
            }
        }
    }

    private sealed class ContractSubject
    {
        private readonly ContractBinder _binder;

        private ContractSubject(CSharpCompilation compilation)
        {
            System.ArgumentNullException.ThrowIfNull(compilation);
            Compilation = compilation;
            _binder = new ContractBinder(compilation, new IrFactory());
        }

        private CSharpCompilation Compilation
        {
            get;
        }

        internal static ContractSubject Create(
            string source,
            bool allowUnsafe = false)
        {
            System.ArgumentNullException.ThrowIfNull(source);
            var compilation = TestCompilation.Create(
                "Contracts",
                source,
                allowUnsafe);
            return new ContractSubject(compilation);
        }

        internal ContractBindingResult Bind(
            string typeName,
            string methodName)
        {
            System.ArgumentNullException.ThrowIfNull(methodName);
            System.ArgumentNullException.ThrowIfNull(typeName);
            var method = GetMethod(typeName, methodName);
            return _binder.Bind(method);
        }

        // Expression semantics belong to the Total binder.
        internal TotalContractBindingResult BindTotal(
            string typeName,
            string methodName)
        {
            System.ArgumentNullException.ThrowIfNull(methodName);
            System.ArgumentNullException.ThrowIfNull(typeName);
            var method = GetMethod(typeName, methodName);
            var factory = new IrFactory(IrExecutionSemantics.Total);
            return new ContractBinder(Compilation, factory).BindTotal(new TotalLoweringContext(factory, method));
        }

        internal ContractBindingResult Bind(IMethodSymbol method)
        {
            System.ArgumentNullException.ThrowIfNull(method);
            return _binder.Bind(method);
        }

        internal ContractBindingResult Bind(
            string typeName,
            string methodName,
            int parameterCount,
            bool isStatic)
        {
            System.ArgumentNullException.ThrowIfNull(methodName);
            System.ArgumentNullException.ThrowIfNull(typeName);
            var method = GetMethod(
                typeName,
                methodName,
                parameterCount,
                isStatic);
            return _binder.Bind(method);
        }

        internal ContractBindingResult BindRequires(
            string typeName,
            string methodName)
        {
            System.ArgumentNullException.ThrowIfNull(methodName);
            System.ArgumentNullException.ThrowIfNull(typeName);
            var method = GetMethod(typeName, methodName);
            return _binder.BindRequires(method);
        }

        internal ContractBindingResult BindConstructor(string typeName)
        {
            var type = Compilation.GetTypeByMetadataName(typeName) ??
                       throw new InvalidOperationException(typeName);
            var constructor = type.InstanceConstructors.Single(
                static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (!method.IsImplicitlyDeclared));
            return _binder.Bind(constructor);
        }

        internal ContractBindingResult BindMethodKind(
            string typeName,
            MethodKind methodKind)
        {
            var type = Compilation.GetTypeByMetadataName(typeName) ??
                       throw new InvalidOperationException(typeName);
            var method = type.GetMembers()
                .OfType<IMethodSymbol>()
                .Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.MethodKind == methodKind));
            return _binder.Bind(method);
        }

        internal ContractBindingResult BindCallRequires(
            string callerTypeName,
            string callerMethodName,
            string calledMethodName)
        {
            System.ArgumentNullException.ThrowIfNull(calledMethodName);
            System.ArgumentNullException.ThrowIfNull(callerMethodName);
            System.ArgumentNullException.ThrowIfNull(callerTypeName);
            var caller = GetMethod(callerTypeName, callerMethodName);
            var declaration = caller.DeclaringSyntaxReferences
                .Single()
                .GetSyntax();
            var invocation = declaration.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(candidate =>
                    candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Expression switch
                    {
                        MemberAccessExpressionSyntax member =>
                            member.Name.Identifier.ValueText ==
                            calledMethodName,
                        SimpleNameSyntax name =>
                            name.Identifier.ValueText == calledMethodName,
                        _ => false
                    }));
            var model = Compilation.GetSemanticModel(
                invocation.SyntaxTree);
            var target = model.GetSymbolInfo(invocation).Symbol as
                IMethodSymbol ??
                throw new InvalidOperationException(calledMethodName);
            return _binder.BindRequires(target);
        }

        internal IMethodSymbol GetMethod(
            string typeName,
            string methodName)
        {
            System.ArgumentNullException.ThrowIfNull(methodName);
            var type = Compilation.GetTypeByMetadataName(typeName) ??
                       throw new InvalidOperationException(typeName);
            return type.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Single();
        }

        private IMethodSymbol GetMethod(
            string typeName,
            string methodName,
            int parameterCount,
            bool isStatic)
        {
            System.ArgumentNullException.ThrowIfNull(methodName);
            var type = Compilation.GetTypeByMetadataName(typeName) ??
                       throw new InvalidOperationException(typeName);
            return type.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Single(method =>
                    method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Parameters.Length == parameterCount &&
                    method.IsStatic == isStatic));
        }

    }
}
