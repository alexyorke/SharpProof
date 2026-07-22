namespace SharpProof.Symbolic;

internal enum EffectValueRootKind { Receiver, Argument, Captured, Static, Fresh, Ambient, Unknown }
internal enum EffectNullState { Unknown, Null, NonNull }
internal readonly record struct EffectValueRoot(EffectValueRootKind Kind, int Ordinal = -1, string Key = "");

internal sealed class EffectFlowValue {
    private static readonly ImmutableDictionary<string, EffectFlowValue> EmptyMembers =
        ImmutableDictionary.Create<string, EffectFlowValue>(StringComparer.Ordinal);
    internal static EffectFlowValue Unknown { get; } = FromRoot(new(EffectValueRootKind.Unknown));
    internal static EffectFlowValue None { get; } = new([], null, EmptyMembers, [], EffectNullState.Unknown);
    private EffectFlowValue(
        ImmutableHashSet<EffectValueRoot> roots,
        INamedTypeSymbol? exactType,
        ImmutableDictionary<string, EffectFlowValue> members,
        ImmutableArray<EffectBoundCallable> callables,
        EffectNullState nullState) {
        Roots = roots;
        ExactType = exactType;
        Members = members;
        Callables = callables;
        NullState = nullState;
        Key = CreateKey();
    }
    internal ImmutableHashSet<EffectValueRoot> Roots { get; }
    internal INamedTypeSymbol? ExactType { get; }
    internal ImmutableDictionary<string, EffectFlowValue> Members { get; }
    internal ImmutableArray<EffectBoundCallable> Callables { get; }
    internal EffectNullState NullState { get; }
    internal string Key { get; }
    internal bool IsDefinitelyNonNull => NullState == EffectNullState.NonNull;
    internal bool IsDefinitelyNull => NullState == EffectNullState.Null;
    internal static EffectFlowValue FromRoot(EffectValueRoot root, ITypeSymbol? type = null) => new(
        [root],
        Exact(type),
        EmptyMembers,
        [],
        root.Kind == EffectValueRootKind.Fresh ? EffectNullState.NonNull : EffectNullState.Unknown);
    internal static EffectFlowValue Fresh(ITypeSymbol? type) => FromRoot(new(EffectValueRootKind.Fresh), type);
    internal static EffectFlowValue KnownNonNull(ITypeSymbol? type) =>
        new([], Exact(type), EmptyMembers, [], EffectNullState.NonNull);
    internal static EffectFlowValue KnownNull { get; } =
        new([], null, EmptyMembers, [], EffectNullState.Null);
    internal static EffectFlowValue Callable(EffectBoundCallable callable, ITypeSymbol? type) => new(
        [new EffectValueRoot(EffectValueRootKind.Fresh)], Exact(type), EmptyMembers, [callable], EffectNullState.NonNull);
    internal EffectFlowValue WithMember(string member, EffectFlowValue value) => new(
        Roots, ExactType, Members.SetItem(member, value), Callables, NullState);
    internal EffectFlowValue WithCallables(ImmutableArray<EffectBoundCallable> callables) => new(
        Roots, ExactType, Members, callables, NullState);
    internal EffectFlowValue CombineDelegate(EffectFlowValue other) =>
        Merge(other).WithCallables(Callables.AddRange(other.Callables));
    internal EffectFlowValue RemoveDelegate(EffectFlowValue other) {
        if (other.Callables.IsDefaultOrEmpty || other.Callables.Length > Callables.Length) return this;
        var start = -1;
        for (var candidate = Callables.Length - other.Callables.Length; candidate >= 0; candidate--) {
            var matches = true;
            for (var index = 0; index < other.Callables.Length; index++)
                if (!string.Equals(Callables[candidate + index].Key, other.Callables[index].Key, StringComparison.Ordinal)) {
                    matches = false;
                    break;
                }
            if (!matches) continue;
            start = candidate;
            break;
        }
        if (start < 0) return this;
        var remaining = Callables.RemoveRange(start, other.Callables.Length);
        return new EffectFlowValue(Roots, ExactType, Members, remaining,
            remaining.IsDefaultOrEmpty ? EffectNullState.Null : EffectNullState.NonNull);
    }
    internal EffectFlowValue AsDefinitelyNonNull() => new(Roots, ExactType, Members, Callables, EffectNullState.NonNull);
    internal EffectFlowValue AsDefinitelyNull() => new(Roots, ExactType, Members, Callables, EffectNullState.Null);
    internal EffectFlowValue Member(string member) => Members.TryGetValue(member, out var value)
        ? value
        : new EffectFlowValue(Roots, null, EmptyMembers, [], EffectNullState.Unknown);
    internal EffectFlowValue Merge(EffectFlowValue other) {
        if (ReferenceEquals(this, other) || string.Equals(Key, other.Key, StringComparison.Ordinal)) return this;
        var roots = Roots.Union(other.Roots);
        var exact = ExactType != null && SymbolEqualityComparer.Default.Equals(ExactType, other.ExactType)
            ? ExactType
            : null;
        var members = EmptyMembers;
        foreach (var name in Members.Keys.Union(other.Members.Keys, StringComparer.Ordinal))
            members = members.Add(name, Member(name).Merge(other.Member(name)));
        var callables = Callables.AddRange(other.Callables)
            .GroupBy(static callable => callable.Key, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToImmutableArray();
        var nullState = NullState == other.NullState ? NullState : EffectNullState.Unknown;
        return new EffectFlowValue(roots, exact, members, callables, nullState);
    }
    internal EffectFlowValue Instantiate(
        EffectFlowValue? receiver,
        IReadOnlyList<EffectFlowValue> arguments,
        IReadOnlyDictionary<string, EffectFlowValue>? captures = null,
        IMethodSymbol? sourceMethod = null) {
        var result = None;
        foreach (var root in Roots) {
            var mapped = root.Kind switch {
                EffectValueRootKind.Receiver when IsFormalReceiver(root, sourceMethod) => receiver ?? Unknown,
                EffectValueRootKind.Argument when root.Ordinal >= 0 && root.Ordinal < arguments.Count &&
                                                   IsFormalArgument(root, sourceMethod) => arguments[root.Ordinal],
                EffectValueRootKind.Captured when captures != null && captures.TryGetValue(root.Key, out var captured) => captured,
                _ => FromRoot(root, ExactType)
            };
            result = ReferenceEquals(result, None) ? mapped : result.Merge(mapped);
        }
        if (Roots.Count == 0) result = new EffectFlowValue([], ExactType, EmptyMembers, [], NullState);
        else if (NullState == EffectNullState.NonNull) result = result.AsDefinitelyNonNull();
        else if (NullState == EffectNullState.Null) result = result.AsDefinitelyNull();
        foreach (var member in Members)
            result = result.WithMember(member.Key, member.Value.Instantiate(receiver, arguments, captures, sourceMethod));
        if (!Callables.IsDefaultOrEmpty)
            result = result.WithCallables([.. Callables.Select(callable => callable.Instantiate(
                receiver, arguments, captures, sourceMethod))]);
        return result;
    }
    private static bool IsFormalReceiver(EffectValueRoot root, IMethodSymbol? sourceMethod) => sourceMethod == null ||
        string.IsNullOrEmpty(root.Key) ||
        string.Equals(root.Key, EffectFlowState.SymbolKey(sourceMethod), StringComparison.Ordinal);
    private static bool IsFormalArgument(EffectValueRoot root, IMethodSymbol? sourceMethod) => sourceMethod == null ||
        string.IsNullOrEmpty(root.Key) || root.Ordinal < sourceMethod.Parameters.Length &&
        string.Equals(root.Key, EffectFlowState.SymbolKey(sourceMethod.Parameters[root.Ordinal]), StringComparison.Ordinal);
    private string CreateKey() {
        var roots = string.Join(",", Roots.OrderBy(static root => root.Kind).ThenBy(static root => root.Ordinal)
            .ThenBy(static root => root.Key, StringComparer.Ordinal));
        var members = string.Join(",", Members.OrderBy(static member => member.Key, StringComparer.Ordinal)
            .Select(static member => member.Key + "=" + member.Value.Key));
        var callables = string.Join(",", Callables.Select(static callable => callable.Key).OrderBy(static key => key, StringComparer.Ordinal));
        return roots + "|" + ExactType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" + NullState + "|" +
               members + "|" + callables;
    }
    private static INamedTypeSymbol? Exact(ITypeSymbol? type) => type is INamedTypeSymbol {
        TypeKind: not (TypeKind.Interface or TypeKind.Dynamic), IsAbstract: false
    } named ? named : null;
}

internal sealed record EffectBoundCallable(
    IMethodSymbol Method,
    EffectFlowValue? Receiver,
    ImmutableDictionary<string, EffectFlowValue> Captures) {
    internal string Key => RoslynStructuralMethodIdentity.GetCanonicalKey(Method) + "@" + SourceKey(Method) + "|" + Receiver?.Key + "|" +
                           string.Join(",", Captures.OrderBy(static capture => capture.Key, StringComparer.Ordinal)
                               .Select(static capture => capture.Key + "=" + capture.Value.Key));
    private static string SourceKey(IMethodSymbol method) {
        var location = method.Locations.FirstOrDefault(static candidate => candidate.IsInSource);
        return location == null
            ? string.Empty
            : (location.SourceTree?.FilePath ?? string.Empty) + ":" + location.SourceSpan.Start;
    }
    internal EffectBoundCallable Instantiate(
        EffectFlowValue? receiver,
        IReadOnlyList<EffectFlowValue> arguments,
        IReadOnlyDictionary<string, EffectFlowValue>? captures,
        IMethodSymbol? sourceMethod = null) => new(
        Method,
        Receiver?.Instantiate(receiver, arguments, captures, sourceMethod),
        Captures.ToImmutableDictionary(
            static capture => capture.Key,
            capture => capture.Value.Instantiate(receiver, arguments, captures, sourceMethod),
            StringComparer.Ordinal));
}

internal sealed record EffectFlowState(
    EffectFlowValue Receiver,
    ImmutableArray<EffectFlowValue> Parameters,
    ImmutableDictionary<ILocalSymbol, EffectFlowValue> Locals,
    ImmutableDictionary<CaptureId, EffectFlowValue> FlowCaptures,
    ImmutableDictionary<ILocalSymbol, EffectFlowValue> RefLocals,
    bool IsUnreachable = false) {
    internal static EffectFlowState Create(IMethodSymbol method) => new(
        method.IsStatic ? EffectFlowValue.None : EffectFlowValue.FromRoot(
            new(EffectValueRootKind.Receiver, Key: SymbolKey(method)), KnownInputType(method.ContainingType)),
        [.. method.Parameters.Select(parameter => EffectFlowValue.FromRoot(
            new(EffectValueRootKind.Argument, parameter.Ordinal, Key: SymbolKey(parameter)), KnownInputType(parameter.Type)))],
        ImmutableDictionary.Create<ILocalSymbol, EffectFlowValue>(SymbolEqualityComparer.Default),
        ImmutableDictionary<CaptureId, EffectFlowValue>.Empty,
        ImmutableDictionary.Create<ILocalSymbol, EffectFlowValue>(SymbolEqualityComparer.Default));
    internal EffectFlowValue GetParameter(IParameterSymbol parameter) => parameter.Ordinal >= 0 && parameter.Ordinal < Parameters.Length
        ? Parameters[parameter.Ordinal]
        : EffectFlowValue.FromRoot(new(EffectValueRootKind.Captured, Key: SymbolKey(parameter)), parameter.Type);
    internal EffectFlowState SetParameter(IParameterSymbol parameter, EffectFlowValue value) =>
        parameter.Ordinal >= 0 && parameter.Ordinal < Parameters.Length
            ? this with { Parameters = Parameters.SetItem(parameter.Ordinal, value) }
            : this;
    internal EffectFlowState Merge(EffectFlowState other) {
        if (IsUnreachable) return other;
        if (other.IsUnreachable) return this;
        var locals = Locals;
        foreach (var local in Locals.Keys.Union(other.Locals.Keys, SymbolEqualityComparer.Default).OfType<ILocalSymbol>())
            locals = locals.SetItem(local, GetLocal(local).Merge(other.GetLocal(local)));
        var captures = FlowCaptures;
        foreach (var id in FlowCaptures.Keys.Union(other.FlowCaptures.Keys))
            captures = captures.SetItem(id, GetCapture(id).Merge(other.GetCapture(id)));
        var refs = RefLocals;
        foreach (var local in RefLocals.Keys.Union(other.RefLocals.Keys, SymbolEqualityComparer.Default).OfType<ILocalSymbol>())
            refs = refs.SetItem(local, GetRef(local).Merge(other.GetRef(local)));
        var count = Math.Min(Parameters.Length, other.Parameters.Length);
        var parameters = Parameters;
        for (var index = 0; index < count; index++) parameters = parameters.SetItem(index, parameters[index].Merge(other.Parameters[index]));
        return new(Receiver.Merge(other.Receiver), parameters, locals, captures, refs);
    }
    internal EffectFlowValue GetLocal(ILocalSymbol local) => Locals.TryGetValue(local, out var value) ? value : EffectFlowValue.Unknown;
    internal EffectFlowValue GetCapture(CaptureId id) => FlowCaptures.TryGetValue(id, out var value) ? value : EffectFlowValue.Unknown;
    internal EffectFlowValue GetRef(ILocalSymbol local) => RefLocals.TryGetValue(local, out var value) ? value : GetLocal(local);
    internal string Key => IsUnreachable + ";" + Receiver.Key + ";" + string.Join(";", Parameters.Select(static value => value.Key)) + ";" +
                           string.Join(";", Locals.OrderBy(static pair => SymbolKey(pair.Key), StringComparer.Ordinal)
                               .Select(static pair => SymbolKey(pair.Key) + "=" + pair.Value.Key)) + ";" +
                           string.Join(";", FlowCaptures.OrderBy(static pair => pair.Key.GetHashCode())
                               .Select(static pair => pair.Key + "=" + pair.Value.Key)) + ";" +
                           string.Join(";", RefLocals.OrderBy(static pair => SymbolKey(pair.Key), StringComparer.Ordinal)
                               .Select(static pair => SymbolKey(pair.Key) + "=" + pair.Value.Key));
    internal static string SymbolKey(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "@" +
                                                        symbol.Locations.FirstOrDefault()?.SourceSpan.Start;
    private static ITypeSymbol? KnownInputType(ITypeSymbol type) => type.IsValueType || type is INamedTypeSymbol { IsSealed: true }
        ? type
        : null;
}
