namespace SharpProof.Frontend;

internal enum OperationSupportStage { ContractExpressionLowering, EffectDiscovery }

internal readonly struct CSharpOperationDecision(TotalOperationAdmission admission,
    bool contractExpression = false, bool effectDiscovery = false)
{
    internal TotalOperationAdmission Admission { get; } = admission;
    internal bool ContractExpression { get; } = contractExpression;
    internal bool EffectDiscovery { get; } = effectDiscovery;
}

internal static partial class CSharpOperationSemantics
{
    // Frozen decisions for the distinct Roslyn 4.14 operation values. Aliases share a row.
    internal static readonly ImmutableDictionary<OperationKind, CSharpOperationDecision> Operations =
        CreateOperations();

    private static ImmutableDictionary<OperationKind, CSharpOperationDecision> CreateOperations()
    {
        var operations = ImmutableDictionary.CreateBuilder<OperationKind, CSharpOperationDecision>();
        operations[OperationKind.None] = new(TotalOperationAdmission.Invalid);
        operations[OperationKind.Invalid] = new(TotalOperationAdmission.Invalid);
        operations[OperationKind.Block] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.VariableDeclarationGroup] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.Switch] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.Loop] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.Labeled] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.Branch] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.Empty] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.Return] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.YieldBreak] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.Lock] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.Try] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.Using] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.YieldReturn] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.ExpressionStatement] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.LocalFunction] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.Stop] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.End] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.RaiseEvent] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.Literal] = new(TotalOperationAdmission.Scalar, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.Conversion] = new(TotalOperationAdmission.Scalar, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.Invocation] = new(TotalOperationAdmission.Intrinsic, effectDiscovery: true);
        operations[OperationKind.ArrayElementReference] = new(TotalOperationAdmission.Scalar, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.LocalReference] = new(TotalOperationAdmission.Scalar, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.ParameterReference] = new(TotalOperationAdmission.Scalar, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.FieldReference] = new(TotalOperationAdmission.Scalar, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.MethodReference] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.PropertyReference] = new(TotalOperationAdmission.Incomplete, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.EventReference] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.Unary] = new(TotalOperationAdmission.Scalar, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.Binary] = new(TotalOperationAdmission.Scalar, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.Conditional] = new(TotalOperationAdmission.Scalar, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.Coalesce] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.AnonymousFunction] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.ObjectCreation] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.TypeParameterObjectCreation] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.ArrayCreation] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.InstanceReference] = new(TotalOperationAdmission.Incomplete, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.IsType] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.Await] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.SimpleAssignment] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.CompoundAssignment] = new(TotalOperationAdmission.Scalar, effectDiscovery: true);
        operations[OperationKind.Parenthesized] = new(TotalOperationAdmission.Scalar, effectDiscovery: true);
        operations[OperationKind.EventAssignment] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.ConditionalAccess] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.ConditionalAccessInstance] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.InterpolatedString] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.AnonymousObjectCreation] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.ObjectOrCollectionInitializer] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.MemberInitializer] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[(OperationKind)52] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.NameOf] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.Tuple] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.DynamicObjectCreation] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.DynamicMemberReference] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.DynamicInvocation] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.DynamicIndexerAccess] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.TranslatedQuery] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.DelegateCreation] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.DefaultValue] = new(TotalOperationAdmission.Scalar, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.TypeOf] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.SizeOf] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.AddressOf] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.IsPattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.Increment] = new(TotalOperationAdmission.Scalar, effectDiscovery: true);
        operations[OperationKind.Throw] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.Decrement] = new(TotalOperationAdmission.Scalar, effectDiscovery: true);
        operations[OperationKind.DeconstructionAssignment] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.DeclarationExpression] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.OmittedArgument] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.FieldInitializer] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.VariableInitializer] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.PropertyInitializer] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.ParameterInitializer] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.ArrayInitializer] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.VariableDeclarator] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.VariableDeclaration] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.Argument] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.CatchClause] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.SwitchCase] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.CaseClause] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.InterpolatedStringText] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.Interpolation] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.ConstantPattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.DeclarationPattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.TupleBinary] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.MethodBodyOperation] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.ConstructorBodyOperation] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.Discard] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.FlowCapture] = new(TotalOperationAdmission.Structure, effectDiscovery: true);
        operations[OperationKind.FlowCaptureReference] = new(TotalOperationAdmission.Scalar, effectDiscovery: true);
        operations[OperationKind.IsNull] = new(TotalOperationAdmission.Incomplete, contractExpression: true, effectDiscovery: true);
        operations[OperationKind.CaughtException] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.StaticLocalInitializationSemaphore] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.FlowAnonymousFunction] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.CoalesceAssignment] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.Range] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.ReDim] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.ReDimClause] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.RecursivePattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.DiscardPattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.SwitchExpression] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.SwitchExpressionArm] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.PropertySubpattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.UsingDeclaration] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.NegatedPattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.BinaryPattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.TypePattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.RelationalPattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.With] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.InterpolatedStringHandlerCreation] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.InterpolatedStringAddition] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.InterpolatedStringAppendLiteral] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.InterpolatedStringAppendFormatted] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.InterpolatedStringAppendInvalid] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.InterpolatedStringHandlerArgumentPlaceholder] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.FunctionPointerInvocation] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.ListPattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.SlicePattern] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.ImplicitIndexerReference] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.Utf8String] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.Attribute] = new(TotalOperationAdmission.Incomplete, effectDiscovery: true);
        operations[OperationKind.InlineArrayAccess] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.CollectionExpression] = new(TotalOperationAdmission.Incomplete);
        operations[OperationKind.Spread] = new(TotalOperationAdmission.Incomplete);
        return operations.ToImmutable();
    }
    internal static bool IsSupported(OperationSupportStage stage, OperationKind kind)
    {
        Operations.TryGetValue(kind, out var decision);
        return stage switch
        {
            OperationSupportStage.ContractExpressionLowering => decision.ContractExpression,
            OperationSupportStage.EffectDiscovery => decision.EffectDiscovery,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown operation support stage.")
        };
    }
}
