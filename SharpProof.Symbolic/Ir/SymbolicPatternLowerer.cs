namespace SharpProof.Symbolic.Ir;
internal static class SymbolicPatternLowerer {
    private readonly record struct PatternSubject(
        SymbolicTerm Value,
        ITypeSymbol? Type,
        SyntaxNode Source,
        SymbolicLoweringContext Context) {
        internal PatternSubject Project(SymbolicTerm value, ITypeSymbol? type, SyntaxNode source) =>
            new(value, type, source, Context);
    }
    private readonly record struct PatternProjection(
        SymbolicTerm Term,
        ITypeSymbol? Type,
        SymbolicCondition? Access = null);
    private readonly record struct NegatedPattern(PatternSyntax Pattern, bool Negate);
    internal static bool TryLowerNullablePatternCondition(
        IsPatternExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        if (!SymbolicNullableLowerer.TryLowerNullableHasValueTerm(expression.Expression, context, out var hasValue) ||
            !SymbolicNullableLowerer.TryLowerNullableValueTerm(expression.Expression, context, out var value)) {
            condition = null!;
            return false;
        }
        var hasValueCondition = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicTruthAtom(hasValue), expression.Expression, "ir.pattern.nullable.has-value");
        return Try(BindNullable(value, hasValueCondition, expression.Pattern, expression, context), out condition);
    }
    private static SymbolicCondition? BindNullable(
        SymbolicTerm value,
        SymbolicCondition hasValue,
        PatternSyntax pattern,
        SyntaxNode source,
        SymbolicLoweringContext context) {
        pattern = Unwrap(pattern);
        if (pattern is BinaryPatternSyntax binary) {
            var left = BindNullable(value, hasValue, binary.Left, binary.Left, context);
            return left != null &&
                   BindNullable(value, hasValue, binary.Right, binary.Right, context) is { } right
                ? Combine(binary, left, right)
                : null;
        }
        if (pattern is UnaryPatternSyntax unary && unary.IsKind(SyntaxKind.NotPattern))
            return BindNullable(value, hasValue, unary.Pattern, unary.Pattern, context) is { } operand
                ? new SymbolicNotCondition(operand)
                : null;
        var atom = UnwrapNegations(pattern);
        if (atom.Pattern is ConstantPatternSyntax constant && IsNullConstant(constant, context))
            return atom.Negate ? hasValue : new SymbolicNotCondition(hasValue);
        if (atom.Pattern is RecursivePatternSyntax empty && !HasRecursiveSubpatterns(empty))
            return atom.Negate ? new SymbolicNotCondition(hasValue) : hasValue;
        if (IsTrivial(pattern)) return True();
        var typeInfo = context.SemanticModel.GetTypeInfo(pattern, context.CancellationToken);
        return Bind(
            new PatternSubject(value, typeInfo.ConvertedType ?? typeInfo.Type, source, context),
            pattern) is { } bound
            ? And(hasValue, bound)
            : null;
    }
    internal static bool TryLowerPatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) =>
        Try(Bind(new PatternSubject(value, valueType, sourceNode, context), pattern), out condition);
    private static SymbolicCondition? Bind(PatternSubject subject, PatternSyntax pattern) {
        subject.Context.CancellationToken.ThrowIfCancellationRequested();
        pattern = Unwrap(pattern);
        if (pattern is VarPatternSyntax or DeclarationPatternSyntax &&
            BindDesignation(subject, pattern) is { } designation)
            return designation;
        if (pattern is BinaryPatternSyntax binary) {
            var left = Bind(subject, binary.Left);
            return left != null && Bind(subject, binary.Right) is { } right
                ? Combine(binary, left, right)
                : null;
        }
        if (IsTrivial(pattern)) return True();
        var atom = UnwrapNegations(pattern);
        if (atom.Pattern is ConstantPatternSyntax constant) {
            if (IsNullConstant(constant, subject.Context) &&
                BindNull(subject.Value, subject.Source, atom.Negate) is { } nullCondition)
                return nullCondition;
            if (!constant.Expression.IsKind(SyntaxKind.NullLiteralExpression) &&
                BindConstant(subject, constant.Expression, atom.Negate) is { } constantCondition)
                return constantCondition;
        }
        if (atom.Pattern is RelationalPatternSyntax relational &&
            BindRelational(subject, relational, atom.Negate) is { } relationalCondition)
            return relationalCondition;
        if (pattern is ListPatternSyntax list && BindList(subject, list, 0, 0) is { } listCondition)
            return listCondition;
        if (pattern is RecursivePatternSyntax recursive &&
            HasRecursiveSubpatterns(recursive) &&
            BindRecursive(subject, recursive) is { } recursiveCondition)
            return recursiveCondition;
        if (atom.Pattern is RecursivePatternSyntax empty &&
            !HasRecursiveSubpatterns(empty) &&
            BindEmptyRecursive(subject.Value, subject.Type, subject.Source, atom.Negate) is { } emptyCondition)
            return emptyCondition;
        if (pattern is UnaryPatternSyntax unary && unary.IsKind(SyntaxKind.NotPattern))
            return Bind(subject, unary.Pattern) is { } operand ? new SymbolicNotCondition(operand) : null;
        return GetTypeSyntax(pattern) is { } type ? BindType(subject, type, false) : null;
    }
    private static SymbolicCondition? BindDesignation(PatternSubject subject, PatternSyntax pattern) {
        var designation = pattern switch {
            VarPatternSyntax varPattern => varPattern.Designation,
            DeclarationPatternSyntax declaration => declaration.Designation,
            _ => null
        };
        if (designation is DiscardDesignationSyntax)
            return pattern is DeclarationPatternSyntax { Type.IsVar: false } declaration
                ? BindType(subject, declaration.Type, false) ?? True()
                : True();
        if (designation == null || BindVariable(subject, designation, false) is not { } binding) return null;
        return pattern is DeclarationPatternSyntax { Type.IsVar: false } typed &&
               BindType(subject, typed.Type, false) is { } typeCondition
            ? And(typeCondition, binding)
            : binding;
    }
    private static SymbolicCondition? BindVariable(
        PatternSubject subject,
        VariableDesignationSyntax designation,
        bool includeProjections) {
        if (designation is DiscardDesignationSyntax) return True();
        if (designation is not SingleVariableDesignationSyntax single ||
            subject.Context.SemanticModel.GetDeclaredSymbol(single, subject.Context.CancellationToken) is not
                ILocalSymbol local ||
            !SymbolicTypeLowerer.TryGetValueKind(local.Type, out var kind))
            return null;
        var localTerm = new SymbolicVariableTerm(subject.Context.GetVariableName(local), kind);
        if (!SymbolicOperatorLowerer.CanCompareTerms(subject.Value, localTerm, SymbolicRelationOperator.Equal))
            return null;
        var condition = Relation(
            subject.Source, SymbolicRelationOperator.Equal, localTerm, subject.Value, "ir.pattern.designation");
        if (!includeProjections || kind != SmtValueKind.Reference || subject.Value.Kind != SmtValueKind.Reference)
            return condition;
        var localLength = SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(local.Type, localTerm, subject.Source);
        var valueLength = SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(local.Type, subject.Value, subject.Source);
        if (localLength is { IsExact: true, Value: { } exactLocalLength } &&
            valueLength is { IsExact: true, Value: { } exactValueLength })
            condition = And(condition, Relation(
                subject.Source, SymbolicRelationOperator.Equal, exactLocalLength, exactValueLength,
                "ir.pattern.designation-length"));
        if (local.Type.SpecialType == SpecialType.System_String &&
            SymbolicSemanticPipeline.ProjectStringContentTerm(
                localTerm, subject.Source) is { IsExact: true, Value: { } localString } &&
            SymbolicSemanticPipeline.ProjectStringContentTerm(
                subject.Value, subject.Source) is { IsExact: true, Value: { } valueString })
            condition = And(condition, Relation(
                subject.Source, SymbolicRelationOperator.Equal, localString, valueString,
                "ir.pattern.designation-string"));
        return condition;
    }
    private static SymbolicCondition? BindRecursive(PatternSubject subject, RecursivePatternSyntax pattern) {
        SymbolicCondition? combined = subject.Value.Kind == SmtValueKind.Reference
            ? Relation(subject.Source, SymbolicRelationOperator.NotEqual, subject.Value, new SymbolicNullTerm(),
                "ir.pattern.recursive.non-null")
            : null;
        if (pattern.Designation != null) {
            var designation = BindVariable(subject, pattern.Designation, true);
            if (designation == null) return null;
            combined = Append(combined, designation);
        }
        if (pattern.PropertyPatternClause is { } properties)
            foreach (var subpattern in properties.Subpatterns) {
                var projection = CreatePropertyProjection(subject, subpattern);
                if (projection == null ||
                    Bind(subject.Project(projection.Value.Term, projection.Value.Type, subpattern),
                        subpattern.Pattern) is not { } memberCondition)
                    return null;
                combined = Append(combined, memberCondition);
                if (projection.Value.Access != null)
                    combined = combined == null
                        ? projection.Value.Access
                        : And(projection.Value.Access, combined);
            }
        if (pattern.PositionalPatternClause is { } positional)
            for (var index = 0; index < positional.Subpatterns.Count; index++) {
                var subpattern = positional.Subpatterns[index];
                var projection = CreatePositionalProjection(subject.Value, subject.Type, pattern, index, subject.Context);
                if (projection == null ||
                    Bind(subject.Project(projection.Value.Term, projection.Value.Type, subpattern),
                        subpattern.Pattern) is not { } componentCondition)
                    return null;
                combined = Append(combined, componentCondition);
            }
        return combined;
    }
    private static PatternProjection? CreatePositionalProjection(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        RecursivePatternSyntax pattern,
        int index,
        SymbolicLoweringContext context) {
        if (SymbolicTypeFacts.TryGetTuplePositionalField(valueType, index, out var tupleField) &&
            SymbolicTupleLowerer.TryGetTupleElementStorageName(tupleField, out var storageName) &&
            SymbolicTypeLowerer.TryGetValueKind(tupleField.Type, out var tupleKind))
            return new PatternProjection(new SymbolicMemberTerm(value, storageName, tupleKind), tupleField.Type);
        if (value.Kind != SmtValueKind.Reference ||
            context.SemanticModel.GetOperation(pattern, context.CancellationToken) is not
                IRecursivePatternOperation { DeconstructSymbol: IMethodSymbol deconstruct })
            return null;
        var outputs = deconstruct.Parameters.Where(static parameter =>
            parameter.RefKind is RefKind.Out or RefKind.Ref).ToArray();
        if (index < 0 ||
            index >= outputs.Length ||
            !SymbolicTypeLowerer.TryGetValueKind(outputs[index].Type, out var kind))
            return null;
        var parameter = outputs[index];
        var name = "$deconstruct." +
                   deconstruct.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                   "." + parameter.Ordinal;
        return new PatternProjection(new SymbolicMemberTerm(value, name, kind), parameter.Type);
    }
    private static PatternProjection? CreatePropertyProjection(
        PatternSubject subject,
        SubpatternSyntax subpattern) {
        var nameSyntax = (ExpressionSyntax?)subpattern.NameColon?.Name ?? subpattern.ExpressionColon?.Expression;
        if (nameSyntax == null || subject.Value.Kind != SmtValueKind.Reference) return null;
        var names = EnumeratePropertyNames(nameSyntax).ToArray();
        if (names.Length == 0) return null;
        var current = subject.Value;
        var currentType = subject.Type;
        SymbolicCondition? access = null;
        foreach (var syntax in names) {
            var member = ResolvePropertyMember(currentType, syntax.Identifier.ValueText) ??
                         subject.Context.SemanticModel.GetSymbolInfo(syntax, subject.Context.CancellationToken).Symbol;
            var memberType = member switch {
                IPropertySymbol property => property.Type,
                IFieldSymbol field => field.Type,
                _ => null
            };
            if (member == null ||
                memberType == null ||
                !SymbolicTypeLowerer.TryGetValueKind(memberType, out var kind))
                return null;
            current = member.Name is "Length" or "Count" &&
                      kind == SmtValueKind.Int &&
                      SymbolicIndexingLowerer.TryCreateBuiltInLengthReferenceTerm(
                          currentType, current, out var length)
                ? length
                : new SymbolicMemberTerm(current, member.Name, kind);
            if (!ReferenceEquals(syntax, names[names.Length - 1])) {
                if (current.Kind != SmtValueKind.Reference) return null;
                access = Append(access, Relation(
                    syntax, SymbolicRelationOperator.NotEqual, current, new SymbolicNullTerm(),
                    "ir.pattern.property-path.non-null"));
            }
            currentType = memberType;
        }
        return new PatternProjection(current, currentType, access);
    }
    private static ISymbol? ResolvePropertyMember(ITypeSymbol? receiverType, string name) {
        if (receiverType is not INamedTypeSymbol namedType) return null;
        for (INamedTypeSymbol? current = namedType; current != null; current = current.BaseType) {
            var member = current.GetMembers(name)
                .FirstOrDefault(static candidate => candidate is IPropertySymbol or IFieldSymbol);
            if (member != null) return member;
        }
        return namedType.AllInterfaces.SelectMany(type => type.GetMembers(name))
            .FirstOrDefault(static candidate => candidate is IPropertySymbol or IFieldSymbol);
    }
    private static IEnumerable<SimpleNameSyntax> EnumeratePropertyNames(ExpressionSyntax syntax) {
        if (syntax is SimpleNameSyntax simple) {
            yield return simple;
            yield break;
        }
        if (syntax is not MemberAccessExpressionSyntax memberAccess) yield break;
        foreach (var name in EnumeratePropertyNames(memberAccess.Expression)) yield return name;
        yield return memberAccess.Name;
    }
    private static SymbolicCondition? BindList(
        PatternSubject subject,
        ListPatternSyntax pattern,
        int basePrefixCount,
        int baseSuffixCount) {
        if (subject.Value.Kind != SmtValueKind.Reference ||
            !TryGetListPatternShape(subject.Value, subject.Type, out var length, out var elementType, out var elementKind))
            return null;
        var combined = Relation(
            subject.Source, SymbolicRelationOperator.NotEqual, subject.Value, new SymbolicNullTerm(),
            "ir.pattern.list.non-null");
        var sliceIndex = -1;
        for (var index = 0; index < pattern.Patterns.Count; index++)
            if (pattern.Patterns[index] is SlicePatternSyntax) {
                sliceIndex = index;
                break;
            }
        var fixedCount = pattern.Patterns.Count - (sliceIndex >= 0 ? 1 : 0);
        var nestedList = sliceIndex >= 0 &&
                         pattern.Patterns[sliceIndex] is SlicePatternSyntax {
                             Pattern: ListPatternSyntax
                         };
        if (!nestedList)
            combined = And(combined, Relation(
                pattern,
                sliceIndex < 0 ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.GreaterThanOrEqual,
                length,
                new SymbolicIntegerConstantTerm(basePrefixCount + baseSuffixCount + fixedCount),
                sliceIndex < 0 ? "ir.pattern.list.exact-length" : "ir.pattern.list.minimum-length"));
        for (var patternIndex = 0; patternIndex < pattern.Patterns.Count; patternIndex++) {
            var elementPattern = pattern.Patterns[patternIndex];
            if (elementPattern is SlicePatternSyntax slice) {
                if (slice.Pattern is ListPatternSyntax nested) {
                    var nestedCondition = BindList(
                        subject.Project(subject.Value, subject.Type, slice),
                        nested,
                        basePrefixCount + patternIndex,
                        baseSuffixCount + pattern.Patterns.Count - patternIndex - 1);
                    if (nestedCondition == null) return null;
                    combined = And(combined, nestedCondition);
                }
                continue;
            }
            SymbolicTerm index = sliceIndex < 0 || patternIndex < sliceIndex
                ? new SymbolicIntegerConstantTerm(basePrefixCount + patternIndex)
                : new SymbolicFromEndIndexTerm(new SymbolicIntegerConstantTerm(
                    baseSuffixCount + pattern.Patterns.Count - patternIndex));
            var element = new SymbolicElementTerm(subject.Value, index, elementKind);
            var elementCondition = Bind(subject.Project(element, elementType, elementPattern), elementPattern);
            if (elementCondition == null) return null;
            combined = And(combined, elementCondition);
        }
        return combined;
    }
    internal static bool TryGetListPatternShape(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        out SymbolicTerm length,
        out ITypeSymbol elementType,
        out SmtValueKind elementKind) {
        if (valueType is IArrayTypeSymbol { Rank: 1 } array &&
            SymbolicTypeLowerer.TryGetValueKind(array.ElementType, out elementKind)) {
            length = new SymbolicLengthTerm(value);
            elementType = array.ElementType;
            return true;
        }
        if (valueType is INamedTypeSymbol named) {
            var candidates = new[] { named }.Concat(named.AllInterfaces);
            var lengthProperty = candidates.SelectMany(static type => type.GetMembers().OfType<IPropertySymbol>())
                .FirstOrDefault(static property =>
                    !property.IsStatic && !property.IsIndexer &&
                    property.Type.SpecialType == SpecialType.System_Int32 &&
                    property.Name is "Count" or "Length");
            var indexer = candidates.SelectMany(static type => type.GetMembers().OfType<IPropertySymbol>())
                .FirstOrDefault(static property =>
                    !property.IsStatic && property.IsIndexer && property.Parameters.Length == 1 &&
                    property.Parameters[0].Type.SpecialType == SpecialType.System_Int32);
            if (lengthProperty != null &&
                indexer != null &&
                SymbolicTypeLowerer.TryGetValueKind(indexer.Type, out elementKind)) {
                if (!SymbolicIndexingLowerer.TryCreateBuiltInLengthReferenceTerm(valueType, value, out length))
                    length = lengthProperty.Name == "Count"
                        ? new SymbolicCountTerm(value)
                        : new SymbolicLengthTerm(value);
                elementType = indexer.Type;
                return true;
            }
        }
        length = null!;
        elementType = null!;
        elementKind = default;
        return false;
    }
    private static SymbolicCondition? BindNull(SymbolicTerm value, SyntaxNode source, bool negate) =>
        value.Kind == SmtValueKind.Reference
            ? Relation(source, negate ? SymbolicRelationOperator.NotEqual : SymbolicRelationOperator.Equal,
                value, new SymbolicNullTerm(), "ir.pattern.null")
            : null;
    private static bool IsNullConstant(ConstantPatternSyntax pattern, SymbolicLoweringContext context) =>
        context.SemanticModel.GetConstantValue(pattern.Expression, context.CancellationToken) is {
            HasValue: true,
            Value: null
        };
    private static SymbolicCondition? BindConstant(
        PatternSubject subject,
        ExpressionSyntax expression,
        bool negate) {
        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(expression, subject.Context), out var constant) ||
            !SymbolicOperatorLowerer.CanCompareTerms(subject.Value, constant, SymbolicRelationOperator.Equal))
            return null;
        return Relation(subject.Source,
            negate ? SymbolicRelationOperator.NotEqual : SymbolicRelationOperator.Equal,
            subject.Value, constant, "ir.pattern.constant");
    }
    private static SymbolicCondition? BindRelational(
        PatternSubject subject,
        RelationalPatternSyntax pattern,
        bool negate) {
        if (!SymbolicOperatorLowerer.TryGetRelationalPatternOperator(
                pattern.OperatorToken.Kind(), negate, out var op) ||
            subject.Value.Kind != SmtValueKind.Int ||
            !SymbolicLoweringValue.TryGet(
                SymbolicIrLowerer.LowerTerm(pattern.Expression, subject.Context), out var compared) ||
            compared.Kind != SmtValueKind.Int)
            return null;
        return Relation(subject.Source, op, subject.Value, compared, "ir.pattern.relational");
    }
    private static SymbolicCondition? BindEmptyRecursive(
        SymbolicTerm value,
        ITypeSymbol? type,
        SyntaxNode source,
        bool negate) {
        if (type is { IsValueType: true } &&
            type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T)
            return new SymbolicConstantCondition(!negate);
        return value.Kind == SmtValueKind.Reference
            ? Relation(source, negate ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                value, new SymbolicNullTerm(), "ir.pattern.recursive.empty")
            : null;
    }
    private static bool HasRecursiveSubpatterns(RecursivePatternSyntax pattern) =>
        pattern.PropertyPatternClause is { Subpatterns.Count: > 0 } ||
        pattern.PositionalPatternClause is { Subpatterns.Count: > 0 };
    internal static bool TryLowerTypeTestCondition(
        SymbolicTerm value,
        TypeSyntax typeSyntax,
        SyntaxNode sourceNode,
        bool negate,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) =>
        Try(BindType(new PatternSubject(value, null, sourceNode, context), typeSyntax, negate), out condition);
    private static SymbolicCondition? BindType(PatternSubject subject, TypeSyntax syntax, bool negate) {
        var type = subject.Context.SemanticModel.GetTypeInfo(syntax, subject.Context.CancellationToken).Type;
        if (type == null ||
            !SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey(type, out _) ||
            subject.Value.Kind != SmtValueKind.Reference)
            return null;
        var nonNull = Relation(subject.Source, SymbolicRelationOperator.NotEqual,
            subject.Value, new SymbolicNullTerm(), "ir.pattern.type.non-null");
        SymbolicCondition positive = nonNull;
        var seenTypeKeys = new HashSet<string>(StringComparer.Ordinal);
        var isTargetType = true;
        foreach (var compatibleType in
                 SymbolicTypeFacts.EnumerateSelfBaseTypesAndInterfaces(type)) {
            if (!SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey(
                    compatibleType,
                    out var compatibleTypeKey) ||
                !seenTypeKeys.Add(compatibleTypeKey) ||
                compatibleType.SpecialType == SpecialType.System_Object)
                continue;
            var typeAtom = isTargetType && type.IsSealed
                ? new SymbolicExactRuntimeTypeAtom(
                    subject.Value,
                    compatibleTypeKey)
                : new SymbolicTypeTestAtom(
                    subject.Value,
                    compatibleTypeKey);
            positive = And(
                positive,
                SymbolicIrLowerer.CreateFactCondition(
                    typeAtom,
                    subject.Source,
                    "ir.pattern.type.test"));
            isTargetType = false;
        }
        return negate ? new SymbolicNotCondition(positive) : positive;
    }
    private static TypeSyntax? GetTypeSyntax(PatternSyntax pattern) => pattern switch {
        TypePatternSyntax type => type.Type,
        DeclarationPatternSyntax declaration => declaration.Type,
        _ => null
    };
    private static bool IsTrivial(PatternSyntax pattern) =>
        pattern is DiscardPatternSyntax or VarPatternSyntax ||
        pattern is DeclarationPatternSyntax { Type.IsVar: true };
    private static SymbolicCondition? Combine(
        BinaryPatternSyntax pattern,
        SymbolicCondition left,
        SymbolicCondition right) =>
        pattern.OperatorToken.Kind() switch {
            SyntaxKind.AndKeyword => And(left, right),
            SyntaxKind.OrKeyword => new SymbolicBinaryCondition(SymbolicConditionOperator.Or, left, right),
            _ => null
        };
    private static SymbolicCondition Relation(
        SyntaxNode source,
        SymbolicRelationOperator op,
        SymbolicTerm left,
        SymbolicTerm right,
        string provenance) =>
        SymbolicIrLowerer.CreateRelationCondition(op, left, right, source, provenance);
    private static SymbolicCondition Append(SymbolicCondition? combined, SymbolicCondition condition) =>
        combined == null ? condition : And(combined, condition);
    private static SymbolicCondition And(SymbolicCondition left, SymbolicCondition right) =>
        new SymbolicBinaryCondition(SymbolicConditionOperator.And, left, right);
    private static SymbolicCondition True() => new SymbolicConstantCondition(true);
    private static bool Try(SymbolicCondition? candidate, out SymbolicCondition condition) {
        condition = candidate!;
        return candidate != null;
    }
    private static NegatedPattern UnwrapNegations(PatternSyntax pattern) {
        var negate = false;
        while (true) {
            pattern = Unwrap(pattern);
            if (pattern is not UnaryPatternSyntax unary || !unary.IsKind(SyntaxKind.NotPattern))
                return new NegatedPattern(pattern, negate);
            negate = !negate;
            pattern = unary.Pattern;
        }
    }
    private static PatternSyntax Unwrap(PatternSyntax pattern) {
        while (pattern is ParenthesizedPatternSyntax parenthesized) pattern = parenthesized.Pattern;
        return pattern;
    }
}
