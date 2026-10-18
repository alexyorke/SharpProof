using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConditionalCustomModifierIdentityTests
{
    [TestCase("empty", "empty", true)]
    [TestCase("init", "otherInit", true)]
    [TestCase("init", "empty", false)]
    [TestCase("init", "readonly", false)]
    [TestCase("init", "optional", false)]
    [TestCase("optional", "optional", true)]
    public void CompilerProducedModifiersRequireMatchingLengthKindAndType(
        string leftKey, string rightKey, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(rightKey);
        System.ArgumentNullException.ThrowIfNull(leftKey);
        var compilation = TestCompilation.Create("ModifierIdentityProducer", """
            public unsafe class ModifierSubject
            {
                public int P { get; init; }
                public int Q { get; init; }
                private static int storage;
                public static ref readonly int R() => ref storage;
                public static void Plain() { }
                public delegate* unmanaged[SuppressGCTransition]<void> F;
            }
            """, allowUnsafe: true, includeSharpProofReference: false);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var consumer = TestCompilation.Create("ModifierIdentityConsumer", "public class Consumer { }",
                includeSharpProofReference: false)
            .AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        TestCompilation.AssertNoErrors(consumer);
        var subject = consumer.GetTypeByMetadataName("ModifierSubject")!;
        Assert.That(subject, Is.Not.Null);
        Assert.That(subject.Locations.All(location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInMetadata)), Is.True);
        var init = ((IPropertySymbol)subject.GetMembers("P").Single()).SetMethod!.ReturnTypeCustomModifiers;
        var otherInit = ((IPropertySymbol)subject.GetMembers("Q").Single()).SetMethod!.ReturnTypeCustomModifiers;
        var readOnly = ((IMethodSymbol)subject.GetMembers("R").Single()).RefCustomModifiers;
        var optional = ((IFunctionPointerTypeSymbol)((IFieldSymbol)subject.GetMembers("F").Single()).Type)
            .Signature.ReturnTypeCustomModifiers;
        var empty = ((IMethodSymbol)subject.GetMembers("Plain").Single()).ReturnTypeCustomModifiers;
        AssertModifier(init, false, "System.Runtime.CompilerServices.IsExternalInit");
        AssertModifier(otherInit, false, "System.Runtime.CompilerServices.IsExternalInit");
        AssertModifier(readOnly, false, "System.Runtime.InteropServices.InAttribute");
        AssertModifier(optional, true, "System.Runtime.CompilerServices.CallConvSuppressGCTransition");
        Assert.That(empty, Is.Empty);
        var modifiers = new Dictionary<string, ImmutableArray<CustomModifier>>(StringComparer.Ordinal)
        {
            ["empty"] = empty,
            ["init"] = init,
            ["otherInit"] = otherInit,
            ["readonly"] = readOnly,
            ["optional"] = optional
        };

        var actual = ConditionalMethodIdentityPolicy.SameModifiers(modifiers[leftKey], modifiers[rightKey]);

        Assert.That(actual, Is.EqualTo(expected));
    }

    private static void AssertModifier(ImmutableArray<CustomModifier> modifiers,
        bool optional, string metadataName)
    {
        System.ArgumentNullException.ThrowIfNull(metadataName);
        Assert.That(modifiers, Has.Length.EqualTo(1));
        Assert.That(modifiers[0].IsOptional, Is.EqualTo(optional));
        Assert.That(modifiers[0].Modifier.ToDisplayString(), Is.EqualTo(metadataName));
    }
}
