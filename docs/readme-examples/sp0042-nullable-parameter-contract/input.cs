#nullable enable
using System.Diagnostics.CodeAnalysis;
public static class NullableParameter
{
    public static bool TryGet([NotNullWhen(true)] out string? value)
    {
        value = null;
        return true;
    }
}
