namespace SharpProof.Analyzer.Engine.Rules;

internal static class RuleRegistry
{
    internal static ImmutableDictionary<OperationKind, PurityRuleHandler> GetDefaultRules()
    {
        var rules = ImmutableDictionary.CreateBuilder<OperationKind, PurityRuleHandler>();

        // Registration order preserves the former first-rule-wins behavior.
        Add(rules, new MethodInvocationPurityRule().CheckPurity, OperationKind.Invocation);
        Add(rules, new DynamicOperationPurityRule().CheckPurity,
            OperationKind.DynamicInvocation, OperationKind.DynamicMemberReference,
            OperationKind.DynamicObjectCreation, OperationKind.DynamicIndexerAccess);
        Add(rules, new ConstructorInitializerPurityRule().CheckPurity, OperationKind.ConstructorBodyOperation);
        Add(rules, new DelegateCreationPurityRule().CheckPurity, OperationKind.DelegateCreation);
        Add(rules, new AwaitPurityRule().CheckPurity, OperationKind.Await);
        Add(rules, new EventReferencePurityRule().CheckPurity, OperationKind.EventReference);
        Add(rules, new EventAssignmentPurityRule().CheckPurity, OperationKind.EventAssignment);

        Add(rules, new DeconstructionAssignmentPurityRule().CheckPurity, OperationKind.DeconstructionAssignment);
        Add(rules, new AssignmentPurityRule().CheckPurity,
            OperationKind.SimpleAssignment, OperationKind.CompoundAssignment, OperationKind.CoalesceAssignment,
            OperationKind.Increment, OperationKind.Decrement);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.ExpressionStatement);
        Add(rules, AlwaysPure, OperationKind.ParameterReference, OperationKind.LocalReference);
        Add(rules, new FieldReferencePurityRule().CheckPurity, OperationKind.FieldReference);
        Add(rules, AlwaysPure, OperationKind.InstanceReference);

        Add(rules, new ObjectCreationPurityRule().CheckPurity,
            OperationKind.ObjectCreation, OperationKind.TypeParameterObjectCreation);
        Add(rules, new AnonymousObjectCreationPurityRule().CheckPurity, OperationKind.AnonymousObjectCreation);
        Add(rules, new ObjectOrCollectionInitializerPurityRule().CheckPurity,
            OperationKind.ObjectOrCollectionInitializer);
        Add(rules, new ArrayCreationPurityRule().CheckPurity, OperationKind.ArrayCreation);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.ArrayInitializer);
        Add(rules, new ArrayElementReferencePurityRule().CheckPurity, OperationKind.ArrayElementReference);
        Add(rules, new InlineArrayAccessPurityRule().CheckPurity, OperationKind.InlineArrayAccess);
        Add(rules, new CollectionExpressionPurityRule().CheckPurity, OperationKind.CollectionExpression);
        Add(rules, new SpreadOperationPurityRule().CheckPurity, OperationKind.Spread);

        Add(rules, new BinaryOperationPurityRule().CheckPurity, OperationKind.Binary);
        Add(rules, new UnaryOperationPurityRule().CheckPurity, OperationKind.Unary);
        Add(rules, new CoalesceOperationPurityRule().CheckPurity, OperationKind.Coalesce);
        Add(rules, new ConditionalAccessPurityRule().CheckPurity, OperationKind.ConditionalAccess);
        Add(rules, new ConditionalOperationPurityRule().CheckPurity, OperationKind.Conditional);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.Range);
        Add(rules, new ImplicitIndexerReferencePurityRule().CheckPurity, OperationKind.ImplicitIndexerReference);
        Add(rules, new ConversionPurityRule().CheckPurity, OperationKind.Conversion);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.DeclarationExpression);
        Add(rules, AlwaysPure, OperationKind.DefaultValue);
        Add(rules, new InterpolatedStringPurityRule().CheckPurity,
            OperationKind.InterpolatedString, OperationKind.InterpolatedStringHandlerCreation,
            OperationKind.InterpolatedStringHandlerArgumentPlaceholder);
        Add(rules, new PropertyReferencePurityRule().CheckPurity, OperationKind.PropertyReference);
        Add(rules, AlwaysPure,
            OperationKind.Literal, OperationKind.TypeOf, OperationKind.NameOf,
            OperationKind.Utf8String, OperationKind.SizeOf);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.Tuple);

        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.BinaryPattern);
        Add(rules, AlwaysPure,
            OperationKind.ConstantPattern, OperationKind.DeclarationPattern, OperationKind.DiscardPattern);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure,
            OperationKind.NegatedPattern, OperationKind.PropertySubpattern, OperationKind.RelationalPattern);
        Add(rules, new ListPatternPurityRule().CheckPurity, OperationKind.ListPattern);
        Add(rules, new RecursivePatternPurityRule().CheckPurity, OperationKind.RecursivePattern);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure,
            OperationKind.TypePattern, OperationKind.IsType, OperationKind.IsPattern);
        Add(rules, new IsNullPurityRule().CheckPurity, OperationKind.IsNull);
        Add(rules, AlwaysPure,
            OperationKind.Block, OperationKind.MethodBodyOperation, OperationKind.AnonymousFunction,
            OperationKind.FlowAnonymousFunction, OperationKind.LocalFunction, OperationKind.Try,
            OperationKind.CatchClause, OperationKind.VariableDeclarationGroup, OperationKind.VariableDeclaration,
            OperationKind.VariableDeclarator, OperationKind.VariableInitializer, OperationKind.Argument,
            OperationKind.Labeled, OperationKind.Empty, OperationKind.FieldInitializer,
            OperationKind.PropertyInitializer);

        Add(rules, AlwaysPure, OperationKind.Branch);
        Add(rules, new SwitchStatementPurityRule().CheckPurity, OperationKind.Switch);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.CaseClause);
        Add(rules, new SwitchExpressionPurityRule().CheckPurity, OperationKind.SwitchExpression);
        Add(rules, new LoopPurityRule().CheckPurity, OperationKind.Loop);
        Add(rules, new UsingStatementPurityRule().CheckPurity,
            OperationKind.Using, OperationKind.UsingDeclaration);
        Add(rules, new ThrowOperationPurityRule().CheckPurity, OperationKind.Throw);
        Add(rules, new LockStatementPurityRule().CheckPurity, OperationKind.Lock);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.YieldReturn);
        Add(rules, new ReturnStatementPurityRule().CheckPurity, OperationKind.Return);
        Add(rules, new WithOperationPurityRule().CheckPurity, OperationKind.With);

        return rules.ToImmutable();
    }

    private static void Add(
        ImmutableDictionary<OperationKind, PurityRuleHandler>.Builder rules,
        PurityRuleHandler handler,
        params OperationKind[] operationKinds)
    {
        foreach (var kind in operationKinds)
        {
            if (!rules.ContainsKey(kind)) rules.Add(kind, handler);
        }
    }

    private static PurityAnalysisEngine.PurityAnalysisResult AlwaysPure(
        IOperation _,
        PurityAnalysisContext __,
        PurityAnalysisEngine.PurityAnalysisState ___) =>
        PurityAnalysisEngine.PurityAnalysisResult.Pure;
}
