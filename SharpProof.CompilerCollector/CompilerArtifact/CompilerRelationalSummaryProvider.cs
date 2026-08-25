using System.Security.Cryptography;
using System.Text;

// Relational summary inference runs only in the build-time compiler collector.
namespace SharpProof.CompilerArtifact;

internal sealed record CompilerSummaryEvidenceAuthority(
    CompilerSummaryOrigin Origin,
    string CallIdentity,
    string EvidenceSha256,
    string EvidenceIdentity,
    string SourcePath,
    string SourceTreeSha256,
    int SourceStart,
    int SourceLength,
    string OwningModuleName,
    int MethodMetadataToken);

internal sealed class CompilerRelationalSummaryProvider
{
    private readonly CSharpCompilation _compilation;
    private readonly IrFactory _factory;
    private readonly ResolvedApiSpecTable _apiSpecs;
    private readonly CompilerSpecificationPackProvider _specificationPacks;
    private readonly Dictionary<IMethodSymbol, IrRelationalSummary> _summaries =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, CompilerSummaryEvidenceAuthority> _authorities =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<IMethodSymbol> _failed =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<IMethodSymbol> _active =
        new(SymbolEqualityComparer.Default);

    internal CompilerImplementationIlAbstentionReason LastImplementationIlAbstention
    {
        get;
        private set;
    }

    internal ImmutableArray<CompilerSummaryEvidenceAuthority> SummaryEvidenceAuthorities =>
        [.. _authorities.Values
            .OrderBy(static authority => (int)authority.Origin)
            .ThenBy(static authority => authority.CallIdentity, StringComparer.Ordinal)
            .ThenBy(static authority => authority.EvidenceIdentity, StringComparer.Ordinal)
            .ThenBy(static authority => authority.EvidenceSha256, StringComparer.Ordinal)];

    internal CompilerRelationalSummaryProvider(
        CSharpCompilation compilation,
        IrFactory factory,
        ResolvedApiSpecTable apiSpecs,
        IEnumerable<string>? specificationPacks = null)
        : this(
            compilation,
            factory,
            apiSpecs,
            CompilerSpecificationPackProvider.ResolveAuthority(specificationPacks))
    {
    }

    internal CompilerRelationalSummaryProvider(
        CSharpCompilation compilation,
        IrFactory factory,
        ResolvedApiSpecTable apiSpecs,
        CompilerSpecificationPackAuthority specificationPackAuthority)
    {
        _compilation = ArgumentNullGuard.NotNull(
            compilation,
            nameof(compilation));
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        _apiSpecs = ArgumentNullGuard.NotNull(apiSpecs, nameof(apiSpecs));
        _specificationPacks = new CompilerSpecificationPackProvider(
            factory,
            specificationPackAuthority);
    }

    internal bool IsAdmissiblePureCall(IMethodSymbol method)
    {
        return _apiSpecs.IsSideEffectFree(method) ||
            _specificationPacks.CanResolve(method) ||
            IsSourceCandidate(method) ||
            CompilerImplementationIlSummaryLowerer.IsCandidate(
                _compilation,
                method);
    }

    internal bool TryGet(
        IMethodSymbol method,
        IrMemberId member,
        CancellationToken cancellationToken,
        out IrRelationalSummary? summary)
    {
        cancellationToken.ThrowIfCancellationRequested();
        method = Normalize(method);
        if (_summaries.TryGetValue(method, out summary))
        {
            return summary.Signature.Member == member;
        }

        if (_failed.Contains(method) || !_active.Add(method))
        {
            summary = null;
            return false;
        }

        try
        {
            if (!TryBuildSource(
                    method,
                    member,
                    cancellationToken,
                    out summary) &&
                !CompilerImplementationIlSummaryLowerer.TryBuild(
                    _compilation,
                    _factory,
                    method,
                    member,
                    IsAdmissiblePureCall,
                    TryGet,
                    cancellationToken,
                    out summary,
                    out var implementationIlAbstention) &&
                !_specificationPacks.TryBuild(
                    method,
                    member,
                    cancellationToken,
                    out summary))
            {
                LastImplementationIlAbstention = implementationIlAbstention;
                _failed.Add(method);
                return false;
            }

            var authority = CreateAuthority(
                method,
                summary!,
                cancellationToken);
            if (authority == null)
            {
                _failed.Add(method);
                summary = null;
                return false;
            }

            _summaries.Add(method, summary!);
            _authorities.Add(method, authority);
            return true;
        }
        finally
        {
            _active.Remove(method);
        }
    }

