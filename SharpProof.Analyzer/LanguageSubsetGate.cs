namespace SharpProof.Analyzer;

internal enum LanguageSubsetAbstentionReason {
    None,
    UnsupportedCallable,
    MissingOperationRoot,
    UnsupportedOperationKind,
    UnsupportedType,
    UnsupportedOperationShape
}

internal readonly struct LanguageSubsetDecision {
    private LanguageSubsetDecision(
        bool isSupported,
        LanguageSubsetAbstentionReason reason,
        OperationKind? operationKind) {
        IsSupported = isSupported;
        Reason = reason;
        OperationKind = operationKind;
    }

    internal bool IsSupported { get; }
    internal LanguageSubsetAbstentionReason Reason { get; }
    internal OperationKind? OperationKind { get; }

    internal static LanguageSubsetDecision Supported { get; } =
        new(true, LanguageSubsetAbstentionReason.None, null);

    internal static LanguageSubsetDecision Abstain(
        LanguageSubsetAbstentionReason reason,
        OperationKind? operationKind = null) {
        if (reason == LanguageSubsetAbstentionReason.None)
            throw new ArgumentOutOfRangeException(nameof(reason));
        return new LanguageSubsetDecision(false, reason, operationKind);
    }
}

internal static class LanguageSubsetGate {
    internal static readonly ImmutableDictionary<OperationKind, bool> OperationKindDecisions =
        new Dictionary<OperationKind, bool> {
            [OperationKind.None] = false,
            [OperationKind.Invalid] = false,
            [OperationKind.Block] = true,
            [OperationKind.VariableDeclarationGroup] = true,
            [OperationKind.Switch] = true,
            [OperationKind.Loop] = true,
            [OperationKind.Labeled] = true,
            [OperationKind.Branch] = true,
            [OperationKind.Empty] = true,
            [OperationKind.Return] = true,
            [OperationKind.YieldBreak] = false,
            [OperationKind.Lock] = true,
            [OperationKind.Try] = true,
            [OperationKind.Using] = true,
            [OperationKind.YieldReturn] = false,
            [OperationKind.ExpressionStatement] = true,
            [OperationKind.LocalFunction] = false,
            [OperationKind.Stop] = false,
            [OperationKind.End] = false,
            [OperationKind.RaiseEvent] = false,
            [OperationKind.Literal] = true,
            [OperationKind.Conversion] = true,
            [OperationKind.Invocation] = true,
            [OperationKind.ArrayElementReference] = true,
            [OperationKind.LocalReference] = true,
            [OperationKind.ParameterReference] = true,
            [OperationKind.FieldReference] = true,
            [OperationKind.MethodReference] = false,
            [OperationKind.PropertyReference] = true,
            [OperationKind.EventReference] = false,
            [OperationKind.Unary] = true,
            [OperationKind.Binary] = true,
            [OperationKind.Conditional] = true,
            [OperationKind.Coalesce] = true,
            [OperationKind.AnonymousFunction] = false,
            [OperationKind.ObjectCreation] = true,
            [OperationKind.TypeParameterObjectCreation] = false,
            [OperationKind.ArrayCreation] = true,
            [OperationKind.InstanceReference] = true,
            [OperationKind.IsType] = true,
            [OperationKind.Await] = false,
            [OperationKind.SimpleAssignment] = true,
            [OperationKind.CompoundAssignment] = true,
            [OperationKind.Parenthesized] = true,
            [OperationKind.EventAssignment] = false,
            [OperationKind.ConditionalAccess] = true,
            [OperationKind.ConditionalAccessInstance] = true,
            [OperationKind.InterpolatedString] = true,
            [OperationKind.AnonymousObjectCreation] = false,
            [OperationKind.ObjectOrCollectionInitializer] = true,
            [OperationKind.MemberInitializer] = true,
            // OperationKind.CollectionElementInitializer (52) is obsolete but remains
            // part of the Roslyn 4.14 enum and must stay explicitly fail-closed.
            [(OperationKind)52] = false,
            [OperationKind.NameOf] = true,
            [OperationKind.Tuple] = false,
            [OperationKind.DynamicObjectCreation] = false,
            [OperationKind.DynamicMemberReference] = false,
            [OperationKind.DynamicInvocation] = false,
            [OperationKind.DynamicIndexerAccess] = false,
            [OperationKind.TranslatedQuery] = false,
            [OperationKind.DelegateCreation] = false,
            [OperationKind.DefaultValue] = true,
            [OperationKind.TypeOf] = true,
            [OperationKind.SizeOf] = false,
            [OperationKind.AddressOf] = false,
            [OperationKind.IsPattern] = false,
            [OperationKind.Increment] = true,
            [OperationKind.Throw] = true,
            [OperationKind.Decrement] = true,
            [OperationKind.DeconstructionAssignment] = false,
            [OperationKind.DeclarationExpression] = false,
            [OperationKind.OmittedArgument] = false,
            [OperationKind.FieldInitializer] = true,
            [OperationKind.VariableInitializer] = true,
            [OperationKind.PropertyInitializer] = true,
            [OperationKind.ParameterInitializer] = true,
            [OperationKind.ArrayInitializer] = true,
            [OperationKind.VariableDeclarator] = true,
            [OperationKind.VariableDeclaration] = true,
            [OperationKind.Argument] = true,
            [OperationKind.CatchClause] = true,
            [OperationKind.SwitchCase] = true,
            [OperationKind.CaseClause] = true,
            [OperationKind.InterpolatedStringText] = true,
            [OperationKind.Interpolation] = true,
            [OperationKind.ConstantPattern] = false,
            [OperationKind.DeclarationPattern] = false,
            [OperationKind.TupleBinary] = false,
            [OperationKind.MethodBody] = true,
            [OperationKind.ConstructorBody] = true,
            [OperationKind.Discard] = true,
            [OperationKind.FlowCapture] = true,
            [OperationKind.FlowCaptureReference] = true,
            [OperationKind.IsNull] = true,
            [OperationKind.CaughtException] = true,
            [OperationKind.StaticLocalInitializationSemaphore] = false,
            [OperationKind.FlowAnonymousFunction] = false,
            [OperationKind.CoalesceAssignment] = true,
            [OperationKind.Range] = false,
            [OperationKind.ReDim] = false,
            [OperationKind.ReDimClause] = false,
            [OperationKind.RecursivePattern] = false,
            [OperationKind.DiscardPattern] = false,
            [OperationKind.SwitchExpression] = false,
            [OperationKind.SwitchExpressionArm] = false,
            [OperationKind.PropertySubpattern] = false,
            [OperationKind.UsingDeclaration] = true,
            [OperationKind.NegatedPattern] = false,
            [OperationKind.BinaryPattern] = false,
            [OperationKind.TypePattern] = false,
            [OperationKind.RelationalPattern] = false,
            [OperationKind.With] = false,
            [OperationKind.InterpolatedStringHandlerCreation] = false,
            [OperationKind.InterpolatedStringAddition] = false,
            [OperationKind.InterpolatedStringAppendLiteral] = false,
            [OperationKind.InterpolatedStringAppendFormatted] = false,
            [OperationKind.InterpolatedStringAppendInvalid] = false,
            [OperationKind.InterpolatedStringHandlerArgumentPlaceholder] = false,
            [OperationKind.FunctionPointerInvocation] = false,
            [OperationKind.ListPattern] = false,
            [OperationKind.SlicePattern] = false,
            [OperationKind.ImplicitIndexerReference] = false,
            [OperationKind.Utf8String] = false,
            [OperationKind.Attribute] = true,
            [OperationKind.InlineArrayAccess] = false,
            [OperationKind.CollectionExpression] = false,
            [OperationKind.Spread] = false
        }.ToImmutableDictionary();

