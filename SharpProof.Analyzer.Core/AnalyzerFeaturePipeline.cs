namespace SharpProof.Analyzer;

internal static partial class AnalyzerFeaturePipeline
{
    internal static void ValidateNestedCallableDeclaration(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (AnalyzerGeneratedCodePolicy.IsGenerated(
                context.Node.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }

        SharpProofControlAttributePolicy.ValidateNestedCallableDeclaration(
            context.Node,
            context.SemanticModel,
            session,
            context.ReportDiagnostic,
            context.CancellationToken);
    }

    internal static void AnalyzeUnselectedOperationBlock(
        OperationBlockAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.OwningSymbol is not IMethodSymbol method ||
            method.DeclaringSyntaxReferences.IsDefaultOrEmpty ||
            IsNestedCallable(method) ||
            session.IsContractCompanion(method))
        {
            return;
        }

        var declaration = FindDeclaration(
            method,
            context.OperationBlocks,
            context.CancellationToken);
        if (declaration == null)
        {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
            return;
        }

        if (AnalyzerGeneratedCodePolicy.IsGenerated(
                method,
                declaration.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }

        var semanticModel = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(context.Compilation, declaration.SyntaxTree);
        var outcome = RequiresCallSiteAnalyzer.Analyze(
            method,
            declaration,
            semanticModel,
            session,
            context.ReportDiagnostic,
            context.CancellationToken);
        session.RecordSemanticOutcome(method, outcome);
    }

    internal static void ValidateMethodAttributes(
        SymbolAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Symbol is not IMethodSymbol method ||
            method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
        {
            return;
        }
        if (method.PartialImplementationPart != null)
        {
            return;
        }

        EffectContractDiagnostics.ValidateArguments(method, session, context.ReportDiagnostic);
        ClosedContractDiagnostics.Validate(method, session, context.ReportDiagnostic);
        var rejectedContractApi =
            session.Attributes.GetRejectedSelectionFeatures(method) !=
            ContractSelectionFeatures.None;
        var rejectedCallableApi =
            session.Attributes.GetRejectedCallableSelectionFeatures(method) !=
            ContractSelectionFeatures.None;
        if (rejectedCallableApi &&
            session.TryMarkRejectedContractApiReported(method))
        {
            ReportRejectedContractApi(
                method,
                context.ReportDiagnostic,
                context.CancellationToken);
        }
        var selection = GetSelection(
            method, session, context.ReportDiagnostic, context.CancellationToken);
        if (IsConcreteSemicolonAccessor(method, context.CancellationToken) &&
            selection.Any)
        {
            var declaration = method.DeclaringSyntaxReferences[0]
                .GetSyntax(context.CancellationToken);
            if (AnalyzerGeneratedCodePolicy.IsGenerated(
                    method,
                    declaration.SyntaxTree,
                    context.Compilation,
                    context.CancellationToken))
            {
                return;
            }
            if (selection.IsSuppressed)
            {
                session.RecordSemanticOutcome(
                    method,
                    AnalyzerSemanticOutcome.Suppressed);
                return;
            }
            session.RegisterSelectedSemicolonAccessor(method);
        }
        if ((!method.IsAbstract && !method.IsExtern) || !selection.Any)
        {
            return;
        }

        if (selection.IsSuppressed)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Suppressed);
            return;
        }
        if (rejectedContractApi)
        {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
            return;
        }
        if (!selection.Contracts &&
            selection.Effects &&
            session.ResolveEffectContract(method) is
            { Kind: EffectContractResolutionKind.Valid })
        {
            var outcome = EffectContractDiagnostics.Analyze(
                method,
                method.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken),
                session,
                context.ReportDiagnostic,
                context.CancellationToken);
            session.RecordSemanticOutcome(
                method,
                outcome == AnalyzerSemanticOutcome.NotApplicable
                    ? AnalyzerSemanticOutcome.Proven
                    : outcome);
            return;
        }
        context.ReportDiagnostic(Diagnostic.Create(
            GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
            AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(method, context.CancellationToken),
            method.Name,
            LanguageSubsetAbstentionReason.MissingOperationRoot));
        session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Abstained);
    }

    internal static void AnalyzeOperationBlock(
        OperationBlockAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.OwningSymbol is not IMethodSymbol method)
        {
            return;
        }

        if (IsConcreteSemicolonAccessor(method, context.CancellationToken))
        {
            return;
        }

        if (session.IsContractCompanion(method))
        {
            return;
        }
        if (InvocationEmissionPolicy.IsUnimplementedPartial(method))
        {
            return;
        }

        if (method.PartialImplementationPart != null)
        {
            return;
        }
        if (method.PartialDefinitionPart != null &&
            !session.TryBeginExecutableAnalysis(method))
        {
            return;
        }
        method = EffectAnalysisSession.NormalizeMethod(method);

        if (method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Abstained);
            return;
        }

        var declaration = FindDeclaration(
            method, context.OperationBlocks, context.CancellationToken);
        if (declaration == null)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Abstained);
            return;
        }
        var rejectedContractApi =
            session.Attributes.GetRejectedSelectionFeatures(method) !=
                ContractSelectionFeatures.None ||
            session.GetContractClauses(method)
                .HasRejectedContractApiUsage;
        var selection = GetSelection(
            method, session, context.ReportDiagnostic, context.CancellationToken);
        if (!selection.Any &&
            !rejectedContractApi &&
            AnalyzerGeneratedCodePolicy.IsGenerated(
                method,
                declaration.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }

        var hasInvalidContractClauses =
            ValidateContractClauses(
                method,
                session,
                context.ReportDiagnostic,
                context.CancellationToken);
        if (rejectedContractApi)
        {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
            return;
        }
        if (selection.IsSuppressed)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Suppressed);
            return;
        }

        var semanticModel = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
            context.Compilation, declaration.SyntaxTree);
        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        var subsetIncompleteReported = false;
        var classifySubset = selection.Any;
        var subset = classifySubset
            ? LanguageSubsetGate.ClassifyEffects(
                method,
                declaration,
                semanticModel,
                context.OperationBlocks,
                session.HasResolvedApiSpec,
                context.CancellationToken)
            : LanguageSubsetDecision.Supported;
        if (!subset.IsSupported)
        {
            if (selection.Any)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration),
                    method.Name,
                    subset.OperationKind is { } operation
                        ? subset.Reason + " (" + operation + ")"
                        : subset.Reason.ToString()));
                subsetIncompleteReported = true;
            }

            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome, AnalyzerSemanticOutcome.Abstained);
        }
        else if (selection.Effects)
        {
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                EffectContractDiagnostics.Analyze(
                    method,
                    declaration,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken));
        }

        if (session.Configuration.ContractsEnabled &&
            !IsNestedCallable(method))
        {
            var requiresOutcome =
                RequiresCallSiteAnalyzer.Analyze(
                    method,
                    declaration,
                    semanticModel,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken);
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                requiresOutcome);
            if (selection.Contracts &&
                requiresOutcome == AnalyzerSemanticOutcome.Unknown &&
                !subsetIncompleteReported &&
                !hasInvalidContractClauses &&
                !method.IsAbstract &&
                !method.IsExtern)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration),
                    method.Name,
                    "RequiresCallSiteAnalysisUnknown"));
            }
        }

        session.RecordSemanticOutcome(method, outcome);
    }

    internal static void ReconcileSelectedSemicolonAccessors(
        CompilationAnalysisContext context,
        AnalyzerSession session)
    {
        foreach (var method in session.GetUnrecordedSelectedSemicolonAccessors())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                    method,
                    context.CancellationToken),
                method.Name,
                LanguageSubsetAbstentionReason.MissingOperationRoot));
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
        }
    }

    private static bool IsNestedCallable(IMethodSymbol method)
    {
        return method.MethodKind is
            MethodKind.LocalFunction or MethodKind.AnonymousFunction;
    }

    private static bool IsConcreteSemicolonAccessor(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        return !method.IsAbstract &&
            !method.IsExtern &&
            method.MethodKind is
                MethodKind.PropertyGet or
                MethodKind.PropertySet or
                MethodKind.EventAdd or
                MethodKind.EventRemove &&
            method.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax(cancellationToken) is AccessorDeclarationSyntax
                {
                    Body: null,
                    ExpressionBody: null,
                    SemicolonToken.RawKind: not 0
                });
    }

    internal static void AnalyzePrimaryConstructor(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not TypeDeclarationSyntax declaration ||
            !PrimaryConstructorCallableInventory.TryGet(
                declaration,
                context.SemanticModel,
                context.CancellationToken,
                out var constructor) ||
            AnalyzerGeneratedCodePolicy.IsGenerated(
                constructor,
                declaration.SyntaxTree,
                context.Compilation,
                context.CancellationToken) ||
            !session.TryBeginRequiresCallSiteAnalysis(constructor))
        {
            return;
        }

        var outcome = SharpProofControlAttributePolicy
            .ValidateAndShouldSuppress(
                constructor,
                session,
                context.ReportDiagnostic,
                context.CancellationToken)
            ? AnalyzerSemanticOutcome.Suppressed
            : RequiresCallSiteAnalyzer.AnalyzePrimaryConstructorInitializer(
                constructor,
                declaration,
                context.SemanticModel,
                session,
                context.ReportDiagnostic,
                context.CancellationToken);
        session.RecordSemanticOutcome(constructor, outcome);
    }

    internal static void AnalyzeMemberInitializer(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Node is not EqualsValueClauseSyntax initializer ||
            initializer.Parent is not VariableDeclaratorSyntax and not PropertyDeclarationSyntax)
        {
            return;
        }
        var symbol = initializer.Parent switch
        {
            VariableDeclaratorSyntax variable => context.SemanticModel.GetDeclaredSymbol(
                variable, context.CancellationToken),
            PropertyDeclarationSyntax property => context.SemanticModel.GetDeclaredSymbol(
                property, context.CancellationToken),
            _ => null
        };
        if (symbol is not IFieldSymbol and
            not IPropertySymbol and
            not IEventSymbol ||
            symbol.ContainingType is not { } type)
        {
            return;
        }
        var isStatic = symbol.IsStatic;
        var constructors = (isStatic
                ? type.StaticConstructors
                : type.InstanceConstructors)
            .OrderBy(static candidate => candidate.DeclaringSyntaxReferences
                .FirstOrDefault()?.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.DeclaringSyntaxReferences
                .FirstOrDefault()?.Span.Start ?? int.MaxValue)
            .Where(candidate =>
                !AnalyzerGeneratedCodePolicy.IsGenerated(
                    candidate,
                    candidate.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree ??
                        initializer.SyntaxTree,
                    context.Compilation,
                    context.CancellationToken))
            .ToArray();
        var root = context.SemanticModel.GetOperation(
            initializer.Value, context.CancellationToken);
        if (constructors.Length == 0 || root == null ||
            AnalyzerGeneratedCodePolicy.IsGenerated(
                symbol,
                initializer.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }
        IMethodSymbol? constructor = null;
        foreach (var candidate in constructors)
        {
            if (SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
                    candidate,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken))
            {
                session.RecordSemanticOutcome(
                    candidate,
                    AnalyzerSemanticOutcome.Suppressed);
                continue;
            }
            constructor = candidate;
            break;
        }
        if (constructor == null)
        {
            return;
        }
        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        foreach (var operation in RequiresCallSiteDiscovery
                     .ExecutableUnflowedDescendantsAndSelf(root))
        {
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                RequiresCallSiteAnalyzer.AnalyzeInitializerCall(
                    constructor, initializer, operation,
                    context.SemanticModel, session,
                    context.ReportDiagnostic, context.CancellationToken));
        }
        session.RecordSemanticOutcome(constructor, outcome);
    }

    private static bool ValidateContractClauses(
        IMethodSymbol method,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var inventory = session.GetContractClauses(method);
        if (inventory.HasRejectedContractApiUsage &&
            session.TryMarkRejectedContractApiReported(method))
        {
            ReportRejectedContractApi(
                method,
                reportDiagnostic,
                cancellationToken);
        }
        var intrinsicViolations =
            session.GetContractIntrinsicViolations(inventory);
        ReportInvalidIntrinsics(intrinsicViolations, session, reportDiagnostic);
        ReportInvalidClauses(inventory.Clauses, reportDiagnostic);
        foreach (var owner in GetNestedOwners(inventory, session.Compilation))
        {
            ReportInvalidClauses(session.GetContractClauses(owner).Clauses, reportDiagnostic);
        }
        return inventory.HasRejectedContractApiUsage ||
            inventory.HasPlacementErrors ||
            !intrinsicViolations.IsDefaultOrEmpty;
    }

    private static void ReportRejectedContractApi(
        IMethodSymbol method,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        SharpProofControlAttributePolicy.ReportRejectedContractApi(
            method.Name,
            AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                method,
                cancellationToken),
            reportDiagnostic);
    }

    private static IEnumerable<IMethodSymbol> GetNestedOwners(
        ContractClauseInventory inventory,
        Compilation compilation)
    {
        return inventory.Clauses
            .Where(static clause => clause.Placement == ContractClausePlacement.NestedCallable)
            .Select(clause => SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, clause.Invocation.Syntax.SyntaxTree)
                .GetEnclosingSymbol(clause.Invocation.Syntax.SpanStart))
            .OfType<IMethodSymbol>()
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default);
    }

    private static void ReportInvalidIntrinsics(
        ImmutableArray<ContractIntrinsicViolation> violations,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic)
    {
        foreach (var violation in violations)
        {
            if (!session.TryMarkContractIntrinsicValidated(violation))
            {
                continue;
            }

            var isOld = violation.Failure is
                ContractBindingFailure.OldOutsideEnsures or ContractBindingFailure.NestedOld;
            var (argument, reason) = DescribeIntrinsicViolation(violation.Failure, isOld);
            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                isOld ? "Contract.Old" : "Contract.Result",
                argument,
                reason,
                violation.Invocation.Syntax.GetLocation()));
        }
    }

    private static (string Argument, string Reason) DescribeIntrinsicViolation(
        ContractBindingFailure failure,
        bool isOld)
    {
        return AnalyzerDiagnosticCatalog.DescribeIntrinsicViolation(
            failure, isOld);
    }

    private static void ReportInvalidClauses(
        ImmutableArray<ContractClauseOccurrence> clauses,
        Action<Diagnostic> reportDiagnostic)
    {
        foreach (var clause in clauses)
        {
            if (clause.IsValid ||
                clause.Placement == ContractClausePlacement.NestedCallable)
            {
                continue;
            }

            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                "Contract." + clause.Kind,
                "<placement>",
                DescribePlacement(clause.Placement),
                clause.Location));
        }
    }

    private static string DescribePlacement(ContractClausePlacement placement)
    {
        return AnalyzerDiagnosticCatalog.DescribePlacement(placement);
    }

    private static MethodSelection GetSelection(
        IMethodSymbol method,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var features = session.Attributes.Select(
            method,
            session.Configuration.ContractsEnabled &&
            session.ResolveContractSource(method).HasSelectedContractIntent) &
            ((session.Configuration.ContractsEnabled
                  ? ContractSelectionFeatures.Contracts
                  : ContractSelectionFeatures.None) |
             (session.Configuration.EffectsEnabled
                  ? ContractSelectionFeatures.Effects
                  : ContractSelectionFeatures.None));
        var suppressed = SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
            method, session, reportDiagnostic, cancellationToken);
        return new(features, suppressed);
    }

    private static SyntaxNode? FindDeclaration(
        IMethodSymbol method,
        ImmutableArray<IOperation> operationBlocks,
        CancellationToken cancellationToken)
    {
        var operationSyntax = operationBlocks
            .OrderByDescending(static operation => operation.Syntax.Span.Length)
            .FirstOrDefault()?.Syntax;
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operationSyntax == null ||
                reference.SyntaxTree == operationSyntax.SyntaxTree &&
                reference.Span.Contains(operationSyntax.Span))
            {
                return NormalizeDeclaration(reference.GetSyntax(cancellationToken));
            }
        }
        return null;
    }

    private static SyntaxNode NormalizeDeclaration(SyntaxNode declaration)
    {
        return declaration switch
        {
            ArrowExpressionClauseSyntax { Parent: { } parent } => parent,
            _ => declaration
        };
    }

    private readonly partial record struct MethodSelection
    {
        internal bool Contracts => (Features & ContractSelectionFeatures.Contracts) != 0;
        internal bool Effects => (Features & ContractSelectionFeatures.Effects) != 0;
        internal bool Any => Contracts || Effects;
    }
}
