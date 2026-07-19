namespace SharpProof.Analyzer.Engine.Rules;

internal static class OwnedFreshMutableObjectClassifier
{

    internal static bool IsOwnedFreshMutableObjectReference(
        IOperation? operation,
        SyntaxNode observationSyntax,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState? currentState)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (operation is IConversionOperation conversionOperation && conversionOperation.Operand != null)
            return IsOwnedFreshMutableObjectReference(conversionOperation.Operand, observationSyntax, context,
                currentState);

        if (operation is ILocalReferenceOperation localReference)
            return IsOwnedFreshMutableLocal(
                localReference.Local,
                observationSyntax,
                context.SemanticModel,
                currentState,
                new HashSet<ILocalSymbol>(SymbolEq.Default),
                context.CancellationToken);

        return (operation is IFieldReferenceOperation fieldReference &&
                fieldReference.Field.IsReadOnly &&
                IsOwnedFreshMutableReadonlyFieldReference(fieldReference, observationSyntax, context.SemanticModel,
                    context.CancellationToken)) ||
               (operation is IPropertyReferenceOperation propertyReference &&
                IsOwnedFreshMutableStablePropertyReference(propertyReference, observationSyntax, context.SemanticModel,
                    context.CancellationToken));
    }

    internal static bool IsOwnedFreshMutableLocal(
        ILocalSymbol localSymbol,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        PurityAnalysisEngine.PurityAnalysisState? currentState,
        CancellationToken cancellationToken)
    {
        return IsOwnedFreshMutableLocal(
            localSymbol,
            observationSyntax,
            semanticModel,
            currentState,
            new HashSet<ILocalSymbol>(SymbolEq.Default),
            cancellationToken);
    }

    internal static bool IsOwnedFreshMutableReadonlyFieldReference(
        IFieldReferenceOperation fieldReferenceOperation,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return IsOwnedFreshMutableStableMemberReference(
            fieldReferenceOperation.Instance,
            fieldReferenceOperation.Field,
            observationSyntax,
            semanticModel,
            cancellationToken);
    }

    private static bool IsOwnedFreshMutableStablePropertyReference(
        IPropertyReferenceOperation propertyReferenceOperation,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (propertyReferenceOperation.Property.SetMethod != null &&
            !propertyReferenceOperation.Property.SetMethod.IsInitOnly)
            return false;

        return IsOwnedFreshMutableStableMemberReference(
            propertyReferenceOperation.Instance,
            propertyReferenceOperation.Property,
            observationSyntax,
            semanticModel,
            cancellationToken);
    }

    private static bool IsOwnedFreshMutableStableMemberReference(
        IOperation? instance,
        ISymbol member,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!TryGetStableAssignedValue(
                instance,
                member,
                observationSyntax,
                semanticModel,
                new HashSet<ILocalSymbol>(SymbolEq.Default),
                cancellationToken,
                out var valueOperation))
            return false;

        return HasStableFreshMutableObjectValueInOperation(
            valueOperation,
            observationSyntax,
            semanticModel,
            new HashSet<ILocalSymbol>(SymbolEq.Default),
            cancellationToken);
    }

    private static bool TryGetStableAssignedValue(
        IFieldReferenceOperation fieldReferenceOperation,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken,
        out IOperation valueOperation)
    {
        return TryGetStableAssignedValue(
            fieldReferenceOperation.Instance,
            fieldReferenceOperation.Field,
            observationSyntax,
            semanticModel,
            visitedLocals,
            cancellationToken,
            out valueOperation);
    }

    private static bool TryGetStableAssignedValue(
        IPropertyReferenceOperation propertyReferenceOperation,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken,
        out IOperation valueOperation)
    {
        return TryGetStableAssignedValue(
            propertyReferenceOperation.Instance,
            propertyReferenceOperation.Property,
            observationSyntax,
            semanticModel,
            visitedLocals,
            cancellationToken,
            out valueOperation);
    }

    private static bool TryGetStableAssignedValue(
        IOperation? instance,
        ISymbol member,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken,
        out IOperation valueOperation)
    {
        if (!TryResolveStableObjectCreationInitializer(
                instance,
                observationSyntax,
                semanticModel,
                visitedLocals,
                cancellationToken,
                out var objectCreationOperation))
        {
            valueOperation = null!;
            return false;
        }

        foreach (var assignment in objectCreationOperation.DescendantsAndSelf().OfType<ISimpleAssignmentOperation>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SymbolEq.AreEqual(GetReferencedMemberSymbol(assignment.Target), member))
                continue;

            valueOperation = assignment.Value;
            return true;
        }

        if (objectCreationOperation.Constructor != null)
            foreach (var argument in objectCreationOperation.Arguments)
            {
                var parameter = argument.Parameter;
                if (parameter != null &&
                    RuleAnalysisHelper.ConstructorStoresParameterMatching(
                        objectCreationOperation.Constructor,
                        parameter,
                        semanticModel,
                        cancellationToken,
                        target => IsThisInstanceMemberReference(target, member)))
                {
                    valueOperation = argument.Value;
                    return true;
                }
            }

        valueOperation = null!;
        return false;
    }

    private static bool TryResolveStableObjectCreationInitializer(
        IOperation? operation,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken,
        out IObjectCreationOperation objectCreationOperation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        switch (unwrappedOperation)
        {
            case IObjectCreationOperation directObjectCreation:
                objectCreationOperation = directObjectCreation;
                return true;

            case ILocalReferenceOperation localReference:
                return TryGetStableLocalObjectCreationInitializer(
                    localReference.Local,
                    observationSyntax,
                    semanticModel,
                    visitedLocals,
                    cancellationToken,
                    out objectCreationOperation);

            case IInvocationOperation invocationOperation
                when PurityAnalysisEngine.TryGetSingleReturnedValueFromInvocation(
                    invocationOperation,
                    semanticModel,
                    out var returnedOperation,
                    out _,
                    out var returnedSemanticModel,
                    cancellationToken):
                return TryResolveStableObjectCreationInitializer(
                    returnedOperation,
                    observationSyntax,
                    returnedSemanticModel,
                    visitedLocals,
                    cancellationToken,
                    out objectCreationOperation);

            case IFieldReferenceOperation fieldReference when fieldReference.Field.IsReadOnly &&
                                                              TryGetStableAssignedValue(fieldReference,
                                                                  observationSyntax, semanticModel, visitedLocals,
                                                                  cancellationToken, out var fieldValue):
                return TryResolveStableObjectCreationInitializer(fieldValue, observationSyntax, semanticModel,
                    visitedLocals, cancellationToken, out objectCreationOperation);

            case IPropertyReferenceOperation propertyReference
                when (propertyReference.Property.SetMethod == null ||
                      propertyReference.Property.SetMethod.IsInitOnly) &&
                     TryGetStableAssignedValue(propertyReference, observationSyntax, semanticModel, visitedLocals,
                         cancellationToken, out var propertyValue):
                return TryResolveStableObjectCreationInitializer(propertyValue, observationSyntax, semanticModel,
                    visitedLocals, cancellationToken, out objectCreationOperation);

            default:
                objectCreationOperation = null!;
                return false;
        }
    }

    private static bool TryGetStableLocalObjectCreationInitializer(
        ILocalSymbol localSymbol,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken,
        out IObjectCreationOperation objectCreationOperation)
    {
        if (!RuleAnalysisHelper.TryGetStableLocalInitializer(
                localSymbol,
                observationSyntax,
                semanticModel,
                visitedLocals,
                cancellationToken,
                out var initializerSyntax,
                out var initializerOperation))
        {
            objectCreationOperation = null!;
            return false;
        }

        if (initializerOperation is IObjectCreationOperation directObjectCreation)
        {
            objectCreationOperation = directObjectCreation;
            return true;
        }

        if (initializerOperation is ILocalReferenceOperation localReference)
            return TryGetStableLocalObjectCreationInitializer(
                localReference.Local,
                initializerSyntax,
                semanticModel,
                visitedLocals,
                cancellationToken,
                out objectCreationOperation);

        if (initializerOperation is IInvocationOperation invocationOperation &&
            PurityAnalysisEngine.TryGetSingleReturnedValueFromInvocation(
                invocationOperation,
                semanticModel,
                out var returnedOperation,
                out _,
                out var returnedSemanticModel,
                cancellationToken))
            return TryResolveStableObjectCreationInitializer(
                returnedOperation,
                initializerSyntax,
                returnedSemanticModel,
                visitedLocals,
                cancellationToken,
                out objectCreationOperation);

        objectCreationOperation = null!;
        return false;
    }

    private static bool IsThisInstanceMemberReference(IOperation operation, ISymbol memberSymbol)
    {
        var (targetMember, targetInstance) = operation switch
        {
            IFieldReferenceOperation fieldReference => ((ISymbol?)fieldReference.Field, fieldReference.Instance),
            IPropertyReferenceOperation propertyReference => (propertyReference.Property, propertyReference.Instance),
            _ => (null, null)
        };

        return targetMember != null &&
               SymbolEq.AreEqual(targetMember, memberSymbol) &&
               RuleAnalysisHelper.IsThisOrImplicitInstance(targetInstance);
    }

    private static ISymbol? GetReferencedMemberSymbol(IOperation? operation)
    {
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        return unwrappedOperation switch
        {
            IFieldReferenceOperation fieldReference => fieldReference.Field,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            _ => null
        };
    }

    private static bool HasStableFreshMutableObjectValue(
        ILocalSymbol localSymbol,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken)
    {
        if (!RuleAnalysisHelper.TryGetStableLocalInitializer(
                localSymbol,
                observationSyntax,
                semanticModel,
                visitedLocals,
                cancellationToken,
                out var initializerSyntax,
                out var initializerOperation))
            return false;

        if (initializerOperation is IObjectCreationOperation objectCreationOperation &&
            RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
            return true;

        if (initializerOperation is ILocalReferenceOperation localReference)
            return IsOwnedFreshMutableLocal(localReference.Local, initializerSyntax, semanticModel, null, visitedLocals,
                cancellationToken);

        if (initializerOperation is IConditionalOperation conditionalOperation)
        {
            if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
            {
                var selectedBranch = conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse;
                if (selectedBranch == null) return false;

                return HasStableFreshMutableObjectValueInOperation(
                    selectedBranch,
                    initializerSyntax,
                    semanticModel,
                    visitedLocals,
                    cancellationToken);
            }

            return (conditionalOperation.WhenTrue != null &&
                    HasStableFreshMutableObjectValueInOperation(conditionalOperation.WhenTrue, initializerSyntax,
                        semanticModel, visitedLocals, cancellationToken)) ||
                   (conditionalOperation.WhenFalse != null &&
                    HasStableFreshMutableObjectValueInOperation(conditionalOperation.WhenFalse, initializerSyntax,
                        semanticModel, visitedLocals, cancellationToken));
        }

        if (initializerOperation is ICoalesceOperation coalesceOperation)
            return HasStableFreshMutableObjectValueInOperation(coalesceOperation.Value, initializerSyntax,
                       semanticModel, visitedLocals, cancellationToken) ||
                   HasStableFreshMutableObjectValueInOperation(coalesceOperation.WhenNull, initializerSyntax,
                       semanticModel, visitedLocals, cancellationToken);

        return initializerOperation != null &&
               HasStableFreshMutableObjectValueInOperation(
                   initializerOperation,
                   initializerSyntax,
                   semanticModel,
                   visitedLocals,
                   cancellationToken);
    }

    private static bool IsOwnedFreshMutableLocal(
        ILocalSymbol localSymbol,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        PurityAnalysisEngine.PurityAnalysisState? currentState,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (currentState is { } state &&
            RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(localSymbol.Type) &&
            PuritySymbolicStateFacts.HasSymbolicOwnedFactForSymbol(localSymbol, state))
            return true;

        return HasStableFreshMutableObjectValue(
                   localSymbol,
                   observationSyntax,
                   semanticModel,
                   visitedLocals,
                   cancellationToken) ||
               IsAssignedFreshMutableObjectOnAllPaths(
                   localSymbol,
                   observationSyntax,
                   semanticModel,
                   visitedLocals,
                   cancellationToken);
    }

    private static bool IsAssignedFreshMutableObjectOnAllPaths(
        ILocalSymbol localSymbol,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault();
        var containingBlock = declaratorSyntax?.FirstAncestorOrSelf<BlockSyntax>();
        if (declaratorSyntax == null ||
            declaratorSyntax.Initializer != null ||
            containingBlock == null ||
            declaratorSyntax.SpanStart >= observationSyntax.SpanStart)
            return false;

        var statements = containingBlock.Statements
            .Where(statement => statement.SpanStart > declaratorSyntax.SpanStart &&
                                statement.SpanStart < observationSyntax.SpanStart)
            .ToArray();
        var states = AnalyzeFreshMutableAssignments(
            statements,
            false,
            localSymbol,
            observationSyntax,
            semanticModel,
            visitedLocals,
            cancellationToken);
        return states.Count > 0 && states.All(static assignedFresh => assignedFresh);
    }

    private static List<bool> AnalyzeFreshMutableAssignments(
        IReadOnlyList<StatementSyntax> statements,
        bool assignedFresh,
        ILocalSymbol localSymbol,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken)
    {
        var states = new List<bool> { assignedFresh };
        foreach (var statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextStates = new List<bool>();
            foreach (var state in states)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nextStates.AddRange(AnalyzeFreshMutableAssignment(
                    statement,
                    state,
                    localSymbol,
                    observationSyntax,
                    semanticModel,
                    visitedLocals,
                    cancellationToken));
            }

            states = nextStates;
        }

        return states;
    }

    private static List<bool> AnalyzeFreshMutableAssignment(
        StatementSyntax statement,
        bool assignedFresh,
        ILocalSymbol localSymbol,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (statement is IfStatementSyntax ifStatement)
        {
            var thenStates = AnalyzeFreshMutableAssignments(
                GetStatementList(ifStatement.Statement),
                assignedFresh,
                localSymbol,
                observationSyntax,
                semanticModel,
                visitedLocals,
                cancellationToken);
            var elseStates = ifStatement.Else == null
                ? new List<bool> { assignedFresh }
                : AnalyzeFreshMutableAssignments(
                    GetStatementList(ifStatement.Else.Statement),
                    assignedFresh,
                    localSymbol,
                    observationSyntax,
                    semanticModel,
                    visitedLocals,
                    cancellationToken);

            thenStates.AddRange(elseStates);
            return thenStates;
        }

        var current = assignedFresh;
        foreach (var assignment in statement.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } assignedSymbol ||
                !SymbolEq.AreEqual(assignedSymbol, localSymbol))
                continue;

            current = IsFreshMutableAssignmentValue(
                assignment.Right,
                observationSyntax,
                semanticModel,
                visitedLocals,
                cancellationToken);
        }

        return new List<bool> { current };
    }

    private static IReadOnlyList<StatementSyntax> GetStatementList(StatementSyntax statement)
    {
        return statement is BlockSyntax block
            ? block.Statements.ToArray()
            : new[] { statement };
    }

    private static bool IsFreshMutableAssignmentValue(
        ExpressionSyntax valueSyntax,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var valueOperation =
            PurityAnalysisEngine.SkipImplicitConversions(semanticModel.GetOperation(valueSyntax, cancellationToken));
        if (valueOperation == null) return false;

        return HasStableFreshMutableObjectValueInOperation(
            valueOperation,
            observationSyntax,
            semanticModel,
            new HashSet<ILocalSymbol>(visitedLocals, SymbolEq.Default),
            cancellationToken);
    }

    private static bool HasStableFreshMutableObjectValueInOperation(
        IOperation operation,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        if (unwrappedOperation is IObjectCreationOperation objectCreationOperation)
        {
            if (RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type)) return true;

            if (objectCreationOperation.Constructor != null)
                foreach (var argument in objectCreationOperation.Arguments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var parameter = argument.Parameter;
                    if (parameter == null ||
                        !ConstructorStoresParameterInStableMember(objectCreationOperation.Constructor, parameter,
                            semanticModel, cancellationToken))
                        continue;

                    if (HasStableFreshMutableObjectValueInOperation(
                            argument.Value,
                            observationSyntax,
                            semanticModel,
                            visitedLocals,
                            cancellationToken))
                        return true;
                }
        }

        if (unwrappedOperation is ILocalReferenceOperation localReference)
            return IsOwnedFreshMutableLocal(localReference.Local, observationSyntax, semanticModel, null, visitedLocals,
                cancellationToken);

        if (unwrappedOperation is IInvocationOperation invocationOperation &&
            PurityAnalysisEngine.TryGetSingleReturnedValueFromInvocation(
                invocationOperation,
                semanticModel,
                out var returnedOperation,
                out _,
                out var returnedSemanticModel,
                cancellationToken))
            return HasStableFreshMutableObjectValueInOperation(
                returnedOperation,
                observationSyntax,
                returnedSemanticModel,
                visitedLocals,
                cancellationToken);

        return false;
    }

    private static bool ConstructorStoresParameterInStableMember(
        IMethodSymbol constructor,
        IParameterSymbol parameter,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return RuleAnalysisHelper.ConstructorStoresParameterMatching(
            constructor,
            parameter,
            semanticModel,
            cancellationToken,
            static target =>
                (target is IFieldReferenceOperation fieldReference &&
                 fieldReference.Field.IsReadOnly &&
                 RuleAnalysisHelper.IsThisOrImplicitInstance(fieldReference.Instance)) ||
                (target is IPropertyReferenceOperation propertyReference &&
                 (propertyReference.Property.SetMethod == null || propertyReference.Property.SetMethod.IsInitOnly) &&
                 RuleAnalysisHelper.IsThisOrImplicitInstance(propertyReference.Instance)));
    }
}
