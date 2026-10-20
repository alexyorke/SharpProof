using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ImmutableDictionaryCaptureTests
{
    [Test]
    public void IdenticalDuplicatePairsAreRejectedWithoutChangingPublishedState()
    {
        const string key = "duplicate";
        const int value = 7;
        const string message = "An item with the same key has already been added. Key: duplicate";
        var map = ImmutableDictionary<string, int>.Empty.AddUnique(key, value);
        var mapError = Assert.Throws<ArgumentException>(
            (Action)(() => map.AddUnique(key, value)));
        Assert.That(mapError!.Message, Is.EqualTo(message));
        Assert.That(map.Count, Is.EqualTo(1));
        Assert.That(map[key], Is.EqualTo(value));

        // Failure must leave the owned construction builder unchanged.
        var builder = ImmutableDictionary.CreateBuilder<string, int>();
        builder.AddUnique(key, value);
        var builderError = Assert.Throws<ArgumentException>(
            (Action)(() => builder.AddUnique(key, value)));
        var snapshot = builder.ToImmutable();
        Assert.That(builderError!.Message, Is.EqualTo(message));
        Assert.That(snapshot, Is.EqualTo(map));

        ImmutableArray<(string Key, int Value)> source = [(key, value), (key, value)];
        var createError = Assert.Throws<ArgumentException>(
            (Action)(() => ImmutableDictionaryCapture.Create(
                source, item => item.Key, item => item.Value)));
        Assert.That(createError!.Message, Is.EqualTo(message));
        Assert.That(source.Length, Is.EqualTo(2));
        Assert.That(source[0], Is.EqualTo(source[1]));
    }
}
