// Contract exports run only in the build-time compiler collector.
using System.Security.Cryptography;
using System.Text;

namespace SharpProof.CompilerCollector;

// The producer side of cross-project Requires: the public and internal
// callables of this compilation whose source Requires read only parameters.
internal static class CompilerContractsExportProducer
{
    internal static CompilerContractsExport Create(CSharpCompilation compilation, string targetFramework,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        targetFramework = RepositoryParameterGuard.NotNull(targetFramework, nameof(targetFramework));
        var session = new CompilerSemanticSession(compilation, cancellationToken);
        var trees = compilation.SyntaxTrees.ToImmutableDictionary(static tree => tree, static tree => tree.FilePath);
        // A clause span names its document by path; a path two trees share
        // cannot be projected, and its clauses are not exported.
        var documents = trees.GroupBy(static pair => pair.Value, StringComparer.Ordinal)
            .Where(static group => group.Count() == 1)
            .ToImmutableDictionary(static group => group.Key, static group => group.Single().Key, StringComparer.Ordinal);
        var callables = ImmutableArray.CreateBuilder<CompilerExportedCallable>();
        foreach (var method in Callables(compilation.Assembly.GlobalNamespace, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Export(compilation, session, trees, documents, method, cancellationToken) is { } callable)
            { callables.Add(callable); }
        }
        var identity = compilation.Assembly.Identity;
        return new CompilerContractsExport
        {
            Schema = CompilerContractsExportVersions.Schema,
            Version = CompilerContractsExportVersions.Current,
            AssemblyName = identity.Name,
            AssemblyIdentity = identity.ToString(),
            PublicKeyToken = PublicKeyToken(identity),
            TargetFramework = targetFramework,
            Callables = [.. callables.OrderBy(static callable => callable.DocumentationId, StringComparer.Ordinal)]
        };
    }

    internal static string PublicKeyToken(AssemblyIdentity identity)
    {
        identity = RepositoryParameterGuard.NotNull(identity, nameof(identity));
        return string.Concat(identity.PublicKeyToken.Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    // Methods, constructors and accessors another assembly can name.
    private static IEnumerable<IMethodSymbol> Callables(INamespaceSymbol root, CancellationToken cancellationToken)
    {
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = pending.Pop();
            foreach (var member in owner.GetMembers())
            {
                if (member is INamespaceSymbol nested)
                { pending.Push(nested); }
                else if (member is INamedTypeSymbol type && Visible(type))
                { pending.Push(type); }
                else if (member is IMethodSymbol method && !method.IsImplicitlyDeclared && !method.DeclaringSyntaxReferences.IsEmpty &&
                    Visible(method) && method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.PropertyGet or
                        MethodKind.PropertySet)
                { yield return method; }
            }
        }

        static bool Visible(ISymbol symbol)
        {
            return symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;
        }
    }

    private static CompilerExportedCallable? Export(CSharpCompilation compilation, CompilerSemanticSession session,
        ImmutableDictionary<SyntaxTree, string> trees, ImmutableDictionary<string, SyntaxTree> documents,
        IMethodSymbol method, CancellationToken cancellationToken)
    {
        var resolution = session.ResolveContractSource(method);
        var requires = resolution.Inventory.Clauses.Where(static clause => clause.Kind == BoundContractKind.Requires).ToImmutableArray();
        if (requires.IsEmpty || method.GetDocumentationCommentId() is not { Length: > 0 } documentationId)
        { return null; }
        var digest = DeclarationDigest(method, requires, cancellationToken);
        var incomplete = new CompilerExportedCallable { DocumentationId = documentationId, DeclarationSha256 = digest, Complete = false };
        var context = new TotalLoweringContext(new IrFactory(), method, tree => trees[tree]);
        context.ModelReceiver();
        var binding = new TotalContractBinder(compilation, context.Factory).BindTotalSourceRequires(context, cancellationToken);
        if (!binding.IsSuccess || binding.Clauses.Length != requires.Length ||
            binding.Clauses.Any(static clause => clause.Kind != BoundContractKind.Requires))
        { return incomplete; }
        var locations = ImmutableArray.CreateBuilder<WorkerSourceLocation>(binding.Clauses.Length);
        foreach (var clause in binding.Clauses)
        {
            if (context.Factory.GetOperationInfo(clause.SourceOperation).SourceSpan is not { Length: > 0 } span ||
                !documents.TryGetValue(span.Document, out var tree))
            { return incomplete; }
            locations.Add(CompilerSourceLocationProjection.Create(Location.Create(tree,
                new Microsoft.CodeAnalysis.Text.TextSpan(span.Start, span.Length))));
        }
        // The receiver is not exported: a clause that reads it, or reads any
        // state but the parameters, fails the export codec's own validation.
        try
        {
            var artifact = CompilerTotalCallableArtifactCodec.EncodeExport(context.Factory,
                [.. context.Parameters.Select(static parameter => new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState))],
                [.. binding.Clauses.Select(static clause => new CompilerTotalClause(CompilerContractKind.Requires, clause.Value, clause.SafeCondition,
                    clause.SourceOperation, null, null))], cancellationToken);
            _ = CompilerTotalCallableArtifactCodec.DecodeExport(artifact, cancellationToken);
            return incomplete with { Complete = true, Requires = artifact, ClauseLocations = locations.MoveToImmutable() };
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException or
            KeyNotFoundException)
        { return incomplete; }
    }

