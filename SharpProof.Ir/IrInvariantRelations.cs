namespace SharpProof.Ir;

// An unknown loop invariant: a pure application of a member with this prefix
// to a loop's state at its header. A Horn solver searches for its meaning; no
// proof query ever contains one.
internal static class IrInvariantRelations
{
    internal const string Prefix = "invariant:";

    internal static IrOpaqueTerm Create(IrFactory factory, int ordinal, ImmutableArray<IrTerm> state)
    {
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            Prefix + ordinal.ToString(CultureInfo.InvariantCulture), factory.BooleanType, true, [.. state.Select(term => term.Type)]);
        return factory.PureOpaque(member, null, [.. state]);
    }

    internal static bool IsRelation(IrFactory factory, IrTerm term)
    {
        return term is IrOpaqueTerm { Purity: IrOpaquePurity.Pure, Receiver: null } opaque &&
            factory.GetString(factory.GetMemberInfo(opaque.Member).Name).StartsWith(Prefix, StringComparison.Ordinal);
    }
}
