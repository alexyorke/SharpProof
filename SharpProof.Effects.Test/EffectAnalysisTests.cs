namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectAnalysisTests
{
    [Test]
    public void PureArithmeticHasNoMayEffects()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static int Add(int left, int right) => left + right;
            }
            """,
            "Sample",
            "Add");

        Assert.That(result.Summary.Reads.IsEmpty, Is.True);
        Assert.That(result.Summary.Writes.IsEmpty, Is.True);
        Assert.That(result.Summary.Allocation, Is.EqualTo(EffectAllocationKind.None));
        Assert.That(result.Summary.Capabilities.IsEmpty, Is.True);
        Assert.That(result.Summary.Throws.IsEmpty, Is.True);
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(result.Projection.Effects, Is.EqualTo(SharpProofEffect.None));
        Assert.That(result.Projection.Capabilities, Is.EqualTo(SharpProofCapability.None));
    }

    [Test]
    public void NameOfIsAnExactEffectFreeCompileTimeOperation()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static string Name() => nameof(Sample);
            }
            """,
            "Sample",
            "Name");

        Assert.That(result.Summary.Allocation, Is.EqualTo(EffectAllocationKind.None));
        Assert.That(result.Summary.Throws.IsEmpty, Is.True);
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(result.Projection.IsComplete, Is.True);
    }

    [Test]
    public void StringConstructionDistinguishesKnownAndUnknownAllocation()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static string Runtime(string left, string right) =>
                    left + right;
                public static string Constant() => "sharp" + "proof";
                public static string Interpolated(int value) =>
                    $"value: {value}";
                public static string InterpolatedConstant() => $"sharp";
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var runtime = session.Analyze(Method(compilation, "Runtime"));
        var constant = session.Analyze(Method(compilation, "Constant"));
        var interpolated = session.Analyze(Method(compilation, "Interpolated"));
        var interpolatedConstant = session.Analyze(
            Method(compilation, "InterpolatedConstant"));

        Assert.That(
            runtime.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(
            runtime.Projection.Effects & SharpProofEffect.Allocates,
            Is.EqualTo(SharpProofEffect.Allocates));
        Assert.That(
            constant.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.None));
        Assert.That(
            interpolated.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Unknown));
        Assert.That(interpolated.Summary.Throws.IncludesUnknown, Is.True);
        Assert.That(interpolated.Projection.IsComplete, Is.False);
        Assert.That(
            interpolatedConstant.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.None));
    }

    [Test]
    public void ConversionEffectsPreventFalseZeroAllocationAndDoesNotThrowProofs()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static object Box(int value) => value;
                public static string Cast(object value) => (string)value;
                public static int Unbox(object value) => (int)value;
                public static int Unwrap(int? value) => (int)value;
                public static int Dynamic(dynamic value) => (int)value;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var boxing = session.Analyze(Method(compilation, "Box"));
        Assert.That(
            boxing.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(
            boxing.Projection.Effects & SharpProofEffect.Allocates,
            Is.EqualTo(SharpProofEffect.Allocates));
        Assert.That(boxing.Projection.IsComplete, Is.True);

        var cast = session.Analyze(Method(compilation, "Cast"));
        AssertThrows(cast.Summary, "System.InvalidCastException");
        Assert.That(
            cast.Projection.Effects & SharpProofEffect.Throws,
            Is.EqualTo(SharpProofEffect.Throws));
        Assert.That(cast.Projection.IsComplete, Is.True);

        var unboxing = session.Analyze(Method(compilation, "Unbox"));
        AssertThrows(
            unboxing.Summary,
            "System.InvalidCastException",
            "System.NullReferenceException");
        Assert.That(unboxing.Projection.IsComplete, Is.True);

        var nullable = session.Analyze(Method(compilation, "Unwrap"));
        AssertThrows(nullable.Summary, "System.InvalidOperationException");
        Assert.That(nullable.Projection.IsComplete, Is.True);

        var dynamic = session.Analyze(Method(compilation, "Dynamic"));
        Assert.That(
            dynamic.Summary.Completeness,
            Is.EqualTo(EffectCompleteness.Incomplete));
        Assert.That(
            dynamic.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Unknown));
        Assert.That(dynamic.Summary.Throws.IncludesUnknown, Is.True);
        Assert.That(dynamic.Projection.IsComplete, Is.False);
    }

    [Test]
    public void ManagedAllocationUsesModeledObjectConstructor()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static object Create() => new object();
            }
            """,
            "Sample",
            "Create");

        Assert.That(result.Summary.Allocation, Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(result.Summary.Uncertainty, Is.EqualTo(EffectUncertainty.DirectCall));
        Assert.That(
            result.Projection.Effects,
            Is.EqualTo(SharpProofEffect.Allocates));
        Assert.That(result.Projection.IsComplete, Is.True);
    }

    [Test]
    public void VolatileFieldAccessRequiresSynchronizationCapability()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample {
                private volatile int _volatileValue;
                private int _ordinaryValue;

                public int ReadVolatile() => _volatileValue;
                public int ReadOrdinary() => _ordinaryValue;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var volatileRead = session.Analyze(Method(compilation, "ReadVolatile"));
        var ordinaryRead = session.Analyze(Method(compilation, "ReadOrdinary"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                volatileRead.Summary.Capabilities.Contains(
                    EffectCapabilityKind.Synchronization),
                Is.True);
            Assert.That(
                volatileRead.Projection.Capabilities,
                Is.EqualTo(SharpProofCapability.Synchronization));
            Assert.That(
                ordinaryRead.Summary.Capabilities.IsEmpty,
                Is.True);
            Assert.That(
                ordinaryRead.Projection.Capabilities,
                Is.EqualTo(SharpProofCapability.None));
        }
    }

    [Test]
    public void CompileTimeConstantsDoNotReadStaticState()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private const int Answer = 42;

                public static int ReadConstant() => Answer;
                public static System.DayOfWeek ReadEnum() =>
                    System.DayOfWeek.Monday;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                session.Analyze(Method(compilation, "ReadConstant"))
                    .Summary.Reads.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "ReadEnum"))
                    .Summary.Reads.IsEmpty,
                Is.True);
        }
    }

    [Test]
    public void PropertyIncrementUsesBothAccessorsWithoutBecomingIncomplete()
    {
        var result = Analyze(
            """
            public sealed class Sample {
                private int _value;

                private int Value {
                    get => _value;
                    set => _value = value;
                }

                public void Increment() => Value++;
            }
            """,
            "Sample",
            "Increment");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Reads.Regions, Does.Contain(
                EffectRegionId.Receiver));
            Assert.That(result.Summary.Writes.Regions, Does.Contain(
                EffectRegionId.Receiver));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(result.Projection.IsComplete, Is.True);
        }
    }

    [Test]
    public void ValueTypeConstructionDoesNotReportManagedAllocation()
    {
        var result = Analyze(
            """
            public readonly struct Token {
                public Token(int value) {
                }
            }
            public static class Sample {
                public static Token Create() => new Token(1);
            }
            """,
            "Sample",
            "Create");

        Assert.That(result.Summary.Allocation, Is.EqualTo(EffectAllocationKind.None));
        Assert.That(
            result.Projection.Effects & SharpProofEffect.Allocates,
            Is.EqualTo(SharpProofEffect.None));
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
    }

    [Test]
    public void ObjectAndCollectionInitializersContributeTheirEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System.Collections;

            public sealed class Value {
                public int Number;

                public Value() {
                }
            }

            public sealed class Values : IEnumerable {
                public Values() {
                }

                public void Add(int value) {
                }

                public IEnumerator GetEnumerator() =>
                    throw new System.NotSupportedException();
            }

            public static class Sample {
                private static int s_state;

                private static int SideEffect() {
                    s_state = 1;
                    return 1;
                }

                public static Value ObjectInitializer() =>
                    new Value { Number = SideEffect() };

                public static Values CollectionInitializer() =>
                    new Values { SideEffect() };

                public static int[] ArrayInitializer() =>
                    new[] { SideEffect() };
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var objectInitializer = session.Analyze(
            Method(compilation, "ObjectInitializer"));
        var collectionInitializer = session.Analyze(
            Method(compilation, "CollectionInitializer"));
        var arrayInitializer = session.Analyze(
            Method(compilation, "ArrayInitializer"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                objectInitializer.Summary.Writes.IsUnknown,
                Is.True);
            Assert.That(
                collectionInitializer.Summary.Writes.Contains(
                    EffectRegionId.Static()),
                Is.True);
            Assert.That(
                objectInitializer.Projection.IsComplete,
                Is.False);
            Assert.That(
                collectionInitializer.Projection.Effects &
                SharpProofEffect.WritesStaticState,
                Is.EqualTo(SharpProofEffect.WritesStaticState));
            Assert.That(
                arrayInitializer.Projection.Effects &
                SharpProofEffect.WritesStaticState,
                Is.EqualTo(SharpProofEffect.WritesStaticState));
        }
    }

    [Test]
    public void ConstructorMemberInitializersContributeTheirEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample {
                private static int s_state;
                private int _value = SideEffect();

                public Sample() {
                }

                private static int SideEffect() {
                    s_state = 1;
                    return 1;
                }
            }
            """);
        var constructor = EffectTestHost.RequireType(compilation, "Sample")
            .InstanceConstructors
            .Single(static method =>
                !method.IsImplicitlyDeclared &&
                method.Parameters.Length == 0);

        var result = new EffectAnalysisSession(compilation).Analyze(constructor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Receiver),
                Is.True);
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                result.Projection.Effects &
                (SharpProofEffect.WritesReceiverState |
                 SharpProofEffect.WritesStaticState),
                Is.EqualTo(
                    SharpProofEffect.WritesReceiverState |
                    SharpProofEffect.WritesStaticState));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void PossibleTypeInitializationFailsClosed()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample {
                private static int s_state = Initialize();

                public Sample() {
                }

                public static int Read() => s_state;

                private static int Initialize() => 1;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var constructor = EffectTestHost.RequireType(compilation, "Sample")
            .InstanceConstructors
            .Single(static method =>
                !method.IsImplicitlyDeclared &&
                method.Parameters.Length == 0);

        foreach (var method in new[] {
                     constructor,
                     Method(compilation, "Read")
                 })
        {
            var result = session.Analyze(method);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete),
                method.MetadataName);
            Assert.That(
                result.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
                Is.EqualTo(EffectUncertainty.UnmodeledCall),
                method.MetadataName);
            Assert.That(
                result.Projection.IsComplete,
                Is.False,
                method.MetadataName);
        }
    }

    [Test]
    public void CrossTypeStaticFieldAccessAccountsForTypeInitialization()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Global {
                public static int Value;
            }

            public static class Initialized {
                public static readonly object Value = Initialize();

                private static object Initialize() {
                    Global.Value = 1;
                    return new object();
                }
            }

            public static class Sample {
                public static object Read() => Initialized.Value;
            }
            """);

        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Read"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                result.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
                Is.EqualTo(EffectUncertainty.UnmodeledCall));
            Assert.That(result.Summary.Reads.IsUnknown, Is.True);
            Assert.That(result.Summary.Writes.IsUnknown, Is.True);
            Assert.That(result.Projection.IsComplete, Is.False);
        }
    }

    [Test]
    public void MetadataStaticFieldAccessFailsClosedAtTypeInitializationBoundary()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            public static class ExternalInitialized {
                public static readonly object Value = new object();
            }
            """,
            "ExternalInitializedAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static object Read() => ExternalInitialized.Value;
            }
            """,
            externalReference);

        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Read"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                result.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
                Is.EqualTo(EffectUncertainty.UnmodeledCall));
            Assert.That(result.Projection.IsComplete, Is.False);
        }
    }

    [Test]
    public void FieldWritesRetainReceiverAndStaticRegions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample {
                private int _value;
                private static int s_value;

                public void WriteReceiver() => _value = 1;
                public static void WriteStatic() => s_value = 1;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var receiver = session.Analyze(
            EffectTestHost.RequireMethod(compilation, "Sample", "WriteReceiver"));
        var @static = session.Analyze(
            EffectTestHost.RequireMethod(compilation, "Sample", "WriteStatic"));

        Assert.That(
            receiver.Summary.Writes.Contains(EffectRegionId.Receiver),
            Is.True);
        Assert.That(
            receiver.Projection.Effects,
            Is.EqualTo(SharpProofEffect.WritesReceiverState));
        Assert.That(
            @static.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
        Assert.That(
            @static.Projection.Effects,
            Is.EqualTo(SharpProofEffect.WritesStaticState));
    }

    [Test]
    public void LocalAliasesRetainCallerOwnedAndFreshRegions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                private static Box? s_box;
                public int Value;

                public void ReceiverAlias() {
                    var alias = this;
                    alias.Value = 1;
                }

                public static void ParameterAlias(Box value) {
                    var alias = value;
                    alias.Value = 1;
                }

                public static void StaticAlias() {
                    var alias = s_box;
                    if (alias != null) {
                        alias.Value = 1;
                    }
                }

                public static void FreshAlias() {
                    var alias = new int[1];
                    alias[0] = 1;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var receiver = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Box",
                "ReceiverAlias"));
        var parameter = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Box",
                "ParameterAlias"));
        var @static = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Box",
                "StaticAlias"));
        var fresh = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Box",
                "FreshAlias"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                receiver.Summary.Writes.Contains(EffectRegionId.Receiver),
                Is.True);
            Assert.That(
                parameter.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(
                @static.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(fresh.Summary.Writes.IsEmpty, Is.False);
            Assert.That(
                fresh.Summary.Writes.Regions,
                Has.All.Property(nameof(EffectRegionId.Kind))
                    .EqualTo(EffectRegionKind.Fresh));
            Assert.That(
                new[] {
                    receiver.Summary,
                    parameter.Summary,
                    @static.Summary,
                    fresh.Summary
                },
                Has.All.Property(nameof(EffectSummary.Completeness))
                    .EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void TrustedCompleteExternalContractIsTheCapabilityOverride()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalFixture {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.ReadsAmbientState,
                    Capabilities = SharpProofCapability.Console,
                    IsDeterministic = true,
                    Complete = true)]
                public static void Touch() {
                }
            }
            """,
            "ExternalFixtureAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Invoke() => ExternalFixture.Touch();
            }
            """,
            externalReference);

        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(compilation, "Sample", "Invoke"));

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(
            result.Summary.Reads.Contains(EffectRegionId.Ambient),
            Is.True);
        Assert.That(
            result.Summary.Capabilities.Contains(EffectCapabilityKind.Console),
            Is.True);
        Assert.That(
            result.Projection.Effects,
            Is.EqualTo(SharpProofEffect.ReadsAmbientState));
        Assert.That(result.Projection.IsComplete, Is.True);
        Assert.That(
            result.Projection.Capabilities,
            Is.EqualTo(SharpProofCapability.Console));
    }

    [Test]
    public void TrustedCompleteBodylessSourceContractIsResolved()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public static class Sample {
                [SharpProofTrusted("reviewed native implementation")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static extern void Boundary();

                public static void Invoke() => Boundary();
            }
            """);

        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Invoke"));

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(result.Summary.Reads.IsEmpty, Is.True);
        Assert.That(result.Summary.Writes.IsEmpty, Is.True);
        Assert.That(result.Summary.Throws.IsEmpty, Is.True);
    }

    [Test]
    public void ExternalSummaryRequiresBothTrustAndCompleteContract()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalTrustFixture {
                public static void Neither() {
                }

                [SharpProofTrusted("reviewed implementation")]
                public static void TrustOnly() {
                }

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void ContractOnly() {
                }

                [SharpProofTrusted("reviewed implementation")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void Both() {
                }

                [SharpProofTrusted("reviewed implementation")]
                [EffectContract(SharpProofEffect.None, Complete = false)]
                public static void Incomplete() {
                }

                [SharpProofTrusted("reviewed implementation")]
                [EffectContract(SharpProofEffect.None)]
                public static void ImplicitDefaults() {
                }

                [SharpProofTrusted(" ")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void InvalidReason() {
                }
            }
            """,
            "ExternalTrustFixtureAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            "public static class Sample { }",
            externalReference);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] {
                     "Neither",
                     "TrustOnly",
                     "ContractOnly",
                     "Incomplete",
                     "ImplicitDefaults",
                     "InvalidReason"
                 })
        {
            var result = session.Analyze(
                EffectTestHost.RequireMethod(
                    compilation,
                    "ExternalTrustFixture",
                    methodName));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete),
                methodName);
            Assert.That(result.Summary.Reads.IsUnknown, Is.True, methodName);
            Assert.That(result.Summary.Writes.IsUnknown, Is.True, methodName);
            Assert.That(result.Summary.Throws.IncludesUnknown, Is.True, methodName);
            Assert.That(result.Projection.IsComplete, Is.False, methodName);
        }

        var accepted = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "ExternalTrustFixture",
                "Both"));
        Assert.That(
            accepted.Summary.Completeness,
            Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(accepted.Summary.Reads.IsEmpty, Is.True);
        Assert.That(accepted.Summary.Writes.IsEmpty, Is.True);
        Assert.That(accepted.Summary.Throws.IsEmpty, Is.True);
        Assert.That(accepted.Projection.IsComplete, Is.True);
    }

    [Test]
    public void VirtualPropertyAndInterfaceIndexerDispatchFailClosed()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            [SharpProofTrusted("reviewed external type")]
            public class ExternalBase {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public virtual int Value => 1;
            }

            [SharpProofTrusted("reviewed external interface")]
            public interface IExternalIndex {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                int this[int index] { set; }
            }
            """,
            "ExternalPropertyFixtureAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int Read(ExternalBase value) => value.Value;
                public static void Write(IExternalIndex value) => value[0] = 1;
            }
            """,
            externalReference);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] { "Read", "Write" })
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete),
                methodName);
            Assert.That(
                result.Summary.Uncertainty & EffectUncertainty.Dispatch,
                Is.EqualTo(EffectUncertainty.Dispatch),
                methodName);
            Assert.That(result.Projection.IsComplete, Is.False, methodName);
        }
    }

    [Test]
    public void ExplicitAndImplicitExceptionsRemainResolved()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                public static void Explicit(Exception exception) => throw exception;
                public static int Divide(int left, int right) => left / right;
                public static int Remainder(int left, int right) => left % right;
                public static int? NullableDivide(int? left, int? right) =>
                    left / right;
                public static int? NullableRemainder(int? left, int? right) =>
                    left % right;
                public static uint? NullableUnsignedDivide(
                    uint? left,
                    uint? right) => left / right;
                public static uint? NullableUnsignedRemainder(
                    uint? left,
                    uint? right) => left % right;
                public static nint NativeDivide(nint left, nint right) =>
                    left / right;
                public static nint NativeRemainder(nint left, nint right) =>
                    left % right;
                public static nuint NativeUnsignedDivide(
                    nuint left,
                    nuint right) => left / right;
                public static nuint NativeUnsignedRemainder(
                    nuint left,
                    nuint right) => left % right;
                public static int CompoundDivide(int left, int right) {
                    left /= right;
                    return left;
                }
                public static int CompoundRemainder(int left, int right) {
                    left %= right;
                    return left;
                }
                public static int Length(string text) => text.Length;
                public static int Index(int[] values, int index) => values[index];
                public static int CheckedAdd(int left, int right) =>
                    checked(left + right);
                public static int CheckedIncrement(int value) {
                    checked {
                        value++;
                    }
                    return value;
                }
                public static int[] Array(int length) => new int[length];
                public static int[] FixedArray() => new int[1];
                public static void Lock(object gate) {
                    lock (gate) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertThrows(
            session.Analyze(Method(compilation, "Explicit")).Summary,
            "System.Exception");
        AssertThrows(
            session.Analyze(Method(compilation, "Divide")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "Remainder")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NullableDivide")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NullableRemainder")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NullableUnsignedDivide")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "NullableUnsignedRemainder")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "NativeDivide")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NativeRemainder")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NativeUnsignedDivide")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "NativeUnsignedRemainder")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "CompoundDivide")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "CompoundRemainder")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "Length")).Summary,
            "System.NullReferenceException");
        AssertThrows(
            session.Analyze(Method(compilation, "Index")).Summary,
            "System.NullReferenceException",
            "System.IndexOutOfRangeException");
        AssertThrows(
            session.Analyze(Method(compilation, "CheckedAdd")).Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "CheckedIncrement")).Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "Array")).Summary,
            "System.OverflowException");
        var lockSummary = session.Analyze(
            Method(compilation, "Lock")).Summary;
        AssertContainsThrows(
            lockSummary,
            "System.ArgumentNullException");
        Assert.That(
            lockSummary.Capabilities.Contains(
                EffectCapabilityKind.Synchronization),
            Is.True);
        Assert.That(
            lockSummary.Uncertainty,
            Is.EqualTo(EffectUncertainty.None));
        Assert.That(
            lockSummary.Completeness,
            Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(
            session.Analyze(Method(compilation, "FixedArray"))
                .Summary.Throws.IsEmpty,
            Is.True);
    }

    [Test]
    public void ApiSpecMakesModeledExternalCallComplete()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static int Absolute(int value) => System.Math.Abs(value);
            }
            """,
            "Sample",
            "Absolute");

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(result.Summary.Reads.IsEmpty, Is.True);
        Assert.That(result.Summary.Writes.IsEmpty, Is.True);
        AssertThrows(result.Summary, "System.OverflowException");
        Assert.That(
            result.Projection.Effects,
            Is.EqualTo(SharpProofEffect.Throws));
        Assert.That(result.Projection.IsComplete, Is.True);
    }

    [Test]
    public void CompilerElisionSkipsGhostArgumentsButDirectIntrinsicsThrow()
    {
        const string source =
            """
            using SharpProof.Attributes;

            public static class Sample {
                public static int Elided(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() == Contract.Old(value));
                    return value;
                }

                public static int DirectResult() => Contract.Result<int>();
                public static int DirectOld(int value) => Contract.Old(value);
            }
            """;
        var compilation = EffectTestHost.CreateCompilation(source);
        var session = new EffectAnalysisSession(compilation);
        var directResult = session.Analyze(
            Method(compilation, "DirectResult")).Summary;
        var directOld = session.Analyze(
            Method(compilation, "DirectOld")).Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                session.Analyze(Method(compilation, "Elided"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            AssertThrows(
                directResult,
                "System.InvalidOperationException");
            AssertThrows(
                directOld,
                "System.InvalidOperationException");
            Assert.That(
                directResult.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                directOld.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
        }

        var enabledTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.CSharp12)
                .WithPreprocessorSymbols(Contract.ConditionalSymbol),
            path: "EffectsContractsEnabled.cs");
        var enabledCompilation = EffectTestHost.CreateCompilation(
            [enabledTree],
            "EffectsContractsEnabled");
        var enabled = new EffectAnalysisSession(enabledCompilation).Analyze(
            Method(enabledCompilation, "Elided")).Summary;

        AssertThrows(enabled, "System.InvalidOperationException");
        Assert.That(
            enabled.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));

        var directiveCompilation = EffectTestHost.CreateCompilation(
            "#define " + Contract.ConditionalSymbol +
            Environment.NewLine +
            source);
        var directiveEnabled = new EffectAnalysisSession(
            directiveCompilation).Analyze(
            Method(directiveCompilation, "Elided")).Summary;

        AssertThrows(
            directiveEnabled,
            "System.InvalidOperationException");
        Assert.That(
            directiveEnabled.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
    }

    [Test]
    public void UnmodeledMetadataCallFailsClosed()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static System.Guid CreateGuid() => System.Guid.NewGuid();
            }
            """,
            "Sample",
            "CreateGuid");

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
        Assert.That(result.Summary.Reads.IsUnknown, Is.True);
        Assert.That(result.Summary.Writes.IsUnknown, Is.True);
        Assert.That(result.Summary.Throws.IncludesUnknown, Is.True);
        Assert.That(
            result.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
            Is.EqualTo(EffectUncertainty.UnmodeledCall));
        Assert.That(result.Projection.IsComplete, Is.False);
    }

    [Test]
    public void SourceSummaryRemapsParameterWritesAtDepthZero()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public int Value;
            }

            public static class Sample {
                private static void Mutate(Box value) => value.Value = 1;
                public static void Invoke(Box value) => Mutate(value);
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Invoke"));

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.True);
        Assert.That(result.Summary.Writes.IsUnknown, Is.False);
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        AssertThrows(result.Summary, "System.NullReferenceException");
        Assert.That(
            result.Projection.Effects,
            Is.EqualTo(
                SharpProofEffect.WritesArgumentState |
                SharpProofEffect.Throws));
        Assert.That(result.Projection.IsComplete, Is.True);
    }

    [Test]
    public void ReducedSourceExtensionRemapsItsReceiverArgument()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public int Value;
            }

            public static class BoxExtensions {
                public static void Mutate(this Box value) => value.Value = 1;
            }

            public static class Sample {
                public static void Invoke(Box value) => value.Mutate();
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Invoke"));

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.True);
        Assert.That(result.Summary.Writes.IsUnknown, Is.False);
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
    }

    [Test]
    public void RefParameterWritesRemapToTheCaller()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static void Set(ref int value) => value = 1;
                public static void Invoke(ref int value) => Set(ref value);
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Invoke"));

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.True);
        Assert.That(result.Summary.Writes.IsUnknown, Is.False);
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
    }

    [Test]
    public void RecursiveSccStartsConservative()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static void Recur() => Recur();
            }
            """,
            "Sample",
            "Recur");

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
        Assert.That(result.Summary.Reads.IsUnknown, Is.True);
        Assert.That(result.Summary.Writes.IsUnknown, Is.True);
        Assert.That(
            result.Summary.Uncertainty & EffectUncertainty.Recursion,
            Is.EqualTo(EffectUncertainty.Recursion));
        Assert.That(result.Projection.IsComplete, Is.False);
    }

    [Test]
    public void ReachableControlFlowCycleMayDiverge()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static void Loop(bool keepGoing) {
                    while (keepGoing) {
                    }
                }
            }
            """,
            "Sample",
            "Loop");

        Assert.That(
            result.Summary.Termination,
            Is.EqualTo(EffectTermination.MayDiverge));
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(
            result.Summary.AnalysisIncompleteReason,
            Is.EqualTo(EffectAnalysisIncompleteReason.None));
        Assert.That(result.Projection.IsComplete, Is.True);
    }

    [Test]
    public void ScalarImpossibleBranchDoesNotContributeEffects()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static object? Allocate(int value) {
                    if (value > 0 && value < 0) {
                        return new object();
                    }
                    return null;
                }
            }
            """,
            "Sample",
            "Allocate");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Projection.Effects & SharpProofEffect.Allocates,
                Is.EqualTo(SharpProofEffect.None));
            Assert.That(result.Projection.IsComplete, Is.True);
            Assert.That(
                result.Summary.AnalysisIncompleteReason,
                Is.EqualTo(EffectAnalysisIncompleteReason.None));
        }
    }

    [Test]
    public void ReferenceArrayStoreRetainsAllImplicitExceptions()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static void Store(object[] values, object value) =>
                    values[0] = value;
            }
            """,
            "Sample",
            "Store");

        AssertThrows(
            result.Summary,
            "System.NullReferenceException",
            "System.IndexOutOfRangeException",
            "System.ArrayTypeMismatchException");
        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.True);
    }

    [Test]
    public void ResolvedNullReceiverThrowSurvivesUnknownDispatch()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static string Invoke(object value) => value.ToString();
            }
            """,
            "Sample",
            "Invoke");

        Assert.That(result.Summary.Throws.IncludesUnknown, Is.True);
        AssertContainsThrows(result.Summary, "System.NullReferenceException");
        Assert.That(
            result.Projection.Effects & SharpProofEffect.Throws,
            Is.EqualTo(SharpProofEffect.Throws));
        Assert.That(result.Projection.IsComplete, Is.False);
    }

    [Test]
    public void DefinitelyNonNullReceiverDoesNotAddNullReferenceException()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static int Invoke() => new object().GetHashCode();
            }
            """,
            "Sample",
            "Invoke");

        Assert.That(
            result.Summary.Throws.Types.Select(static type => type.MetadataName),
            Does.Not.Contain("NullReferenceException"));
    }

    [Test]
    public void TryCastReceiverCanStillThrowNullReferenceException()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static int Invoke() =>
                    (new object() as string)!.Length;
            }
            """,
            "Sample",
            "Invoke");

        Assert.That(
            result.Summary.Throws.Types.Select(static type => type.MetadataName),
            Does.Contain("NullReferenceException"));
    }

    [Test]
    public void SourceEffectContractCannotOverrideTheBody()
    {
        var result = Analyze(
            """
            using SharpProof.Attributes;

            public static class Sample {
                private static int s_value;

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void Write() => s_value = 1;
            }
            """,
            "Sample",
            "Write");

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
        Assert.That(
            result.Projection.Effects,
            Is.EqualTo(SharpProofEffect.WritesStaticState));
    }

    [Test]
    public void UntrustedSourceContractRetainsItsDecodedSummaryForChecking()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public static class Sample {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void Empty() {
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var resolution = session.ResolveExternalContract(
            Method(compilation, "Empty"));

        Assert.That(
            resolution.Kind,
            Is.EqualTo(EffectContractResolutionKind.Untrusted));
        Assert.That(
            resolution.Summary.Completeness,
            Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(resolution.Summary.Reads.IsEmpty, Is.True);
        Assert.That(resolution.Summary.Writes.IsEmpty, Is.True);
        Assert.That(
            resolution.Summary.Capabilities.Contains(
                EffectCapabilityKind.Randomness),
            Is.True);
    }

    [Test]
    public void AnalyzeBuildsOnlyTheRequestedReachableCallGraph()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int Reachable(int value) => value + 1;
                public static int Selected(int value) => Reachable(value);
                public static int Unselected(int value) => value - 1;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var selected = Method(compilation, "Selected");

        var first = session.Analyze(selected);
        var second = session.Analyze(selected);

        Assert.That(session.AnalyzedSourceMethodCount, Is.EqualTo(2));
        Assert.That(ReferenceEquals(first.Summary, second.Summary), Is.True);

        session.Analyze(Method(compilation, "Unselected"));

        Assert.That(session.AnalyzedSourceMethodCount, Is.EqualTo(3));
        Assert.That(session.AnalyzeAll(), Has.Length.EqualTo(3));
    }

    [Test]
    public void AnalyzeAllOrderAndSummariesAreDeterministic()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Zeta {
                public static int Last(int value) => Alpha.First(value);
            }

            public static class Alpha {
                public static int First(int value) => value + 1;
                public static int Second(int value) => First(value) + 1;
            }
            """);

        var first = new EffectAnalysisSession(compilation).AnalyzeAll();
        var second = new EffectAnalysisSession(compilation).AnalyzeAll();

        Assert.That(
            second.Select(ResultKey),
            Is.EqualTo(first.Select(ResultKey)));
        Assert.That(
            second.Select(static result => result.Summary),
            Is.EqualTo(first.Select(static result => result.Summary)));
        Assert.That(
            second.Select(static result => result.Projection),
            Is.EqualTo(first.Select(static result => result.Projection)));
    }

    [Test]
    public void ColdConcurrentAnalysisPublishesOneDeterministicCache()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int s_value;

                private static void Write(int value) => s_value = value;
                public static void Invoke(int value) => Write(value);
            }
            """);
        var method = Method(compilation, "Invoke");
        var session = new EffectAnalysisSession(compilation);
        var results = new EffectMethodResult?[64];

        System.Threading.Tasks.Parallel.For(
            0,
            results.Length,
            index => results[index] = session.Analyze(method));

        var expected = results[0] ??
                       throw new InvalidOperationException(
                           "Concurrent analysis produced no result.");
        foreach (var result in results)
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(
                ReferenceEquals(result!.Summary, expected.Summary),
                Is.True);
            Assert.That(result.Projection, Is.EqualTo(expected.Projection));
        }
        Assert.That(
            session.AnalyzeAll().Select(ResultKey),
            Is.EqualTo(session.AnalyzeAll().Select(ResultKey)));
    }

    [Test]
    public void UnsupportedDynamicInvocationFailsClosed()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static object? Invoke(dynamic value) => value();
            }
            """,
            "Sample",
            "Invoke");

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
        Assert.That(
            result.Summary.Uncertainty & EffectUncertainty.UnsupportedOperation,
            Is.EqualTo(EffectUncertainty.UnsupportedOperation));
        Assert.That(result.Projection.IsComplete, Is.False);
    }

    [Test]
    public void ConditionalAccessDoesNotInventNullReceiverExceptions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Receiver {
                public int Value => 1;
                public int GetValue() => 1;
                public Child? Child => null;
            }

            public sealed class Child {
                public int Value => 1;
            }

            public static class Sample {
                public static int? Read(Receiver? receiver) =>
                    receiver?.Value;

                public static int? Invoke(Receiver? receiver) =>
                    receiver?.GetValue();

                public static int? Nested(Receiver? receiver) =>
                    receiver?.Child.Value;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                session.Analyze(Method(compilation, "Read"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "Invoke"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            AssertThrows(
                session.Analyze(Method(compilation, "Nested")).Summary,
                "System.NullReferenceException");
        }
    }

    [Test]
    public void ExceptionFlowReportsOnlyExceptionsThatEscape()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                private static void ThrowInvalid(
                    InvalidOperationException exception) =>
                    throw exception;

                private static bool ThrowFilter(
                    ApplicationException exception) =>
                    throw exception;

                public static void ExactCatch(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) {
                    }
                }

                public static void BaseCatch(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (Exception) {
                    }
                }

                public static void TrueFilter(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) when (true) {
                    }
                }

                public static void FalseFilter(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) when (false) {
                    }
                }

                public static void Rethrow(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) {
                        throw;
                    }
                }

                public static void ThrowingFilter(
                    InvalidOperationException exception,
                    ApplicationException filterException) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException)
                        when (ThrowFilter(filterException)) {
                    }
                }

                public static void NestedRethrow(
                    InvalidOperationException exception) {
                    try {
                        try {
                            ThrowInvalid(exception);
                        }
                        catch (InvalidOperationException) {
                            throw;
                        }
                    }
                    catch (Exception) {
                    }
                }

                public static void HandlerThrows(
                    InvalidOperationException exception,
                    ApplicationException handlerException) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) {
                        throw handlerException;
                    }
                }

                public static void ThrowFromFinally(
                    InvalidOperationException exception,
                    ApplicationException finallyException) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) {
                    }
                    finally {
                        throw finallyException;
                    }
                }

                public static void FinallyOverrides(
                    InvalidOperationException exception,
                    ApplicationException finallyException) {
                    try {
                        ThrowInvalid(exception);
                    }
                    finally {
                        throw finallyException;
                    }
                }

                public static void NonReturningFinally(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    finally {
                        while (true) {
                        }
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                session.Analyze(Method(compilation, "ExactCatch"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "BaseCatch"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "TrueFilter"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            AssertThrows(
                session.Analyze(Method(compilation, "FalseFilter")).Summary,
                "System.InvalidOperationException");
            AssertThrows(
                session.Analyze(Method(compilation, "Rethrow")).Summary,
                "System.InvalidOperationException");
            AssertThrows(
                session.Analyze(Method(compilation, "ThrowingFilter")).Summary,
                "System.InvalidOperationException");
            Assert.That(
                session.Analyze(Method(compilation, "NestedRethrow"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            AssertThrows(
                session.Analyze(Method(compilation, "HandlerThrows")).Summary,
                "System.ApplicationException");
            AssertThrows(
                session.Analyze(Method(compilation, "ThrowFromFinally")).Summary,
                "System.ApplicationException");
            AssertThrows(
                session.Analyze(Method(compilation, "FinallyOverrides")).Summary,
                "System.ApplicationException");
            Assert.That(
                session.Analyze(Method(compilation, "NonReturningFinally"))
                    .Summary.Throws.IsEmpty,
                Is.True);
        }
    }

    [Test]
    public void CatchAllConsumesUnknownManagedThrowsOnlyWhenUnfiltered()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public interface IExternal {
                void Run();
            }

            public static class Sample {
                public static void CatchAll(IExternal external) {
                    try {
                        external.Run();
                    }
                    catch {
                    }
                }

                public static void CatchException(IExternal external) {
                    try {
                        external.Run();
                    }
                    catch (Exception) {
                    }
                }

                public static void Filtered(IExternal external) {
                    try {
                        external.Run();
                    }
                    catch (Exception) when (true) {
                    }
                }

                public static void Rethrow(IExternal external) {
                    try {
                        external.Run();
                    }
                    catch (Exception) {
                        throw;
                    }
                }

                public static void NormalFinally(IExternal external) {
                    try {
                        external.Run();
                    }
                    finally {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                session.Analyze(Method(compilation, "CatchAll"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "CatchException"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "Filtered"))
                    .Summary.Throws.IncludesUnknown,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "Rethrow"))
                    .Summary.Throws.IncludesUnknown,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "NormalFinally"))
                    .Summary.Throws.IncludesUnknown,
                Is.True);
        }
    }

    [Test]
    public void AcyclicFlowDischargesOnlyProvenImplicitExceptions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            using SharpProof.Attributes;

            public static class Sample {
                public static int RequiredDivide(
                    [InRange(-10, 10)] int value,
                    int divisor) {
                    Contract.Requires(divisor != 0);
                    return value / divisor;
                }

                public static int GuardedDivide(
                    [InRange(-10, 10)] int value,
                    int divisor) {
                    if (divisor == 0) {
                        return 0;
                    }
                    return value / divisor;
                }

                public static int CheckedAdd(
                    [InRange(0, 10)] int left,
                    [InRange(0, 10)] int right) =>
                    checked(left + right);

                public static int PositiveSize(
                    [Positive] int length) =>
                    new int[length].Length;

                public static int GuardedIndex(int index) {
                    var values = new int[2];
                    if (index < 0 || index >= values.Length) {
                        return 0;
                    }
                    return values[index];
                }

                public static int NonNullReceiver(
                    [NotNull] string value) {
                    lock (value) {
                        return value.Length;
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var method in new[] {
                     "RequiredDivide",
                     "GuardedDivide",
                     "CheckedAdd",
                     "PositiveSize",
                     "GuardedIndex",
                     "NonNullReceiver"
                 })
        {
            var throws = session.Analyze(Method(compilation, method))
                .Summary.Throws;
            Assert.That(
                throws.IsEmpty,
                Is.True,
                method + ": unknown=" + throws.IncludesUnknown + "; " +
                string.Join(
                    ", ",
                    throws.Types.Select(static type =>
                        type.ContainingNamespace.MetadataName + "." +
                        type.MetadataName)));
        }
    }

    [Test]
    public void ReassignmentsWrapAndBadIndexesRetainImplicitExceptions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            using SharpProof.Attributes;

            public static class Sample {
                public static int ReassignedDivisor(int divisor) {
                    if (divisor == 0) {
                        return 0;
                    }
                    divisor = 0;
                    return 1 / divisor;
                }

                public static int NegativeIndex() =>
                    (new int[2])[-1];

                public static int TooLargeIndex() =>
                    (new int[2])[2];

                public static int CheckedBoundary(int value) =>
                    checked(value + 1);

                public static int NarrowedDivisor(long value) {
                    var divisor = unchecked((int)value);
                    return 1 / divisor;
                }

                public static int ReassignedNull(
                    [NotNull] string value) {
                    value = null!;
                    return value.Length;
                }

                public static int DivisionEvaluationOrder(int value) =>
                    value / (value = -1);

                public static int CheckedEvaluationOrder(int value) =>
                    checked(value + (value = 0));

                public static int ArrayEvaluationOrder() {
                    var first = new int[0];
                    var other = new int[2];
                    return first[(first = other).Length - 1];
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertThrows(
            session.Analyze(Method(compilation, "ReassignedDivisor")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "NegativeIndex")).Summary,
            "System.IndexOutOfRangeException");
        AssertThrows(
            session.Analyze(Method(compilation, "TooLargeIndex")).Summary,
            "System.IndexOutOfRangeException");
        AssertThrows(
            session.Analyze(Method(compilation, "CheckedBoundary")).Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NarrowedDivisor")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "ReassignedNull")).Summary,
            "System.NullReferenceException");
        AssertThrows(
            session.Analyze(Method(compilation, "DivisionEvaluationOrder"))
                .Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "CheckedEvaluationOrder"))
                .Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "ArrayEvaluationOrder"))
                .Summary,
            "System.IndexOutOfRangeException");
    }

    [Test]
    public void ReturnAnnotationsRefineReceiversDivisorsAndIndexes()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            using SharpProof.Attributes;

            public static class Sample {
                [return: NotNull]
                private static string Text() => "";

                [return: Positive]
                private static int Divisor() => 1;

                [return: InRange(0, 1)]
                private static int Index() => 1;

                private static string TextProperty {
                    [return: NotNull]
                    get => "";
                }

                [return: InRange(2, 1)]
                private static int Malformed() => 0;

                public static int Safe() {
                    var values = new int[2];
                    return Text().Length +
                        TextProperty.Length +
                        10 / Divisor() +
                        values[Index()];
                }

                public static int MalformedRemainsUnsafe() =>
                    10 / Malformed();
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        Assert.That(
            session.Analyze(Method(compilation, "Safe"))
                .Summary.Throws.IsEmpty,
            Is.True);
        AssertThrows(
            session.Analyze(Method(compilation, "MalformedRemainsUnsafe"))
                .Summary,
            "System.DivideByZeroException");
    }

    [Test]
    public void CallsThatCanMutateLocalsInvalidateFlowFacts()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private delegate void Mutation();

                private static void SetZero(ref int value) => value = 0;

                private static void CreateZero(out int value) => value = 0;

                public static int RefCall() {
                    var value = 1;
                    SetZero(ref value);
                    return 1 / value;
                }

                public static int OutConstructor() {
                    var value = 1;
                    _ = new Holder(out value);
                    return 1 / value;
                }

                public static int LocalFunctionCall() {
                    var value = 1;
                    void Mutate() => value = 0;
                    Mutate();
                    return 1 / value;
                }

                public static int DelegateCall() {
                    var value = 1;
                    Mutation mutate = () => value = 0;
                    mutate();
                    return 1 / value;
                }

                private sealed class Holder {
                    public Holder(out int value) => CreateZero(out value);
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var method in new[] {
                     "RefCall",
                     "OutConstructor",
                     "LocalFunctionCall",
                     "DelegateCall"
                 })
        {
            AssertContainsThrows(
                session.Analyze(Method(compilation, method)).Summary,
                "System.DivideByZeroException");
        }
    }

    [Test]
    public void DirectWitnessesAreNarrowDeterministicAndOrdered()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Threading;

            public sealed class UserException : Exception {
            }

            public sealed class Sample {
                private int _field;
                private volatile int _volatile;

                public object Allocate() => new object();
                public int[] AllocateArray() => new int[1];
                public void Throw() => throw new InvalidOperationException();
                public void ThrowUser() => throw new UserException();
                public void Write() => _field = 1;
                public int Read() => _field;
                public void VolatileWrite() => _volatile = 1;
                public int VolatileRead() => _volatile;

                public void Synchronize() {
                    lock (new object()) {
                    }
                }

                public void EnterMonitor() => Monitor.Enter(this);

                public void Conditional(bool condition) {
                    if (condition) {
                        _field = 1;
                    }
                }

                public void Multiple() {
                    _field = 1;
                    _field = 2;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertKinds("Allocate", "managed-allocation");
        AssertKinds("AllocateArray", "managed-array-allocation");
        AssertKinds("Throw", "managed-allocation", "explicit-throw");
        AssertKinds("ThrowUser", "managed-allocation", "explicit-throw");
        AssertKinds("Write", "direct-field-write");
        AssertKinds("Read", "direct-field-read");
        AssertKinds("VolatileWrite", "direct-field-write", "volatile-field-access");
        AssertKinds("VolatileRead", "direct-field-read", "volatile-field-access");
        AssertKinds("Synchronize", "managed-allocation", "synchronization-lock");
        AssertKinds("EnterMonitor", "synchronization-call");
        AssertKinds("Conditional");
        AssertKinds("Multiple");

        var frameworkThrow = Witnesses("Throw");
        var userThrow = Witnesses("ThrowUser");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(frameworkThrow[1].ExceptionType?.MetadataName,
                Is.EqualTo(nameof(InvalidOperationException)));
            Assert.That(userThrow[1].ExceptionType, Is.Null);
            Assert.That(Witnesses("VolatileRead")[1].Capabilities,
                Is.EqualTo(EffectContractCapabilityKind.Synchronization));
            Assert.That(Witnesses("Synchronize")[0].Effects,
                Is.EqualTo(EffectContractKind.Allocates));
        }
        return;

        ImmutableArray<EffectDirectWitness> Witnesses(string name)
        {
            return session.Analyze(Method(compilation, name)).DirectWitnesses;
        }

        void AssertKinds(string name, params string[] expected)
        {
            Assert.That(Witnesses(name).Select(static witness => witness.Kind),
                Is.EqualTo(expected), name);
        }
    }

    private static EffectMethodResult Analyze(
        string source,
        string typeMetadataName,
        string methodName)
    {
        var compilation = EffectTestHost.CreateCompilation(source);
        return new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                typeMetadataName,
                methodName));
    }

    private static IMethodSymbol Method(
        Compilation compilation,
        string methodName)
    {
        return EffectTestHost.RequireMethod(compilation, "Sample", methodName);
    }

    private static void AssertThrows(
        EffectSummary summary,
        params string[] metadataNames)
    {
        Assert.That(summary.Throws.IncludesUnknown, Is.False);
        AssertContainsThrows(summary, metadataNames);
    }

    private static void AssertContainsThrows(
        EffectSummary summary,
        params string[] metadataNames)
    {
        var actual = summary.Throws.Types
            .Select(static type =>
                type.ContainingNamespace.MetadataName + "." + type.MetadataName)
            .ToImmutableArray();
        foreach (var metadataName in metadataNames)
        {
            Assert.That(
                actual,
                Does.Contain(metadataName));
        }
    }

    private static string ResultKey(EffectMethodResult result)
    {
        return result.Method.ContainingType.MetadataName + "." +
        result.Method.MetadataName + "/" +
        result.Method.Parameters.Length;
    }
}
