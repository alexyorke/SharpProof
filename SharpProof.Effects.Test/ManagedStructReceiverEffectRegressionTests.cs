using System.Reflection;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ManagedStructReceiverEffectRegressionTests
{
    private static readonly string[] s_managedMutatorNames =
    [
        "MutateByValue",
        "MutateIn",
        "MutateReadonlyField",
        "MutateBoxedArgument"
    ];

    [Test]
    public void ManagedReceiverCopiesRetainReachableWrites()
    {
        var boundary = EffectTestHost.EmitImage(
            """
            using SharpProof.Attributes;

            public interface IManagedMutator
            {
                void Mutate();
            }

            public struct ManagedValue : IManagedMutator
            {
                public int[] Items;

                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.ReadsReceiverState |
                        SharpProofEffect.WritesReceiverState,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public void Mutate() => Items[0]++;
            }

            public struct ScalarValue
            {
                public int Value;

                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.ReadsReceiverState |
                        SharpProofEffect.WritesReceiverState,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public void Mutate() => Value++;
            }

            public sealed class Owner
            {
                public readonly ManagedValue Value;

                public Owner(ManagedValue value) => Value = value;
            }

            public static class ExternalMutator
            {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.WritesArgumentState,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public static void Mutate(IManagedMutator value) =>
                    value.Mutate();
            }

            public static class RuntimeCases
            {
                public static void MutateByValue(ManagedValue value) =>
                    value.Mutate();

                public static void MutateIn(in ManagedValue value) =>
                    value.Mutate();

                public static void MutateReadonlyField(Owner owner) =>
                    owner.Value.Mutate();

                public static void MutateBoxedArgument(ManagedValue value) =>
                    ExternalMutator.Mutate(value);

                public static void MutateScalarIn(in ScalarValue value) =>
                    value.Mutate();
            }
            """,
            "ManagedStructReceiverBoundary");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample
            {
                public static void ByValue(ManagedValue value) =>
                    value.Mutate();

                public static void In(in ManagedValue value) =>
                    value.Mutate();

                public static void ReadonlyField(Owner owner) =>
                    owner.Value.Mutate();

                public static void BoxedArgument(ManagedValue value) =>
                    ExternalMutator.Mutate(value);

                public static void ScalarIn(in ScalarValue value) =>
                    value.Mutate();
            }
            """,
            boundary.Reference);

        var byValue = EffectTestHost.AnalyzeSample(compilation, "ByValue");
        var byIn = EffectTestHost.AnalyzeSample(compilation, "In");
        var readonlyField = EffectTestHost.AnalyzeSample(
            compilation,
            "ReadonlyField");
        var boxedArgument = EffectTestHost.AnalyzeSample(
            compilation,
            "BoxedArgument");
        var scalarIn = EffectTestHost.AnalyzeSample(
            compilation,
            "ScalarIn");

        AssertHasObservableArgumentWrite(byValue);
        AssertHasObservableArgumentWrite(byIn);
        AssertHasObservableArgumentWrite(readonlyField);
        AssertHasObservableArgumentWrite(boxedArgument);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(scalarIn.Summary.Writes.IsEmpty, Is.True);
            Assert.That(
                scalarIn.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                EffectContractMappings.IsObservablePure(scalarIn.Summary),
                Is.True);
            Assert.That(
                scalarIn.Projection.Effects.HasFlag(
                    SharpProofEffect.WritesArgumentState),
                Is.False);
        }

        RuntimeAssemblyTestHost.WithRuntimeAssembly(
            "SharpProof.Effects.Test.ManagedStructReceiverBoundary",
            boundary.Image,
            assembly =>
            {
                var managedType = assembly.GetType(
                    "ManagedValue",
                    throwOnError: true)!;
                var itemsField = managedType.GetField("Items")!;
                foreach (var methodName in s_managedMutatorNames)
                {
                    var items = new int[1];
                    var value = Activator.CreateInstance(managedType)!;
                    itemsField.SetValue(value, items);
                    object argument = value;
                    if (methodName == "MutateReadonlyField")
                    {
                        var ownerType = assembly.GetType(
                            "Owner",
                            throwOnError: true)!;
                        argument = Activator.CreateInstance(
                            ownerType,
                            [value])!;
                    }

                    RequireRuntimeMethod(assembly, methodName)
                        .Invoke(null, [argument]);
                    Assert.That(items[0], Is.EqualTo(1), methodName);
                }

                var scalarType = assembly.GetType(
                    "ScalarValue",
                    throwOnError: true)!;
                var scalar = Activator.CreateInstance(scalarType)!;
                var valueField = scalarType.GetField("Value")!;
                valueField.SetValue(scalar, 0);
                RequireRuntimeMethod(assembly, "MutateScalarIn")
                    .Invoke(null, [scalar]);
                Assert.That(valueField.GetValue(scalar), Is.EqualTo(0));
            });
    }

    private static void AssertHasObservableArgumentWrite(
        EffectMethodResult result)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(result.Summary.Writes.IsUnknown, Is.False);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                EffectContractMappings.IsObservablePure(result.Summary),
                Is.False);
            Assert.That(
                result.Projection.Effects.HasFlag(
                    SharpProofEffect.WritesArgumentState),
                Is.True);
        }
    }

    private static MethodInfo RequireRuntimeMethod(
        Assembly assembly,
        string methodName)
    {
        return assembly.GetType("RuntimeCases", throwOnError: true)!
                   .GetMethod(methodName) ??
               throw new InvalidOperationException(
                   $"Runtime method '{methodName}' was not found.");
    }
}
