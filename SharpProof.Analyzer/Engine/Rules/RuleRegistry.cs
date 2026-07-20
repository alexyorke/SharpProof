namespace SharpProof.Analyzer.Engine.Rules;

internal static class RuleRegistry {
    internal static ImmutableDictionary<OperationKind, PurityRuleHandler> GetDefaultRules() {
        var rules = ImmutableDictionary.CreateBuilder<OperationKind, PurityRuleHandler>();

        // Registration order preserves the former first-rule-wins behavior.
        Add(rules, new MethodInvocationPurityRule().CheckPurity, OperationKind.Invocation);
        Add(rules, new DynamicOperationPurityRule().CheckPurity,
            OperationKind.DynamicInvocation, OperationKind.DynamicMemberReference,
            OperationKind.DynamicObjectCreation, OperationKind.DynamicIndexerAccess);
        Add(rules, new ConstructorInitializerPurityRule().CheckPurity, OperationKind.ConstructorBodyOperation);
        Add(rules, new DelegateCreationPurityRule().CheckPurity, OperationKind.DelegateCreation);
        AddTyped<IAwaitOperation>(rules, AwaitPurityRule.CheckTyped, OperationKind.Await);
        AddTyped<IEventReferenceOperation>(rules, CoreOperationPurityRules.CheckEventReference,
            OperationKind.EventReference);
        AddTyped<IEventAssignmentOperation>(rules, CoreOperationPurityRules.CheckEventAssignment,
            OperationKind.EventAssignment);

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
        AddTyped<IAnonymousObjectCreationOperation>(rules, AnonymousObjectCreationPurityRule.CheckTyped,
            OperationKind.AnonymousObjectCreation);
        AddTyped<IObjectOrCollectionInitializerOperation>(rules,
            CoreOperationPurityRules.CheckObjectOrCollectionInitializer,
            OperationKind.ObjectOrCollectionInitializer);
        AddTyped<IArrayCreationOperation>(rules, CoreOperationPurityRules.CheckArrayCreation,
            OperationKind.ArrayCreation);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.ArrayInitializer);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.ArrayElementReference);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.InlineArrayAccess);
        AddTyped<ICollectionExpressionOperation>(rules, CoreOperationPurityRules.CheckCollectionExpression,
            OperationKind.CollectionExpression);
        AddTyped<ISpreadOperation>(rules, CoreOperationPurityRules.CheckSpread, OperationKind.Spread);

        AddTyped<IBinaryOperation>(rules, BinaryOperationPurityRule.CheckTyped, OperationKind.Binary);
        AddTyped<IUnaryOperation>(rules, CoreOperationPurityRules.CheckUnary, OperationKind.Unary);
        AddTyped<ICoalesceOperation>(rules, CoreOperationPurityRules.CheckCoalesce, OperationKind.Coalesce);
        AddTyped<IConditionalAccessOperation>(rules, CoreOperationPurityRules.CheckConditionalAccess,
            OperationKind.ConditionalAccess);
        AddTyped<IConditionalOperation>(rules, ConditionalOperationPurityRule.CheckTyped,
            OperationKind.Conditional);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.Range);
        AddTyped<IImplicitIndexerReferenceOperation>(rules, ImplicitIndexerReferencePurityRule.CheckTyped,
            OperationKind.ImplicitIndexerReference);
        AddTyped<IConversionOperation>(rules, ConversionPurityRule.CheckTyped, OperationKind.Conversion);
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
        AddTyped<IRecursivePatternOperation>(rules, CoreOperationPurityRules.CheckRecursivePattern,
            OperationKind.RecursivePattern);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure,
            OperationKind.TypePattern, OperationKind.IsType, OperationKind.IsPattern);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.IsNull);
        Add(rules, AlwaysPure,
            OperationKind.Block, OperationKind.MethodBodyOperation, OperationKind.AnonymousFunction,
            OperationKind.FlowAnonymousFunction, OperationKind.LocalFunction, OperationKind.Try,
            OperationKind.CatchClause, OperationKind.VariableDeclarationGroup, OperationKind.VariableDeclaration,
            OperationKind.VariableDeclarator, OperationKind.VariableInitializer, OperationKind.Argument,
            OperationKind.Labeled, OperationKind.Empty, OperationKind.FieldInitializer,
            OperationKind.PropertyInitializer);

        Add(rules, AlwaysPure, OperationKind.Branch);
        AddTyped<ISwitchOperation>(rules, CoreOperationPurityRules.CheckSwitchStatement, OperationKind.Switch);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.CaseClause);
        AddTyped<ISwitchExpressionOperation>(rules, CoreOperationPurityRules.CheckSwitchExpression,
            OperationKind.SwitchExpression);
        Add(rules, new LoopPurityRule().CheckPurity, OperationKind.Loop);
        Add(rules, new UsingStatementPurityRule().CheckPurity,
            OperationKind.Using, OperationKind.UsingDeclaration);
        Add(rules, new ThrowOperationPurityRule().CheckPurity, OperationKind.Throw);
        AddTyped<ILockOperation>(rules, CoreOperationPurityRules.CheckLock, OperationKind.Lock);
        Add(rules, ChildOperationsPurityRule.CheckChildOperationsArePure, OperationKind.YieldReturn);
        Add(rules, new ReturnStatementPurityRule().CheckPurity, OperationKind.Return);
        AddTyped<IWithOperation>(rules, CoreOperationPurityRules.CheckWith, OperationKind.With);

        return rules.ToImmutable();
    }

    private static void Add(
        ImmutableDictionary<OperationKind, PurityRuleHandler>.Builder rules,
        PurityRuleHandler handler,
        params OperationKind[] operationKinds) {
        foreach (var kind in operationKinds) {
            if (!rules.ContainsKey(kind)) rules.Add(kind, handler);
        }
    }

    private static void AddTyped<TOperation>(
        ImmutableDictionary<OperationKind, PurityRuleHandler>.Builder rules,
        Func<TOperation, PurityAnalysisContext, PurityAnalysisEngine.PurityAnalysisState,
            PurityAnalysisEngine.PurityAnalysisResult> handler,
        params OperationKind[] operationKinds)
        where TOperation : class, IOperation =>
        Add(
            rules,
            (operation, context, state) => operation is TOperation typed
                ? handler(typed, context, state)
                : PurityAnalysisEngine.PurityAnalysisResult.Pure,
            operationKinds);

    private static PurityAnalysisEngine.PurityAnalysisResult AlwaysPure(
        IOperation _,
        PurityAnalysisContext __,
        PurityAnalysisEngine.PurityAnalysisState ___) =>
        PurityAnalysisEngine.PurityAnalysisResult.Pure;
}
