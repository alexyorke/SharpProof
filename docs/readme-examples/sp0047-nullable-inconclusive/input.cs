#nullable enable
using System.Diagnostics.CodeAnalysis;
public sealed class NullableUnknown
{
    private int _reads;
    private string? Current => _reads++ == 0 ? "value" : null;
    [MemberNotNull(nameof(Current))]
    public void Initialize() { }
}
