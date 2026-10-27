using System.Text;

namespace SharpProof.Contracts;

// A canonical structural rendering of a contract condition. Parameters are
// identified by ordinal, so companion and override conditions written over
// differently named parameters compare equal when their operations match.
internal static class ContractStructuralKey
{
    internal static string Create(IOperation operation)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        var builder = new StringBuilder();
        Append(builder, operation);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, IOperation operation)
    {
        builder = ArgumentNullGuard.NotNull(builder, nameof(builder));
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        builder.Append('(').Append(operation.Kind);
        switch (operation)
        {
            case IParameterReferenceOperation parameter:
                builder.Append(" p").Append(parameter.Parameter.Ordinal);
                break;
            case ILocalReferenceOperation local:
                builder.Append(" l:").Append(local.Local.Name);
                break;
            case IBinaryOperation binary:
                builder.Append(' ').Append(binary.OperatorKind).Append(binary.IsChecked ? " checked" : string.Empty);
                break;
            case IUnaryOperation unary:
                builder.Append(' ').Append(unary.OperatorKind).Append(unary.IsChecked ? " checked" : string.Empty);
                break;
            case IMemberReferenceOperation member:
                builder.Append(' ').Append(member.Member.OriginalDefinition.GetDocumentationCommentId());
                break;
            case IInvocationOperation invocation:
                builder.Append(' ').Append(invocation.TargetMethod.OriginalDefinition.GetDocumentationCommentId());
                break;
            case IConversionOperation conversion:
                builder.Append(' ').Append(conversion.Type?.GetDocumentationCommentId())
                    .Append(conversion.IsChecked ? " checked" : string.Empty);
                break;
        }
        if (operation.ConstantValue is { HasValue: true } constant)
        {
            builder.Append(" =").Append(operation.Type?.SpecialType).Append(':')
                .Append(Convert.ToString(constant.Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        foreach (var child in operation.ChildOperations)
        {
            Append(builder, child);
        }
        builder.Append(')');
    }
}
