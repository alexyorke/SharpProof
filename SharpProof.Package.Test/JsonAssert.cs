using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Package.Test;

internal static class JsonAssert
{
    internal static void Equal(JsonElement root, string path, string? expected, string? message = null)
    {
        Assert.That(ElementAt(root, path).GetString(), Is.EqualTo(expected), message);
    }

    internal static void Equal(JsonElement root, string path, int expected, string? message = null)
    {
        Assert.That(ElementAt(root, path).GetInt32(), Is.EqualTo(expected), message);
    }

    internal static void Equal(JsonElement root, string path, long expected, string? message = null)
    {
        Assert.That(ElementAt(root, path).GetInt64(), Is.EqualTo(expected), message);
    }

    internal static void Equal(JsonElement root, string path, bool expected, string? message = null)
    {
        Assert.That(ElementAt(root, path).GetBoolean(), expected ? Is.True : Is.False, message);
    }

    private static JsonElement ElementAt(JsonElement root, string path)
    {
        foreach (var part in path.Split(['.', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
        {
            root = int.TryParse(part, out var index)
                ? root[index]
                : root.GetProperty(part);
        }
        return root;
    }
}
