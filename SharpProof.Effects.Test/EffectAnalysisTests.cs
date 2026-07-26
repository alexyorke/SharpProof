namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectAnalysisTests {
    [Test]
    public void PureArithmeticHasNoMayEffects() {
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
    public void ConversionEffectsPreventFalseZeroAllocationAndDoesNotThrowProofs() {
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
    public void ManagedAllocationUsesModeledObjectConstructor() {
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
    public void ObjectAndCollectionInitializersContributeTheirEffects() {
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

        using (Assert.EnterMultipleScope()) {
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
    public void ConstructorMemberInitializersContributeTheirEffects() {
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

        using (Assert.EnterMultipleScope()) {
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
    public void PossibleTypeInitializationFailsClosed() {
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
                 }) {
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
    public void CrossTypeStaticFieldAccessAccountsForTypeInitialization() {
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

        using (Assert.EnterMultipleScope()) {
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
    public void MetadataStaticFieldAccessFailsClosedAtTypeInitializationBoundary() {
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

        using (Assert.EnterMultipleScope()) {
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
    public void FieldWritesRetainReceiverAndStaticRegions() {
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
    public void LocalAliasesRetainCallerOwnedAndFreshRegions() {
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

        using (Assert.EnterMultipleScope()) {
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
    public void TrustedCompleteExternalContractIsTheCapabilityOverride() {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalFixture {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.ReadsAmbientState,
                    Capabilities = SharpProofCapability.Console,
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
    public void ExternalSummaryRequiresBothTrustAndCompleteContract() {
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
                     "InvalidReason"
                 }) {
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
    public void VirtualPropertyAndInterfaceIndexerDispatchFailClosed() {
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

        foreach (var methodName in new[] { "Read", "Write" }) {
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
    public void ExplicitAndImplicitExceptionsRemainResolved() {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                public static void Explicit(Exception exception) => throw exception;
                public static int Divide(int left, int right) => left / right;
                public static int Remainder(int left, int right) => left % right;
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
    public void ApiSpecMakesModeledExternalCallComplete() {
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
    public void CompilerElisionSkipsGhostArgumentsButDirectIntrinsicsThrow() {
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

        using (Assert.EnterMultipleScope()) {
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
    public void UnmodeledMetadataCallFailsClosed() {
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
    public void SourceSummaryRemapsParameterWritesAtDepthZero() {
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
    public void ReducedSourceExtensionRemapsItsReceiverArgument() {
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
    public void RefParameterWritesRemapToTheCaller() {
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
    public void RecursiveSccStartsConservative() {
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
    public void ReachableControlFlowCycleMayDiverge() {
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
    }

    [Test]
    public void ReferenceArrayStoreRetainsAllImplicitExceptions() {
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
    public void ResolvedNullReceiverThrowSurvivesUnknownDispatch() {
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
    public void SourceEffectContractCannotOverrideTheBody() {
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
    public void AnalyzeAllOrderAndSummariesAreDeterministic() {
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
    public void ColdConcurrentAnalysisPublishesOneDeterministicCache() {
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
        foreach (var result in results) {
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
    public void UnsupportedDynamicInvocationFailsClosed() {
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

    private static EffectMethodResult Analyze(
        string source,
        string typeMetadataName,
        string methodName) {
        var compilation = EffectTestHost.CreateCompilation(source);
        return new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                typeMetadataName,
                methodName));
    }

    private static IMethodSymbol Method(
        Compilation compilation,
        string methodName) =>
        EffectTestHost.RequireMethod(compilation, "Sample", methodName);

    private static void AssertThrows(
        EffectSummary summary,
        params string[] metadataNames) {
        Assert.That(summary.Throws.IncludesUnknown, Is.False);
        AssertContainsThrows(summary, metadataNames);
    }

    private static void AssertContainsThrows(
        EffectSummary summary,
        params string[] metadataNames) {
        var actual = summary.Throws.Types
            .Select(static type =>
                type.ContainingNamespace.MetadataName + "." + type.MetadataName)
            .ToImmutableArray();
        foreach (var metadataName in metadataNames)
            Assert.That(
                actual,
                Does.Contain(metadataName));
    }

    private static string ResultKey(EffectMethodResult result) =>
        result.Method.ContainingType.MetadataName + "." +
        result.Method.MetadataName + "/" +
        result.Method.Parameters.Length;
}
