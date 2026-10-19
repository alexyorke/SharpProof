using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private void Pattern(PatternSyntax syntax, HmType input)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            if (input is null)
            { throw new ArgumentNullException(nameof(input)); }
            if (syntax is ParenthesizedPatternSyntax parenthesized)
            { Pattern(parenthesized.Pattern, input); return; }
            if (model.GetOperation(syntax, cancellationToken) is not IPatternOperation operation)
            { throw new InferenceException("Unresolved pattern."); }
            BindPattern(operation, input);
        }

        private void BindPattern(IPatternOperation pattern, HmType input)
        {
            if (pattern is null)
            { throw new ArgumentNullException(nameof(pattern)); }
            if (input is null)
            { throw new ArgumentNullException(nameof(input)); }
            cancellationToken.ThrowIfCancellationRequested();
            var narrowed = pattern is IRecursivePatternOperation { Syntax: RecursivePatternSyntax { Type: not null } } typed
                ? CSharpTypes.Rigid(typed.MatchedType) : PatternNarrowing(pattern.InputType, pattern.NarrowedType, input);
            switch (pattern)
            {
                case IDeclarationPatternOperation declaration:
                    BindPatternSymbol(declaration.DeclaredSymbol, declaration.Syntax is VarPatternSyntax ? input : CSharpTypes.Rigid(declaration.MatchedType!));
                    break;
                case IDiscardPatternOperation or ITypePatternOperation:
                    break;
                case IConstantPatternOperation or IRelationalPatternOperation:
                    // Numeric promotions, enum constants and null tests retain their domain.
                    UnifyDeconstructionReceiver(CSharpTypes.Rigid(pattern.InputType), input);
                    break;
                case INegatedPatternOperation negated:
                    BindPattern(negated.Pattern, input);
                    break;
                case IBinaryPatternOperation binary:
                    BindPattern(binary.LeftPattern, input);
                    BindPattern(binary.RightPattern, PatternNarrowing(pattern.InputType, binary.RightPattern.InputType, input));
                    break;
                case IRecursivePatternOperation recursive:
                    if (recursive.DeconstructSymbol is INamedTypeSymbol runtimeTuple && runtimeTuple.ToDisplayString() == "System.Runtime.CompilerServices.ITuple")
                    { UnifyDeconstructionReceiver(CSharpTypes.Rigid(pattern.InputType), input); }
                    BindPatternSymbol(recursive.DeclaredSymbol, narrowed);
                    var elements = PatternDeconstruction(recursive, narrowed);
                    for (var i = 0; i < recursive.DeconstructionSubpatterns.Length; i++)
                    { BindPattern(recursive.DeconstructionSubpatterns[i], elements[i]); }
                    foreach (var property in recursive.PropertySubpatterns)
                    { BindPattern(property.Pattern, PatternMemberValue(property.Member, narrowed, recursive.NarrowedType)); }
                    break;
                case IListPatternOperation list:
                    if (list.InputType.SpecialType == SpecialType.System_String)
                    { UnifyDeconstructionReceiver(new HmType("string"), input); }
                    BindPatternSymbol(list.DeclaredSymbol, narrowed);
                    HmType? element = null;
                    foreach (var child in list.Patterns)
                    {
                        if (child is ISlicePatternOperation)
                        { BindPattern(child, narrowed); continue; }
                        element ??= list.InputType is IArrayTypeSymbol && narrowed.Name.StartsWith("[", StringComparison.Ordinal)
                            ? narrowed.Arguments[0] : list.InputType.SpecialType == SpecialType.System_String ? new HmType("char")
                            : PatternSymbolType(list.IndexerSymbol, narrowed, list.NarrowedType);
                        BindPattern(child, element);
                    }
                    break;
                case ISlicePatternOperation slice:
                    if (slice.Pattern is not null)
                    {
                        var sliced = slice.SliceSymbol is null ? input : PatternSymbolType(slice.SliceSymbol, input, slice.InputType);
                        BindPattern(slice.Pattern, sliced);
                    }
                    break;
                default:
                    throw new InferenceException("Unsupported pattern operation: " + pattern.Kind);
            }
        }

        private static HmType PatternNarrowing(ITypeSymbol original, ITypeSymbol narrowed, HmType input)
        {
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (narrowed is null)
            { throw new ArgumentNullException(nameof(narrowed)); }
            if (input is null)
            { throw new ArgumentNullException(nameof(input)); }
            if (SymbolEqualityComparer.Default.Equals(original, narrowed))
            { return ConversionCore(input); }
            if (original is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } optional
                && SymbolEqualityComparer.Default.Equals(optional.TypeArguments[0], narrowed) && input.Root.Name == CSharpTypes.NullableValue)
            { return input.Root.Arguments[0]; }
            return CSharpTypes.Rigid(narrowed);
        }

        private void BindPatternSymbol(ISymbol? symbol, HmType type)
        {
            if (type is null)
            { throw new ArgumentNullException(nameof(type)); }
            if (symbol is null)
            { return; }
            if (environment.TryGetValue(symbol, out var previous))
            { HmType.Unify(previous, type); }
            else
            { environment.Add(symbol, type); }
        }

        private static Dictionary<ITypeParameterSymbol, HmType> PatternOwnerMapping(INamedTypeSymbol owner, HmType input, ITypeSymbol original)
        {
            if (owner is null)
            { throw new ArgumentNullException(nameof(owner)); }
            if (input is null)
            { throw new ArgumentNullException(nameof(input)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            var projected = ProjectDeconstructionReceiver(original, input, owner);
            if (projected is null)
            { UnifyDeconstructionReceiver(CSharpTypes.Rigid(original), input); AddNamedArguments(owner, mapping); return mapping; }
            projected = ConversionCore(projected);
            for (var i = 0; i < owner.TypeArguments.Length; i++)
            { mapping[owner.OriginalDefinition.TypeParameters[i]] = projected.Arguments[i]; }
            return mapping;
        }

        private static HmType PatternSymbolType(ISymbol? symbol, HmType input, ITypeSymbol original)
        {
            if (input is null)
            { throw new ArgumentNullException(nameof(input)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (symbol is null)
            { throw new InferenceException("Unresolved pattern member."); }
            var mapping = PatternOwnerMapping(symbol.ContainingType, input, original);
            return symbol.OriginalDefinition switch
            {
                IPropertySymbol property => CSharpTypes.Rigid(property.Type, mapping),
                IFieldSymbol field => CSharpTypes.Rigid(field.Type, mapping),
                IMethodSymbol method => CSharpTypes.Rigid(method.ReturnType, mapping),
                _ => throw new InferenceException("Unsupported pattern member.")
            };
        }

        private static HmType PatternMemberValue(IOperation operation, HmType input, ITypeSymbol original)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            if (input is null)
            { throw new ArgumentNullException(nameof(input)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (operation is IInstanceReferenceOperation)
            { return input; }
            if (operation is not IMemberReferenceOperation member || member.Instance is null)
            { throw new InferenceException("Unsupported property subpattern."); }
            var receiver = PatternMemberValue(member.Instance, input, original);
            return PatternSymbolType(member.Member, receiver, member.Instance.Type ?? original);
        }

        private HmType[] PatternDeconstruction(IRecursivePatternOperation pattern, HmType input)
        {
            if (pattern is null)
            { throw new ArgumentNullException(nameof(pattern)); }
            if (input is null)
            { throw new ArgumentNullException(nameof(input)); }
            if (pattern.DeconstructionSubpatterns.Length == 0)
            { return []; }
            if (pattern.MatchedType is INamedTypeSymbol { IsTupleType: true } && input.Root.Name == "Tuple")
            { return input.Root.Arguments.ToArray(); }
            if (pattern.DeconstructSymbol is INamedTypeSymbol runtimeTuple && runtimeTuple.ToDisplayString() == "System.Runtime.CompilerServices.ITuple")
            {
                return pattern.DeconstructionSubpatterns.Select(child =>
                { if (child is null) { throw new ArgumentNullException(nameof(child)); } return CSharpTypes.Rigid(child.InputType); }).ToArray();
            }
            if (pattern.DeconstructSymbol is not IMethodSymbol method)
            { throw new InferenceException("Unresolved positional pattern deconstructor."); }
            var mapping = method.IsStatic ? new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default)
                : PatternOwnerMapping(method.ContainingType, input, pattern.MatchedType);
            foreach (var parameter in method.OriginalDefinition.TypeParameters)
            { mapping[parameter] = HmType.Variable(); }
            if (method.IsStatic)
            { BindConvertedArgument(pattern.MatchedType, method.Parameters[0].Type, input, CSharpTypes.Rigid(method.OriginalDefinition.Parameters[0].Type, mapping)); }
            foreach (var parameter in method.OriginalDefinition.TypeParameters)
            { ImportConstraints(parameter, mapping[parameter], mapping, method.TypeArguments[parameter.Ordinal]); }
            return method.OriginalDefinition.Parameters.Skip(method.IsStatic ? 1 : 0).Select(parameter =>
            {
                if (parameter is null)
                { throw new ArgumentNullException(nameof(parameter)); }
                return CSharpTypes.Rigid(parameter.Type, mapping);
            }).ToArray();
        }

        private bool PreservesPatternBindings(SyntaxNode before, SyntaxNode after, SemanticModel beforeModel, SemanticModel afterModel, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (beforeModel is null)
            { throw new ArgumentNullException(nameof(beforeModel)); }
            if (afterModel is null)
            { throw new ArgumentNullException(nameof(afterModel)); }
            if (changedMethod is null)
            { throw new ArgumentNullException(nameof(changedMethod)); }
            if (changedTree is null)
            { throw new ArgumentNullException(nameof(changedTree)); }
            var originals = before.DescendantNodes().OfType<PatternSyntax>().ToArray();
            var candidates = after.DescendantNodes().OfType<PatternSyntax>().ToArray();
            if (originals.Length != candidates.Length)
            { return false; }
            for (var i = 0; i < originals.Length; i++)
            {
                var left = beforeModel.GetOperation(originals[i], cancellationToken);
                var right = afterModel.GetOperation(candidates[i], cancellationToken);
                if (!SamePatternProtocol(left, right, changedMethod, changedTree))
                { return false; }
            }
            return true;
        }

        private bool SamePatternMember(ISymbol? before, ISymbol? after, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
        {
            if (changedMethod is null)
            { throw new ArgumentNullException(nameof(changedMethod)); }
            if (changedTree is null)
            { throw new ArgumentNullException(nameof(changedTree)); }
            if (before is IMethodSymbol original && after is IMethodSymbol candidate
                && SymbolEqualityComparer.Default.Equals(original.OriginalDefinition, currentMethod?.OriginalDefinition)
                && candidate.DeclaringSyntaxReferences.Any(reference =>
                {
                    if (reference is null)
                    { throw new ArgumentNullException(nameof(reference)); }
                    return reference.SyntaxTree == changedTree && reference.Span.Start == changedMethod.SpanStart;
                }))
            { return SameCallSignature(original, candidate); }
            return ProtocolMember(before) == ProtocolMember(after);
        }

        private bool SamePatternProtocol(IOperation? before, IOperation? after, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
        {
            if (changedMethod is null)
            { throw new ArgumentNullException(nameof(changedMethod)); }
            if (changedTree is null)
            { throw new ArgumentNullException(nameof(changedTree)); }
            if (before?.Kind != after?.Kind)
            { return false; }
            switch (before)
            {
                case IRecursivePatternOperation original when after is IRecursivePatternOperation candidate:
                    if (!SamePatternMember(original.DeconstructSymbol, candidate.DeconstructSymbol, changedMethod, changedTree)
                        || original.PropertySubpatterns.Length != candidate.PropertySubpatterns.Length)
                    { return false; }
                    for (var i = 0; i < original.PropertySubpatterns.Length; i++)
                    { if (!SamePatternProtocol(original.PropertySubpatterns[i].Member, candidate.PropertySubpatterns[i].Member, changedMethod, changedTree)) { return false; } }
                    return true;
                case IListPatternOperation original when after is IListPatternOperation candidate:
                    return SamePatternMember(original.LengthSymbol, candidate.LengthSymbol, changedMethod, changedTree)
                        && SamePatternMember(original.IndexerSymbol, candidate.IndexerSymbol, changedMethod, changedTree);
                case ISlicePatternOperation original when after is ISlicePatternOperation candidate:
                    return SamePatternMember(original.SliceSymbol, candidate.SliceSymbol, changedMethod, changedTree);
                case IMemberReferenceOperation original when after is IMemberReferenceOperation candidate:
                    return SamePatternMember(original.Member, candidate.Member, changedMethod, changedTree)
                        && SamePatternProtocol(original.Instance, candidate.Instance, changedMethod, changedTree);
                default:
                    return true;
            }
        }
    }
}
