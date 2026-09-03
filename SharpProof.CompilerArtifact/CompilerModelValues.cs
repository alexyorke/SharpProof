using System.Globalization;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerModelValues
{
    internal static bool TryCreateValue(
        IrFactory factory,
        CompilerCanonicalVariable variable,
        WorkerModelValue row,
        out IrValue value)
    {
        var type = factory.GetVariableInfo(variable.Variable).Type;
        if (type == factory.BooleanType &&
            row is { Kind: nameof(IrValueKind.Boolean), Value: "true" or "false" })
        {
            value = factory.CreateBooleanValue(row.Value == "true");
            return true;
        }

        if (type == factory.IntegerType &&
            row is { Kind: nameof(IrValueKind.Integer) } &&
            long.TryParse(
                row.Value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var integer) &&
            row.Value == integer.ToString(CultureInfo.InvariantCulture) &&
            (variable.SourceIntegerInterval is not { } interval ||
             integer >= interval.Minimum && integer <= interval.Maximum))
        {
            value = factory.CreateIntegerValue(integer);
            return true;
        }

        value = null!;
        return false;
    }

    internal static bool EntryAssumptionsHold(
        CompilerCallablePreparation target,
        IReadOnlyDictionary<IrVarId, IrValue> model,
        CancellationToken cancellationToken)
    {
        var interpreter = new IrInterpreter(target.Factory);
        foreach (var clause in target.Clauses.Where(static clause =>
                     clause.Kind is CompilerContractKind.Requires or
                         CompilerContractKind.Assume))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evaluated = interpreter.Evaluate(
                clause.Condition,
                model,
                cancellationToken);
            if (evaluated.Status != IrEvaluationStatus.Value ||
                evaluated.Value is not
                { Kind: IrValueKind.Boolean, Boolean: true })
            {
                return false;
            }
        }

        return true;
    }
}
