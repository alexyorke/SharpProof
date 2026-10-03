using SharpProof.Frontend.Host;

namespace SharpProof.Analyzer;

// The one Total IR body lowering behind the compiler's claims and the
// analyzer's advisories. Source callees are inlined with their Requires, and
// opaque calls carry the effects their API specification declares.
internal static class TotalBodyLowering
{
    internal const int MaximumWork = 4096;

    // Declarations whose body the Total IR lowers: methods, operators,
    // accessors, expression-bodied properties, constructors and local
    // functions.
    internal static bool IsBodyDeclaration(SyntaxNode? declaration)
    {
        return declaration is MethodDeclarationSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax or
            AccessorDeclarationSyntax or ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax or IndexerDeclarationSyntax } or
            ConstructorDeclarationSyntax or LocalFunctionStatementSyntax;
    }

    // The control flow graph of a body declaration, or null when its body
    // does not run exactly as written: a constructor that also runs member
    // initializers or a base constructor other than object's, and a local
    // function that captures state.
    internal static ControlFlowGraph? CreateGraph(SyntaxNode declaration, IMethodSymbol method, SemanticModel model,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (declaration)
            {
                case ConstructorDeclarationSyntax constructor:
                    return IsPlainConstructor(constructor, method, cancellationToken)
                        ? ControlFlowGraph.Create(constructor, model, cancellationToken) : null;
                case LocalFunctionStatementSyntax local:
                    if (model.GetOperation(local, cancellationToken) is not { } operation || Captures(operation, method))
                    { return null; }
                    var owner = local.Ancestors().FirstOrDefault(node => node is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax ||
                        IsBodyDeclaration(node));
                    var ownerSymbol = owner == null ? null : model.GetDeclaredSymbol(owner, cancellationToken) as IMethodSymbol ??
                        (owner.Parent?.Parent is BasePropertyDeclarationSyntax property
                            ? (model.GetDeclaredSymbol(property, cancellationToken) as IPropertySymbol)?.GetMethod : null);
                    return owner is null or AnonymousFunctionExpressionSyntax || ownerSymbol == null ? null
                        : CreateGraph(owner, ownerSymbol, model, cancellationToken)?.GetLocalFunctionControlFlowGraph(method, cancellationToken);
                default:
                    return ControlFlowGraph.Create(declaration, model, cancellationToken);
            }
        }
        catch (ArgumentException)
        { return null; }
    }

    private static bool IsPlainConstructor(ConstructorDeclarationSyntax constructor, IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        if (method.IsStatic || constructor.Initializer is { } initializer &&
            (initializer.ThisOrBaseKeyword.IsKind(SyntaxKind.ThisKeyword) || initializer.ArgumentList.Arguments.Count != 0) ||
            method.ContainingType is not { IsReferenceType: true, IsRecord: false, BaseType.SpecialType: SpecialType.System_Object } type)
        { return false; }
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration || declaration.ParameterList != null ||
                declaration.Members.Any(member =>
                    member is FieldDeclarationSyntax field && !field.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                        field.Declaration.Variables.Any(variable => variable.Initializer != null) ||
                    member is PropertyDeclarationSyntax property && !property.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                        property.Initializer != null ||
                    member is EventFieldDeclarationSyntax eventField && !eventField.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                        eventField.Declaration.Variables.Any(variable => variable.Initializer != null)))
            { return false; }
        }
        return true;
    }

    // A local function that reads an enclosing local, parameter or `this`, or
    // nests another function, does not run from its own parameters alone.
    private static bool Captures(IOperation body, IMethodSymbol method)
    {
        return body.Descendants().Any(operation => operation switch
        {
            ILocalReferenceOperation local => !SymbolEqualityComparer.Default.Equals(local.Local.ContainingSymbol, method),
            IParameterReferenceOperation parameter => !SymbolEqualityComparer.Default.Equals(parameter.Parameter.ContainingSymbol, method),
            IInstanceReferenceOperation or ILocalFunctionOperation or IAnonymousFunctionOperation => true,
            _ => false
        });
    }

    internal static AccessorDeclarationSyntax? AutoAccessor(SyntaxNode declaration)
    { return declaration is AccessorDeclarationSyntax { Body: null, ExpressionBody: null } accessor ? accessor : null; }

    internal static FrontendProgramLoweringResult Lower(CSharpCompilation compilation, ControlFlowGraph graph,
        TotalLoweringContext context, CancellationToken cancellationToken, ResolveTotalIlBody? resolveIl = null,
        Func<IMethodSymbol, TotalScalarCallModel?>? resolveScalarModel = null,
        Func<INamedTypeSymbol, bool>? initializationFree = null,
        Func<TotalLoweringContext, TotalIlBody, bool>? prepareMetadata = null, bool opaqueCalls = false)
    {
        var lowering = LowerOnce(compilation, graph, context, resolveIl, resolveScalarModel,
            initializationFree, prepareMetadata, opaqueCalls, approximateElementReads: false, cancellationToken);
        // A body that writes elements reads them as approximations.
        return opaqueCalls && lowering.IsExact && MutatesElements(lowering.Program) &&
            ReadsElements(context.Factory, BodyTerms(lowering.Program))
            ? LowerOnce(compilation, graph, context, resolveIl, resolveScalarModel,
                initializationFree, prepareMetadata, opaqueCalls, approximateElementReads: true, cancellationToken)
            : lowering;
    }

    private static FrontendProgramLoweringResult LowerOnce(CSharpCompilation compilation, ControlFlowGraph graph,
        TotalLoweringContext context, ResolveTotalIlBody? resolveIl,
        Func<IMethodSymbol, TotalScalarCallModel?>? resolveScalarModel, Func<INamedTypeSymbol, bool>? initializationFree,
        Func<TotalLoweringContext, TotalIlBody, bool>? prepareMetadata, bool opaqueCalls, bool approximateElementReads,
        CancellationToken cancellationToken)
    {
        context.AllowObjectWidening = prepareMetadata != null;
        var apiSpecs = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        var contracts = new ExternalEffectResolver(compilation, apiSpecs);
        var invocationEmission = new InvocationEmissionPolicy(compilation);
        return new RoslynProgramLowerer(context.Factory).LowerCandidate(graph, context, frame =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (initializationFree != null && !initializationFree(frame.Target.ContainingType))
            { return false; }
            var contracts = new ContractBinder(compilation, context.Factory).BindTotalRequires(frame);
            if (!contracts.IsSuccess || frame.Target.DeclaringSyntaxReferences.Length != 1)
            { return false; }
            frame.SourceCallPreconditions = [.. contracts.Clauses.Where(clause => clause.Kind == BoundContractKind.Requires)
                .Select(clause => new TotalSourcePrecondition(clause.Value, clause.SafeCondition, clause.SourceOperation))];
            var syntax = frame.Target.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
            var operation = CompilationModelProvider.GetSemanticModel(compilation, syntax.SyntaxTree)
                .GetOperation(syntax, cancellationToken);
            if (operation == null)
            { return false; }
            var pending = new Stack<IOperation>();
            pending.Push(operation);
            var remaining = MaximumWork;
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { return false; }
                var current = pending.Pop();
                if (current is IInvocationOperation invocation && frame.IsSpecificationOperation(current))
                {
                    if (!invocationEmission.IsElided(current))
                    { frame.RestoreSpecificationCall(invocation); }
                    // Emitted calls and their arguments use ordinary body
                    // lowering. Elided arguments do not execute. Neither case
                    // imports callee proof assumptions into the caller.
                    continue;
                }
                foreach (var child in current.ChildOperations)
                { pending.Push(child); }
            }
            frame.DiscardSpecificationAssumptions();
            return true;
        }, resolveIl == null ? null : (method, token) => TrustedContractEffects(contracts, method) == null ? resolveIl(method, token) : null,
            cancellationToken,
            method => ResolveScalarModel(method, context.Factory, apiSpecs) ?? resolveScalarModel?.Invoke(method),
            prepareMetadata, opaqueCalls, method => OpaqueEffects(contracts, apiSpecs, method), approximateElementReads);
    }

    internal static TotalScalarCallModel? ResolveScalarModel(IMethodSymbol method, IrFactory factory, ResolvedApiSpecTable apiSpecs)
    {
        if (!apiSpecs.TryGet(method, out var spec))
        { return null; }
        return spec.Template.Target.DocumentationCommentId switch
        {
            "M:System.Math.Abs(System.Int32)" =>
                new TotalScalarCallModel(1, arguments => CSharpOperationSemantics.Int32MathAbs(factory, arguments[0])),
            "M:System.Array.Empty``1" when CSharpOperationSemantics.IsReferenceDomain(method.ReturnType) =>
                new TotalScalarCallModel(0, _ => CSharpOperationSemantics.ArrayEmpty(factory,
                    new RoslynTypeMapper(factory).GetTypeId(method.ReturnType))),
            "M:System.String.Concat(System.String,System.String)" =>
                new TotalScalarCallModel(2, arguments => CSharpOperationSemantics.StringConcat(factory,
                    arguments[0], arguments[1]), stringConcatenation: true),
            _ => null
        };
    }

    // What a call may do: a trusted complete effect contract bounds it, then
    // an API specification; a call with neither may do anything.
    private static IrOpaqueCallEffects? OpaqueEffects(ExternalEffectResolver contracts, ResolvedApiSpecTable specs,
        IMethodSymbol method)
    {
        if (TrustedContractEffects(contracts, method) is { } trusted)
        { return trusted; }
        if (!specs.TryGet(method, out var spec))
        { return null; }
        var facets = spec.Template.Facets;
        var declared = facets.Effects.Effects;
        if ((declared & SpecEffect.Unknown) != 0)
        { return IrOpaqueCallEffects.All; }
        var effects = IrOpaqueCallEffects.None;
        if (facets.Throws.Behavior != SpecThrowBehavior.DoesNotThrow)
        { effects |= IrOpaqueCallEffects.Throws; }
        if (facets.Allocation.Behavior != SpecAllocationBehavior.None)
        { effects |= IrOpaqueCallEffects.Allocates; }
        if ((declared & (SpecEffect.WritesReceiverState | SpecEffect.WritesArgumentState | SpecEffect.WritesAmbientState | SpecEffect.InputOutput)) != 0)
        { effects |= IrOpaqueCallEffects.Writes; }
        if ((declared & SpecEffect.Synchronization) != 0)
        { effects |= IrOpaqueCallEffects.Synchronizes; }
        if ((declared & (SpecEffect.ReadsReceiverState | SpecEffect.ReadsArgumentState | SpecEffect.ReadsAmbientState | SpecEffect.InputOutput)) != 0)
        { effects |= IrOpaqueCallEffects.Reads; }
        if ((declared & SpecEffect.ReadsAmbientState) != 0)
        { effects |= IrOpaqueCallEffects.ReadsAmbient; }
        if ((declared & SpecEffect.InputOutput) != 0)
        { effects |= IrOpaqueCallEffects.InputOutput; }
        if ((declared & SpecEffect.NativeCode) != 0)
        { effects |= IrOpaqueCallEffects.NativeCode; }
        if ((declared & SpecEffect.Reflection) != 0)
        { effects |= IrOpaqueCallEffects.Reflection; }
        if ((declared & SpecEffect.Nondeterminism) != 0)
        { effects |= IrOpaqueCallEffects.Nondeterminism; }
        return effects;
    }

    // A trusted complete contract is a metadata callee's boundary: its
    // implementation is not inlined.
    private static IrOpaqueCallEffects? TrustedContractEffects(ExternalEffectResolver contracts, IMethodSymbol method)
    {
        var contract = contracts.ResolveContract(method);
        return contract.Kind == EffectContractResolutionKind.Valid ||
            contract is { Kind: EffectContractResolutionKind.Incomplete, Summary.AnalysisIncompleteReason: EffectAnalysisIncompleteReason.CallPreconditionNotProven } &&
            !HasParameterContracts(method)
            ? ContractEffects(EffectSummaryProjector.Project(contract.Summary)) : null;
    }

    // A metadata contract that is not declared precondition-free holds when
    // the callee's preconditions do; one without parameter contracts has none.
    private static bool HasParameterContracts(IMethodSymbol method)
    {
        return method.Parameters.SelectMany(parameter => parameter.GetAttributes()).Any(attribute =>
            attribute.AttributeClass?.ContainingNamespace is { Name: "Attributes", ContainingNamespace: { Name: "SharpProof", ContainingNamespace.IsGlobalNamespace: true } });
    }

    private static IrOpaqueCallEffects ContractEffects(EffectProjection declared)
    {
        const EffectContractKind Writes = EffectContractKind.WritesReceiverState | EffectContractKind.WritesArgumentState |
            EffectContractKind.WritesCapturedState | EffectContractKind.WritesStaticState | EffectContractKind.WritesAmbientState;
        const EffectContractKind Reads = EffectContractKind.ReadsReceiverState | EffectContractKind.ReadsArgumentState |
            EffectContractKind.ReadsCapturedState | EffectContractKind.ReadsStaticState | EffectContractKind.ReadsAmbientState;
        var effects = IrOpaqueCallEffects.None;
        foreach (var (kind, call) in new[] {
            (EffectContractKind.Throws, IrOpaqueCallEffects.Throws), (EffectContractKind.Allocates, IrOpaqueCallEffects.Allocates),
            (Writes, IrOpaqueCallEffects.Writes), (Reads, IrOpaqueCallEffects.Reads),
            (EffectContractKind.ReadsAmbientState | EffectContractKind.ReadsStaticState | EffectContractKind.ReadsCapturedState,
                IrOpaqueCallEffects.ReadsAmbient),
            (EffectContractKind.Synchronizes, IrOpaqueCallEffects.Synchronizes),
            (EffectContractKind.UsesNondeterminism, IrOpaqueCallEffects.Nondeterminism),
            (EffectContractKind.UsesNativeCode, IrOpaqueCallEffects.NativeCode),
            (EffectContractKind.UsesReflection, IrOpaqueCallEffects.Reflection) })
        {
            if ((declared.Effects & kind) != 0)
            { effects |= call; }
        }
        return IrOpaqueCallSite.WithCapabilities(effects, (int)declared.Capabilities);
    }

    // The IR reads an array element as a pure function of the reference, which
    // holds only while nothing writes elements: neither an element store nor an
    // opaque call, which may write any array. Entry preconditions see the
    // initial arrays; every other read must then be an approximation.
    internal static bool MutatesElements(IrProgram program)
    {
        return program.Blocks.SelectMany(block => block.Instructions).Any(instruction =>
            instruction is IrCallInstruction { Receiver: null, Target: null } or IrWriteInstruction { Region: IrWriteRegion.Element });
    }

    internal static IEnumerable<IrTerm> BodyTerms(IrProgram program)
    { return program.Blocks.SelectMany(block => block.Instructions).SelectMany(IrInstructionFacts.ReadTerms); }

    internal static bool ReadsElements(IrFactory factory, IEnumerable<IrTerm> terms)
    {
        var pending = new Stack<IrTerm>(terms);
        while (pending.Count != 0)
        {
            switch (pending.Pop())
            {
                case IrSequenceAccessTerm access:
                    if (factory.GetTypeInfo(access.Sequence.Type).Kind != IrTypeKind.String)
                    { return true; }
                    pending.Push(access.Sequence);
                    pending.Push(access.Index);
                    break;
                case IrOpaqueTerm:
                    return true;
                case IrUnaryTerm unary:
                    pending.Push(unary.Operand);
                    break;
                case IrBinaryTerm binary:
                    pending.Push(binary.Left);
                    pending.Push(binary.Right);
                    break;
                case IrConditionalTerm conditional:
                    pending.Push(conditional.Condition);
                    pending.Push(conditional.WhenTrue);
                    pending.Push(conditional.WhenFalse);
                    break;
                case IrCastTerm cast:
                    pending.Push(cast.Operand);
                    break;
                case IrLengthTerm length:
                    pending.Push(length.Value);
                    break;
            }
        }
        return false;
    }
}
