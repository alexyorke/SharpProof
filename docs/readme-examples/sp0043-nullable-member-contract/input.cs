#nullable enable
using System.Diagnostics.CodeAnalysis;
public sealed class NullableMember
{
    private string? _name;
    [MemberNotNull(nameof(_name))]
    public void Initialize() { }
}
