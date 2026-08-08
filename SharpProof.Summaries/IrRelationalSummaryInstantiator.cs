namespace SharpProof.Summaries;

public static class IrRelationalSummaryInstantiator
{
    public static IrSummaryInstantiation Instantiate(
        IrRelationalSummary summary,
        IrTerm? receiver,
        IReadOnlyList<IrTerm> arguments,
        int instanceOrdinal)
    {
        if (summary == null)
        {
            throw new ArgumentNullException(nameof(summary));
        }

        if (arguments == null)
        {
            throw new ArgumentNullException(nameof(arguments));
        }

        if (instanceOrdinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(instanceOrdinal));
        }

        var factory = summary.Factory;
        var signature = summary.Signature;
        if (signature.Receiver.HasValue != (receiver != null) ||
            arguments.Count != signature.Parameters.Length)
        {
            throw new ArgumentException(
                "The call shape does not match the relational summary.",
                nameof(arguments));
        }

        var replacements = new Dictionary<IrVarId, IrTerm>();
        if (signature.Receiver.HasValue)
        {
            EnsureType(
                factory,
                signature.Receiver.Value,
                receiver!,
                nameof(receiver));
            replacements.Add(signature.Receiver.Value, receiver!);
        }

        for (var index = 0; index < arguments.Count; index++)
        {
            EnsureType(
                factory,
                signature.Parameters[index],
                arguments[index],
                nameof(arguments));
            replacements.Add(signature.Parameters[index], arguments[index]);
        }

        var fresh = ImmutableArray.CreateBuilder<IrVarId>(
            summary.ExistentialVariables.Length + 1);
        var result = CreateFresh(
            factory,
            signature.Result,
            instanceOrdinal,
            "result",
            0);
        fresh.Add(result);
        replacements.Add(signature.Result, factory.Variable(result));
        for (var index = 0;
             index < summary.ExistentialVariables.Length;
             index++)
        {
            var variable = summary.ExistentialVariables[index];
            var replacement = CreateFresh(
                factory,
                variable,
                instanceOrdinal,
                "existential",
                index);
            fresh.Add(replacement);
            replacements.Add(variable, factory.Variable(replacement));
        }

        return new IrSummaryInstantiation(
            result,
            IrSubstitution.Substitute(
                factory,
                summary.NormalCompletion,
                replacements),
            IrSubstitution.Substitute(
                factory,
                summary.NormalRelation,
                replacements),
            fresh.MoveToImmutable());
    }

    private static IrVarId CreateFresh(
        IrFactory factory,
        IrVarId template,
        int instanceOrdinal,
        string role,
        int ordinal)
    {
        var type = factory.GetVariableInfo(template).Type;
        return factory.CreateVariable(
            "summary:" +
            instanceOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":" + role + ":" +
            ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            type);
    }

    private static void EnsureType(
        IrFactory factory,
        IrVarId expected,
        IrTerm actual,
        string parameterName)
    {
        if (actual == null ||
            factory.GetVariableInfo(expected).Type != actual.Type)
        {
            throw new ArgumentException(
                "The actual summary input has the wrong IR type.",
                parameterName);
        }
    }
}
