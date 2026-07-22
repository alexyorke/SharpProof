using SharpProof.Attributes;
namespace SharpProof.Symbolic;

internal sealed record CompilerMethodEffectSummary(
    MethodEffects Effects,
    EffectFlowValue ReturnValue,
    EffectFlowValue Receiver,
    ImmutableArray<EffectFlowValue> Parameters,
    ImmutableArray<int> WrittenArgumentOrdinals = default,
    ImmutableArray<int> ReadArgumentOrdinals = default,
    SharpProofEffect BoundArgumentEffects = SharpProofEffect.None,
    SharpProofEffect BoundReceiverEffects = SharpProofEffect.None);

internal sealed class MethodEffectAnalysisSession(
    Compilation compilation,
    CancellationToken cancellationToken,
    Func<IMethodSymbol, MethodEffects?>? externalContractResolver = null,
    SmtAnalysisService? smtAnalysis = null) {
    private readonly Dictionary<IMethodSymbol, CompilerMethodEffectSummary> _cache = new(SymbolEqualityComparer.Default);
    private readonly HashSet<IMethodSymbol> _active = new(SymbolEqualityComparer.Default);
    private readonly object _gate = new();
    private readonly MetadataMethodEffectAnalyzer _metadata = new(compilation);
    private Compilation Compilation => compilation;
    private CancellationToken CancellationToken => cancellationToken;
    internal MethodEffects Analyze(IMethodSymbol method, SyntaxNode declaration, SemanticModel semanticModel) {
        lock (_gate) return AnalyzeCore(method, declaration, semanticModel);
    }
    private MethodEffects AnalyzeCore(IMethodSymbol method, SyntaxNode declaration, SemanticModel semanticModel) {
        var result = AnalyzeSummary(method, declaration, semanticModel, null).Effects;
        if (method.MethodKind == MethodKind.StaticConstructor) return result;
        if (method.IsStatic) return IncludeTypeInitializerEffects(result, method.ContainingType);
        if (method.MethodKind != MethodKind.Constructor) return result;
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (var current = method.ContainingType; current != null; current = current.BaseType) hierarchy.Push(current);
        while (hierarchy.Count != 0) result = IncludeTypeInitializerEffects(result, hierarchy.Pop());
        return result;
    }
    private MethodEffects IncludeTypeInitializerEffects(MethodEffects result, INamedTypeSymbol type) {
        if (type.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(static candidate =>
                candidate.MethodKind == MethodKind.StaticConstructor) is not { } initializer)
            return result;
        var initializerDeclaration = initializer.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) ??
                                     initializer.ContainingType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        if (initializerDeclaration == null) return result;
        var initializerEffects = WrapTypeInitializerExceptions(AnalyzeSummary(initializer, initializerDeclaration,
            compilation.GetSemanticModel(initializerDeclaration.SyntaxTree), null).Effects);
        return Union(initializerEffects, result);
    }
    private CompilerMethodEffectSummary AnalyzeSummary(
        IMethodSymbol method,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        ImmutableDictionary<string, EffectFlowValue>? captures) {
        method = Normalize(method);
        if (captures == null && _cache.TryGetValue(method, out var cached)) return cached;
        if (!_active.Add(method)) return UnknownSummary("recursive_call", declaration, method);
        try {
            var accumulator = new EffectAccumulator(method);
            var state = EffectFlowState.Create(method);
            var domain = new EffectFlowDomain(this, method, semanticModel, accumulator, captures);
            if (method.MethodKind == MethodKind.StaticConstructor)
                state = AnalyzeStaticInitializers(method, accumulator, state);
            var root = MethodBodyOperationResolver.GetMethodBodyRootOperation(declaration, semanticModel, cancellationToken, true);
            if (root == null && declaration is AnonymousFunctionExpressionSyntax &&
                semanticModel.GetOperation(declaration, cancellationToken) is IAnonymousFunctionOperation anonymous)
                root = anonymous.Body;
            if (root == null && declaration is TypeDeclarationSyntax typeDeclaration) {
                if (method.MethodKind == MethodKind.StaticConstructor) {
                    var initializer = new CompilerMethodEffectSummary(accumulator.Build(), EffectFlowValue.None,
                        state.Receiver, state.Parameters);
                    if (captures == null) _cache[method] = initializer;
                    return initializer;
                }
                state = domain.AnalyzePrimaryInitializers(typeDeclaration, state);
                var primary = new CompilerMethodEffectSummary(accumulator.Build(), EffectFlowValue.None,
                    state.Receiver, state.Parameters);
                if (captures == null) _cache[method] = primary;
                return primary;
            }
            if (root == null) return MetadataSummary(method);
            ControlFlowGraph? graph;
            try { graph = ControlFlowGraph.Create(declaration, semanticModel, cancellationToken); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { graph = null; }
            if (method.MethodKind == MethodKind.Constructor) {
                var initializedReceiver = domain.ApplyDeclaredInitializers(state.Receiver, method.ContainingType, ref state);
                state = state with { Receiver = initializedReceiver };
            }
            state = domain.AnalyzeConstructorInitializer(declaration, state);
            EffectFlowState finalState;
            if (graph == null) {
                finalState = state;
                foreach (var operation in root.ChildOperations)
                    finalState = domain.Transfer(finalState, operation);
            }
            else {
                domain.SetControlFlowGraph(graph);
                var result = BoundedControlFlowAnalysis.Run(graph, state, domain, cancellationToken);
                if (result.Truncated) accumulator.AddUnknown(declaration, "effect_cfg_budget_exhausted");
                finalState = result.Exits.TryGetValue(graph.Blocks[graph.Blocks.Length - 1], out var exit)
                    ? exit
                    : result.Exits.Values.Aggregate(state, static (current, value) => current.Merge(value));
            }
            finalState = domain.AnalyzeSemanticAdapters(root, finalState);
            finalState = domain.AnalyzeExpressionBody(declaration, finalState);
            var summary = new CompilerMethodEffectSummary(
                accumulator.Build(), domain.ReturnValue, finalState.Receiver, finalState.Parameters,
                accumulator.WrittenArgumentOrdinals, accumulator.ReadArgumentOrdinals,
                accumulator.BoundArgumentEffects, accumulator.BoundReceiverEffects);
            summary = summary with { Effects = AddCompilerAllocations(summary.Effects, declaration, semanticModel) };
            if (smtAnalysis != null) summary = summary with { Effects = AddRuntimeHazards(summary.Effects, declaration, semanticModel) };
            if (captures == null) _cache[method] = summary;
            return summary;
        }
        finally { _active.Remove(method); }
    }
    private EffectFlowState AnalyzeStaticInitializers(
        IMethodSymbol method,
        EffectAccumulator accumulator,
        EffectFlowState state) {
        foreach (var reference in method.ContainingType.DeclaringSyntaxReferences) {
            if (reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration) continue;
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            var domain = new EffectFlowDomain(this, method, model, accumulator, null);
            foreach (var member in declaration.Members) {
                if (member is BaseFieldDeclarationSyntax field) {
                    foreach (var variable in field.Declaration.Variables) {
                        if (variable.Initializer == null ||
                            model.GetDeclaredSymbol(variable, cancellationToken) is not { IsStatic: true } symbol)
                            continue;
                        accumulator.Add(SharpProofEffect.WritesStaticState, variable, symbol, "static_initializer_write");
                        if (model.GetOperation(variable.Initializer.Value, cancellationToken) is { } operation)
                            state = domain.Transfer(state, operation);
                    }
                }
                else if (member is PropertyDeclarationSyntax { Initializer: { } initializer } property &&
                         model.GetDeclaredSymbol(property, cancellationToken) is { IsStatic: true } symbol) {
                    accumulator.Add(SharpProofEffect.WritesStaticState, property, symbol, "static_initializer_write");
                    if (model.GetOperation(initializer.Value, cancellationToken) is { } operation)
                        state = domain.Transfer(state, operation);
                }
            }
        }
        return state;
    }
    private CompilerMethodEffectSummary MetadataSummary(IMethodSymbol method) {
        if (TryReadEffectContract(method, out var contract))
            return new(contract, EffectFlowValue.Unknown, EffectFlowValue.Unknown, []);
        var effects = _metadata.Analyze(method);
        return new(effects, EffectFlowValue.Unknown, EffectFlowValue.Unknown, []);
    }
    private static CompilerMethodEffectSummary UnknownSummary(string reason, SyntaxNode site, IMethodSymbol method) => new(
        new MethodEffects(
            SharpProofEffect.Unknown,
            SharpProofCapability.None,
            [MethodExceptionFact.Boundary("System.Exception", MethodExceptionSource.Unknown, reason, SharpProofVerdict.Unknown)],
            [Site(SharpProofEffect.Unknown, site, method, reason)],
            [Reason(reason)]),
        EffectFlowValue.Unknown,
        EffectFlowValue.Unknown,
        []);
    private static IMethodSymbol Normalize(IMethodSymbol method) =>
        ((method.ReducedFrom ?? method).PartialImplementationPart ?? method.ReducedFrom ?? method).OriginalDefinition;
    private bool TryReadEffectContract(IMethodSymbol method, out MethodEffects effects) {
        var matches = method.GetAttributes().Where(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "SharpProof.Attributes.EffectContractAttribute").ToArray();
        var configured = externalContractResolver?.Invoke(method);
        if (matches.Length == 0) {
            effects = configured!;
            return configured != null;
        }
        var result = Contract(matches[0]);
        for (var index = 1; index < matches.Length; index++) {
            var next = Contract(matches[index]);
            if ((result.Effects & ~SharpProofEffect.Unknown) != (next.Effects & ~SharpProofEffect.Unknown) &&
                (result.Effects & SharpProofEffect.Unknown) == 0 && (next.Effects & SharpProofEffect.Unknown) == 0)
                result = Union(result, next) with {
                    Effects = result.Effects | next.Effects | SharpProofEffect.Unknown,
                    UnknownReasons = [Reason("conflicting_effect_contracts")]
                };
            else result = Union(result, next);
        }
        effects = configured == null ? result : Union(result, configured);
        return true;
    }
    private static MethodEffects Contract(AttributeData attribute) {
        var rawValue = attribute.ConstructorArguments.Length == 0 ? null : attribute.ConstructorArguments[0].Value;
        var value = rawValue != null
            ? (SharpProofEffect)Convert.ToInt64(rawValue, CultureInfo.InvariantCulture)
            : SharpProofEffect.Unknown;
        var capabilities = ReadNamed(attribute, "Capabilities", SharpProofCapability.None);
        var complete = ReadNamed(attribute, "Complete", false);
        var deterministic = ReadNamed(attribute, "IsDeterministic", true);
        var exceptionArgument = attribute.NamedArguments.FirstOrDefault(static pair => pair.Key == "ThrownExceptions");
        var exceptions = exceptionArgument.Key == null ? [] : exceptionArgument.Value.Values;
        if (!deterministic) value |= SharpProofEffect.UsesNondeterminism;
        if (!exceptions.IsDefaultOrEmpty) value |= SharpProofEffect.Throws;
        var invalid = !EnumFlagsDefined(value) || !EnumFlagsDefined(capabilities);
        if (!complete) value |= SharpProofEffect.Unknown;
        if (invalid) value |= SharpProofEffect.Unknown;
        return new(
            value,
            capabilities,
            [.. exceptions.Where(static item => item.Value is ITypeSymbol).Select(static item =>
                MethodExceptionFact.Boundary(((ITypeSymbol)item.Value!).ToDisplayString(), MethodExceptionSource.Contract,
                    "effect_contract"))],
            [],
            invalid ? [ConfigurationReason("invalid_effect_contract_flags")] : complete ? [] : [Reason("incomplete_effect_contract")]);
    }
    private static T ReadNamed<T>(AttributeData attribute, string name, T fallback) where T : struct {
        var value = attribute.NamedArguments.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.Ordinal)).Value.Value;
        if (value == null) return fallback;
        return typeof(T).IsEnum ? (T)Enum.ToObject(typeof(T), value) : (T)value;
    }
    private MethodEffects AddRuntimeHazards(MethodEffects effects, SyntaxNode declaration, SemanticModel semanticModel) {
        var result = effects;
        var hazards = new SymbolicRuntimeHazardQueryService().QueryNodeRuntimeHazards(
            declaration, semanticModel, smtAnalysis!, cancellationToken,
            new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true));
        foreach (var hazard in hazards.Hazards) {
            if (hazard.Kind == SymbolicRuntimeHazardKind.DirectThrow &&
                hazard.Category.IndexOf("throw_null", StringComparison.Ordinal) < 0)
                continue;
            var escape = hazard.Status switch {
                SymbolicRuntimeHazardStatus.Proven => SharpProofVerdict.Proven,
                SymbolicRuntimeHazardStatus.Unreachable => SharpProofVerdict.Disproven,
                _ => SharpProofVerdict.Unknown
            };
            if (IsCaughtHazard(declaration, semanticModel, hazard.SpanStart, hazard.ExceptionType))
                escape = SharpProofVerdict.Disproven;
            if (escape == SharpProofVerdict.Proven && result.ExceptionFacts.Any(fact =>
                    fact.Escape == SharpProofVerdict.Disproven && fact.Source == MethodExceptionSource.ExplicitThrow &&
                    (fact.SpanStart <= hazard.SpanStart && hazard.SpanStart <= fact.SpanStart + fact.SpanLength ||
                     hazard.Category.IndexOf("throw", StringComparison.OrdinalIgnoreCase) >= 0)))
                escape = SharpProofVerdict.Disproven;
            var fact = new MethodExceptionFact(
                hazard.ExceptionType, escape, MethodExceptionSource.RuntimeHazard, hazard.Category, string.Empty,
                hazard.SpanStart, hazard.SpanEnd - hazard.SpanStart, false, hazard.Category, hazard.Kind.ToString());
            result = result with {
                Effects = escape == SharpProofVerdict.Proven ? result.Effects | SharpProofEffect.Throws : result.Effects,
                ExceptionFacts = result.ExceptionFacts.Add(fact)
            };
        }
        return result;
    }
    private static bool IsCaughtHazard(SyntaxNode declaration, SemanticModel model, int position, string exceptionType) {
        var node = declaration.FindToken(position).Parent;
        var thrownType = node?.AncestorsAndSelf().OfType<ThrowStatementSyntax>().FirstOrDefault()?.Expression is { } expression
            ? model.GetTypeInfo(expression).Type as INamedTypeSymbol
            : null;
        var hazardType = thrownType ?? model.Compilation.GetTypeByMetadataName(exceptionType);
        for (var current = node; current != null; current = current.Parent) {
            if (current is not TryStatementSyntax statement) continue;
            var inTry = statement.Block.Span.Contains(position);
            var inCatch = statement.Catches.Any(clause => clause.Block.Span.Contains(position));
            if (!inTry && !inCatch) continue;
            if (statement.Finally is { } finallyClause &&
                model.AnalyzeControlFlow(finallyClause.Block) is { EndPointIsReachable: false })
                return true;
            if (!inTry) continue;
            foreach (var clause in statement.Catches) {
                if (clause.Filter != null && model.GetConstantValue(clause.Filter.FilterExpression) is not
                    { HasValue: true, Value: true })
                    continue;
                if (clause.Declaration == null) return true;
                var caught = model.GetTypeInfo(clause.Declaration.Type).Type;
                if (caught != null && (caught.ToDisplayString() == exceptionType || caught.Name == exceptionType)) return true;
                for (var candidate = hazardType; candidate != null; candidate = candidate.BaseType)
                    if (SymbolEqualityComparer.Default.Equals(candidate, caught)) return true;
            }
        }
        return false;
    }
    private static MethodEffects AddCompilerAllocations(MethodEffects effects, SyntaxNode declaration, SemanticModel model) {
        var sites = effects.Sites.ToBuilder();
        var flags = effects.Effects;
        var executionNodes = CSharpSyntaxFacts.DescendantNodesInExecution(declaration);
        foreach (var expression in executionNodes.OfType<ExpressionSyntax>()) {
            if (ReferenceEquals(expression, declaration)) continue;
            var type = model.GetTypeInfo(expression).ConvertedType;
            var allocates = expression switch {
                BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) &&
                                                  type?.SpecialType == SpecialType.System_String &&
                                                  !model.GetConstantValue(expression).HasValue => true,
                InterpolatedStringExpressionSyntax when type?.SpecialType == SpecialType.System_String => true,
                WithExpressionSyntax when type?.IsReferenceType == true => true,
                AnonymousFunctionExpressionSyntax when type?.TypeKind == TypeKind.Delegate => true,
                CollectionExpressionSyntax when type is IArrayTypeSymbol || type?.IsReferenceType == true => true,
                _ => type?.TypeKind == TypeKind.Delegate && model.GetSymbolInfo(expression).Symbol is IMethodSymbol
            };
            if (!allocates) continue;
            flags |= SharpProofEffect.Allocates;
            sites.Add(Site(SharpProofEffect.Allocates, expression, type, "compiler_generated_allocation"));
        }
        if (executionNodes.OfType<YieldStatementSyntax>().Any() ||
            IsAsyncDeclaration(declaration) && executionNodes.OfType<AwaitExpressionSyntax>().Any()) {
            flags |= SharpProofEffect.Allocates;
            sites.Add(Site(SharpProofEffect.Allocates, declaration, null, "state_machine_allocation"));
        }
        return effects with { Effects = flags, Sites = sites.ToImmutable() };
    }
    private static bool IsAsyncDeclaration(SyntaxNode declaration) => declaration switch {
        MethodDeclarationSyntax method => method.Modifiers.Any(SyntaxKind.AsyncKeyword),
        LocalFunctionStatementSyntax localFunction => localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword),
        AnonymousFunctionExpressionSyntax anonymousFunction => anonymousFunction.AsyncKeyword.RawKind != 0,
        _ => false
    };
    private static bool EnumFlagsDefined<T>(T value) where T : struct, Enum {
        var all = Enum.GetValues(typeof(T)).Cast<T>().Aggregate(0L, static (bits, item) => bits | Convert.ToInt64(item));
        return (Convert.ToInt64(value) & ~all) == 0;
    }
    private static MethodEffects Union(MethodEffects left, MethodEffects right) => new(
        left.Effects | right.Effects,
        left.Capabilities | right.Capabilities,
        [.. left.ExceptionFacts.AddRange(right.ExceptionFacts).Distinct()],
        [.. left.Sites.AddRange(right.Sites).Distinct()],
        [.. left.UnknownReasons.AddRange(right.UnknownReasons).Distinct()]);
    private static MethodEffects WrapTypeInitializerExceptions(MethodEffects summary) {
        var escaping = summary.ExceptionFacts.Where(static fact => fact.Escape != SharpProofVerdict.Disproven).ToArray();
        if (escaping.Length == 0) return summary;
        var escape = escaping.Any(static fact => fact.Escape == SharpProofVerdict.Proven)
            ? SharpProofVerdict.Proven
            : SharpProofVerdict.Unknown;
        var inner = summary.ExceptionFacts.Select(static fact => fact.Escape == SharpProofVerdict.Disproven
            ? fact
            : fact with { Escape = SharpProofVerdict.Disproven, Reason = "type_initializer_inner_exception" });
        return summary with {
            ExceptionFacts = [.. inner.Append(MethodExceptionFact.Boundary(
                "System.TypeInitializationException", MethodExceptionSource.Callee,
                "type_initializer_exception", escape))]
        };
    }
    private static MethodEffectSite Site(SharpProofEffect effect, SyntaxNode syntax, ISymbol? symbol, string reason) => new(
        effect, SharpProofCapability.None, syntax.ToString(), symbol?.ToDisplayString() ?? string.Empty,
        syntax.SpanStart, syntax.Span.Length, false, reason, Origin(effect));
    private static SharpProofUnknownReason Reason(string reason) => new("SP-EFFECT-UNKNOWN", "Effects", reason, false, false);
    private static SharpProofUnknownReason ConfigurationReason(string reason) =>
        new("SP-EFFECT-CONFIG", "Effects", reason, false, true);
    private static MethodEffectOrigin Origin(SharpProofEffect effect) {
        if ((effect & (SharpProofEffect.ReadsAmbientState | SharpProofEffect.WritesAmbientState)) != 0) return MethodEffectOrigin.Ambient;
        if ((effect & (SharpProofEffect.ReadsReceiverState | SharpProofEffect.WritesReceiverState)) != 0) return MethodEffectOrigin.Receiver;
        if ((effect & (SharpProofEffect.ReadsArgumentState | SharpProofEffect.WritesArgumentState)) != 0) return MethodEffectOrigin.Argument;
        if ((effect & (SharpProofEffect.ReadsCapturedState | SharpProofEffect.WritesCapturedState)) != 0) return MethodEffectOrigin.Captured;
        if ((effect & (SharpProofEffect.ReadsStaticState | SharpProofEffect.WritesStaticState)) != 0) return MethodEffectOrigin.Static;
        if ((effect & SharpProofEffect.WritesFreshOwnedState) != 0) return MethodEffectOrigin.FreshOwned;
        if ((effect & SharpProofEffect.Allocates) != 0) return MethodEffectOrigin.Allocation;
        return MethodEffectOrigin.Unknown;
    }

    private sealed class EffectFlowDomain(
        MethodEffectAnalysisSession session,
        IMethodSymbol method,
        SemanticModel semanticModel,
        EffectAccumulator effects,
        ImmutableDictionary<string, EffectFlowValue>? boundCaptures) : IControlFlowDomain<EffectFlowState> {
        private readonly HashSet<(OperationKind Kind, TextSpan Span)> _unreachableOperations = [];
        private readonly HashSet<(OperationKind Kind, TextSpan Span)> _reachableOperations = [];
        internal EffectFlowValue ReturnValue { get; private set; } = EffectFlowValue.None;
        private EffectFlowValue LastValue { get; set; } = EffectFlowValue.None;
        public EffectFlowState Transfer(EffectFlowState state, IOperation operation) {
            session.CancellationToken.ThrowIfCancellationRequested();
            if (state.IsUnreachable) return state;
            LastValue = Evaluate(operation, ref state);
            return state;
        }
        public EffectFlowState Refine(EffectFlowState state, IOperation? condition, ControlFlowConditionKind kind,
            bool conditionalSuccessor) {
            if (condition is not IIsNullOperation isNull) return state;
            var value = ResolveConditionValue(isNull.Operand, state);
            var branchWhenTrue = kind switch {
                ControlFlowConditionKind.WhenTrue => conditionalSuccessor,
                ControlFlowConditionKind.WhenFalse => !conditionalSuccessor,
                _ => false
            };
            if (branchWhenTrue && value.IsDefinitelyNonNull || !branchWhenTrue && value.IsDefinitelyNull)
                return state with { IsUnreachable = true };
            var isCoalesceAssignment = isNull.Syntax.Ancestors().OfType<AssignmentExpressionSyntax>()
                .Any(static assignment => assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression));
            return branchWhenTrue || !isCoalesceAssignment
                ? state
                : RefineConditionValue(isNull.Operand, state, value.AsDefinitelyNonNull());
        }
        public EffectFlowState Merge(EffectFlowState current, EffectFlowState incoming) => current.Merge(incoming);
        public EffectFlowState Widen(EffectFlowState previous, EffectFlowState current, BasicBlock block) => current;
        public EffectFlowState CompleteBlock(EffectFlowState state, BasicBlock block) {
            if (state.IsUnreachable) return state;
            if (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return ||
                block.ConditionalSuccessor?.Semantics == ControlFlowBranchSemantics.Return)
                AddReturn(LastValue);
            return state;
        }
        public bool Equivalent(EffectFlowState left, EffectFlowState right) => string.Equals(left.Key, right.Key, StringComparison.Ordinal);
        private static EffectFlowValue ResolveConditionValue(IOperation operation, EffectFlowState state) => operation switch {
            IFlowCaptureReferenceOperation capture => state.GetCapture(capture.Id),
            ILocalReferenceOperation local => state.GetLocal(local.Local),
            IParameterReferenceOperation parameter => state.GetParameter(parameter.Parameter),
            IConversionOperation conversion => ResolveConditionValue(conversion.Operand, state),
            _ => EffectFlowValue.Unknown
        };
        private EffectFlowState RefineConditionValue(
            IOperation operation,
            EffectFlowState state,
            EffectFlowValue value) {
            while (operation is IConversionOperation conversion) operation = conversion.Operand;
            state = operation switch {
                IFlowCaptureReferenceOperation capture => state with {
                    FlowCaptures = state.FlowCaptures.SetItem(capture.Id, value)
                },
                ILocalReferenceOperation local => state with { Locals = state.Locals.SetItem(local.Local, value) },
                IParameterReferenceOperation parameter => state.SetParameter(parameter.Parameter, value),
                _ => state
            };
            return semanticModel.GetSymbolInfo(operation.Syntax, session.CancellationToken).Symbol switch {
                ILocalSymbol local => state with { Locals = state.Locals.SetItem(local, value) },
                IParameterSymbol parameter => state.SetParameter(parameter, value),
                _ => state
            };
        }
        internal void SetControlFlowGraph(ControlFlowGraph graph) {
            foreach (var block in graph.Blocks)
                foreach (var operation in block.Operations.Append(block.BranchValue).Where(static value => value != null)
                             .SelectMany(static value => value!.DescendantsAndSelf())) {
                    var span = operation.Syntax.Span;
                    (block.IsReachable ? _reachableOperations : _unreachableOperations).Add((operation.Kind, span));
                }
        }
        internal EffectFlowState AnalyzeSemanticAdapters(IOperation root, EffectFlowState state) {
            foreach (var operation in root.DescendantsAndSelf())
                if (BelongsToCurrentMethod(operation) &&
                    operation is IThrowOperation or ICoalesceAssignmentOperation or IDeconstructionAssignmentOperation or
                    IAwaitOperation or IForEachLoopOperation or IUsingOperation or ILockOperation or
                    IUsingDeclarationOperation or ICollectionExpressionOperation or IWithOperation or
                    IRecursivePatternOperation or IListPatternOperation or ISlicePatternOperation)
                    Evaluate(operation, ref state);
            var syntaxNodes = CSharpSyntaxFacts.DescendantNodesInExecution(root.Syntax, includeSelf: false);
            if (method.MethodKind == MethodKind.Constructor)
                foreach (var assignmentSyntax in syntaxNodes.OfType<AssignmentExpressionSyntax>()) {
                    var operation = semanticModel.GetOperation(assignmentSyntax, session.CancellationToken);
                    if (operation is ISimpleAssignmentOperation assignment) {
                        var assigned = Evaluate(assignment.Value, ref state);
                        if (assignmentSyntax.Ancestors().Any(static node => node is IfStatementSyntax or SwitchStatementSyntax))
                            assigned = Evaluate(assignment.Target, ref state).Merge(assigned);
                        Assign(assignment.Target, assigned, assignment.IsRef, ref state);
                    }
                    else if (operation is IDeconstructionAssignmentOperation deconstruction) {
                        var assigned = Evaluate(deconstruction.Value, ref state);
                        Assign(deconstruction.Target, assigned, false, ref state);
                    }
                }
            foreach (var fixedSyntax in syntaxNodes.OfType<FixedStatementSyntax>())
                foreach (var variable in fixedSyntax.Declaration.Variables) {
                    var initializer = variable.Initializer?.Value;
                    if (initializer == null) continue;
                    var initializerOperation = semanticModel.GetOperation(initializer, session.CancellationToken);
                    var fixedReceiver = Evaluate(initializerOperation, ref state);
                    InvokeCoreOrValue(FindProtocolMethod(initializerOperation?.Type, "GetPinnableReference", 0),
                        fixedReceiver, initializerOperation!, ref state);
                }
            foreach (var declarator in syntaxNodes.OfType<VariableDeclaratorSyntax>())
                if (semanticModel.GetDeclaredSymbol(declarator, session.CancellationToken) is ILocalSymbol {
                    RefKind: not RefKind.None
                } refLocal && declarator.Initializer?.Value is { } initializer) {
                    var value = Evaluate(semanticModel.GetOperation(initializer, session.CancellationToken), ref state);
                    state = state with { RefLocals = state.RefLocals.SetItem(refLocal, value) };
                }
                else if (semanticModel.GetDeclaredSymbol(declarator, session.CancellationToken) is ILocalSymbol local &&
                         declarator.Initializer?.Value is { } valueSyntax &&
                         (valueSyntax.DescendantNodesAndSelf().OfType<CollectionExpressionSyntax>().Any() ||
                          valueSyntax.DescendantNodesAndSelf().OfType<InitializerExpressionSyntax>().Any())) {
                    var value = Evaluate(semanticModel.GetOperation(valueSyntax, session.CancellationToken), ref state);
                    state = state with { Locals = state.Locals.SetItem(local, value) };
                }
            foreach (var returned in syntaxNodes.OfType<ReturnStatementSyntax>())
                if (returned.Expression is AnonymousFunctionExpressionSyntax) {
                    var value = Evaluate(semanticModel.GetOperation(returned.Expression, session.CancellationToken), ref state);
                    if (!ReferenceEquals(value, EffectFlowValue.None)) ReturnValue = value;
                }
            foreach (var assignment in syntaxNodes.OfType<AssignmentExpressionSyntax>())
                if (assignment.Left.DescendantNodesAndSelf().OfType<ConditionalExpressionSyntax>().FirstOrDefault() is { } conditional) {
                    var target = Evaluate(semanticModel.GetOperation(conditional, session.CancellationToken), ref state);
                    effects.Write(target, assignment.Left, null, "conditional_ref_write");
                }
                else if (semanticModel.GetSymbolInfo(assignment.Left, session.CancellationToken).Symbol is ILocalSymbol {
                    RefKind: not RefKind.None
                } refLocal && !assignment.Right.IsKind(SyntaxKind.RefExpression))
                    effects.Write(state.GetRef(refLocal), assignment, refLocal, "ref_local_write");
                else if (semanticModel.GetSymbolInfo(assignment.Left, session.CancellationToken).Symbol is
                         IMethodSymbol { ReturnsByRef: true } or IPropertySymbol { ReturnsByRef: true }) {
                    var target = Evaluate(semanticModel.GetOperation(assignment.Left, session.CancellationToken), ref state);
                    effects.Write(target, assignment.Left, null, "ref_return_write");
                }
            foreach (var filter in syntaxNodes.OfType<CatchFilterClauseSyntax>()) {
                var filterState = state;
                Evaluate(semanticModel.GetOperation(filter.FilterExpression, session.CancellationToken), ref filterState);
                state = state.Merge(filterState);
            }
            return state;
        }
        internal EffectFlowState AnalyzeConstructorInitializer(SyntaxNode declaration, EffectFlowState state) {
            if (method.MethodKind != MethodKind.Constructor) return state;
            IMethodSymbol? target = null;
            SeparatedSyntaxList<ArgumentSyntax> arguments = default;
            SyntaxNode site = declaration;
            if (declaration is ConstructorDeclarationSyntax constructor) {
                site = constructor.Initializer is null ? constructor : constructor.Initializer;
                if (constructor.Initializer != null) {
                    target = semanticModel.GetSymbolInfo(constructor.Initializer, session.CancellationToken).Symbol as IMethodSymbol;
                    arguments = constructor.Initializer.ArgumentList.Arguments;
                }
                else target = method.ContainingType.BaseType?.InstanceConstructors
                    .FirstOrDefault(static candidate => candidate.Parameters.Length == 0);
            }
            if (target == null) return state;
            var values = arguments.Select(argument => Evaluate(semanticModel.GetOperation(argument.Expression,
                session.CancellationToken), ref state)).ToArray();
            effects.Add(SharpProofEffect.DirectCall, site, target, "direct_call");
            var summary = GetSummary(target, null);
            AddSummary(summary.Effects, state.Receiver, values, semanticModel.GetOperation(site, session.CancellationToken) ??
                semanticModel.GetOperation(declaration, session.CancellationToken)!, target,
                summary.WrittenArgumentOrdinals, summary.ReadArgumentOrdinals,
                summary.BoundArgumentEffects, summary.BoundReceiverEffects);
            return state with { Receiver = summary.Receiver.Instantiate(state.Receiver, values, sourceMethod: target) };
        }
        internal EffectFlowState AnalyzePrimaryInitializers(TypeDeclarationSyntax declaration, EffectFlowState state) {
            foreach (var parameter in method.Parameters)
                if (method.ContainingType.GetMembers().OfType<IPropertySymbol>().FirstOrDefault(property =>
                        string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)) is { } property)
                    state = state with {
                        Receiver = state.Receiver.WithMember(MemberKey(property), state.GetParameter(parameter))
                    };
            state = state with { Receiver = ApplyDeclaredInitializers(state.Receiver, method.ContainingType, ref state, false) };
            if (declaration.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().FirstOrDefault() is { } baseType &&
                semanticModel.GetSymbolInfo(baseType, session.CancellationToken).Symbol is IMethodSymbol constructor) {
                var values = baseType.ArgumentList.Arguments.Select(argument => Evaluate(semanticModel.GetOperation(
                    argument.Expression, session.CancellationToken), ref state)).ToArray();
                effects.Add(SharpProofEffect.DirectCall, baseType, constructor, "direct_call");
                var summary = GetSummary(constructor, null);
                AddSummary(summary.Effects, state.Receiver, values, semanticModel.GetOperation(baseType,
                    session.CancellationToken)!, constructor, summary.WrittenArgumentOrdinals,
                    summary.ReadArgumentOrdinals, summary.BoundArgumentEffects, summary.BoundReceiverEffects);
                state = state with { Receiver = summary.Receiver.Instantiate(state.Receiver, values, sourceMethod: constructor) };
            }
            return state;
        }
        internal EffectFlowState AnalyzeExpressionBody(SyntaxNode declaration, EffectFlowState state) {
            if (!ReferenceEquals(ReturnValue, EffectFlowValue.None)) return state;
            var expression = declaration.ChildNodes().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault()?.Expression;
            if (expression == null) return state;
            var value = Evaluate(semanticModel.GetOperation(expression, session.CancellationToken), ref state);
            AddReturn(value);
            return state;
        }

        private EffectFlowValue Evaluate(IOperation? operation, ref EffectFlowState state) {
            if (operation == null) return EffectFlowValue.None;
            if (!BelongsToCurrentMethod(operation) || IsCompilerLoweredProtocolOperation(operation) || IsUnreachable(operation) ||
                IsCompileTimeSkipped(operation))
                return EffectFlowValue.None;
            if (operation is IFlowAnonymousFunctionOperation) {
                var semanticFunction = semanticModel.GetOperation(operation.Syntax, session.CancellationToken);
                if (semanticFunction is IAnonymousFunctionOperation flowFunction)
                    return BindCallable(flowFunction, flowFunction.Type, ref state);
                if (semanticFunction is IDelegateCreationOperation { Target: IAnonymousFunctionOperation targetFunction } creation)
                    return BindCallable(targetFunction, creation.Type, ref state);
            }
            switch (operation) {
                case IExpressionStatementOperation expression:
                    return Evaluate(expression.Operation, ref state);
                case IVariableDeclarationGroupOperation group:
                    foreach (var declaration in group.Declarations)
                        foreach (var declarator in declaration.Declarators)
                            Evaluate(declarator, ref state);
                    return EffectFlowValue.None;
                case IVariableDeclaratorOperation declarator:
                    var initial = Evaluate(declarator.Initializer?.Value, ref state);
                    state = state with { Locals = state.Locals.SetItem(declarator.Symbol, initial) };
                    if (declarator.Symbol.RefKind != RefKind.None)
                        state = state with { RefLocals = state.RefLocals.SetItem(declarator.Symbol, initial) };
                    return initial;
                case IFlowCaptureOperation capture:
                    var captured = Evaluate(capture.Value, ref state);
                    state = state with { FlowCaptures = state.FlowCaptures.SetItem(capture.Id, captured) };
                    return captured;
                case IFlowCaptureReferenceOperation captureReference:
                    return state.GetCapture(captureReference.Id);
                case ILocalReferenceOperation local:
                    var localKey = EffectFlowState.SymbolKey(local.Local);
                    return boundCaptures != null && boundCaptures.TryGetValue(localKey, out var boundLocal)
                        ? boundLocal
                        : local.Local.RefKind == RefKind.None ? state.GetLocal(local.Local) : state.GetRef(local.Local);
                case IParameterReferenceOperation parameter:
                    var parameterKey = EffectFlowState.SymbolKey(parameter.Parameter);
                    return boundCaptures != null && boundCaptures.TryGetValue(parameterKey, out var boundParameter)
                        ? boundParameter
                        : state.GetParameter(parameter.Parameter);
                case IDiscardOperation:
                    return EffectFlowValue.None;
                case IDeclarationExpressionOperation declaration:
                    return Evaluate(declaration.Expression, ref state);
                case IInstanceReferenceOperation instance:
                    var instanceKey = EffectFlowState.SymbolKey(instance.Type ?? method.ContainingType);
                    return boundCaptures != null && boundCaptures.TryGetValue(instanceKey, out var boundReceiver)
                        ? boundReceiver
                        : state.Receiver;
                case IParenthesizedOperation parenthesized:
                    return Evaluate(parenthesized.Operand, ref state);
                case IConversionOperation conversion:
                    return EvaluateConversion(conversion, ref state);
                case ILiteralOperation { ConstantValue: { HasValue: true, Value: not null } } literal:
                    return EffectFlowValue.KnownNonNull(literal.Type);
                case ILiteralOperation { ConstantValue: { HasValue: true, Value: null } }:
                    return EffectFlowValue.KnownNull;
                case ITypeOfOperation typeOf:
                    return EffectFlowValue.KnownNonNull(typeOf.Type);
                case INameOfOperation nameOf:
                    return EffectFlowValue.KnownNonNull(nameOf.Type);
                case ILiteralOperation or IDefaultValueOperation:
                    return EffectFlowValue.None;
                case IReturnOperation returned:
                    var returnedValue = Evaluate(returned.ReturnedValue, ref state);
                    AddReturn(returnedValue);
                    return returnedValue;
                case ISimpleAssignmentOperation assignment:
                    var value = Evaluate(assignment.Value, ref state);
                    Assign(assignment.Target, value, assignment.IsRef, ref state);
                    return value;
                case IDeconstructionAssignmentOperation deconstruction:
                    var deconstructed = Evaluate(deconstruction.Value, ref state);
                    if (deconstruction.Syntax is AssignmentExpressionSyntax deconstructionSyntax &&
                        semanticModel.GetDeconstructionInfo(deconstructionSyntax).Method is { } deconstructMethod)
                        InvokeCore(deconstructMethod, deconstructed,
                            deconstructMethod.ReducedFrom != null || deconstructMethod.IsExtensionMethod ? [deconstructed] : [],
                            [], deconstruction, ref state);
                    Assign(deconstruction.Target, deconstructed, false, ref state);
                    return deconstructed;
                case ICoalesceAssignmentOperation coalesceAssignment:
                    var current = Evaluate(coalesceAssignment.Target, ref state);
                    if (current.IsDefinitelyNonNull) return current;
                    var fallback = Evaluate(coalesceAssignment.Value, ref state);
                    var merged = current.Merge(fallback);
                    if (fallback.IsDefinitelyNonNull) merged = merged.AsDefinitelyNonNull();
                    Assign(coalesceAssignment.Target, merged, false, ref state);
                    return merged;
                case ICompoundAssignmentOperation compound:
                    var compoundTarget = Evaluate(compound.Target, ref state);
                    var compoundValue = Evaluate(compound.Value, ref state);
                    if (compound.Type?.TypeKind == TypeKind.Delegate) {
                        if (compound.OperatorKind == BinaryOperatorKind.Add)
                            Assign(compound.Target, compoundTarget.Merge(compoundValue), false, ref state);
                        return compoundTarget;
                    }
                    var compoundResult = compound.OperatorMethod == null &&
                                         (compound.Target.Type?.TypeKind == TypeKind.Dynamic ||
                                          compound.Value.Type?.TypeKind == TypeKind.Dynamic)
                        ? DynamicDispatch(compound, "dynamic_operator_dispatch")
                        : compound.OperatorMethod == null
                            ? compoundTarget
                        : InvokeCore(compound.OperatorMethod, EffectFlowValue.None,
                            [compoundTarget, compoundValue], [], compound, ref state);
                    Assign(compound.Target, compoundResult, false, ref state);
                    return compoundResult;
                case IIncrementOrDecrementOperation increment:
                    var incrementTarget = Evaluate(increment.Target, ref state);
                    if (incrementTarget.Roots.Any(static root => root.Kind == EffectValueRootKind.Unknown) &&
                        increment.Syntax.Ancestors().OfType<ForEachStatementSyntax>().FirstOrDefault(loop =>
                            loop.Type.ToString().StartsWith("ref ", StringComparison.Ordinal)) is { } refLoop &&
                        semanticModel.GetOperation(refLoop.Expression, session.CancellationToken) is IConversionOperation refConversion) {
                        incrementTarget = EvaluateConversion(refConversion, ref state);
                        if (increment.Target is ILocalReferenceOperation { Local.RefKind: not RefKind.None } refLocal)
                            state = state with { RefLocals = state.RefLocals.SetItem(refLocal.Local, incrementTarget) };
                    }
                    var incrementResult = increment.OperatorMethod == null &&
                                          increment.Target.Type?.TypeKind == TypeKind.Dynamic
                        ? DynamicDispatch(increment, "dynamic_operator_dispatch")
                        : increment.OperatorMethod == null
                            ? incrementTarget
                        : InvokeCore(increment.OperatorMethod, EffectFlowValue.None,
                            [incrementTarget], [], increment, ref state);
                    Assign(increment.Target, incrementResult, false, ref state);
                    return incrementResult;
                case IEventAssignmentOperation {
                    EventReference: IEventReferenceOperation eventReference
                } eventAssignment:
                    var eventReceiver = eventReference.Event.IsStatic
                        ? EffectFlowValue.FromRoot(new(
                            EffectValueRootKind.Static,
                            Key: MemberKey(eventReference.Event)),
                            eventReference.Type)
                        : Evaluate(eventReference.Instance, ref state);
                    var handler = Evaluate(eventAssignment.HandlerValue, ref state);
                    var accessor = eventAssignment.Adds
                        ? eventReference.Event.AddMethod
                        : eventReference.Event.RemoveMethod;
                    if (accessor?.IsImplicitlyDeclared != false)
                        effects.Write(eventReceiver, eventAssignment.Syntax, eventReference.Event, "event_assignment");
                    if (accessor != null)
                        InvokeCore(accessor, eventReceiver, [handler], [], eventAssignment, ref state);
                    return eventReceiver;
                case IFieldReferenceOperation field:
                    return EvaluateField(field, ref state);
                case IPropertyReferenceOperation property:
                    return EvaluateProperty(property, ref state);
                case IArrayElementReferenceOperation element:
                    var array = Evaluate(element.ArrayReference, ref state);
                    foreach (var index in element.Indices) Evaluate(index, ref state);
                    effects.Read(array, element.Syntax, element.Type as ISymbol, "array_element_read");
                    return array.Member(IndexKey(element.Indices));
                case IInlineArrayAccessOperation inlineArray:
                    var inlineReceiver = Evaluate(inlineArray.Instance, ref state);
                    Evaluate(inlineArray.Argument, ref state);
                    effects.Read(inlineReceiver, inlineArray.Syntax, inlineArray.Type as ISymbol, "inline_array_read");
                    return inlineReceiver.Member("#?");
                case IImplicitIndexerReferenceOperation implicitIndexer:
                    var indexerReceiver = Evaluate(implicitIndexer.Instance, ref state);
                    if (implicitIndexer.LengthSymbol is IPropertySymbol lengthProperty)
                        InvokeCoreOrValue(lengthProperty.GetMethod, indexerReceiver, implicitIndexer, ref state);
                    if (implicitIndexer.IndexerSymbol is IPropertySymbol indexerProperty)
                        InvokeCoreOrValue(indexerProperty.GetMethod, indexerReceiver, implicitIndexer, ref state);
                    return indexerReceiver.Member("#?");
                case IObjectCreationOperation creation:
                    return EvaluateCreation(creation, ref state);
                case ITypeParameterObjectCreationOperation typeParameterCreation:
                    effects.Add(SharpProofEffect.Allocates, typeParameterCreation.Syntax,
                        typeParameterCreation.Type, "generic_object_allocation");
                    effects.Add(SharpProofEffect.DispatchUncertainty, typeParameterCreation.Syntax,
                        typeParameterCreation.Type, "generic_constructor_dispatch");
                    effects.AddUnknown(typeParameterCreation.Syntax, "generic_constructor_dispatch");
                    return EffectFlowValue.Fresh(typeParameterCreation.Type);
                case IArrayCreationOperation arrayCreation:
                    foreach (var size in arrayCreation.DimensionSizes) Evaluate(size, ref state);
                    var arrayValue = EffectFlowValue.Fresh(arrayCreation.Type);
                    if (arrayCreation.Initializer != null)
                        for (var index = 0; index < arrayCreation.Initializer.ElementValues.Length; index++)
                            arrayValue = arrayValue.WithMember("#" + index, Evaluate(arrayCreation.Initializer.ElementValues[index], ref state));
                    effects.Add(SharpProofEffect.Allocates, arrayCreation.Syntax, arrayCreation.Type, "array_allocation");
                    return arrayValue;
                case ICollectionExpressionOperation collection:
                    var collectionValue = EffectFlowValue.Fresh(collection.Type);
                    if (collection.Type is IArrayTypeSymbol || collection.Type?.IsReferenceType == true)
                        effects.Add(SharpProofEffect.Allocates, collection.Syntax, collection.Type, "collection_expression_allocation");
                    var outputIndex = 0;
                    for (var index = 0; index < collection.Elements.Length; index++) {
                        EffectFlowValue elementValue;
                        if (collection.Elements[index] is ISpreadOperation spread) {
                            var sourceOperation = spread.Syntax is SpreadElementSyntax sourceSpread
                                ? semanticModel.GetOperation(sourceSpread.Expression, session.CancellationToken)
                                : spread.Operand;
                            elementValue = Evaluate(sourceOperation, ref state);
                            AnalyzeEnumeration(spread.Operand.Type, elementValue, spread, ref state);
                            if (elementValue.Members.Count != 0)
                                foreach (var member in elementValue.Members.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                                    collectionValue = collectionValue.WithMember("#" + outputIndex++, member.Value);
                            else collectionValue = collectionValue.WithMember("#" + outputIndex++, elementValue.Member("#?"));
                        }
                        else {
                            elementValue = Evaluate(collection.Elements[index], ref state);
                            var add = FindProtocolMethod(collection.Type, "Add", 1);
                            if (add != null) InvokeCore(add, collectionValue, [elementValue], [], collection.Elements[index], ref state);
                            collectionValue = collectionValue.WithMember("#" + outputIndex++, elementValue);
                        }
                    }
                    if (collection.ConstructMethod != null)
                        InvokeCore(collection.ConstructMethod, collectionValue, [], [], collection, ref state);
                    return collectionValue;
                case IAnonymousObjectCreationOperation anonymousObject:
                    var anonymous = EffectFlowValue.Fresh(anonymousObject.Type);
                    foreach (var initializer in anonymousObject.Initializers) {
                        if (initializer is not ISimpleAssignmentOperation assignment) continue;
                        var memberValue = Evaluate(assignment.Value, ref state);
                        var key = MemberKey(assignment.Target);
                        if (key != null) anonymous = anonymous.WithMember(key, memberValue);
                    }
                    effects.Add(SharpProofEffect.Allocates, anonymousObject.Syntax, anonymousObject.Type, "anonymous_object_allocation");
                    return anonymous;
                case ITupleOperation tuple:
                    var tupleValue = EffectFlowValue.Fresh(tuple.Type);
                    for (var index = 0; index < tuple.Elements.Length; index++)
                        tupleValue = tupleValue.WithMember("#" + index, Evaluate(tuple.Elements[index], ref state));
                    return tupleValue;
                case IConditionalOperation conditional:
                    Evaluate(conditional.Condition, ref state);
                    var trueState = state;
                    var falseState = state;
                    var whenTrue = Evaluate(conditional.WhenTrue, ref trueState);
                    var whenFalse = Evaluate(conditional.WhenFalse, ref falseState);
                    state = trueState.Merge(falseState);
                    return whenTrue.Merge(whenFalse);
                case ICoalesceOperation coalesce:
                    var coalesced = Evaluate(coalesce.Value, ref state);
                    return coalesced.IsDefinitelyNonNull
                        ? coalesced
                        : MergeCoalesce(coalesced, Evaluate(coalesce.WhenNull, ref state));
                case IBinaryOperation binary:
                    var left = Evaluate(binary.LeftOperand, ref state);
                    var right = Evaluate(binary.RightOperand, ref state);
                    return binary.OperatorMethod == null &&
                           (binary.LeftOperand.Type?.TypeKind == TypeKind.Dynamic ||
                            binary.RightOperand.Type?.TypeKind == TypeKind.Dynamic)
                        ? DynamicDispatch(binary, "dynamic_operator_dispatch")
                        : binary.OperatorMethod == null
                            ? right
                        : InvokeCore(binary.OperatorMethod, EffectFlowValue.None, [left, right], [], binary, ref state);
                case IUnaryOperation unary:
                    var unaryOperand = Evaluate(unary.Operand, ref state);
                    return unary.OperatorMethod == null && unary.Operand.Type?.TypeKind == TypeKind.Dynamic
                        ? DynamicDispatch(unary, "dynamic_operator_dispatch")
                        : unary.OperatorMethod == null
                            ? unaryOperand
                        : InvokeCore(unary.OperatorMethod, EffectFlowValue.None, [unaryOperand], [], unary, ref state);
                case ISwitchExpressionOperation switchExpression:
                    Evaluate(switchExpression.Value, ref state);
                    var switchValue = EffectFlowValue.None;
                    foreach (var arm in switchExpression.Arms) {
                        var armState = state;
                        var armValue = Evaluate(arm.Value, ref armState);
                        switchValue = ReferenceEquals(switchValue, EffectFlowValue.None) ? armValue : switchValue.Merge(armValue);
                        state = state.Merge(armState);
                    }
                    return switchValue;
                case IInvocationOperation invocation:
                    if (IsOmittedInvocation(invocation)) return EffectFlowValue.None;
                    return Invoke(invocation.TargetMethod, invocation.Instance, invocation.Arguments, invocation, ref state);
                case IFunctionPointerInvocationOperation:
                    foreach (var child in operation.ChildOperations) Evaluate(child, ref state);
                    effects.Add(SharpProofEffect.DispatchUncertainty, operation.Syntax, null,
                        "function_pointer_dispatch");
                    effects.AddUnknown(operation.Syntax, "function_pointer_dispatch");
                    return EffectFlowValue.Unknown;
                case IDynamicInvocationOperation or IDynamicObjectCreationOperation or
                    IDynamicIndexerAccessOperation or IDynamicMemberReferenceOperation:
                    foreach (var child in operation.ChildOperations) Evaluate(child, ref state);
                    effects.Add(SharpProofEffect.DispatchUncertainty, operation.Syntax, null, "dynamic_dispatch");
                    effects.AddUnknown(operation.Syntax, "dynamic_dispatch");
                    return EffectFlowValue.Unknown;
                case IAwaitOperation awaited:
                    var awaitedValue = Evaluate(awaited.Operation, ref state);
                    if (awaited.Syntax is AwaitExpressionSyntax awaitSyntax) {
                        var info = semanticModel.GetAwaitExpressionInfo(awaitSyntax);
                        var awaiter = InvokeCoreOrValue(info.GetAwaiterMethod, awaitedValue, awaited, ref state);
                        InvokeCoreOrValue(info.IsCompletedProperty?.GetMethod, awaiter, awaited, ref state);
                        InvokeCoreOrValue(info.GetResultMethod, awaiter, awaited, ref state);
                        var continuation = FindProtocolMethod(info.GetAwaiterMethod?.ReturnType, "UnsafeOnCompleted", 1) ??
                                           FindImplementedProtocolMethod(
                                               info.GetAwaiterMethod?.ReturnType, "UnsafeOnCompleted", 1) ??
                                           FindProtocolMethod(info.GetAwaiterMethod?.ReturnType, "OnCompleted", 1) ??
                                           FindImplementedProtocolMethod(
                                               info.GetAwaiterMethod?.ReturnType, "OnCompleted", 1);
                        InvokeCoreOrValue(continuation, awaiter, awaited, ref state);
                    }
                    return awaitedValue;
                case IForEachLoopOperation loop:
                    return EvaluateForEach(loop, ref state);
                case IUsingOperation usingOperation:
                    var resource = Evaluate(usingOperation.Resources, ref state);
                    if (usingOperation.Resources is IVariableDeclarationGroupOperation declarations)
                        foreach (var declarator in declarations.Declarations.SelectMany(static declaration => declaration.Declarators))
                            AnalyzeDisposal(declarator.Symbol.Type, state.GetLocal(declarator.Symbol), usingOperation,
                                usingOperation.IsAsynchronous, ref state);
                    else AnalyzeDisposal(usingOperation.Resources.Type, resource, usingOperation, usingOperation.IsAsynchronous, ref state);
                    Evaluate(usingOperation.Body, ref state);
                    return resource;
                case IUsingDeclarationOperation usingDeclaration:
                    var declarationValue = Evaluate(usingDeclaration.DeclarationGroup, ref state);
                    foreach (var declarator in usingDeclaration.DeclarationGroup.Declarations.SelectMany(static value => value.Declarators))
                        AnalyzeDisposal(declarator.Symbol.Type, state.GetLocal(declarator.Symbol), usingDeclaration,
                            usingDeclaration.IsAsynchronous, ref state);
                    return declarationValue;
                case IWithOperation withOperation:
                    var original = Evaluate(withOperation.Operand, ref state);
                    var clone = EffectFlowValue.Fresh(withOperation.Type);
                    var cloneMethod = withOperation.CloneMethod;
                    if (cloneMethod != null) {
                        var exactClone = SymbolicDispatchFacts.ResolveExactDispatchTarget(cloneMethod, null, original.ExactType);
                        if (exactClone == null && (cloneMethod.IsVirtual || cloneMethod.IsOverride))
                            InvokeCore(cloneMethod, original, [], [], withOperation, ref state);
                        else if ((exactClone ?? cloneMethod).ContainingType is { } cloneType) {
                            clone = EffectFlowValue.Fresh(cloneType);
                            var copyConstructor = cloneType.InstanceConstructors.FirstOrDefault(candidate =>
                                candidate.Parameters.Length == 1 &&
                                SymbolEqualityComparer.Default.Equals(candidate.Parameters[0].Type, cloneType));
                            if (copyConstructor != null)
                                InvokeCore(copyConstructor, clone, [original], [], withOperation, ref state);
                            else InvokeCore(exactClone ?? cloneMethod, original, [], [], withOperation, ref state);
                        }
                    }
                    Evaluate(withOperation.Initializer, ref state);
                    return clone;
                case IRecursivePatternOperation { DeconstructSymbol: IMethodSymbol deconstruct } recursivePattern:
                    InvokeCoreOrValue(deconstruct, FindPatternInput(recursivePattern, ref state), recursivePattern, ref state);
                    return EffectFlowValue.None;
                case IListPatternOperation listPattern:
                    var patternValue = FindPatternInput(listPattern, ref state);
                    if (listPattern.LengthSymbol is IPropertySymbol length)
                        InvokeCoreOrValue(length.GetMethod, patternValue, listPattern, ref state);
                    if (listPattern.Patterns.Length != 0 && listPattern.IndexerSymbol is IPropertySymbol indexer)
                        InvokeCoreOrValue(indexer.GetMethod, patternValue, listPattern, ref state);
                    foreach (var slice in listPattern.Patterns.OfType<ISlicePatternOperation>())
                        Evaluate(slice, ref state);
                    return EffectFlowValue.None;
                case ISlicePatternOperation slicePattern:
                    var sliceValue = FindPatternInput(slicePattern, ref state);
                    var sliced = InvokeCoreOrValue(slicePattern.SliceSymbol switch {
                        IPropertySymbol property => property.GetMethod,
                        IMethodSymbol sliceMethod => sliceMethod,
                        _ => null
                    }, sliceValue, slicePattern, ref state);
                    if (slicePattern.Pattern is IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol declared })
                        state = state with { Locals = state.Locals.SetItem(declared, sliced) };
                    return sliced;
                case IDelegateCreationOperation delegateCreation:
                    effects.Add(SharpProofEffect.Allocates, delegateCreation.Syntax, delegateCreation.Type, "delegate_allocation");
                    return BindCallable(delegateCreation.Target, delegateCreation.Type, ref state);
                case IAnonymousFunctionOperation function:
                    return BindCallable(function, function.Type, ref state);
                case IMethodReferenceOperation methodReference:
                    return BindCallable(methodReference, methodReference.Type, ref state);
                case IThrowOperation thrown:
                    if (thrown.IsImplicit) return EffectFlowValue.None;
                    var exception = Evaluate(thrown.Exception, ref state);
                    if (thrown.Exception?.ConstantValue is not { HasValue: true, Value: null }) {
                        var thrownType = thrown.Syntax.AncestorsAndSelf().OfType<ThrowStatementSyntax>()
                                             .Where(static statement => statement.Expression != null)
                                             .Select(statement => semanticModel.GetTypeInfo(statement.Expression!,
                                                 session.CancellationToken).Type).FirstOrDefault() ??
                                         thrown.Syntax.AncestorsAndSelf().OfType<ThrowExpressionSyntax>()
                                             .Select(expression => semanticModel.GetTypeInfo(expression.Expression,
                                                 session.CancellationToken).Type).FirstOrDefault() ??
                                         thrown.Exception?.Type ??
                                         thrown.Syntax.Ancestors().OfType<CatchClauseSyntax>()
                                             .Select(clause => clause.Declaration == null
                                                 ? session.Compilation.GetTypeByMetadataName("System.Exception")
                                                 : semanticModel.GetTypeInfo(clause.Declaration.Type,
                                                     session.CancellationToken).Type).FirstOrDefault();
                        if (thrown.Syntax.AncestorsAndSelf().Any(static syntax => syntax is CatchFilterClauseSyntax))
                            effects.Caught(thrownType, thrown.Syntax, "catch_filter_exception");
                        else if (IsOverriddenByFinally(thrown.Syntax))
                            effects.Caught(thrownType, thrown.Syntax, "finally_replaces_exception");
                        else if (IsCaught(thrown, thrownType)) effects.Caught(thrownType, thrown.Syntax, "caught_explicit_throw");
                        else effects.Throw(thrownType, thrown.Syntax, "explicit_throw");
                    }
                    return exception;
                case ILockOperation locked:
                    Evaluate(locked.LockedValue, ref state);
                    effects.Add(SharpProofEffect.Synchronizes, SharpProofCapability.Synchronization,
                        locked.Syntax, null, "synchronization");
                    Evaluate(locked.Body, ref state);
                    return EffectFlowValue.None;
                default:
                    var result = EffectFlowValue.None;
                    foreach (var child in operation.ChildOperations) result = Evaluate(child, ref state);
                    return result;
            }
        }
        private static EffectFlowValue MergeCoalesce(EffectFlowValue value, EffectFlowValue fallback) {
            if (value.IsDefinitelyNull) return fallback;
            var merged = value.Merge(fallback);
            return fallback.IsDefinitelyNonNull ? merged.AsDefinitelyNonNull() : merged;
        }
        private void AddReturn(EffectFlowValue value) =>
            ReturnValue = ReferenceEquals(ReturnValue, EffectFlowValue.None) ? value : ReturnValue.Merge(value);
        private bool BelongsToCurrentMethod(IOperation operation) {
            for (var current = operation.Parent; current != null; current = current.Parent)
                if (current is IAnonymousFunctionOperation anonymous)
                    return SymbolEqualityComparer.Default.Equals(anonymous.Symbol.OriginalDefinition, method.OriginalDefinition);
                else if (current is ILocalFunctionOperation localFunction)
                    return SymbolEqualityComparer.Default.Equals(localFunction.Symbol.OriginalDefinition, method.OriginalDefinition);
            return true;
        }
        private static bool IsCompilerLoweredProtocolOperation(IOperation operation) =>
            operation is not (IForEachLoopOperation or IAwaitOperation or IUsingOperation or IUsingDeclarationOperation or
                ICollectionExpressionOperation or IWithOperation) &&
            (operation.Syntax.AncestorsAndSelf().OfType<CommonForEachStatementSyntax>()
                 .Any(loop => !loop.Statement.Span.Contains(operation.Syntax.Span)) || operation.IsImplicit &&
            operation.Syntax.AncestorsAndSelf().Any(static syntax => syntax is AwaitExpressionSyntax or
                CommonForEachStatementSyntax or UsingStatementSyntax or LocalDeclarationStatementSyntax {
                    UsingKeyword.RawKind: not 0
                } or CollectionExpressionSyntax or WithExpressionSyntax or
                InitializerExpressionSyntax { Parent: ObjectCreationExpressionSyntax }));
        private bool IsUnreachable(IOperation operation) => semanticModel.GetDiagnostics(operation.Syntax.Span, session.CancellationToken)
            .Any(static diagnostic => diagnostic.Id == "CS0162");
        private bool IsCaught(IThrowOperation thrown, ITypeSymbol? exceptionType) {
            for (var current = thrown.Syntax.Parent; current != null; current = current.Parent) {
                if (current is not TryStatementSyntax tryStatement || !tryStatement.Block.Span.Contains(thrown.Syntax.Span)) continue;
                foreach (var clause in tryStatement.Catches) {
                    if (!IsGuaranteedCatch(clause)) continue;
                    if (clause.Declaration == null) return true;
                    var caughtType = semanticModel.GetTypeInfo(clause.Declaration.Type, session.CancellationToken).Type;
                    for (var candidate = exceptionType as INamedTypeSymbol; candidate != null; candidate = candidate.BaseType)
                        if (SymbolEqualityComparer.Default.Equals(candidate, caughtType)) return true;
                }
            }
            return false;
        }
        private bool IsCaught(SyntaxNode syntax, string exceptionType) {
            var exceptionSymbol = session.Compilation.GetTypeByMetadataName(exceptionType) as INamedTypeSymbol;
            for (var current = syntax.Parent; current != null; current = current.Parent) {
                if (current is not TryStatementSyntax tryStatement || !tryStatement.Block.Span.Contains(syntax.Span)) continue;
                foreach (var clause in tryStatement.Catches) {
                    if (!IsGuaranteedCatch(clause)) continue;
                    if (clause.Declaration == null) return true;
                    var caughtType = semanticModel.GetTypeInfo(clause.Declaration.Type, session.CancellationToken).Type;
                    if (caughtType?.ToDisplayString() == "System.Exception" ||
                        caughtType?.ToDisplayString() == exceptionType || caughtType?.Name == exceptionType)
                        return true;
                    for (var candidate = exceptionSymbol; candidate != null; candidate = candidate.BaseType)
                        if (SymbolEqualityComparer.Default.Equals(candidate, caughtType)) return true;
                }
            }
            return false;
        }
        private bool IsGuaranteedCatch(CatchClauseSyntax clause) {
            if (clause.Filter == null) return true;
            var value = semanticModel.GetConstantValue(clause.Filter.FilterExpression, session.CancellationToken);
            return value is { HasValue: true, Value: true };
        }
        private bool IsOverriddenByFinally(SyntaxNode syntax) {
            for (var current = syntax.Parent; current != null; current = current.Parent) {
                if (current is not TryStatementSyntax { Finally: { } finallyClause } tryStatement ||
                    finallyClause.Block.Span.Contains(syntax.Span) ||
                    !tryStatement.Block.Span.Contains(syntax.Span) &&
                    !tryStatement.Catches.Any(clause => clause.Block.Span.Contains(syntax.Span)))
                    continue;
                if (semanticModel.AnalyzeControlFlow(finallyClause.Block) is { EndPointIsReachable: false }) return true;
            }
            return false;
        }
        private MethodEffects ApplyCatches(MethodEffects summary, IOperation site) {
            var inFilter = site.Syntax.AncestorsAndSelf().Any(static syntax => syntax is CatchFilterClauseSyntax);
            return summary with {
                ExceptionFacts = [.. summary.ExceptionFacts.Select(fact =>
                    fact.Escape != SharpProofVerdict.Disproven &&
                    (inFilter || IsOverriddenByFinally(site.Syntax) || IsCaught(site.Syntax, fact.ExceptionType))
                        ? fact with { Escape = SharpProofVerdict.Disproven }
                        : fact)]
            };
        }
        private bool IsCompileTimeSkipped(IOperation operation) {
            var key = (operation.Kind, operation.Syntax.Span);
            foreach (var statement in operation.Syntax.Ancestors().OfType<IfStatementSyntax>()) {
                var constant = semanticModel.GetConstantValue(statement.Condition, session.CancellationToken);
                if (constant is not { HasValue: true, Value: bool condition }) continue;
                if (!condition && statement.Statement.Span.Contains(operation.Syntax.Span) ||
                    condition && statement.Else?.Statement.Span.Contains(operation.Syntax.Span) == true)
                    return true;
            }
            if (operation.IsImplicit && operation is IObjectCreationOperation { Type.Name: "SwitchExpressionException" } &&
                operation.Syntax is SwitchExpressionSyntax expression && (TrySelectArm(expression, out _) || IsExhaustive(expression)))
                return true;
            if (operation.Syntax.Ancestors().OfType<SwitchExpressionArmSyntax>().FirstOrDefault() is { } arm &&
                TrySelectArm(arm.Parent as SwitchExpressionSyntax, out var selected) && arm.Span != selected!.Span)
                return true;
            if (operation.Syntax.Ancestors().OfType<SwitchSectionSyntax>().FirstOrDefault() is { } section &&
                TrySelectSection(section.Parent as SwitchStatementSyntax, out var selectedSection) && section.Span != selectedSection!.Span)
                return true;
            return _unreachableOperations.Contains(key) && !_reachableOperations.Contains(key);
        }
        private bool IsExhaustive(SwitchExpressionSyntax expression) =>
            expression.Arms.Any(static arm => arm.Pattern is DiscardPatternSyntax) ||
            semanticModel.GetTypeInfo(expression.GoverningExpression, session.CancellationToken).Type?.SpecialType ==
                SpecialType.System_Boolean && expression.Arms.Where(static arm => arm.Pattern is ConstantPatternSyntax)
                .Select(arm => semanticModel.GetConstantValue(((ConstantPatternSyntax)arm.Pattern).Expression,
                    session.CancellationToken).Value).OfType<bool>().Distinct().Count() == 2;
        private bool TrySelectArm(SwitchExpressionSyntax? expression, out SwitchExpressionArmSyntax? selected) {
            selected = null;
            if (expression == null || semanticModel.GetConstantValue(expression.GoverningExpression, session.CancellationToken) is not
                { HasValue: true } value) return false;
            foreach (var arm in expression.Arms) {
                if (!CanEvaluatePattern(arm.Pattern, value.Value)) return false;
                if (!Matches(arm.Pattern, value.Value)) continue;
                if (arm.WhenClause == null) {
                    selected = arm;
                    return true;
                }
                var guard = semanticModel.GetConstantValue(arm.WhenClause.Condition, session.CancellationToken);
                if (guard is not { HasValue: true, Value: bool condition }) return false;
                if (!condition) continue;
                selected = arm;
                return true;
            }
            return false;
        }
        private bool TrySelectSection(SwitchStatementSyntax? statement, out SwitchSectionSyntax? selected) {
            selected = null;
            if (statement == null || statement.DescendantNodes().OfType<GotoStatementSyntax>().Any(static branch =>
                    branch.IsKind(SyntaxKind.GotoCaseStatement) || branch.IsKind(SyntaxKind.GotoDefaultStatement)) ||
                semanticModel.GetConstantValue(statement.Expression, session.CancellationToken) is not
                { HasValue: true } value) return false;
            foreach (var section in statement.Sections)
                foreach (var label in section.Labels) {
                    if (label is CaseSwitchLabelSyntax constant && Equals(semanticModel.GetConstantValue(constant.Value,
                            session.CancellationToken).Value, value.Value)) {
                        selected = section;
                        return true;
                    }
                    if (label is not CasePatternSwitchLabelSyntax pattern) continue;
                    if (!CanEvaluatePattern(pattern.Pattern, value.Value)) return false;
                    if (!Matches(pattern.Pattern, value.Value)) continue;
                    if (pattern.WhenClause == null) {
                        selected = section;
                        return true;
                    }
                    var guard = semanticModel.GetConstantValue(pattern.WhenClause.Condition, session.CancellationToken);
                    if (guard is not { HasValue: true, Value: bool condition }) return false;
                    if (!condition) continue;
                    selected = section;
                    return true;
                }
            selected ??= statement.Sections.FirstOrDefault(section =>
                section.Labels.Any(static label => label is DefaultSwitchLabelSyntax));
            return selected != null;
        }
        private bool CanEvaluatePattern(PatternSyntax pattern, object? value) => pattern switch {
            DiscardPatternSyntax or VarPatternSyntax => true,
            ConstantPatternSyntax constant => semanticModel.GetConstantValue(constant.Expression,
                session.CancellationToken).HasValue,
            ParenthesizedPatternSyntax parenthesized => CanEvaluatePattern(parenthesized.Pattern, value),
            UnaryPatternSyntax unary when unary.IsKind(SyntaxKind.NotPattern) => CanEvaluatePattern(unary.Pattern, value),
            BinaryPatternSyntax binary when binary.IsKind(SyntaxKind.AndPattern) || binary.IsKind(SyntaxKind.OrPattern) =>
                CanEvaluatePattern(binary.Left, value) && CanEvaluatePattern(binary.Right, value),
            RelationalPatternSyntax relational => CanCompare(value, semanticModel.GetConstantValue(relational.Expression,
                session.CancellationToken).Value),
            TypePatternSyntax type => IsSupportedConstantType(semanticModel.GetTypeInfo(type.Type,
                session.CancellationToken).Type),
            DeclarationPatternSyntax declaration => IsSupportedConstantType(semanticModel.GetTypeInfo(declaration.Type,
                session.CancellationToken).Type),
            _ => false
        };
        private bool Matches(PatternSyntax pattern, object? value) => pattern switch {
            DiscardPatternSyntax => true,
            VarPatternSyntax => true,
            ConstantPatternSyntax constant => Equals(semanticModel.GetConstantValue(constant.Expression, session.CancellationToken).Value, value),
            ParenthesizedPatternSyntax parenthesized => Matches(parenthesized.Pattern, value),
            UnaryPatternSyntax unary when unary.IsKind(SyntaxKind.NotPattern) => !Matches(unary.Pattern, value),
            BinaryPatternSyntax binary when binary.IsKind(SyntaxKind.AndPattern) => Matches(binary.Left, value) && Matches(binary.Right, value),
            BinaryPatternSyntax binary when binary.IsKind(SyntaxKind.OrPattern) => Matches(binary.Left, value) || Matches(binary.Right, value),
            RelationalPatternSyntax relational => Compare(value, semanticModel.GetConstantValue(relational.Expression,
                session.CancellationToken).Value, relational.OperatorToken.Kind()),
            TypePatternSyntax type => IsConstantType(value, semanticModel.GetTypeInfo(type.Type, session.CancellationToken).Type),
            DeclarationPatternSyntax declaration => IsConstantType(value,
                semanticModel.GetTypeInfo(declaration.Type, session.CancellationToken).Type),
            _ => false
        };
        private static bool CanCompare(object? left, object? right) {
            if (left == null || right == null) return false;
            try {
                Convert.ToDecimal(left, CultureInfo.InvariantCulture);
                Convert.ToDecimal(right, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException) {
                return false;
            }
        }
        private static bool Compare(object? left, object? right, SyntaxKind kind) {
            if (left == null || right == null) return false;
            try {
                var comparison = decimal.Compare(Convert.ToDecimal(left, CultureInfo.InvariantCulture),
                    Convert.ToDecimal(right, CultureInfo.InvariantCulture));
                return kind switch {
                    SyntaxKind.LessThanToken => comparison < 0,
                    SyntaxKind.LessThanEqualsToken => comparison <= 0,
                    SyntaxKind.GreaterThanToken => comparison > 0,
                    SyntaxKind.GreaterThanEqualsToken => comparison >= 0,
                    _ => false
                };
            }
            catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException) { return false; }
        }
        private static bool IsConstantType(object? value, ITypeSymbol? type) => value != null && type?.SpecialType switch {
            SpecialType.System_Boolean => value is bool,
            SpecialType.System_String => value is string,
            SpecialType.System_Char => value is char,
            SpecialType.System_Int32 => value is int,
            SpecialType.System_Int64 => value is long,
            _ => false
        };
        private static bool IsSupportedConstantType(ITypeSymbol? type) => type?.SpecialType is
            SpecialType.System_Boolean or SpecialType.System_String or SpecialType.System_Char or
            SpecialType.System_Int32 or SpecialType.System_Int64;
        private static bool IsOmittedInvocation(IInvocationOperation invocation) {
            var target = invocation.TargetMethod;
            if (target.PartialDefinitionPart != null && target.PartialImplementationPart == null ||
                target.IsPartialDefinition && target.PartialImplementationPart == null)
                return true;
            return target.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute" &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string symbol &&
                invocation.Syntax.SyntaxTree.Options is CSharpParseOptions parseOptions &&
                !parseOptions.PreprocessorSymbolNames.Contains(symbol, StringComparer.Ordinal) &&
                !invocation.Syntax.SyntaxTree.GetRoot().DescendantTrivia(descendIntoTrivia: true)
                    .Select(static trivia => trivia.GetStructure()).OfType<DefineDirectiveTriviaSyntax>()
                    .Any(directive => directive.IsActive && directive.Name.ValueText == symbol));
        }
        private EffectFlowValue EvaluateConversion(IConversionOperation conversion, ref EffectFlowState state) {
            var operand = Evaluate(conversion.Operand, ref state);
            if (conversion.Operand.Type?.TypeKind == TypeKind.Dynamic &&
                conversion.Type?.TypeKind != TypeKind.Dynamic) {
                effects.Add(SharpProofEffect.DispatchUncertainty, conversion.Syntax, conversion.Type,
                    "dynamic_conversion_dispatch");
                effects.AddUnknown(conversion.Syntax, "dynamic_conversion_dispatch");
                return EffectFlowValue.Unknown;
            }
            if (conversion.Conversion.IsImplicit && conversion.Operand.Type?.IsValueType == true &&
                conversion.Type?.IsReferenceType == true)
                effects.Add(SharpProofEffect.Allocates, conversion.Syntax, conversion.Type, "boxing_allocation");
            if (conversion.OperatorMethod == null) return operand;
            return InvokeCore(conversion.OperatorMethod, EffectFlowValue.None, [operand], [], conversion, ref state);
        }
        private EffectFlowValue DynamicDispatch(IOperation operation, string reason) {
            effects.Add(SharpProofEffect.DispatchUncertainty, operation.Syntax, operation.Type, reason);
            effects.AddUnknown(operation.Syntax, reason);
            return EffectFlowValue.Unknown;
        }
        private EffectFlowValue EvaluateField(IFieldReferenceOperation field, ref EffectFlowState state) {
            if (field.Field.IsConst) return EffectFlowValue.None;
            if (field.Field.IsStatic) {
                AddTypeInitializerEffects(field.Field.ContainingType, field);
                effects.Add(SharpProofEffect.ReadsStaticState, field.Syntax, field.Field, "static_field_read");
                return EffectFlowValue.FromRoot(new(EffectValueRootKind.Static, Key: MemberKey(field.Field)), field.Type);
            }
            var receiver = Evaluate(field.Instance, ref state);
            effects.Read(receiver, field.Syntax, field.Field, "instance_field_read");
            return receiver.Member(MemberKey(field.Field));
        }
        private EffectFlowValue EvaluateProperty(IPropertyReferenceOperation property, ref EffectFlowState state) {
            var receiver = property.Property.IsStatic
                ? EffectFlowValue.FromRoot(new(EffectValueRootKind.Static, Key: MemberKey(property.Property)), property.Type)
                : Evaluate(property.Instance, ref state);
            foreach (var argument in property.Arguments) Evaluate(argument.Value, ref state);
            if (property.Property.IsStatic)
                effects.Add(SharpProofEffect.ReadsStaticState, property.Syntax, property.Property, "static_property_read");
            else
                effects.Read(receiver, property.Syntax, property.Property, "property_read");
            var key = MemberKey(property) ?? MemberKey(property.Property);
            if (property.Property.GetMethod == null) return receiver.Member(key);
            if (!property.Property.GetMethod.IsVirtual && !property.Property.GetMethod.IsOverride &&
                (property.Property.GetMethod.IsImplicitlyDeclared ||
                property.Property is { IsAbstract: false, ContainingType.TypeKind: not TypeKind.Interface } &&
                property.Property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(session.CancellationToken) is
                    PropertyDeclarationSyntax { AccessorList.Accessors: var accessors } &&
                accessors.All(static accessor => accessor is { Body: null, ExpressionBody: null })))
                return receiver.Member(key);
            var invoked = Invoke(property.Property.GetMethod, property.Instance, property.Arguments, property, ref state);
            if (receiver.Members.TryGetValue(key, out var tracked)) return tracked;
            var recovered = property.Syntax is ExpressionSyntax expression && IsObjectInitializerAccess(expression)
                ? RecoverSourceValue(expression, ref state)
                : EffectFlowValue.Unknown;
            return recovered.Roots.Any(static root => root.Kind != EffectValueRootKind.Unknown) ? recovered : invoked;
        }
        private bool IsObjectInitializerAccess(ExpressionSyntax expression) {
            var root = expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().FirstOrDefault();
            return root != null && semanticModel.GetSymbolInfo(root, session.CancellationToken).Symbol is ILocalSymbol local &&
                   local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(session.CancellationToken) is
                       VariableDeclaratorSyntax { Initializer.Value: ObjectCreationExpressionSyntax { Initializer: not null } };
        }
        private EffectFlowValue RecoverSourceValue(ExpressionSyntax expression, ref EffectFlowState state) {
            while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
            var symbol = semanticModel.GetSymbolInfo(expression, session.CancellationToken).Symbol;
            if (symbol is IParameterSymbol parameter) return state.GetParameter(parameter);
            if (symbol is ILocalSymbol local && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(session.CancellationToken) is
                VariableDeclaratorSyntax { Initializer.Value: { } initializer })
                return RecoverSourceValue(initializer, ref state);
            if (expression is ConditionalExpressionSyntax conditional)
                return RecoverSourceValue(conditional.WhenTrue, ref state).Merge(RecoverSourceValue(conditional.WhenFalse, ref state));
            if (expression is ObjectCreationExpressionSyntax creation) {
                var value = EffectFlowValue.Fresh(semanticModel.GetTypeInfo(creation, session.CancellationToken).Type);
                foreach (var assignment in creation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>() ?? [])
                    if (semanticModel.GetSymbolInfo(assignment.Left, session.CancellationToken).Symbol is ISymbol member)
                        value = value.WithMember(MemberKey(member), RecoverSourceValue(assignment.Right, ref state));
                return value;
            }
            if (expression is MemberAccessExpressionSyntax access && symbol is ISymbol memberSymbol) {
                if (memberSymbol.IsStatic)
                    return EffectFlowValue.FromRoot(new(EffectValueRootKind.Static, Key: MemberKey(memberSymbol)));
                return RecoverSourceValue(access.Expression, ref state).Member(MemberKey(memberSymbol));
            }
            return EffectFlowValue.Unknown;
        }
        private EffectFlowValue EvaluateCreation(IObjectCreationOperation creation, ref EffectFlowState state) {
            var arguments = EvaluateArguments(creation.Arguments, ref state);
            var value = EffectFlowValue.Fresh(creation.Type);
            effects.Add(SharpProofEffect.Allocates, creation.Syntax, creation.Type, "object_allocation");
            AddConstructionTypeInitializerEffects(creation.Type, creation);
            if (creation.Constructor != null) {
                var summary = GetSummary(creation.Constructor, null);
                AddSummary(summary.Effects, value, arguments, creation, creation.Constructor,
                    summary.WrittenArgumentOrdinals, summary.ReadArgumentOrdinals,
                    summary.BoundArgumentEffects, summary.BoundReceiverEffects);
                value = summary.Receiver.Instantiate(value, arguments, sourceMethod: creation.Constructor);
                ApplyRefArguments(summary, creation.Constructor, creation.Arguments, value, arguments, ref state);
            }
            if (creation.Constructor?.IsImplicitlyDeclared == true && creation.Type is INamedTypeSymbol {
                BaseType: { } baseType
            }) {
                var baseConstructor = baseType.InstanceConstructors.FirstOrDefault(static constructor =>
                    constructor.Parameters.All(parameter => parameter.IsOptional));
                if (baseConstructor != null) {
                    var baseArguments = baseConstructor.Parameters.Select(static _ => EffectFlowValue.None).ToArray();
                    var baseSummary = GetSummary(baseConstructor, null);
                    AddSummary(baseSummary.Effects, value, baseArguments, creation, baseConstructor,
                        baseSummary.WrittenArgumentOrdinals, baseSummary.ReadArgumentOrdinals,
                        baseSummary.BoundArgumentEffects, baseSummary.BoundReceiverEffects);
                    value = baseSummary.Receiver.Instantiate(value, baseArguments, sourceMethod: baseConstructor);
                }
            }
            if (creation.Constructor?.IsImplicitlyDeclared != false)
                value = ApplyDeclaredInitializers(value, creation.Type, ref state);
            if (creation.Initializer != null)
                foreach (var initializer in creation.Initializer.Initializers) {
                    if (initializer is ISimpleAssignmentOperation assignment) {
                        var assigned = Evaluate(assignment.Value, ref state);
                        var key = MemberKey(assignment.Target);
                        if (key != null) value = value.WithMember(key, assigned);
                    }
                    else if (initializer is IInvocationOperation add) {
                        var added = EvaluateArguments(add.Arguments, ref state);
                        InvokeCore(add.TargetMethod, value, added, add.Arguments, add, ref state);
                        var element = added.Count == 0 ? EffectFlowValue.Unknown : added[added.Count - 1];
                        var key = added.Count > 1 ? IndexKey([add.Arguments[0].Value]) : "#" + value.Members.Count;
                        value = value.WithMember(key, element);
                    }
                    else Evaluate(initializer, ref state);
                }
            return value;
        }
        internal EffectFlowValue ApplyDeclaredInitializers(EffectFlowValue value, ITypeSymbol? type,
            ref EffectFlowState state, bool includeBase = true) {
            foreach (var declaration in type?.DeclaringSyntaxReferences ?? []) {
                if (declaration.GetSyntax(session.CancellationToken) is not TypeDeclarationSyntax typeDeclaration) continue;
                var declarationModel = session.Compilation.GetSemanticModel(declaration.SyntaxTree);
                var declarationDomain = declaration.SyntaxTree == semanticModel.SyntaxTree
                    ? this
                    : new EffectFlowDomain(session, method, declarationModel, effects, boundCaptures);
                foreach (var member in typeDeclaration.Members) {
                    if (member is BaseFieldDeclarationSyntax field)
                        foreach (var variable in field.Declaration.Variables) {
                            if (variable.Initializer == null || declarationModel.GetDeclaredSymbol(variable,
                                    session.CancellationToken) is not { IsStatic: false } symbol) continue;
                            value = value.WithMember(MemberKey(symbol), declarationDomain.Evaluate(declarationModel.GetOperation(
                                variable.Initializer.Value, session.CancellationToken), ref state));
                        }
                    else if (member is PropertyDeclarationSyntax { Initializer: { } initializer } property &&
                             declarationModel.GetDeclaredSymbol(property, session.CancellationToken) is
                                 IPropertySymbol { IsStatic: false } symbol)
                        value = value.WithMember(MemberKey(symbol), declarationDomain.Evaluate(declarationModel.GetOperation(initializer.Value,
                            session.CancellationToken), ref state));
                }
            }
            if (includeBase && type?.BaseType != null) value = ApplyDeclaredInitializers(value, type.BaseType, ref state);
            return value;
        }
        private EffectFlowValue Invoke(
            IMethodSymbol target,
            IOperation? receiverOperation,
            ImmutableArray<IArgumentOperation> arguments,
            IOperation site,
            ref EffectFlowState state,
            IReadOnlyList<EffectFlowValue>? evaluatedArguments = null) {
            var receiver = Evaluate(receiverOperation, ref state);
            if (receiver.Roots.Any(static root => root.Kind == EffectValueRootKind.Unknown) &&
                receiverOperation?.Syntax is SwitchExpressionSyntax or ConditionalExpressionSyntax)
                receiver = Evaluate(semanticModel.GetOperation(receiverOperation.Syntax, session.CancellationToken), ref state);
            var values = evaluatedArguments ?? EvaluateArguments(arguments, ref state);
            return InvokeCore(target, receiver, values, arguments, site, ref state);
        }
        private EffectFlowValue InvokeCore(
            IMethodSymbol target,
            EffectFlowValue receiver,
            IReadOnlyList<EffectFlowValue> values,
            ImmutableArray<IArgumentOperation> arguments,
            IOperation site,
            ref EffectFlowState state) {
            if (target.MethodKind == MethodKind.LocalFunction) {
                var declaration = target.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(session.CancellationToken);
                if (declaration != null && semanticModel.GetOperation(declaration, session.CancellationToken) is ILocalFunctionOperation local) {
                    var bound = BindCallable(local, null, ref state).Callables.FirstOrDefault();
                    if (bound != null) {
                        var localSummary = GetSummary(bound.Method, bound.Captures);
                        if (CapturesCanMapArgumentEffects(bound.Captures))
                            AddSummary(localSummary.Effects, bound.Receiver, values, site, bound.Method,
                                localSummary.WrittenArgumentOrdinals, localSummary.ReadArgumentOrdinals,
                                localSummary.BoundArgumentEffects, localSummary.BoundReceiverEffects);
                        else
                            effects.AddTransitive(localSummary.Effects, site.Syntax, bound.Method, "source_call");
                        return localSummary.ReturnValue.Instantiate(bound.Receiver, values, bound.Captures, bound.Method);
                    }
                }
            }
            if (target.MethodKind == MethodKind.DelegateInvoke && !receiver.Callables.IsDefaultOrEmpty) {
                var returned = EffectFlowValue.None;
                foreach (var callable in receiver.Callables) {
                    if (callable.Method.IsStatic && callable.Method.MethodKind != MethodKind.StaticConstructor)
                        AddTypeInitializerEffects(callable.Method.ContainingType, site);
                    var callableSummary = GetSummary(callable.Method, callable.Captures);
                    if (callable.Method.MethodKind is MethodKind.AnonymousFunction or MethodKind.LocalFunction &&
                        !CapturesCanMapArgumentEffects(callable.Captures))
                        effects.AddTransitive(callableSummary.Effects, site.Syntax, callable.Method, "source_call");
                    else
                        AddSummary(callableSummary.Effects, callable.Receiver, values, site, callable.Method,
                            callableSummary.WrittenArgumentOrdinals, callableSummary.ReadArgumentOrdinals,
                            callableSummary.BoundArgumentEffects, callableSummary.BoundReceiverEffects);
                    var value = callableSummary.ReturnValue.Instantiate(
                        callable.Receiver, values, callable.Captures, callable.Method);
                    returned = ReferenceEquals(returned, EffectFlowValue.None) ? value : returned.Merge(value);
                }
                return returned;
            }
            target = Normalize(target);
            var exactTarget = SymbolicDispatchFacts.ResolveExactDispatchTarget(target, null, receiver.ExactType);
            if (exactTarget != null) target = exactTarget;
            effects.Add(SharpProofEffect.DirectCall, site.Syntax, target, "direct_call");
            if (target.IsStatic && target.MethodKind != MethodKind.StaticConstructor)
                AddTypeInitializerEffects(target.ContainingType, site);
            if (exactTarget == null && (target.IsVirtual || target.IsOverride ||
                target.ContainingType?.TypeKind == TypeKind.Interface)) {
                effects.Add(SharpProofEffect.DispatchUncertainty, site.Syntax, target, "dispatch_uncertainty");
                effects.AddUnknown(site.Syntax, "unresolved_dispatch", target);
                return EffectFlowValue.Unknown;
            }
            if (target.IsImplicitlyDeclared) return EffectFlowValue.None;
            if (target.GetDllImportData() != null) {
                effects.Add(SharpProofEffect.UsesNativeCode, SharpProofCapability.NativeInterop, site.Syntax, target, "native_call");
                if (session.TryReadEffectContract(target, out var nativeContract)) {
                    AddSummary(nativeContract, receiver, values, site, target);
                    return EffectFlowValue.Unknown;
                }
                effects.AddUnknown(site.Syntax, "native_exception_boundary", target);
                return EffectFlowValue.Unknown;
            }
            var summary = GetSummary(target, null);
            AddSummary(summary.Effects, receiver, values, site, target,
                summary.WrittenArgumentOrdinals, summary.ReadArgumentOrdinals,
                summary.BoundArgumentEffects, summary.BoundReceiverEffects);
            if ((summary.Effects.Effects & SharpProofEffect.WritesStaticState) != 0 &&
                values.Any(value => value.Roots.Any(static root => root.Kind == EffectValueRootKind.Fresh)) &&
                PublishesArgument(target, receiver.Roots.Count == 0 ||
                    receiver.Roots.Any(static root => root.Kind != EffectValueRootKind.Fresh)))
                effects.Add(SharpProofEffect.WritesCapturedState, site.Syntax, target, "published_fresh_argument");
            ApplyRefArguments(summary, target, arguments, receiver, values, ref state);
            var returnedValue = summary.ReturnValue.Instantiate(receiver, values, sourceMethod: target);
            return target.ReturnsByRef && returnedValue.Roots.Any(static root => root.Kind == EffectValueRootKind.Unknown)
                ? ResolveRefReturn(target, receiver, values)
                : returnedValue;
        }
        private static bool CapturesCanMapArgumentEffects(ImmutableDictionary<string, EffectFlowValue> captures) =>
            captures.Values.All(static capture => capture.Roots.All(static root =>
                root.Kind is EffectValueRootKind.Fresh or EffectValueRootKind.Argument or EffectValueRootKind.Receiver));
        private void AddTypeInitializerEffects(ITypeSymbol? type, IOperation site) {
            if (type is not INamedTypeSymbol named || method.MethodKind == MethodKind.StaticConstructor &&
                SymbolEqualityComparer.Default.Equals(method.ContainingType, named) ||
                named.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(static candidate =>
                    candidate.MethodKind == MethodKind.StaticConstructor) is not { } initializer)
                return;
            var initializerSummary = GetSummary(initializer, null);
            AddSummary(MethodEffectAnalysisSession.WrapTypeInitializerExceptions(initializerSummary.Effects),
                EffectFlowValue.None, [], site, initializer);
        }
        private void AddConstructionTypeInitializerEffects(ITypeSymbol? type, IOperation site) {
            if (type is not INamedTypeSymbol named) return;
            var hierarchy = new Stack<INamedTypeSymbol>();
            for (var current = named; current != null; current = current.BaseType) hierarchy.Push(current);
            while (hierarchy.Count != 0) AddTypeInitializerEffects(hierarchy.Pop(), site);
        }
        private EffectFlowValue ResolveRefReturn(
            IMethodSymbol target,
            EffectFlowValue receiver,
            IReadOnlyList<EffectFlowValue> arguments) {
            var declaration = target.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(session.CancellationToken);
            var expression = declaration?.DescendantNodes().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault()?.Expression ??
                             declaration?.DescendantNodes().OfType<ReturnStatementSyntax>().FirstOrDefault()?.Expression;
            if (expression == null) return EffectFlowValue.Unknown;
            while (expression is RefExpressionSyntax refExpression) expression = refExpression.Expression;
            var model = session.Compilation.GetSemanticModel(expression.SyntaxTree);
            if (expression is IdentifierNameSyntax identifier) {
                var symbol = model.GetSymbolInfo(identifier, session.CancellationToken).Symbol;
                if (symbol is IParameterSymbol parameter && parameter.Ordinal < arguments.Count) return arguments[parameter.Ordinal];
                if (symbol is IFieldSymbol { IsStatic: true } field)
                    return EffectFlowValue.FromRoot(new(EffectValueRootKind.Static, Key: MemberKey(field)), field.Type);
            }
            if (expression is MemberAccessExpressionSyntax member &&
                model.GetSymbolInfo(member, session.CancellationToken).Symbol is ISymbol memberSymbol) {
                var instance = ResolveRefExpression(member.Expression, receiver, arguments, model);
                return memberSymbol.IsStatic
                    ? EffectFlowValue.FromRoot(new(EffectValueRootKind.Static, Key: MemberKey(memberSymbol)))
                    : instance.Member(MemberKey(memberSymbol));
            }
            return EffectFlowValue.Unknown;
        }
        private static EffectFlowValue ResolveRefExpression(
            ExpressionSyntax expression,
            EffectFlowValue receiver,
            IReadOnlyList<EffectFlowValue> arguments,
            SemanticModel model) {
            if (expression is ThisExpressionSyntax) return receiver;
            if (model.GetSymbolInfo(expression).Symbol is IParameterSymbol parameter && parameter.Ordinal < arguments.Count)
                return arguments[parameter.Ordinal];
            return EffectFlowValue.Unknown;
        }
        private bool PublishesArgument(IMethodSymbol target, bool receiverMayEscape) {
            foreach (var reference in target.DeclaringSyntaxReferences) {
                if (reference.GetSyntax(session.CancellationToken) is not { } declaration) continue;
                var model = session.Compilation.GetSemanticModel(declaration.SyntaxTree);
                foreach (var assignment in CSharpSyntaxFacts.DescendantNodesInExecution(declaration)
                             .OfType<AssignmentExpressionSyntax>()) {
                    var publicationTarget = model.GetSymbolInfo(assignment.Left, session.CancellationToken).Symbol;
                    if (publicationTarget is not (IFieldSymbol or IPropertySymbol) ||
                        assignment.Left is MemberAccessExpressionSyntax memberAccess &&
                        IsEphemeralFreshReceiver(memberAccess.Expression, declaration, model))
                        continue;
                    if (!assignment.Right.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(identifier =>
                            model.GetSymbolInfo(identifier, session.CancellationToken).Symbol is IParameterSymbol parameter &&
                            target.Parameters.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, parameter))))
                        continue;
                    if (publicationTarget is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true }) return true;
                    if (assignment.Left is IdentifierNameSyntax) {
                        if (receiverMayEscape) return true;
                        continue;
                    }
                    if (assignment.Left is MemberAccessExpressionSyntax access) {
                        if (access.Expression is ThisExpressionSyntax or BaseExpressionSyntax) {
                            if (receiverMayEscape) return true;
                            continue;
                        }
                        return true;
                    }
                    return true;
                }
            }
            return false;
        }
        private bool IsEphemeralFreshReceiver(
            ExpressionSyntax receiver,
            SyntaxNode declaration,
            SemanticModel model) {
            if (model.GetSymbolInfo(receiver, session.CancellationToken).Symbol is not ILocalSymbol local ||
                local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(session.CancellationToken) is not
                    VariableDeclaratorSyntax { Initializer.Value: { } initializer } ||
                initializer is not (ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or
                    ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or CollectionExpressionSyntax or
                    StackAllocArrayCreationExpressionSyntax))
                return false;
            return CSharpSyntaxFacts.DescendantNodesInExecution(declaration).OfType<IdentifierNameSyntax>()
                .Where(identifier => SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(identifier, session.CancellationToken).Symbol, local))
                .All(identifier => identifier.Parent is MemberAccessExpressionSyntax access &&
                                   ReferenceEquals(access.Expression, identifier) &&
                                   model.GetSymbolInfo(access, session.CancellationToken).Symbol is
                                       IFieldSymbol or IPropertySymbol);
        }
        private EffectFlowValue InvokeCoreOrValue(
            IMethodSymbol? target,
            EffectFlowValue receiver,
            IOperation site,
            ref EffectFlowState state) => target == null
                ? receiver
                : InvokeCore(target, receiver, [], [], site, ref state);
        private EffectFlowValue EvaluateForEach(IForEachLoopOperation loop, ref EffectFlowState state) {
            var collection = Evaluate(loop.Collection, ref state);
            if (loop.Syntax is CommonForEachStatementSyntax sourceSyntax) {
                var sourceOperation = semanticModel.GetOperation(sourceSyntax.Expression, session.CancellationToken);
                collection = sourceOperation is IConversionOperation conversion
                    ? EvaluateConversion(conversion, ref state)
                    : Evaluate(sourceOperation, ref state);
            }
            if (loop.Syntax is CommonForEachStatementSyntax source &&
                semanticModel.GetSymbolInfo(source.Expression, session.CancellationToken).Symbol is IParameterSymbol parameter)
                collection = state.GetParameter(parameter);
            foreach (var local in loop.Locals.Where(static local => local.RefKind != RefKind.None))
                state = state with { RefLocals = state.RefLocals.SetItem(local, collection) };
            if (loop.Syntax is ForEachStatementSyntax forEach &&
                semanticModel.GetDeclaredSymbol(forEach, session.CancellationToken) is ILocalSymbol { RefKind: not RefKind.None } refLocal)
                state = state with { RefLocals = state.RefLocals.SetItem(refLocal, collection) };
            var type = loop.Syntax is CommonForEachStatementSyntax sourceLoop
                ? semanticModel.GetTypeInfo(sourceLoop.Expression, session.CancellationToken).Type ?? loop.Collection.Type
                : loop.Collection.Type;
            var definition = type?.OriginalDefinition.ToDisplayString();
            if (loop.Syntax is ForEachVariableStatementSyntax variableSyntax &&
                semanticModel.GetDeconstructionInfo(variableSyntax).Method is { } deconstruct)
                InvokeCore(deconstruct, collection,
                    deconstruct.ReducedFrom != null || deconstruct.IsExtensionMethod ? [collection] : [], [], loop, ref state);
            if (type is IArrayTypeSymbol || type?.SpecialType == SpecialType.System_String ||
                definition is "System.Span<T>" or "System.ReadOnlySpan<T>" || IsInlineArray(type)) {
                effects.Read(collection, loop.Syntax, type, "intrinsic_foreach_read");
                Evaluate(loop.Body, ref state);
                if (loop.Syntax is ForEachStatementSyntax refLoop && refLoop.Type.ToString().StartsWith("ref ", StringComparison.Ordinal) &&
                    RefIterationVariableIsMutated(refLoop))
                    effects.Write(collection, loop.Syntax, type, "ref_foreach_write");
                return collection;
            }
            if (loop.Syntax is not CommonForEachStatementSyntax syntax) return collection;
            var info = semanticModel.GetForEachStatementInfo(syntax);
            var enumerator = info.GetEnumeratorMethod == null
                ? EffectFlowValue.Unknown
                : InvokeCore(info.GetEnumeratorMethod, collection,
                    info.GetEnumeratorMethod.ReducedFrom != null || info.GetEnumeratorMethod.IsExtensionMethod ? [collection] : [],
                    [], loop, ref state);
            var moveNext = ResolveProtocolImplementation(info.MoveNextMethod, enumerator);
            var getCurrent = ResolveProtocolImplementation(info.CurrentProperty?.GetMethod, enumerator);
            var dispose = ResolveProtocolImplementation(info.DisposeMethod, enumerator);
            var moveNextResult = InvokeCoreOrValue(moveNext, enumerator, loop, ref state);
            InvokeCoreOrValue(getCurrent, enumerator, loop, ref state);
            var disposeResult = InvokeCoreOrValue(dispose, enumerator, loop, ref state);
            if (loop.IsAsynchronous) {
                AnalyzeAwaitable(moveNext?.ReturnType, moveNextResult, loop, ref state);
                AnalyzeAwaitable(dispose?.ReturnType, disposeResult, loop, ref state);
            }
            Evaluate(loop.Body, ref state);
            return collection;
        }
        private bool RefIterationVariableIsMutated(ForEachStatementSyntax loop) {
            var iterationVariable = semanticModel.GetDeclaredSymbol(loop, session.CancellationToken);
            if (iterationVariable == null) return false;
            bool ReferencesIterationVariable(ExpressionSyntax expression) => SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(expression, session.CancellationToken).Symbol, iterationVariable);
            return CSharpSyntaxFacts.DescendantNodesInExecution(loop.Statement).Any(node => node switch {
                AssignmentExpressionSyntax assignment => ReferencesIterationVariable(assignment.Left),
                PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                                                         prefix.IsKind(SyntaxKind.PreDecrementExpression) =>
                    ReferencesIterationVariable(prefix.Operand),
                PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                                                           postfix.IsKind(SyntaxKind.PostDecrementExpression) =>
                    ReferencesIterationVariable(postfix.Operand),
                _ => false
            });
        }
        private void AnalyzeEnumeration(ITypeSymbol? type, EffectFlowValue collection, IOperation site,
            ref EffectFlowState state) {
            var getEnumerator = FindProtocolMethod(type, "GetEnumerator", 0) ??
                                FindImplementedProtocolMethod(type, "GetEnumerator", 0);
            if (getEnumerator == null) return;
            var enumerator = InvokeCore(getEnumerator, collection, [], [], site, ref state);
            if (enumerator.Roots.Count == 0) return;
            InvokeCoreOrValue(ResolveProtocolImplementation(
                FindProtocolMethod(getEnumerator.ReturnType, "MoveNext", 0), enumerator), enumerator, site, ref state);
            InvokeCoreOrValue(ResolveProtocolImplementation(
                FindProtocolProperty(getEnumerator.ReturnType, "Current")?.GetMethod, enumerator),
                enumerator, site, ref state);
            InvokeCoreOrValue(ResolveProtocolImplementation(
                FindProtocolMethod(getEnumerator.ReturnType, "Dispose", 0), enumerator), enumerator, site, ref state);
        }
        private void AnalyzeDisposal(
            ITypeSymbol? type,
            EffectFlowValue receiver,
            IOperation site,
            bool asynchronous,
            ref EffectFlowState state) {
            var interfaceType = session.Compilation.GetTypeByMetadataName(
                asynchronous ? "System.IAsyncDisposable" : "System.IDisposable");
            var name = asynchronous ? "DisposeAsync" : "Dispose";
            var member = interfaceType?.GetMembers(name).OfType<IMethodSymbol>().FirstOrDefault();
            var implementation = member == null || type is not INamedTypeSymbol named
                ? null
                : named.FindImplementationForInterfaceMember(member) as IMethodSymbol;
            implementation ??= type is ITypeParameterSymbol ? member : null;
            implementation ??= FindProtocolMethod(type, name, 0);
            if (implementation == null) return;
            var result = InvokeCore(implementation, receiver, [], [], site, ref state);
            if (asynchronous) AnalyzeAwaitable(implementation.ReturnType, result, site, ref state);
        }
        private void AnalyzeAwaitable(
            ITypeSymbol? type,
            EffectFlowValue receiver,
            IOperation site,
            ref EffectFlowState state) {
            var extensionAwaiters = semanticModel.LookupSymbols(site.Syntax.SpanStart, name: "GetAwaiter", includeReducedExtensionMethods: true)
                .OfType<IMethodSymbol>().Concat(session.Compilation.GetSymbolsWithName("GetAwaiter", SymbolFilter.Member,
                    session.CancellationToken).OfType<IMethodSymbol>().Where(static method => method.IsExtensionMethod));
            var getAwaiter = FindProtocolMethod(type, "GetAwaiter", 0) ?? extensionAwaiters
                .Select(method => method.ReducedFrom != null ? method : type == null ? null : method.ReduceExtensionMethod(type))
                .FirstOrDefault(static method => method?.Parameters.Length == 0);
            var awaiter = InvokeCoreOrValue(getAwaiter, receiver, site, ref state);
            InvokeCoreOrValue(FindProtocolMethod(getAwaiter?.ReturnType, "GetResult", 0), awaiter, site, ref state);
            var continuation = FindProtocolMethod(getAwaiter?.ReturnType, "UnsafeOnCompleted", 1) ??
                               FindImplementedProtocolMethod(getAwaiter?.ReturnType, "UnsafeOnCompleted", 1) ??
                               FindProtocolMethod(getAwaiter?.ReturnType, "OnCompleted", 1) ??
                               FindImplementedProtocolMethod(getAwaiter?.ReturnType, "OnCompleted", 1);
            InvokeCoreOrValue(continuation, awaiter, site, ref state);
        }
        private EffectFlowValue FindPatternInput(IOperation pattern, ref EffectFlowState state) {
            for (var parent = pattern.Parent; parent != null; parent = parent.Parent) {
                if (parent is IIsPatternOperation isPattern) return Evaluate(isPattern.Value, ref state);
                if (parent is ISwitchOperation switchOperation) return Evaluate(switchOperation.Value, ref state);
                if (parent is ISwitchExpressionOperation switchExpression) return Evaluate(switchExpression.Value, ref state);
            }
            return EffectFlowValue.Unknown;
        }
        private static IMethodSymbol? FindProtocolMethod(ITypeSymbol? type, string name, int parameterCount) {
            if (type is not INamedTypeSymbol named) return null;
            for (var current = named; current != null; current = current.BaseType) {
                var method = current.GetMembers(name).OfType<IMethodSymbol>()
                    .FirstOrDefault(candidate => !candidate.IsStatic && candidate.Parameters.Length == parameterCount);
                if (method != null) return method;
            }
            if (named.TypeKind != TypeKind.Interface) return null;
            foreach (var interfaceType in named.AllInterfaces) {
                var method = interfaceType.GetMembers(name).OfType<IMethodSymbol>()
                    .FirstOrDefault(candidate => !candidate.IsStatic && candidate.Parameters.Length == parameterCount);
                if (method != null) return method;
            }
            return null;
        }
        private static IPropertySymbol? FindProtocolProperty(ITypeSymbol? type, string name) {
            if (type is not INamedTypeSymbol named) return null;
            for (var current = named; current != null; current = current.BaseType) {
                var property = current.GetMembers(name).OfType<IPropertySymbol>()
                    .FirstOrDefault(static candidate => !candidate.IsStatic && candidate.Parameters.Length == 0);
                if (property != null) return property;
            }
            if (named.TypeKind != TypeKind.Interface) return null;
            foreach (var interfaceType in named.AllInterfaces) {
                var property = interfaceType.GetMembers(name).OfType<IPropertySymbol>()
                    .FirstOrDefault(static candidate => !candidate.IsStatic && candidate.Parameters.Length == 0);
                if (property != null) return property;
            }
            return null;
        }
        private static IMethodSymbol? ResolveProtocolImplementation(
            IMethodSymbol? member,
            EffectFlowValue receiver) {
            if (member == null || member.ContainingType.TypeKind != TypeKind.Interface ||
                receiver.ExactType is not { } exactType)
                return member;
            return exactType.FindImplementationForInterfaceMember(member) as IMethodSymbol ?? member;
        }
        private static IMethodSymbol? FindImplementedProtocolMethod(
            ITypeSymbol? type,
            string name,
            int parameterCount) {
            if (type is not INamedTypeSymbol named) return null;
            foreach (var interfaceType in named.AllInterfaces.OrderByDescending(static candidate => candidate.IsGenericType)) {
                foreach (var member in interfaceType.GetMembers(name).OfType<IMethodSymbol>()) {
                    if (member.IsStatic || member.Parameters.Length != parameterCount) continue;
                    if (named.FindImplementationForInterfaceMember(member) is IMethodSymbol implementation)
                        return implementation;
                }
            }
            return null;
        }
        private static bool IsInlineArray(ITypeSymbol? type) => type?.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.InlineArrayAttribute") == true;
        private CompilerMethodEffectSummary GetSummary(
            IMethodSymbol target,
            ImmutableDictionary<string, EffectFlowValue>? captures) {
            target = Normalize(target);
            if (target is { MethodKind: MethodKind.StaticConstructor, IsImplicitlyDeclared: true } &&
                target.ContainingType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(session.CancellationToken) is
                    TypeDeclarationSyntax typeDeclaration)
                return session.AnalyzeSummary(target, typeDeclaration,
                    session.Compilation.GetSemanticModel(typeDeclaration.SyntaxTree), captures);
            if (target.IsImplicitlyDeclared) {
                if (target.MethodKind == MethodKind.Constructor && target.ContainingType.BaseType is { } baseType &&
                    baseType.InstanceConstructors.FirstOrDefault(static constructor => constructor.Parameters.All(
                        parameter => parameter.IsOptional)) is { } baseConstructor) {
                    var inherited = GetSummary(baseConstructor, null);
                    var receiver = EffectFlowValue.FromRoot(new(EffectValueRootKind.Receiver), target.ContainingType);
                    return inherited with {
                        Receiver = inherited.Receiver.Instantiate(receiver,
                        baseConstructor.Parameters.Select(static _ => EffectFlowValue.None).ToArray(),
                        sourceMethod: baseConstructor)
                    };
                }
                return EmptySummary(target);
            }
            var syntax = target.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(session.CancellationToken);
            if (syntax is AccessorDeclarationSyntax { Body: null, ExpressionBody: null }) return EmptySummary(target);
            if (target.AssociatedSymbol is IPropertySymbol property &&
                property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(session.CancellationToken) is
                    PropertyDeclarationSyntax { AccessorList: { } accessors } &&
                accessors.Accessors.All(static accessor => accessor is { Body: null, ExpressionBody: null }))
                return EmptySummary(target);
            if (syntax == null) {
                if (IsIntrinsic(target)) return EmptySummary(target);
                if (TryFrameworkSummary(target, out var framework))
                    return new(framework, FrameworkReturn(target),
                        EffectFlowValue.FromRoot(new(EffectValueRootKind.Receiver), target.ContainingType),
                        [.. target.Parameters.Select(static _ => EffectFlowValue.None)]);
                return session.MetadataSummary(target);
            }
            return session.AnalyzeSummary(target, syntax, session.Compilation.GetSemanticModel(syntax.SyntaxTree), captures);
        }
        private void AddSummary(
            MethodEffects summary,
            EffectFlowValue? receiver,
            IReadOnlyList<EffectFlowValue> arguments,
            IOperation site,
            IMethodSymbol target,
            ImmutableArray<int> writtenArgumentOrdinals = default,
            ImmutableArray<int> readArgumentOrdinals = default,
            SharpProofEffect boundArgumentEffects = SharpProofEffect.None,
            SharpProofEffect boundReceiverEffects = SharpProofEffect.None) {
            summary = ApplyCatches(summary, site);
            const SharpProofEffect relative = SharpProofEffect.ReadsReceiverState | SharpProofEffect.WritesReceiverState |
                                                SharpProofEffect.ReadsArgumentState | SharpProofEffect.WritesArgumentState;
            var unboundEffects = summary.Effects & ~boundReceiverEffects;
            var mapped = unboundEffects & ~relative;
            if ((unboundEffects & SharpProofEffect.ReadsReceiverState) != 0)
                mapped |= effects.ReadEffect(receiver ?? EffectFlowValue.Unknown);
            if ((unboundEffects & SharpProofEffect.WritesReceiverState) != 0)
                mapped |= effects.WriteEffect(receiver ?? EffectFlowValue.Unknown);
            var candidateArguments = arguments.Where(static argument => argument.Roots.Count != 0).ToArray();
            if ((summary.Effects & SharpProofEffect.ReadsArgumentState) != 0) {
                EffectFlowValue[] readArguments = readArgumentOrdinals.IsDefault
                    ? candidateArguments
                    : [.. readArgumentOrdinals.Where(ordinal => ordinal >= 0 && ordinal < arguments.Count)
                        .Select(ordinal => arguments[ordinal]).Where(static argument => argument.Roots.Count != 0)];
                if (readArguments.Length != 0)
                    mapped |= readArguments.Aggregate(SharpProofEffect.None,
                        (current, argument) => current | effects.ReadEffect(argument));
                else if (readArgumentOrdinals.IsDefault)
                    mapped |= SharpProofEffect.ReadsArgumentState | SharpProofEffect.Unknown;
            }
            if ((summary.Effects & SharpProofEffect.WritesArgumentState) != 0) {
                EffectFlowValue[] writtenArguments = writtenArgumentOrdinals.IsDefault
                    ? candidateArguments
                    : [.. writtenArgumentOrdinals.Where(ordinal => ordinal >= 0 && ordinal < arguments.Count)
                        .Select(ordinal => arguments[ordinal]).Where(static argument => argument.Roots.Count != 0)];
                if (writtenArguments.Length != 0)
                    mapped |= writtenArguments.Aggregate(SharpProofEffect.None,
                        (current, argument) => current | effects.WriteEffect(argument));
                else if (writtenArgumentOrdinals.IsDefault)
                    mapped |= SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown;
            }
            mapped |= boundArgumentEffects | boundReceiverEffects;
            effects.AddTransitive(summary with { Effects = mapped }, site.Syntax, target, "source_call");
        }
        private void ApplyRefArguments(
            CompilerMethodEffectSummary summary,
            IMethodSymbol target,
            ImmutableArray<IArgumentOperation> arguments,
            EffectFlowValue receiver,
            IReadOnlyList<EffectFlowValue> values,
            ref EffectFlowState state) {
            foreach (var argument in arguments) {
                if (argument.Parameter is not { RefKind: not RefKind.None } parameter || parameter.Ordinal >= summary.Parameters.Length)
                    continue;
                Assign(argument.Value, summary.Parameters[parameter.Ordinal].Instantiate(
                    receiver, values, sourceMethod: target), false, ref state);
            }
        }
        private IReadOnlyList<EffectFlowValue> EvaluateArguments(
            ImmutableArray<IArgumentOperation> arguments,
            ref EffectFlowState state) {
            if (arguments.IsDefaultOrEmpty) return [];
            var values = new EffectFlowValue[arguments.Max(static argument => argument.Parameter?.Ordinal ?? 0) + 1];
            for (var index = 0; index < values.Length; index++) values[index] = EffectFlowValue.None;
            foreach (var argument in arguments) {
                var value = Evaluate(argument.Value, ref state);
                var ordinal = argument.Parameter?.Ordinal ?? 0;
                if (ordinal >= 0 && ordinal < values.Length) values[ordinal] = value;
            }
            return values;
        }
        private void Assign(IOperation target, EffectFlowValue value, bool isRef, ref EffectFlowState state) {
            while (target is IConversionOperation { OperatorMethod: null } conversion) target = conversion.Operand;
            if (target is IInvocationOperation { TargetMethod.ReturnsByRef: true } refInvocation) {
                effects.Write(Evaluate(refInvocation, ref state), target.Syntax, refInvocation.TargetMethod, "ref_return_write");
                return;
            }
            if (target is IPropertyReferenceOperation { Property.ReturnsByRef: true } refProperty) {
                effects.Write(EvaluateProperty(refProperty, ref state), target.Syntax, refProperty.Property, "ref_return_write");
                return;
            }
            switch (target) {
                case IFlowCaptureReferenceOperation capture:
                    state = state with { FlowCaptures = state.FlowCaptures.SetItem(capture.Id, value) };
                    state = semanticModel.GetSymbolInfo(capture.Syntax, session.CancellationToken).Symbol switch {
                        ILocalSymbol local => state with { Locals = state.Locals.SetItem(local, value) },
                        IParameterSymbol parameter => state.SetParameter(parameter, value),
                        _ => state
                    };
                    return;
                case ILocalReferenceOperation local:
                    if (local.Local.RefKind != RefKind.None && !isRef)
                        effects.Write(state.GetRef(local.Local), target.Syntax, local.Local, "ref_local_write");
                    else if (local.Local.RefKind != RefKind.None)
                        state = state with { RefLocals = state.RefLocals.SetItem(local.Local, value) };
                    else
                        state = state with { Locals = state.Locals.SetItem(local.Local, value) };
                    return;
                case IParameterReferenceOperation parameter:
                    if (parameter.Parameter.RefKind != RefKind.None)
                        effects.Write(state.GetParameter(parameter.Parameter), target.Syntax, parameter.Parameter, "ref_parameter_write");
                    state = state.SetParameter(parameter.Parameter, value);
                    return;
                case IDeclarationExpressionOperation declaration:
                    Assign(declaration.Expression, value, isRef, ref state);
                    return;
                case IFieldReferenceOperation field:
                    if (field.Field.IsStatic) AddTypeInitializerEffects(field.Field.ContainingType, field);
                    AssignMember(field.Instance, MemberKey(field.Field), value, field.Field.IsStatic, field.Syntax, field.Field, ref state);
                    return;
                case IPropertyReferenceOperation property:
                    AssignMember(property.Instance, MemberKey(property) ?? MemberKey(property.Property), value, property.Property.IsStatic,
                        property.Syntax, property.Property, ref state);
                    if (property.Property.SetMethod != null) {
                        var setterReceiver = Evaluate(property.Instance, ref state);
                        var setterValues = EvaluateArguments(property.Arguments, ref state).Append(value).ToArray();
                        InvokeCore(property.Property.SetMethod, setterReceiver, setterValues, property.Arguments, property, ref state);
                    }
                    return;
                case IArrayElementReferenceOperation array:
                    AssignMember(array.ArrayReference, IndexKey(array.Indices), value, false, array.Syntax, array.Type as ISymbol, ref state);
                    return;
                case IInlineArrayAccessOperation inlineArray:
                    AssignMember(inlineArray.Instance, "#?", value, false, inlineArray.Syntax, inlineArray.Type as ISymbol, ref state);
                    return;
                case IImplicitIndexerReferenceOperation implicitIndexer:
                    var indexerReceiver = Evaluate(implicitIndexer.Instance, ref state);
                    effects.Write(indexerReceiver, implicitIndexer.Syntax, null, "implicit_indexer_write");
                    if (implicitIndexer.IndexerSymbol is IPropertySymbol { SetMethod: { } setter })
                        InvokeCore(setter, indexerReceiver, [value], [], implicitIndexer, ref state);
                    return;
                case ITupleOperation tuple:
                    for (var index = 0; index < tuple.Elements.Length; index++)
                        Assign(tuple.Elements[index], value.Member("#" + index), false, ref state);
                    return;
                default:
                    var receiver = Evaluate(target, ref state);
                    effects.Write(receiver, target.Syntax, null, "assignment");
                    return;
            }
        }
        private void AssignMember(
            IOperation? receiverOperation,
            string key,
            EffectFlowValue value,
            bool isStatic,
            SyntaxNode syntax,
            ISymbol? symbol,
            ref EffectFlowState state) {
            if (isStatic) {
                effects.Add(SharpProofEffect.WritesStaticState, syntax, symbol, "static_member_write");
                return;
            }
            var receiver = Evaluate(receiverOperation, ref state);
            effects.Write(receiver, syntax, symbol, "instance_member_write");
            switch (receiverOperation) {
                case IFlowCaptureReferenceOperation capture:
                    state = state with { FlowCaptures = state.FlowCaptures.SetItem(capture.Id, receiver.WithMember(key, value)) };
                    break;
                case ILocalReferenceOperation local:
                    state = state with { Locals = state.Locals.SetItem(local.Local, receiver.WithMember(key, value)) };
                    break;
                case IParameterReferenceOperation parameter:
                    state = state.SetParameter(parameter.Parameter, receiver.WithMember(key, value));
                    break;
                case IInstanceReferenceOperation:
                    state = state with { Receiver = receiver.WithMember(key, value) };
                    break;
            }
        }
        private EffectFlowValue BindCallable(IOperation target, ITypeSymbol? type, ref EffectFlowState state) {
            while (target is IDelegateCreationOperation creation) target = creation.Target;
            if (target is IFlowAnonymousFunctionOperation) {
                var semanticFunction = semanticModel.GetOperation(target.Syntax, session.CancellationToken);
                if (semanticFunction is IDelegateCreationOperation semanticCreation) semanticFunction = semanticCreation.Target;
                if (semanticFunction is IAnonymousFunctionOperation anonymousFunction) target = anonymousFunction;
            }
            IMethodSymbol? targetMethod = target switch {
                IAnonymousFunctionOperation function => function.Symbol,
                IMethodReferenceOperation reference => reference.Method,
                ILocalFunctionOperation localFunction => localFunction.Symbol,
                _ => null
            };
            if (targetMethod == null) return EffectFlowValue.Unknown;
            var receiver = target is IMethodReferenceOperation methodReference
                ? Evaluate(methodReference.Instance, ref state)
                : EffectFlowValue.None;
            var captures = ImmutableDictionary.CreateBuilder<string, EffectFlowValue>(StringComparer.Ordinal);
            foreach (var reference in target.DescendantsAndSelf()) {
                switch (reference) {
                    case ILocalReferenceOperation local
                        when !SymbolEqualityComparer.Default.Equals(local.Local.ContainingSymbol, targetMethod):
                        captures[EffectFlowState.SymbolKey(local.Local)] = state.GetLocal(local.Local);
                        break;
                    case IParameterReferenceOperation parameter
                        when !SymbolEqualityComparer.Default.Equals(parameter.Parameter.ContainingSymbol, targetMethod):
                        captures[EffectFlowState.SymbolKey(parameter.Parameter)] = state.GetParameter(parameter.Parameter);
                        break;
                    case IInstanceReferenceOperation instance
                        when targetMethod.MethodKind is MethodKind.AnonymousFunction or MethodKind.LocalFunction:
                        captures[EffectFlowState.SymbolKey(instance.Type ?? method.ContainingType)] = state.Receiver;
                        break;
                }
            }
            return EffectFlowValue.Callable(new(targetMethod, receiver, captures.ToImmutable()), type);
        }
        private static string MemberKey(ISymbol symbol) => symbol is IFieldSymbol { ContainingType.IsTupleType: true } tupleField
            ? "#" + tupleField.ContainingType.TupleElements.TakeWhile(field =>
                !SymbolEqualityComparer.Default.Equals(field, tupleField) &&
                !SymbolEqualityComparer.Default.Equals(field, tupleField.CorrespondingTupleField)).Count()
            : symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        private static string? MemberKey(IOperation operation) => operation switch {
            IFieldReferenceOperation field => MemberKey(field.Field),
            IPropertyReferenceOperation { Arguments.Length: > 0 } property => IndexKey(
                [.. property.Arguments.Select(static argument => argument.Value)]),
            IPropertyReferenceOperation property => MemberKey(property.Property),
            IArrayElementReferenceOperation array => IndexKey(array.Indices),
            _ => null
        };
        private static string IndexKey(ImmutableArray<IOperation> indices) => "#" + string.Join(",", indices.Select(index =>
            index.ConstantValue is { HasValue: true, Value: { } value }
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : "?"));
        private static bool IsIntrinsic(IMethodSymbol target) =>
            target is { MethodKind: MethodKind.Constructor, ContainingType.SpecialType: SpecialType.System_Object } ||
            target is { MethodKind: MethodKind.Constructor } && target.ContainingType?.ToDisplayString() == "System.Exception" ||
            target.Name == "TryParse" && SymbolicTypeFacts.IsBuiltInNumericSpecialType(
                target.ContainingType?.SpecialType ?? SpecialType.None) &&
                target.ContainingType?.SpecialType != SpecialType.System_Char ||
            target is {
                MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol {
                    Name: "Length", Type.SpecialType: SpecialType.System_Int32, ContainingType.SpecialType: SpecialType.System_Array
                }
            } ||
            target is { MethodKind: MethodKind.PropertyGet, ContainingType.SpecialType: SpecialType.System_String } ||
            target.ContainingType?.ToDisplayString() is "System.Index" or "System.Range" ||
            target.ContainingType?.ToDisplayString().StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal) == true ||
            target.ContainingType?.ToDisplayString().StartsWith("System.Runtime.CompilerServices.ValueTaskAwaiter", StringComparison.Ordinal) == true ||
            target.ContainingType?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ||
            target is { MethodKind: MethodKind.PropertyGet, Name: "HasValue", ContainingType.SpecialType: SpecialType.System_Nullable_T };
        private static CompilerMethodEffectSummary EmptySummary(IMethodSymbol target) => new(
            new(SharpProofEffect.None, SharpProofCapability.None, [], [], []),
            target.ReturnsVoid ? EffectFlowValue.None : EffectFlowValue.Unknown,
            EffectFlowValue.FromRoot(new(EffectValueRootKind.Receiver), target.ContainingType),
            [.. target.Parameters.Select(parameter => EffectFlowValue.FromRoot(
                new(EffectValueRootKind.Argument, parameter.Ordinal), parameter.Type))]);
        private static EffectFlowValue FrameworkReturn(IMethodSymbol target) => target.ReturnsVoid
            ? EffectFlowValue.None
            : target.ReturnType.IsReferenceType
                ? EffectFlowValue.Fresh(target.ReturnType)
                : EffectFlowValue.None;
        private static bool TryFrameworkSummary(IMethodSymbol method, out MethodEffects summary) {
            var type = method.ContainingType;
            var definition = type?.OriginalDefinition.ToDisplayString();
            var numeric = SymbolicTypeFacts.IsBuiltInNumericSpecialType(type?.SpecialType ?? SpecialType.None) &&
                          type?.SpecialType != SpecialType.System_Char;
            SharpProofEffect? effects = (definition, method.MethodKind, method.Name) switch {
                ("System.Collections.Generic.List<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>",
                    MethodKind.PropertyGet, _) => SharpProofEffect.ReadsReceiverState,
                ("System.Collections.Generic.List<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>",
                    MethodKind.PropertySet or MethodKind.Constructor, _) => SharpProofEffect.WritesReceiverState,
                ("System.Collections.Generic.List<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>", _, "Add") =>
                    SharpProofEffect.WritesReceiverState | SharpProofEffect.Allocates,
                (_, _, "IsNullOrEmpty" or "IsNullOrWhiteSpace") when type?.SpecialType == SpecialType.System_String =>
                    SharpProofEffect.None,
                (_, _, "Parse") when numeric => SharpProofEffect.Throws,
                (_, _, "ToString") when numeric => SharpProofEffect.Allocates,
                (_, _, "Split" or "Substring" or "Trim" or "TrimStart" or "TrimEnd")
                    when type?.SpecialType == SpecialType.System_String => SharpProofEffect.Allocates,
                ("System.Span<T>" or "System.ReadOnlySpan<T>", _, "ToArray") => SharpProofEffect.Allocates,
                _ => null
            };
            var exceptions = effects == SharpProofEffect.Throws
                ? ImmutableArray.Create(
                    MethodExceptionFact.Boundary("System.FormatException", MethodExceptionSource.Contract, "framework_parse_model"),
                    MethodExceptionFact.Boundary("System.OverflowException", MethodExceptionSource.Contract, "framework_parse_model"))
                : [];
            summary = effects.HasValue ? new(effects.Value, SharpProofCapability.None, exceptions, [], []) : null!;
            return effects.HasValue;
        }
    }

    private sealed class EffectAccumulator(IMethodSymbol method) {
        private SharpProofEffect _effects;
        private SharpProofCapability _capabilities;
        private readonly ImmutableArray<MethodEffectSite>.Builder _sites = ImmutableArray.CreateBuilder<MethodEffectSite>();
        private readonly ImmutableArray<MethodExceptionFact>.Builder _exceptions = ImmutableArray.CreateBuilder<MethodExceptionFact>();
        private readonly ImmutableArray<SharpProofUnknownReason>.Builder _unknowns = ImmutableArray.CreateBuilder<SharpProofUnknownReason>();
        private readonly HashSet<int> _writtenArgumentOrdinals = [];
        private readonly HashSet<int> _readArgumentOrdinals = [];
        internal ImmutableArray<int> WrittenArgumentOrdinals => [.. _writtenArgumentOrdinals.OrderBy(static ordinal => ordinal)];
        internal ImmutableArray<int> ReadArgumentOrdinals => [.. _readArgumentOrdinals.OrderBy(static ordinal => ordinal)];
        internal SharpProofEffect BoundArgumentEffects { get; private set; }
        internal SharpProofEffect BoundReceiverEffects { get; private set; }
        internal void Add(SharpProofEffect effect, SyntaxNode syntax, ISymbol? symbol, string reason) {
            _effects |= effect;
            _sites.Add(Site(effect, syntax, symbol, reason));
        }
        internal void Add(SharpProofEffect effect, SharpProofCapability capabilities, SyntaxNode syntax, ISymbol? symbol,
            string reason) {
            _capabilities |= capabilities;
            Add(effect, syntax, symbol, reason);
        }
        internal void Read(EffectFlowValue value, SyntaxNode syntax, ISymbol? symbol, string reason) {
            foreach (var root in value.Roots)
                if (root is { Kind: EffectValueRootKind.Argument, Ordinal: >= 0 }) {
                    if (IsFormalArgumentRoot(root)) _readArgumentOrdinals.Add(root.Ordinal);
                    else BoundArgumentEffects |= SharpProofEffect.ReadsArgumentState;
                }
                else if (root.Kind == EffectValueRootKind.Receiver && !IsFormalReceiverRoot(root))
                    BoundReceiverEffects |= SharpProofEffect.ReadsReceiverState;
            Add(ReadEffect(value), syntax, symbol, reason);
        }
        internal void Write(EffectFlowValue value, SyntaxNode syntax, ISymbol? symbol, string reason) {
            foreach (var root in value.Roots)
                if (root is { Kind: EffectValueRootKind.Argument, Ordinal: >= 0 }) {
                    if (IsFormalArgumentRoot(root)) _writtenArgumentOrdinals.Add(root.Ordinal);
                    else BoundArgumentEffects |= SharpProofEffect.WritesArgumentState;
                }
                else if (root.Kind == EffectValueRootKind.Receiver && !IsFormalReceiverRoot(root))
                    BoundReceiverEffects |= SharpProofEffect.WritesReceiverState;
            Add(WriteEffect(value), syntax, symbol, reason);
        }
        private bool IsFormalArgumentRoot(EffectValueRoot root) => root.Ordinal < method.Parameters.Length &&
            string.Equals(root.Key, EffectFlowState.SymbolKey(method.Parameters[root.Ordinal]), StringComparison.Ordinal);
        private bool IsFormalReceiverRoot(EffectValueRoot root) =>
            string.Equals(root.Key, EffectFlowState.SymbolKey(method), StringComparison.Ordinal);
        internal SharpProofEffect ReadEffect(EffectFlowValue value) => Map(value, write: false);
        internal SharpProofEffect WriteEffect(EffectFlowValue value) => Map(value, write: true);
        private static SharpProofEffect Map(EffectFlowValue value, bool write) {
            var result = SharpProofEffect.None;
            foreach (var root in value.Roots)
                result |= (root.Kind, write) switch {
                    (EffectValueRootKind.Receiver, false) => SharpProofEffect.ReadsReceiverState,
                    (EffectValueRootKind.Receiver, true) => SharpProofEffect.WritesReceiverState,
                    (EffectValueRootKind.Argument, false) => SharpProofEffect.ReadsArgumentState,
                    (EffectValueRootKind.Argument, true) => SharpProofEffect.WritesArgumentState,
                    (EffectValueRootKind.Captured, false) => SharpProofEffect.ReadsCapturedState,
                    (EffectValueRootKind.Captured, true) => SharpProofEffect.WritesCapturedState,
                    (EffectValueRootKind.Static, false) => SharpProofEffect.ReadsStaticState,
                    (EffectValueRootKind.Static, true) => SharpProofEffect.WritesStaticState,
                    (EffectValueRootKind.Ambient, false) => SharpProofEffect.ReadsAmbientState,
                    (EffectValueRootKind.Ambient, true) => SharpProofEffect.WritesAmbientState,
                    (EffectValueRootKind.Fresh, true) => SharpProofEffect.WritesFreshOwnedState,
                    (EffectValueRootKind.Unknown, _) => SharpProofEffect.Unknown,
                    _ => SharpProofEffect.None
                };
            return result;
        }
        internal void Throw(ITypeSymbol? type, SyntaxNode syntax, string reason) {
            _effects |= SharpProofEffect.Throws;
            AddException(type, syntax, reason, SharpProofVerdict.Proven);
        }
        internal void Caught(ITypeSymbol? type, SyntaxNode syntax, string reason) =>
            AddException(type, syntax, reason, SharpProofVerdict.Disproven);
        private void AddException(ITypeSymbol? type, SyntaxNode syntax, string reason, SharpProofVerdict escape) =>
            _exceptions.Add(new(type?.ToDisplayString() ?? "System.Exception", escape,
                MethodExceptionSource.ExplicitThrow, syntax.ToString(), string.Empty, syntax.SpanStart, syntax.Span.Length,
                false, reason));
        internal void AddUnknown(SyntaxNode syntax, string reason, ISymbol? symbol = null) {
            Add(SharpProofEffect.Unknown, syntax, symbol, reason);
            _unknowns.Add(Reason(reason));
        }
        internal void AddTransitive(MethodEffects summary, SyntaxNode syntax, ISymbol symbol, string reason) {
            _effects |= summary.Effects;
            _capabilities |= summary.Capabilities;
            _unknowns.AddRange(summary.UnknownReasons);
            _exceptions.AddRange(summary.ExceptionFacts.Select(fact => fact with {
                IsTransitive = true,
                Reason = reason,
                Source = MethodExceptionSource.Callee
            }));
            if (summary.Effects != SharpProofEffect.None) _sites.Add(Site(summary.Effects, syntax, symbol, reason) with { IsTransitive = true });
        }
        internal MethodEffects Build() => new(
            _effects,
            _capabilities,
            [.. _exceptions.Distinct()],
            [.. _sites.Distinct()],
            [.. _unknowns.Distinct()]);
    }
}
