namespace SharpProof.Frontend;

internal enum OperationSupportStage
{
    ContractExpressionLowering,
    EffectDiscovery
}

/// <summary>
/// Closed Roslyn operation support decisions shared by compiler-facing stages.
/// Shape and type checks remain with the stage that owns their semantics.
/// </summary>
internal static class OperationSupportCatalog
{
    internal static bool IsSupported(
        OperationSupportStage stage,
        OperationKind kind)
    {
        if (!Enum.IsDefined(typeof(OperationKind), kind))
        {
            return false;
        }

        return stage switch
        {
            OperationSupportStage.ContractExpressionLowering =>
                SupportsContractExpression(kind),
            OperationSupportStage.EffectDiscovery =>
                SupportsEffectDiscovery(kind),
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown operation support stage.")
        };
    }

    private static bool SupportsContractExpression(OperationKind kind)
    {
        return kind is
            OperationKind.Literal or
            OperationKind.LocalReference or
            OperationKind.ParameterReference or
            OperationKind.InstanceReference or
            OperationKind.DefaultValue or
            OperationKind.UnaryOperator or
            OperationKind.BinaryOperator or
            OperationKind.Conversion or
            OperationKind.Conditional or
            OperationKind.IsNull or
            OperationKind.PropertyReference or
            OperationKind.ArrayElementReference;
    }

    private static bool SupportsEffectDiscovery(OperationKind kind)
    {
        return kind is
            OperationKind.Block or
            OperationKind.VariableDeclarationGroup or
            OperationKind.Switch or
            OperationKind.Loop or
            OperationKind.Labeled or
            OperationKind.Branch or
            OperationKind.Empty or
            OperationKind.Return or
            OperationKind.Lock or
            OperationKind.Try or
            OperationKind.Using or
            OperationKind.ExpressionStatement or
            OperationKind.Literal or
            OperationKind.Conversion or
            OperationKind.Invocation or
            OperationKind.ArrayElementReference or
            OperationKind.LocalReference or
            OperationKind.ParameterReference or
            OperationKind.FieldReference or
            OperationKind.PropertyReference or
            OperationKind.Unary or
            OperationKind.Binary or
            OperationKind.Conditional or
            OperationKind.Coalesce or
            OperationKind.ObjectCreation or
            OperationKind.ArrayCreation or
            OperationKind.InstanceReference or
            OperationKind.IsType or
            OperationKind.SimpleAssignment or
            OperationKind.CompoundAssignment or
            OperationKind.Parenthesized or
            OperationKind.ConditionalAccess or
            OperationKind.ConditionalAccessInstance or
            OperationKind.InterpolatedString or
            OperationKind.ObjectOrCollectionInitializer or
            OperationKind.MemberInitializer or
            OperationKind.NameOf or
            OperationKind.DefaultValue or
            OperationKind.TypeOf or
            OperationKind.Increment or
            OperationKind.Throw or
            OperationKind.Decrement or
            OperationKind.FieldInitializer or
            OperationKind.VariableInitializer or
            OperationKind.PropertyInitializer or
            OperationKind.ParameterInitializer or
            OperationKind.ArrayInitializer or
            OperationKind.VariableDeclarator or
            OperationKind.VariableDeclaration or
            OperationKind.Argument or
            OperationKind.CatchClause or
            OperationKind.SwitchCase or
            OperationKind.CaseClause or
            OperationKind.InterpolatedStringText or
            OperationKind.Interpolation or
            OperationKind.MethodBodyOperation or
            OperationKind.ConstructorBodyOperation or
            OperationKind.Discard or
            OperationKind.FlowCapture or
            OperationKind.FlowCaptureReference or
            OperationKind.IsNull or
            OperationKind.CaughtException or
            OperationKind.CoalesceAssignment or
            OperationKind.UsingDeclaration or
            OperationKind.Attribute;
    }
}
