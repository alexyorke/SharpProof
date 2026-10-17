namespace SharpProof.Ir;

// An unknown loop invariant: a pure application of a member with this prefix
// to a loop's state at its header. A Horn solver searches for its meaning; no
// proof query ever contains one.
internal static class IrInvariantRelations
{
    internal const string Prefix = "invariant:";

    internal static IrOpaqueTerm Create(IrFactory factory, int ordinal, ImmutableArray<IrTerm> state)
    {
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        if (state.IsDefault)
        {
            throw new ArgumentException("The relation state must be initialized.", nameof(state));
        }

        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            Prefix + ordinal.ToString(CultureInfo.InvariantCulture), factory.BooleanType, true, [.. state.Select(term => term is null ? throw new System.ArgumentNullException(nameof(term)) : (term.Type))]);
        return factory.PureOpaque(member, null, [.. state]);
    }

    internal static bool IsRelation(IrFactory factory, IrTerm term)
    {
        if (term is null)
        {
            throw new System.ArgumentNullException(nameof(term));
        }
        if (factory is null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        return term is IrOpaqueTerm { Purity: IrOpaquePurity.Pure, Receiver: null } opaque &&
            factory.GetString(factory.GetMemberInfo(opaque.Member).Name).StartsWith(Prefix, StringComparison.Ordinal);
    }
}