    private static string DeclarationDigest(IMethodSymbol method, ImmutableArray<ContractClauseOccurrence> requires,
        CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.RequireInitialized(requires.IsDefault, nameof(requires), "Array must be initialized.");
        var text = new StringBuilder();
        text.Append(method.GetDocumentationCommentId()).Append('\n');
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax(cancellationToken);
            text.Append(syntax is BaseMethodDeclarationSyntax declaration
                ? declaration.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default).ToString()
                : syntax is AccessorDeclarationSyntax accessor ? accessor.Keyword.ValueText : string.Empty).Append('\n');
        }
        foreach (var clause in requires)
        { text.Append(clause.Invocation.Syntax.ToString()).Append('\n'); }
        return HashEncoding.ComputeSha256Hex(Encoding.UTF8.GetBytes(text.ToString()).ToImmutableArray());
    }
}

// The consumer side: the stamped exports of referenced projects that this
// compilation can trust, and the clauses they carry for a callee.
internal sealed class CompilerContractExports
{
    internal static CompilerContractExports Empty { get; } = new([], [], ImmutableDictionary<string, ImmutableHashSet<string>>.Empty);

    private readonly ImmutableArray<Accepted> _accepted;
    private readonly ImmutableDictionary<string, Accepted> _byAssembly;
    // Callees of readable exports that were not accepted (stale, forged or
    // ambiguous). They are never checked; their calls are counted unchecked.
    private readonly ImmutableDictionary<string, ImmutableHashSet<string>> _rejected;
    private readonly Dictionary<(string Assembly, string DocumentationId), CompilerTotalCallableArtifactCodec.DecodedExport?> _decoded = [];

    private sealed record Accepted(CompilerContractExportSnapshot Snapshot, CompilerReferenceModuleSnapshot Image,
        ImmutableDictionary<string, CompilerExportedCallable> Callables)
    {
        public CompilerContractExportSnapshot Snapshot { get; init; } = Snapshot ?? throw new ArgumentNullException(nameof(Snapshot));
        public CompilerReferenceModuleSnapshot Image { get; init; } = Image ?? throw new ArgumentNullException(nameof(Image));
        public ImmutableDictionary<string, CompilerExportedCallable> Callables { get; init; } = Callables ?? throw new ArgumentNullException(nameof(Callables));
    }

