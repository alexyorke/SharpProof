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
        var outcome = RequiresCallSiteTreeAnalyzer.Analyze(
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
        var firstDeclaration = method.DeclaringSyntaxReferences[0]
            .GetSyntax(context.CancellationToken);
        var isGenerated = AnalyzerGeneratedCodePolicy.IsGenerated(
            method,
            firstDeclaration.SyntaxTree,
            context.Compilation,
            context.CancellationToken);
        if (isGenerated)
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
            if (TryRecordSuppressed(method, selection, session))
            {
                return;
            }
            session.RegisterSelectedSemicolonAccessor(method);
        }
        if ((!method.IsAbstract && !method.IsExtern) || !selection.Any)
        {
            return;
        }

        if (TryRecordSuppressed(method, selection, session))
        {
            return;
        }
        if (TryRecordRejectedContractAbstention(
                method,
                rejectedContractApi,
                session))
        {
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
        ReportSelectedAnalysisIncomplete(
            context.ReportDiagnostic,
            AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                method,
                context.CancellationToken),
            method.Name,
            LanguageSubsetAbstentionReason.MissingOperationRoot);
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
        if (TryRecordRejectedContractAbstention(
                method,
                rejectedContractApi,
                session))
        {
            return;
        }
        if (TryRecordSuppressed(method, selection, session))
        {
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
                ReportSelectedAnalysisIncomplete(
                    context.ReportDiagnostic,
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                        declaration),
                    method.Name,
                    DescribeSubset(subset));
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
                RequiresCallSiteTreeAnalyzer.Analyze(
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
                ReportSelectedAnalysisIncomplete(
                    context.ReportDiagnostic,
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                        declaration),
                    method.Name,
                    "RequiresCallSiteAnalysisUnknown");
            }
        }

        session.RecordSemanticOutcome(method, outcome);
    }

    internal static void AnalyzeLambdaEffects(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.SemanticModel.GetOperation(
                context.Node,
                context.CancellationToken) is not
            IAnonymousFunctionOperation anonymousFunction)
        {
            return;
        }

        var method = anonymousFunction.Symbol;
        if (AnalyzerGeneratedCodePolicy.IsGenerated(
                method,
                context.Node.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }

        EffectContractDiagnostics.ValidateArguments(
            method,
            session,
            context.ReportDiagnostic);
        var rejectedContractApi =
            session.Attributes.GetRejectedSelectionFeatures(method) !=
            ContractSelectionFeatures.None;
        if (rejectedContractApi &&
            session.TryMarkRejectedContractApiReported(method))
        {
            ReportRejectedContractApi(
                method,
                context.ReportDiagnostic,
                context.CancellationToken);
        }

        var selection = GetSelection(
            method,
            session,
            context.ReportDiagnostic,
            context.CancellationToken);
        if (!selection.Effects ||
            !session.TryBeginExecutableAnalysis(method))
        {
            return;
        }
        if (TryRecordRejectedContractAbstention(
                method,
                rejectedContractApi,
                session))
        {
            return;
        }
        if (TryRecordSuppressed(method, selection, session))
        {
            return;
        }

        var subset = LanguageSubsetGate.ClassifyEffects(
            method,
            context.Node,
            context.SemanticModel,
            [anonymousFunction.Body],
            session.HasResolvedApiSpec,
            context.CancellationToken);
        if (!subset.IsSupported)
        {
            ReportSelectedAnalysisIncomplete(
                context.ReportDiagnostic,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                    context.Node),
                method.Name,
                DescribeSubset(subset));
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
            return;
        }

        session.RecordSemanticOutcome(
            method,
            EffectContractDiagnostics.Analyze(
                method,
                context.Node,
                session,
                context.ReportDiagnostic,
                context.CancellationToken));
    }

    internal static void ReconcileSelectedSemicolonAccessors(
        CompilationAnalysisContext context,
        AnalyzerSession session)
    {
        foreach (var method in session.GetUnrecordedSelectedSemicolonAccessors())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            ReportSelectedAnalysisIncomplete(
                context.ReportDiagnostic,
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(
                    method,
                    context.CancellationToken),
                method.Name,
                LanguageSubsetAbstentionReason.MissingOperationRoot);
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
                out var constructor) &&
            !PrimaryConstructorCallableInventory.TryGetSynthesizedDefault(
                declaration,
                context.SemanticModel,
                context.CancellationToken,
                out constructor) ||
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
        if (AnalyzerGeneratedCodePolicy.IsGenerated(
                symbol,
                initializer.SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }
        var constructors = (isStatic
                ? type.StaticConstructors
                : type.InstanceConstructors)
            .Select(static candidate =>
            {
                var reference = candidate.DeclaringSyntaxReferences.FirstOrDefault();
                return (Candidate: candidate, Reference: reference);
            })
            .OrderBy(static item => item.Reference?.SyntaxTree.FilePath,
                StringComparer.Ordinal)
            .ThenBy(static item => item.Reference?.Span.Start ?? int.MaxValue)
            .Where(item =>
                !AnalyzerGeneratedCodePolicy.IsGenerated(
                    item.Candidate,
                    item.Reference?.SyntaxTree ??
                        initializer.SyntaxTree,
                    context.Compilation,
                    context.CancellationToken) &&
                !IsThisDelegatingConstructor(
                    item.Candidate,
                    context.CancellationToken))
            .Select(static item => item.Candidate)
            .ToArray();
        if (constructors.Length == 0)
        {
            return;
        }
        var root = context.SemanticModel.GetOperation(
            initializer.Value, context.CancellationToken);
        if (root == null)
        {
            return;
        }
        var eligibleConstructors = ImmutableArray.CreateBuilder<IMethodSymbol>();
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
            eligibleConstructors.Add(candidate);
        }
        if (eligibleConstructors.Count == 0)
        {
            return;
        }
        var operationFacts = new DefiniteOperationFacts(
            context.Compilation,
            context.CancellationToken);
        if (!CanReachMemberInitializer(
                initializer,
                isStatic,
                type,
                context.SemanticModel,
                operationFacts,
                context.CancellationToken))
        {
            return;
        }
        var reportedDiagnostics = new HashSet<MemberInitializerDiagnosticKey>();
        foreach (var constructor in eligibleConstructors)
        {
            var outcome = RequiresCallSiteAnalyzer.AnalyzeInitializerCall(
                constructor,
                initializer,
                root,
                context.SemanticModel,
                session,
                diagnostic =>
                {
                    var key = new MemberInitializerDiagnosticKey(
                        diagnostic.Id,
                        diagnostic.Location.SourceTree,
                        diagnostic.Location.SourceSpan,
                        diagnostic.GetMessage(CultureInfo.InvariantCulture));
                    if (reportedDiagnostics.Add(key))
                    {
                        context.ReportDiagnostic(diagnostic);
                    }
                },
                context.CancellationToken);
            session.RecordSemanticOutcome(constructor, outcome);
        }
    }

    private readonly record struct MemberInitializerDiagnosticKey(
        string Id,
        SyntaxTree? Tree,
        TextSpan Span,
        string Message);

    private static bool IsThisDelegatingConstructor(
        IMethodSymbol constructor,
        CancellationToken cancellationToken)
    {
        return constructor.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<ConstructorDeclarationSyntax>()
            .Any(static declaration =>
                declaration.Initializer?.ThisOrBaseKeyword.IsKind(
                    SyntaxKind.ThisKeyword) == true);
    }

    private static bool CanReachMemberInitializer(
        EqualsValueClauseSyntax target,
        bool isStatic,
        INamedTypeSymbol type,
        SemanticModel semanticModel,
        DefiniteOperationFacts operationFacts,
        CancellationToken cancellationToken)
    {
        foreach (var reference in EffectMethodNodeBuilder
                     .GetMemberInitializerReferences(
                         semanticModel.Compilation,
                         type,
                         isStatic))
        {
            var initializer = GetMemberInitializer(
                reference.GetSyntax(cancellationToken));
            if (initializer == null)
            {
                continue;
            }
            if (initializer.SyntaxTree == target.SyntaxTree &&
                initializer.Span == target.Span)
            {
                return true;
            }
            var model = initializer.SyntaxTree == semanticModel.SyntaxTree
                ? semanticModel
                : SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
                    semanticModel.Compilation,
                    initializer.SyntaxTree);
            var operation = model.GetOperation(
                initializer.Value,
                cancellationToken);
            if (operation != null &&
                !operationFacts.MayCompleteNormally(operation))
            {
                return false;
            }
        }
        return true;
    }

    private static EqualsValueClauseSyntax? GetMemberInitializer(
        SyntaxNode member)
    {
        return member switch
        {
            VariableDeclaratorSyntax { Initializer: { } initializer } =>
                initializer,
            PropertyDeclarationSyntax { Initializer: { } initializer } =>
                initializer,
            _ => null
        };
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

    private static bool TryRecordRejectedContractAbstention(
        IMethodSymbol method,
        bool rejectedContractApi,
        AnalyzerSession session)
    {
        if (!rejectedContractApi)
        {
            return false;
        }

        session.RecordSemanticOutcome(
            method,
            AnalyzerSemanticOutcome.Abstained);
        return true;
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

            reportDiagnostic(
                InvalidContractArgumentDiagnostics.Create(violation));
        }
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
                AnalyzerDiagnosticCatalog.DescribePlacement(clause.Placement),
                clause.Location));
        }
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

    private static bool TryRecordSuppressed(
        IMethodSymbol method,
        MethodSelection selection,
        AnalyzerSession session)
    {
        if (!selection.IsSuppressed)
        {
            return false;
        }

        session.RecordSemanticOutcome(
            method,
            AnalyzerSemanticOutcome.Suppressed);
        return true;
    }

    private static void ReportSelectedAnalysisIncomplete(
        Action<Diagnostic> report,
        Location location,
        string methodName,
        object reason)
    {
        report(Diagnostic.Create(
            GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
            location,
            methodName,
            reason));
    }

    private static string DescribeSubset(LanguageSubsetDecision subset)
    {
        return subset.OperationKind is { } operation
            ? subset.Reason + " (" + operation + ")"
            : subset.Reason.ToString();
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
