using NUnit.Framework;

namespace SharpProof.Worker.Test;

internal sealed class DefaultComparerValueOracleTests
{
    private static int _elementInitializations;
    [Test]
    public void AuditedGetterReadsAReadonlyPerTypeCache()
    {
        var type = typeof(Comparer<int>);
        var getter = type.GetProperty(nameof(Comparer<int>.Default))!.GetMethod!;
        var body = getter.GetMethodBody()!.GetILAsByteArray()!;
        Assert.That(body, Has.Length.EqualTo(6));
        Assert.That(body[0], Is.EqualTo(0x7e)); // ldsfld
        Assert.That(body[5], Is.EqualTo(0x2a)); // ret
        var field = getter.Module.ResolveField(BitConverter.ToInt32(body, 1), type.GetGenericArguments(), null)!;
        Assert.That(field.IsStatic, Is.True);
        Assert.That(field.IsInitOnly, Is.True);
        Assert.That(field.FieldType, Is.EqualTo(type));
    }

    [Test]
    public void GetterDoesNotInvokeComparableCodeOrInitializeItsElementType()
    {
        var before = _elementInitializations;
        Assert.That(Comparer<Poison>.Default, Is.Not.Null);
        Assert.That(Comparer<Poison>.Default, Is.SameAs(Comparer<Poison>.Default));
        Assert.That(_elementInitializations, Is.EqualTo(before));
        _ = new Poison();
    }

    [Test]
    public void RuntimeTypeIdentityDeterminesTheCache()
    {
        Assert.That(Comparer<dynamic>.Default, Is.SameAs(Comparer<object>.Default));
        Assert.That(Comparer<(int Left, int Right)>.Default, Is.SameAs(Comparer<(int First, int Second)>.Default));
        Assert.That(Comparer<string?>.Default, Is.SameAs(Comparer<string>.Default));
        Assert.That(Comparer<int>.Default, Is.Not.SameAs(Comparer<long>.Default));
    }

    private sealed class Poison : IComparable<Poison>
    {
        static Poison()
        { _elementInitializations++; }

        public int CompareTo(Poison? other)
        { throw new InvalidOperationException("The comparer getter must not call CompareTo."); }
    }
}