    private CompilerContractExports(ImmutableArray<Accepted> accepted, ImmutableArray<CompilerContractExportSnapshot> snapshots,
        ImmutableDictionary<string, ImmutableHashSet<string>> rejected)
    {
        RepositoryParameterGuard.RequireInitialized(accepted.IsDefault, nameof(accepted), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(snapshots.IsDefault, nameof(snapshots), "Array must be initialized.");
        _rejected = RepositoryParameterGuard.NotNull(rejected, nameof(rejected));
        _accepted = accepted;
        Snapshots = snapshots;
        _byAssembly = accepted.ToImmutableDictionary(static item => item.Snapshot.AssemblyIdentity, StringComparer.Ordinal);
    }

    // The accepted exports, as the manifest records them for the worker.
    internal ImmutableArray<CompilerContractExportSnapshot> Snapshots { get; }

    // An export is accepted only when its stamp names the exact image this
    // compilation resolved for that assembly. A stale, forged or ambiguous
    // export is never checked: its callees' calls are counted unchecked. An
    // export that cannot be read, or names no referenced assembly, is ignored.
    internal static CompilerContractExports Load(CSharpCompilation compilation, ImmutableArray<AdditionalText> files,
        string projectDirectory, CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        projectDirectory = RepositoryParameterGuard.NotNull(projectDirectory, nameof(projectDirectory));
        if (files.IsDefaultOrEmpty)
        { return Empty; }
        var references = CompilerCompilationCapture.CaptureReferences(compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, cancellationToken);
        var assemblies = compilation.SourceModule.ReferencedAssemblySymbols
            .GroupBy(static assembly => assembly.Identity.ToString(), StringComparer.Ordinal)
            .ToImmutableDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var candidates = ImmutableArray.CreateBuilder<Accepted>();
        var rejected = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<string>>(StringComparer.Ordinal);
        void Reject(string assembly, IEnumerable<string> callables)
        {
            rejected[assembly] = (rejected.TryGetValue(assembly, out var known) ? known : ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal))
                .Union(callables);
        }
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = CompilerCompilationCapture.GetStableAdditionalText(file, cancellationToken).ToString();
            if (CompilerContractsExportJson.TryDeserializeStamped(text) is not { } stamped ||
                !assemblies.TryGetValue(stamped.Export.AssemblyIdentity, out var assembly) ||
                assembly.Identity.Name != stamped.Export.AssemblyName ||
                CompilerContractsExportProducer.PublicKeyToken(assembly.Identity) != stamped.Export.PublicKeyToken ||
                assembly.Modules.FirstOrDefault() is not { } module)
            { continue; }
            var image = references.Where(reference => reference.Identity == stamped.Export.AssemblyIdentity)
                .SelectMany(static reference => reference.Modules)
                .FirstOrDefault(snapshot => snapshot.Name == module.Name &&
                    stamped.Images.Any(stamp => stamp.Sha256 == snapshot.Sha256 && stamp.Mvid == snapshot.Mvid));
            if (image == null)
            {
                Reject(stamped.Export.AssemblyIdentity, stamped.Export.Callables.Select(static callable => callable.DocumentationId));
                continue;
            }
            var path = Path.IsPathRooted(file.Path) ? file.Path : Path.Combine(projectDirectory, file.Path);
            candidates.Add(new(new CompilerContractExportSnapshot
            {
                Path = CompilerCaptureIdentity.NormalizePath(path),
                Sha256 = CompilerCompilationCapture.ComputeTextSha256(Microsoft.CodeAnalysis.Text.SourceText.From(text)),
                AssemblyIdentity = stamped.Export.AssemblyIdentity,
                Images = stamped.Images
            }, image, stamped.Export.Callables.ToImmutableDictionary(static callable => callable.DocumentationId,
                StringComparer.Ordinal)));
        }
        // Two exports for one assembly are ambiguous: neither is trusted.
        foreach (var group in candidates.GroupBy(static item => item.Snapshot.AssemblyIdentity, StringComparer.Ordinal).Where(static group => group.Count() > 1))
        { Reject(group.Key, group.SelectMany(static item => item.Callables.Keys)); }
        ImmutableArray<Accepted> accepted = [.. candidates.GroupBy(static item => item.Snapshot.AssemblyIdentity, StringComparer.Ordinal)
            .Where(group => group.Count() == 1 && !rejected.ContainsKey(group.Key)).Select(static group => group.Single())
            .OrderBy(static item => item.Snapshot.AssemblyIdentity, StringComparer.Ordinal)];
        return accepted.IsEmpty && rejected.Count == 0 ? Empty
            : new(accepted, [.. accepted.Select(static item => item.Snapshot)], rejected.ToImmutable());
    }

    private bool TryFind(IMethodSymbol method, out Accepted accepted, out CompilerExportedCallable callable)
    {
        accepted = null!;
        callable = null!;
        return !_accepted.IsEmpty && method.DeclaringSyntaxReferences.IsEmpty &&
            _byAssembly.TryGetValue(method.ContainingAssembly.Identity.ToString(), out accepted!) &&
            method.GetDocumentationCommentId() is { } id && accepted.Callables.TryGetValue(id, out callable!);
    }

    // A callee another project exported Requires for, checkable or not. A
    // call to it is a precondition call site, so one that does not attach a
    // claim is counted as unchecked.
    internal bool Covers(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        method = CompilerSemanticSession.NormalizeMethod(method);
        return TryFind(method, out _, out _) || IsRejected(method);
    }

    private bool IsRejected(IMethodSymbol method)
    {
        return _rejected.Count != 0 && method.DeclaringSyntaxReferences.IsEmpty &&
            _rejected.TryGetValue(method.ContainingAssembly.Identity.ToString(), out var callables) &&
            method.GetDocumentationCommentId() is { } id && callables.Contains(id);
    }

    // The exported callees, for the census of call names.
    internal IEnumerable<IMethodSymbol> Callees(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            var identity = assembly.Identity.ToString();
            if (!_byAssembly.ContainsKey(identity) && !_rejected.ContainsKey(identity))
            { continue; }
            var pending = new Stack<INamespaceOrTypeSymbol>();
            pending.Push(assembly.GlobalNamespace);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var member in pending.Pop().GetMembers())
                {
                    if (member is INamespaceOrTypeSymbol nested)
                    { pending.Push(nested); }
                    else if (member is IMethodSymbol method && Covers(method))
                    { yield return method; }
                }
            }
        }
    }

    // The exported clauses of a callee, rebuilt in the caller's frame over its
    // parameter entries. False when the callee is exported but any of its
    // clauses cannot be imported: then none is checked at its calls.
    internal bool TryPrepare(TotalLoweringContext frame, CancellationToken cancellationToken,
        out ImmutableArray<TotalExportedPrecondition> clauses)
    {
        frame = RepositoryParameterGuard.NotNull(frame, nameof(frame));
        clauses = [];
        // A callee of a rejected export has clauses that cannot be checked, so
        // none of its call's clauses is: the call stays unchecked and counted.
        if (!TryFind(frame.Target, out var accepted, out var callable))
        { return !IsRejected(frame.Target); }
        if (!callable.Complete || callable.Requires is not { } requires)
        { return false; }
        var key = (accepted.Snapshot.AssemblyIdentity, callable.DocumentationId);
        if (!_decoded.TryGetValue(key, out var decoded))
        {
            try
            { decoded = CompilerTotalCallableArtifactCodec.DecodeExport(requires, cancellationToken); }
            catch (InvalidDataException)
            { decoded = null; }
            _decoded.Add(key, decoded);
        }
        if (decoded == null || decoded.Parameters.Length != frame.Parameters.Length ||
            decoded.Clauses.Length != callable.ClauseLocations.Length)
        { return false; }
        var importer = new ExportedClauseImporter(decoded.Factory, frame.Factory);
        for (var ordinal = 0; ordinal < decoded.Parameters.Length; ordinal++)
        {
            if (!importer.MapParameter(decoded.Parameters[ordinal].Entry, frame.Parameters[ordinal].Entry))
            { return false; }
        }
        var result = ImmutableArray.CreateBuilder<TotalExportedPrecondition>(decoded.Clauses.Length);
        for (var ordinal = 0; ordinal < decoded.Clauses.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clause = decoded.Clauses[ordinal];
            var origin = new CompilerExportedClauseOrigin
            {
                ExportSha256 = accepted.Snapshot.Sha256,
                AssemblyIdentity = accepted.Snapshot.AssemblyIdentity,
                ImageSha256 = accepted.Image.Sha256,
                ModuleMvid = accepted.Image.Mvid,
                DocumentationId = callable.DocumentationId,
                DeclarationSha256 = callable.DeclarationSha256,
                ClauseOrdinal = ordinal,
                Location = callable.ClauseLocations[ordinal]
            };
            if (!importer.TryImport(clause.Value, out var value) || !importer.TryImport(clause.Safe, out var safe) ||
                value.Type != frame.Factory.BooleanType || safe.Type != frame.Factory.BooleanType ||
                !CompilerContractsExportJson.TryGetOriginDigest(origin, out var digest))
            { return false; }
            result.Add(new(value, safe, frame.Factory.CreateOperation("exported Requires"), origin, digest));
        }
        clauses = result.MoveToImmutable();
        return true;
    }

    // Rebuilds a decoded export term in another factory. Only parameter
    // entries, literals and the operators the export codec admits cross over;
    // a type is the parameter type it came from or a primitive one.
    private sealed class ExportedClauseImporter(IrFactory sourceFactory, IrFactory targetFactory)
    {
        private readonly IrFactory source = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
        private readonly IrFactory target = targetFactory ?? throw new ArgumentNullException(nameof(targetFactory));
        private readonly Dictionary<IrVarId, IrVarId> _variables = [];
        private readonly Dictionary<IrTypeId, IrTypeId> _types = [];
        private readonly Dictionary<IrId, IrTerm> _terms = [];

        internal bool MapParameter(IrVarId from, IrVarId to)
        {
            if (_variables.ContainsKey(from) || !MapType(source.GetVariableInfo(from).Type, target.GetVariableInfo(to).Type))
            { return false; }
            _variables.Add(from, to);
            return true;
        }

        private bool MapType(IrTypeId from, IrTypeId to)
        {
            if (_types.TryGetValue(from, out var mapped))
            { return mapped == to; }
            var left = source.GetTypeInfo(from);
            var right = target.GetTypeInfo(to);
            if (left.Kind != right.Kind || left.Width != right.Width || left.Signed != right.Signed ||
                (from == source.StringType) != (to == target.StringType) || (from == source.ObjectType) != (to == target.ObjectType) ||
                left.ElementType.HasValue != right.ElementType.HasValue ||
                left.ElementType is { } element && !MapType(element, right.ElementType!.Value))
            { return false; }
            _types.Add(from, to);
            return true;
        }

        private IrTypeId? Type(IrTypeId from)
        {
            if (_types.TryGetValue(from, out var mapped))
            { return mapped; }
            var info = source.GetTypeInfo(from);
            IrTypeId? result = from == source.BooleanType ? target.BooleanType
                : from == source.StringType ? target.StringType
                : from == source.ObjectType ? target.ObjectType
                : info.Kind == IrTypeKind.Integer ? target.GetOrCreateIntegerType(info.Width, info.Signed)
                : null;
            if (result is { } type)
            { _types.Add(from, type); }
            return result;
        }

        internal bool TryImport(IrTerm term, out IrTerm result)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            try
            {
                result = Import(term, 0)!;
                return result != null;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                result = null!;
                return false;
            }
        }

        private IrTerm? Import(IrTerm term, int depth)
        {
            if (depth > PortableIrGraphCodec.MaximumGraphDepth)
            { return null; }
            if (_terms.TryGetValue(term.Id, out var known))
            { return known; }
            if (Type(term.Type) is not { } type)
            { return null; }
            IrTerm? result = term switch
            {
                IrBooleanTerm boolean => target.Boolean(boolean.Value),
                IrIntegerTerm integer => target.IntegerBits(type, integer.Bits),
                IrStringTerm text => target.String(source.GetString(text.Value)),
                IrNullTerm => target.Null(type),
                IrEmptyArrayTerm => target.EmptyArray(type),
                IrVariableTerm variable => _variables.TryGetValue(variable.Variable, out var mapped) ? target.Variable(mapped) : null,
                IrUnaryTerm unary => Import(unary.Operand, depth + 1) is { } operand ? target.Unary(unary.Operator, operand) : null,
                IrBinaryTerm binary => Import(binary.Left, depth + 1) is { } left && Import(binary.Right, depth + 1) is { } right
                    ? target.Binary(binary.Operator, left, right) : null,
                IrConditionalTerm conditional => Import(conditional.Condition, depth + 1) is { } condition &&
                    Import(conditional.WhenTrue, depth + 1) is { } whenTrue && Import(conditional.WhenFalse, depth + 1) is { } whenFalse
                    ? target.Conditional(condition, whenTrue, whenFalse) : null,
                IrCastTerm cast => Import(cast.Operand, depth + 1) is { } operand ? target.Cast(type, operand) : null,
                IrLengthTerm length => Import(length.Value, depth + 1) is { } value ? target.Length(value) : null,
                IrSequenceAccessTerm access => Import(access.Sequence, depth + 1) is { } sequence && Import(access.Index, depth + 1) is { } index
                    ? target.SequenceAccess(sequence, index) : null,
                _ => null
            };
            if (result == null || result.Type != type)
            { return null; }
            _terms.Add(term.Id, result);
            return result;
        }
    }
}