    internal static LanguageSubsetDecision ClassifyV2Effects(
        IMethodSymbol method,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        ImmutableArray<IOperation> operationBlocks,
        Func<IMethodSymbol, bool> hasResolvedGenericApiSpec,
        CancellationToken cancellationToken) {
        if (!SupportsCallable(method, declaration))
            return LanguageSubsetDecision.Abstain(
                LanguageSubsetAbstentionReason.UnsupportedCallable);
        var roots = operationBlocks.IsDefaultOrEmpty
            ? GetFallbackRoots(declaration, semanticModel, cancellationToken)
            : operationBlocks;
        if (roots.IsDefaultOrEmpty)
            return LanguageSubsetDecision.Abstain(
                LanguageSubsetAbstentionReason.MissingOperationRoot);
        foreach (var root in roots)
            foreach (var operation in root.DescendantsAndSelf()) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!OperationKindDecisions.TryGetValue(
                        operation.Kind,
                        out var supported) ||
                    !supported)
                    return LanguageSubsetDecision.Abstain(
                        LanguageSubsetAbstentionReason.UnsupportedOperationKind,
                        operation.Kind);
                if (IsUnsupportedType(operation.Type))
                    return LanguageSubsetDecision.Abstain(
                        LanguageSubsetAbstentionReason.UnsupportedType,
                        operation.Kind);
                if (!SupportsOperationShape(
                        operation,
                        hasResolvedGenericApiSpec))
                    return LanguageSubsetDecision.Abstain(
                        LanguageSubsetAbstentionReason.UnsupportedOperationShape,
                        operation.Kind);
            }
        return LanguageSubsetDecision.Supported;
    }

    private static ImmutableArray<IOperation> GetFallbackRoots(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        var root = semanticModel.GetOperation(declaration, cancellationToken);
        if (root == null)
            root = declaration switch {
                BaseMethodDeclarationSyntax { Body: not null } method =>
                    semanticModel.GetOperation(method.Body, cancellationToken),
                BaseMethodDeclarationSyntax { ExpressionBody: not null } method =>
                    semanticModel.GetOperation(
                        method.ExpressionBody.Expression,
                        cancellationToken),
                AccessorDeclarationSyntax { Body: not null } accessor =>
                    semanticModel.GetOperation(accessor.Body, cancellationToken),
                AccessorDeclarationSyntax { ExpressionBody: not null } accessor =>
                    semanticModel.GetOperation(
                        accessor.ExpressionBody.Expression,
                        cancellationToken),
                PropertyDeclarationSyntax { ExpressionBody: not null } property =>
                    semanticModel.GetOperation(
                        property.ExpressionBody.Expression,
                        cancellationToken),
                IndexerDeclarationSyntax { ExpressionBody: not null } indexer =>
                    semanticModel.GetOperation(
                        indexer.ExpressionBody.Expression,
                        cancellationToken),
                _ => null
            };
        return root == null ? [] : [root];
    }

    private static bool SupportsCallable(IMethodSymbol method, SyntaxNode declaration) {
        if (method.IsAsync ||
            method.TypeParameters.Length != 0 ||
            method.ReturnsByRef ||
            method.ReturnsByRefReadonly ||
            IsUnsupportedType(method.ReturnType) ||
            method.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None ||
                IsUnsupportedType(parameter.Type)) ||
            ContainsUnsafeSyntax(declaration))
            return false;
        if (declaration is TypeDeclarationSyntax) return false;
        return method.MethodKind is
            MethodKind.Ordinary or
            MethodKind.Constructor or
            MethodKind.StaticConstructor or
            MethodKind.PropertyGet or
            MethodKind.PropertySet or
            MethodKind.EventAdd or
            MethodKind.EventRemove or
            MethodKind.ExplicitInterfaceImplementation;
    }

    private static bool SupportsOperationShape(
        IOperation operation,
        Func<IMethodSymbol, bool> hasResolvedGenericApiSpec) =>
        operation switch {
            ILoopOperation loop =>
                loop.LoopKind is LoopKind.While or LoopKind.For,
            ISwitchOperation @switch =>
                @switch.Cases.SelectMany(static @case => @case.Clauses)
                    .All(IsConstantSwitchClause),
            IArgumentOperation argument =>
                argument.Parameter?.RefKind is null or RefKind.None,
            IVariableDeclaratorOperation declarator =>
                declarator.Symbol.RefKind == RefKind.None &&
                !IsUnsupportedType(declarator.Symbol.Type),
            ILocalReferenceOperation local =>
                local.Local.RefKind == RefKind.None &&
                !IsUnsupportedType(local.Local.Type),
            IInvocationOperation invocation =>
                SupportsCall(invocation.TargetMethod, hasResolvedGenericApiSpec),
            IObjectCreationOperation creation =>
                creation.Constructor != null &&
                SupportsCall(creation.Constructor, hasResolvedGenericApiSpec),
            IPropertyReferenceOperation property =>
                SupportsProperty(property, hasResolvedGenericApiSpec),
            IConversionOperation conversion =>
                conversion.OperatorMethod == null,
            IUnaryOperation unary =>
                unary.OperatorMethod == null,
            IBinaryOperation binary =>
                binary.OperatorMethod == null,
            ICompoundAssignmentOperation compound =>
                compound.OperatorMethod == null,
            IIncrementOrDecrementOperation increment =>
                increment.OperatorMethod == null,
            _ => true
        };

    private static bool IsConstantSwitchClause(ICaseClauseOperation clause) =>
        clause is IDefaultCaseClauseOperation ||
        clause is ISingleValueCaseClauseOperation { Value.ConstantValue.HasValue: true };

    private static bool SupportsProperty(
        IPropertyReferenceOperation property,
        Func<IMethodSymbol, bool> hasResolvedGenericApiSpec) {
        if (!RequiresResolvedGenericApiSpec(property.Property.ContainingType)) return true;
        var accessors = new[] { property.Property.GetMethod, property.Property.SetMethod };
        var availableAccessors = accessors.Where(static accessor => accessor != null).ToArray();
        return availableAccessors.Length != 0 &&
               availableAccessors.All(accessor => hasResolvedGenericApiSpec(accessor!));
    }

    private static bool SupportsCall(
        IMethodSymbol method,
        Func<IMethodSymbol, bool> hasResolvedGenericApiSpec) {
        if (method.MethodKind is
            MethodKind.AnonymousFunction or
            MethodKind.DelegateInvoke or
            MethodKind.FunctionPointerSignature or
            MethodKind.LocalFunction)
            return false;
        if (method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None))
            return false;
        if (!RequiresResolvedGenericApiSpec(method) &&
            !RequiresResolvedGenericApiSpec(method.ContainingType))
            return true;
        return !ContainsOpenType(method) &&
               hasResolvedGenericApiSpec(method);
    }

    private static bool RequiresResolvedGenericApiSpec(ISymbol? symbol) => symbol switch {
        IMethodSymbol method => method.IsGenericMethod,
        INamedTypeSymbol type => type.IsGenericType,
        _ => false
    };

    private static bool ContainsOpenType(IMethodSymbol method) =>
        method.TypeArguments.Any(IsUnsupportedType) ||
        method.Parameters.Any(static parameter => IsUnsupportedType(parameter.Type)) ||
        IsUnsupportedType(method.ReturnType) ||
        method.ContainingType.TypeArguments.Any(IsUnsupportedType);

    private static bool IsUnsupportedType(ITypeSymbol? type) {
        if (type == null) return false;
        if (type.TypeKind is
            TypeKind.Delegate or
            TypeKind.Dynamic or
            TypeKind.FunctionPointer or
            TypeKind.Pointer or
            TypeKind.TypeParameter)
            return true;
        return type switch {
            IArrayTypeSymbol array => IsUnsupportedType(array.ElementType),
            INamedTypeSymbol named =>
                named.IsRefLikeType ||
                named.TypeArguments.Any(IsUnsupportedType),
            _ => false
        };
    }

    private static bool ContainsUnsafeSyntax(SyntaxNode declaration) =>
        declaration.DescendantNodesAndSelf().Any(static node => node is UnsafeStatementSyntax) ||
        declaration.AncestorsAndSelf().Any(HasUnsafeModifier);

    private static bool HasUnsafeModifier(SyntaxNode node) {
        var modifiers = node switch {
            BaseMethodDeclarationSyntax method => method.Modifiers,
            BasePropertyDeclarationSyntax property => property.Modifiers,
            AccessorDeclarationSyntax accessor => accessor.Modifiers,
            LocalFunctionStatementSyntax localFunction => localFunction.Modifiers,
            TypeDeclarationSyntax type => type.Modifiers,
            DelegateDeclarationSyntax @delegate => @delegate.Modifiers,
            _ => default
        };
        return modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.UnsafeKeyword));
    }
}
