namespace SharpProof.Analyzer.Engine.Rules;

internal static class RuleRegistry
{
    public static ImmutableList<IPurityRule> GetDefaultRules()
    {
        // Group by construct families for clarity of ordering (keep stable behavior)
        return ImmutableList.Create<IPurityRule>(
            // Invocation/Calls (keep primary method invocation rule first)
            new MethodInvocationPurityRule(),
            new DynamicOperationPurityRule(),
            new ConstructorInitializerPurityRule(),
            new DelegateCreationPurityRule(),
            new AwaitPurityRule(),
            new EventReferencePurityRule(),
            new EventAssignmentPurityRule(),

            // Assignments/References
            new DeconstructionAssignmentPurityRule(),
            new AssignmentPurityRule(),
            CreateChildOperationsPureRule(OperationKind.ExpressionStatement),
            CreateAlwaysPureRule(OperationKind.ParameterReference),
            CreateAlwaysPureRule(OperationKind.LocalReference),
            new FieldReferencePurityRule(),
            CreateAlwaysPureRule(OperationKind.InstanceReference),

            // Object/Array creation and initialization
            new ObjectCreationPurityRule(),
            new AnonymousObjectCreationPurityRule(),
            new ObjectOrCollectionInitializerPurityRule(),
            new ArrayCreationPurityRule(),
            CreateChildOperationsPureRule(OperationKind.ArrayInitializer),
            new ArrayElementReferencePurityRule(),
            new InlineArrayAccessPurityRule(),
            new CollectionExpressionPurityRule(),
            new SpreadOperationPurityRule(),

            // Expressions/Operators
            new BinaryOperationPurityRule(),
            new UnaryOperationPurityRule(),
            new CoalesceOperationPurityRule(),
            new ConditionalAccessPurityRule(),
            new ConditionalOperationPurityRule(),
            CreateChildOperationsPureRule(OperationKind.Range),
            new ImplicitIndexerReferencePurityRule(),
            new ConversionPurityRule(),
            CreateChildOperationsPureRule(OperationKind.DeclarationExpression),
            CreateAlwaysPureRule(OperationKind.DefaultValue),
            new InterpolatedStringPurityRule(),
            new PropertyReferencePurityRule(),
            CreateAlwaysPureRule(OperationKind.Literal),
            CreateChildOperationsPureRule(OperationKind.Tuple),
            CreateAlwaysPureRule(OperationKind.TypeOf),
            CreateAlwaysPureRule(OperationKind.NameOf),
            CreateAlwaysPureRule(OperationKind.Utf8String),
            CreateAlwaysPureRule(OperationKind.SizeOf),

            // Patterns
            CreateChildOperationsPureRule(OperationKind.BinaryPattern),
            CreateAlwaysPureRule(OperationKind.ConstantPattern),
            CreateAlwaysPureRule(OperationKind.DeclarationPattern),
            CreateAlwaysPureRule(OperationKind.DiscardPattern),
            CreateChildOperationsPureRule(OperationKind.NegatedPattern),
            new ListPatternPurityRule(),
            CreateChildOperationsPureRule(OperationKind.PropertySubpattern),
            CreateChildOperationsPureRule(OperationKind.RelationalPattern),
            new RecursivePatternPurityRule(),
            CreateChildOperationsPureRule(OperationKind.TypePattern, OperationKind.IsType),
            CreateChildOperationsPureRule(OperationKind.IsPattern),
            new IsNullPurityRule(),
            CreateAlwaysPureRule(
                OperationKind.Block,
                OperationKind.MethodBodyOperation,
                OperationKind.AnonymousFunction,
                OperationKind.FlowAnonymousFunction,
                OperationKind.LocalFunction,
                OperationKind.Try,
                OperationKind.CatchClause,
                OperationKind.VariableDeclarationGroup,
                OperationKind.VariableDeclaration,
                OperationKind.VariableDeclarator,
                OperationKind.VariableInitializer,
                OperationKind.Argument,
                OperationKind.Labeled,
                OperationKind.Empty,
                OperationKind.FieldInitializer,
                OperationKind.PropertyInitializer),

            // Control Flow
            CreateAlwaysPureRule(OperationKind.Branch),
            new SwitchStatementPurityRule(),
            CreateChildOperationsPureRule(OperationKind.CaseClause),
            new SwitchExpressionPurityRule(),
            new LoopPurityRule(),
            new UsingStatementPurityRule(),
            new ThrowOperationPurityRule(),
            new LockStatementPurityRule(),
            CreateChildOperationsPureRule(OperationKind.YieldReturn),

            // Returns
            new ReturnStatementPurityRule(),

            // Misc
            new WithOperationPurityRule()
        );
    }

    private static IPurityRule CreateAlwaysPureRule(params OperationKind[] operationKinds)
    {
        return new DeclarativePureOperationRule(operationKinds);
    }

    private static IPurityRule CreateChildOperationsPureRule(params OperationKind[] operationKinds)
    {
        return new ChildOperationsPurityRule(operationKinds);
    }
}