    private bool TryBuildSource(
        IMethodSymbol method,
        IrMemberId member,
        CancellationToken cancellationToken,
        out IrRelationalSummary? summary)
    {
        summary = null;
        if (!IsSourceCandidate(method) ||
            method.DeclaringSyntaxReferences.Length != 1 ||
            method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken)
                is not BaseMethodDeclarationSyntax declaration)
        {
            return false;
        }

        var semanticModel =
            SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
                _compilation,
                declaration.SyntaxTree);
        ControlFlowGraph? graph;
        try
        {
            graph = ControlFlowGraph.Create(declaration, semanticModel);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (graph == null || graph.Blocks.IsDefaultOrEmpty)
        {
            return false;
        }

        var selected = new RoslynProgramLowerer(
            _factory,
            IsAdmissiblePureCall).LowerSelected(
                graph,
                graph.Blocks[0],
                firstOperation: 0,
                static _ => false);
        if (!selected.Lowering.IsExact)
        {
            return false;
        }

        var memberInfo = _factory.GetMemberInfo(member);
        if (!memberInfo.IsStatic ||
            memberInfo.ParameterTypes.Length != method.Parameters.Length)
        {
            return false;
        }

        var parameters = memberInfo.ParameterTypes
            .Select((type, ordinal) => _factory.CreateVariable(
                "summary:parameter:" + ordinal.ToString(
                    CultureInfo.InvariantCulture),
                type))
            .ToImmutableArray();
        var result = _factory.CreateVariable(
            "summary:result",
            memberInfo.ReturnType);
        var environment =
            ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
        foreach (var binding in selected.Lowering.Variables)
        {
            if (binding.Symbol is ILocalSymbol)
            {
                continue;
            }

            if (binding.Symbol is not IParameterSymbol parameter ||
                !SymbolEqualityComparer.Default.Equals(
                    Normalize((IMethodSymbol)parameter.ContainingSymbol),
                    method) ||
                parameter.Ordinal < 0 ||
                parameter.Ordinal >= parameters.Length ||
                _factory.GetVariableInfo(binding.Variable).Type !=
                _factory.GetVariableInfo(parameters[parameter.Ordinal]).Type)
            {
                return false;
            }

            environment.Add(
                binding.Variable,
                _factory.Variable(parameters[parameter.Ordinal]));
        }

        var calls = ImmutableDictionary.CreateBuilder<
            IrInstructionId,
            IrRelationalSummary>();
        foreach (var binding in selected.Calls.OrderBy(
                     static item => item.Key.Id.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var invocation = binding.Value;
            if (!RoslynProgramLowerer.IsDirectInvocation(invocation) ||
                invocation.TargetMethod.Parameters.Any(
                    static parameter => parameter.RefKind != RefKind.None) ||
                !TryGet(
                    invocation.TargetMethod,
                    binding.Key.Member,
                    cancellationToken,
                    out var dependency))
            {
                return false;
            }

            calls.Add(binding.Key.Id, dependency!);
        }

        var signature = new IrSummarySignature(
            member,
            receiver: null,
            parameters,
            result,
            new IrSummaryProvenance(
                IrSummaryOrigin.Source,
                EvidenceSha256(declaration, cancellationToken),
                evidenceCallIdentity: method.GetDocumentationCommentId() ?? string.Empty));
        var built = IrRelationalSummaryBuilder.Build(
            selected.Lowering.Program,
            signature,
            environment.ToImmutable(),
            calls.ToImmutable());
        summary = built.Summary;
        return built.IsSuccess;
    }

    private bool IsSourceCandidate(IMethodSymbol method)
    {
        method = Normalize(method);
        return method.MethodKind == MethodKind.Ordinary &&
            method.IsStatic &&
            !method.IsAbstract &&
            !method.IsExtern &&
            method.TypeParameters.IsEmpty &&
            method.ContainingType.TypeParameters.IsEmpty &&
            SymbolEqualityComparer.Default.Equals(
                method.ContainingAssembly,
                _compilation.Assembly) &&
            method.Parameters.All(static parameter =>
                parameter.RefKind == RefKind.None &&
                IsScalar(parameter.Type)) &&
            IsScalar(method.ReturnType) &&
            method.DeclaringSyntaxReferences.Length == 1;
    }

    private static bool IsScalar(ITypeSymbol type)
    {
        return type.SpecialType == SpecialType.System_Boolean ||
            CSharpScalarSemantics.IsSupportedInteger(type.SpecialType);
    }

    private static IMethodSymbol Normalize(IMethodSymbol method)
    {
        method = method.ReducedFrom ?? method;
        method = method.PartialImplementationPart ?? method;
        return method.OriginalDefinition;
    }

    private static string EvidenceSha256(
        BaseMethodDeclarationSyntax declaration,
        CancellationToken cancellationToken)
    {
        var text = declaration.SyntaxTree.GetText(cancellationToken)
            .ToString(declaration.FullSpan);
        using var hash = SHA256.Create();
        var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(text));
        return string.Concat(bytes.Select(static value =>
            value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static CompilerSummaryEvidenceAuthority? CreateAuthority(
        IMethodSymbol method,
        IrRelationalSummary summary,
        CancellationToken cancellationToken)
    {
        var provenance = summary.Signature.Provenance;
        var callIdentity = provenance.EvidenceCallIdentity;
        if (string.IsNullOrEmpty(callIdentity) ||
            !string.Equals(
                callIdentity,
                method.GetDocumentationCommentId() ?? string.Empty,
                StringComparison.Ordinal))
        {
            return null;
        }

        if (provenance.Origin == IrSummaryOrigin.Source)
        {
            if (method.DeclaringSyntaxReferences.Length != 1 ||
                method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken)
                    is not BaseMethodDeclarationSyntax declaration)
            {
                return null;
            }

            var sourceText = declaration.SyntaxTree.GetText(cancellationToken);
            var sourcePath = declaration.SyntaxTree.FilePath;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return null;
            }

            return new CompilerSummaryEvidenceAuthority(
                CompilerSummaryOrigin.Source,
                callIdentity,
                provenance.EvidenceSha256,
                provenance.EvidenceIdentity,
                CompilerCaptureAuthority.NormalizePath(sourcePath),
                CompilerCompilationCapture.ComputeTextSha256(sourceText),
                declaration.FullSpan.Start,
                declaration.FullSpan.Length,
                string.Empty,
                -1);
        }

        if (provenance.Origin == IrSummaryOrigin.ImplementationIl)
        {
            var moduleName = method.ContainingModule?.Name;
            return string.IsNullOrEmpty(moduleName)
                ? null
                : new CompilerSummaryEvidenceAuthority(
                    CompilerSummaryOrigin.ImplementationIl,
                    callIdentity,
                    provenance.EvidenceSha256,
                    provenance.EvidenceIdentity,
                    string.Empty,
                    string.Empty,
                    -1,
                    -1,
                    moduleName!,
                    method.MetadataToken);
        }

        return provenance.Origin == IrSummaryOrigin.SpecificationPack
            ? new CompilerSummaryEvidenceAuthority(
                CompilerSummaryOrigin.SpecificationPack,
                callIdentity,
                provenance.EvidenceSha256,
                provenance.EvidenceIdentity,
                string.Empty,
                string.Empty,
                -1,
                -1,
                string.Empty,
                -1)
            : null;
    }
}
