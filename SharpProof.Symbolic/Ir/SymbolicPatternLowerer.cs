namespace SharpProof.Symbolic.Ir;
internal static class SymbolicPatternLowerer {
    internal static bool TryLowerNullablePatternCondition(
        IsPatternExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!SymbolicNullableLowerer.TryLowerNullableHasValueTerm(expression.Expression, context, out var hasValue) ||
            !SymbolicNullableLowerer.TryLowerNullableValueTerm(expression.Expression, context, out var value))
            return false;
        var hasValueCondition = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicTruthAtom(hasValue),
            expression.Expression,
            "ir.pattern.nullable.has-value");
        return TryLowerNullablePattern(value, hasValueCondition, expression.Pattern, expression, context, out condition);
    }
    private static bool TryLowerNullablePattern(
        SymbolicTerm value,
        SymbolicCondition hasValue,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        pattern = UnwrapPattern(pattern);
        if (pattern is BinaryPatternSyntax binaryPattern &&
            TryLowerNullablePattern(value, hasValue, binaryPattern.Left, binaryPattern.Left, context, out var left) &&
            TryLowerNullablePattern(value, hasValue, binaryPattern.Right, binaryPattern.Right, context, out var right))
            return TryCombineBinaryPatternConditions(binaryPattern, left, right, out condition);
        if (pattern is UnaryPatternSyntax unaryPattern &&
            unaryPattern.IsKind(SyntaxKind.NotPattern) &&
            TryLowerNullablePattern(value, hasValue, unaryPattern.Pattern, unaryPattern.Pattern, context, out var operand)) {
            condition = new SymbolicNotCondition(operand);
            return true;
        }
        if (TryLowerNullPattern(pattern, context, out var negateNull)) {
            condition = negateNull ? hasValue : new SymbolicNotCondition(hasValue);
            return true;
        }
        if (TryLowerEmptyRecursivePattern(pattern, out var negateRecursive)) {
            condition = negateRecursive ? new SymbolicNotCondition(hasValue) : hasValue;
            return true;
        }
        if (TryLowerTrivialPatternCondition(pattern, out condition)) return true;
        var typeInfo = context.SemanticModel.GetTypeInfo(pattern, context.CancellationToken);
        if (!TryLowerPatternCondition(value, typeInfo.ConvertedType ?? typeInfo.Type, pattern, sourceNode, context, out var valueCondition))
            return false;
        condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, hasValue, valueCondition);
        return true;
    }
    internal static bool TryLowerPatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        context.CancellationToken.ThrowIfCancellationRequested();
        return TryLowerDesignationPatternCondition(value, pattern, sourceNode, context, out condition) ||
               TryLowerTypedBinaryPatternCondition(value, valueType, pattern, sourceNode, context, out condition) ||
               TryLowerTrivialPatternCondition(pattern, out condition) ||
               TryLowerNullPatternCondition(value, pattern, sourceNode, context, out condition) ||
               TryLowerConstantPatternCondition(value, pattern, sourceNode, context, out condition) ||
               TryLowerRelationalPatternCondition(value, pattern, sourceNode, context, out condition) ||
               TryLowerListPatternCondition(value, valueType, pattern, sourceNode, context, out condition) ||
               TryLowerRecursivePatternCondition(value, valueType, pattern, sourceNode, context, out condition) ||
               TryLowerEmptyRecursivePatternCondition(value, valueType, pattern, sourceNode, out condition) ||
               TryLowerTypedUnaryPatternCondition(value, valueType, pattern, sourceNode, context, out condition) ||
               TryLowerTypePatternCondition(value, pattern, sourceNode, context, out condition);
    }
    private static bool TryLowerTypedBinaryPatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        pattern = UnwrapPattern(pattern);
        if (pattern is not BinaryPatternSyntax binaryPattern ||
            !TryLowerPatternCondition(value, valueType, binaryPattern.Left, sourceNode, context, out var left) ||
            !TryLowerPatternCondition(value, valueType, binaryPattern.Right, sourceNode, context, out var right))
            return false;
        return TryCombineBinaryPatternConditions(binaryPattern, left, right, out condition);
    }
    private static bool TryLowerTypedUnaryPatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        pattern = UnwrapPattern(pattern);
        if (pattern is not UnaryPatternSyntax unaryPattern ||
            !unaryPattern.IsKind(SyntaxKind.NotPattern) ||
            !TryLowerPatternCondition(value, valueType, unaryPattern.Pattern, sourceNode, context, out var operand))
            return false;
        condition = new SymbolicNotCondition(operand);
        return true;
    }
    private static bool TryLowerDesignationPatternCondition(
        SymbolicTerm value,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        pattern = UnwrapPattern(pattern);
        VariableDesignationSyntax? designation = pattern switch {
            VarPatternSyntax varPattern => varPattern.Designation,
            DeclarationPatternSyntax declarationPattern => declarationPattern.Designation,
            _ => null
        };
        if (designation is DiscardDesignationSyntax) {
            if (pattern is DeclarationPatternSyntax { Type.IsVar: false } discardedDeclaration &&
                TryLowerTypeTestCondition(value, discardedDeclaration.Type, sourceNode, false, context, out condition))
                return true;
            condition = new SymbolicConstantCondition(true);
            return true;
        }
        if (!TryLowerVariableDesignationCondition(value, designation, sourceNode, context, includeProjections: false, out var binding))
            return false;
        condition = pattern is DeclarationPatternSyntax { Type.IsVar: false } declaration &&
            TryLowerTypeTestCondition(value, declaration.Type, sourceNode, false, context, out var typeCondition)
            ? new SymbolicBinaryCondition(SymbolicConditionOperator.And, typeCondition, binding)
            : binding;
        return true;
    }
    private static bool TryLowerVariableDesignationCondition(
        SymbolicTerm value,
        VariableDesignationSyntax? designation,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        bool includeProjections,
        out SymbolicCondition condition) {
        condition = null!;
        if (designation is DiscardDesignationSyntax) {
            condition = new SymbolicConstantCondition(true);
            return true;
        }
        if (designation is not SingleVariableDesignationSyntax singleDesignation ||
            context.SemanticModel.GetDeclaredSymbol(singleDesignation, context.CancellationToken) is not
                ILocalSymbol local ||
            !SymbolicTypeLowerer.TryGetValueKind(local.Type, out var localKind))
            return false;
        var localTerm = new SymbolicVariableTerm(context.GetVariableName(local), localKind);
        if (!SymbolicOperatorLowerer.CanCompareTerms(value, localTerm, SymbolicRelationOperator.Equal))
            return false;
        condition = SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.Equal,
            localTerm,
            value,
            sourceNode,
            "ir.pattern.designation");
        if (includeProjections &&
            localKind == SmtValueKind.Reference &&
            value.Kind == SmtValueKind.Reference) {
            var localLength = SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(local.Type, localTerm, sourceNode);
            var valueLength = SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(local.Type, value, sourceNode);
            if (localLength is { IsExact: true, Value: { } exactLocalLength } &&
                valueLength is { IsExact: true, Value: { } exactValueLength })
                condition = And(
                    condition,
                    SymbolicIrLowerer.CreateRelationCondition(
                        SymbolicRelationOperator.Equal,
                        exactLocalLength,
                        exactValueLength,
                        sourceNode,
                        "ir.pattern.designation-length"));
            if (local.Type.SpecialType == SpecialType.System_String &&
                SymbolicSemanticPipeline.ProjectStringContentTerm(localTerm, sourceNode) is { IsExact: true, Value: { } localString } &&
                SymbolicSemanticPipeline.ProjectStringContentTerm(value, sourceNode) is { IsExact: true, Value: { } valueString })
                condition = And(
                    condition,
                    SymbolicIrLowerer.CreateRelationCondition(
                        SymbolicRelationOperator.Equal,
                        localString,
                        valueString,
                        sourceNode,
                        "ir.pattern.designation-string"));
        }
        return true;
    }
    private static SymbolicCondition And(SymbolicCondition left, SymbolicCondition right) =>
        new SymbolicBinaryCondition(SymbolicConditionOperator.And, left, right);
    private static bool TryLowerRecursivePatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        pattern = UnwrapPattern(pattern);
        if (pattern is not RecursivePatternSyntax recursivePattern ||
            recursivePattern.PropertyPatternClause is not { Subpatterns.Count: > 0 } &&
            recursivePattern.PositionalPatternClause is not { Subpatterns.Count: > 0 })
            return false;
        SymbolicCondition? combined = null;
        if (value.Kind == SmtValueKind.Reference)
            combined = SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.NotEqual,
                value,
                new SymbolicNullTerm(),
                sourceNode,
                "ir.pattern.recursive.non-null");
        if (recursivePattern.Designation != null) {
            if (!TryLowerVariableDesignationCondition(
                    value,
                    recursivePattern.Designation,
                    sourceNode,
                    context,
                    includeProjections: true,
                    out var designationCondition))
                return false;
            combined = combined == null
                ? designationCondition
                : And(combined, designationCondition);
        }
        if (recursivePattern.PropertyPatternClause is { } propertyClause)
            foreach (var subpattern in propertyClause.Subpatterns) {
                if (!TryLowerPropertySubpatternTerm(
                        value,
                        valueType,
                        subpattern,
                        context,
                        out var member,
                        out var memberType,
                        out var accessCondition) ||
                    !TryLowerPatternCondition(member, memberType, subpattern.Pattern, subpattern, context, out var memberCondition))
                    return false;
                combined = combined == null
                    ? memberCondition
                    : new SymbolicBinaryCondition(SymbolicConditionOperator.And, combined, memberCondition);
                if (accessCondition != null)
                    combined = new SymbolicBinaryCondition(SymbolicConditionOperator.And, accessCondition, combined);
            }
        if (recursivePattern.PositionalPatternClause is { } positionalClause)
            for (var index = 0; index < positionalClause.Subpatterns.Count; index++) {
                var subpattern = positionalClause.Subpatterns[index];
                if (!TryCreateRecursivePatternPositionalTerm(
                        value,
                        valueType,
                        recursivePattern,
                        index,
                        context,
                        out var memberTerm,
                        out var memberType) ||
                    !TryLowerPatternCondition(memberTerm, memberType, subpattern.Pattern, subpattern, context, out var memberCondition))
                    return false;
                combined = combined == null
                    ? memberCondition
                    : new SymbolicBinaryCondition(SymbolicConditionOperator.And, combined, memberCondition);
            }
        if (combined == null) return false;
        condition = combined;
        return true;
    }
    internal static bool TryCreateRecursivePatternPositionalTerm(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        RecursivePatternSyntax recursivePattern,
        int index,
        SymbolicLoweringContext context,
        out SymbolicTerm term,
        out ITypeSymbol? componentType) {
        term = null!;
        componentType = null;
        if (SymbolicTypeFacts.TryGetTuplePositionalField(valueType, index, out var tupleField) &&
            SymbolicTupleLowerer.TryGetTupleElementStorageName(tupleField, out var storageName) &&
            SymbolicTypeLowerer.TryGetValueKind(tupleField.Type, out var tupleKind)) {
            term = new SymbolicMemberTerm(value, storageName, tupleKind);
            componentType = tupleField.Type;
            return true;
        }
        if (value.Kind != SmtValueKind.Reference ||
            context.SemanticModel.GetOperation(recursivePattern, context.CancellationToken) is not
                IRecursivePatternOperation { DeconstructSymbol: IMethodSymbol deconstructMethod })
            return false;
        var outputParameters = deconstructMethod.Parameters
            .Where(static parameter => parameter.RefKind is RefKind.Out or RefKind.Ref)
            .ToArray();
        if (index < 0 || index >= outputParameters.Length) return false;
        var outputParameter = outputParameters[index];
        if (!SymbolicTypeLowerer.TryGetValueKind(outputParameter.Type, out var outputKind)) return false;
        var projectionName = "$deconstruct." +
                             deconstructMethod.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                             "." + outputParameter.Ordinal;
        term = new SymbolicMemberTerm(value, projectionName, outputKind);
        componentType = outputParameter.Type;
        return true;
    }
    private static bool TryLowerPropertySubpatternTerm(
        SymbolicTerm receiver,
        ITypeSymbol? receiverType,
        SubpatternSyntax subpattern,
        SymbolicLoweringContext context,
        out SymbolicTerm term,
        out ITypeSymbol? memberType,
        out SymbolicCondition? accessCondition) {
        term = null!;
        memberType = null;
        accessCondition = null;
        var nameSyntax = (ExpressionSyntax?)subpattern.NameColon?.Name ?? subpattern.ExpressionColon?.Expression;
        if (nameSyntax == null || receiver.Kind != SmtValueKind.Reference)
            return false;
        var memberNames = new List<SimpleNameSyntax>();
        CollectPropertyPatternMemberNames(nameSyntax, memberNames);
        if (memberNames.Count == 0) return false;
        var current = receiver;
        var currentType = receiverType;
        for (var index = 0; index < memberNames.Count; index++) {
            var memberSyntax = memberNames[index];
            var member = ResolvePropertyPatternMember(currentType, memberSyntax.Identifier.ValueText) ??
                         context.SemanticModel.GetSymbolInfo(memberSyntax, context.CancellationToken).Symbol;
            memberType = member switch {
                IPropertySymbol property => property.Type,
                IFieldSymbol field => field.Type,
                _ => null
            };
            if (member == null || memberType == null || !SymbolicTypeLowerer.TryGetValueKind(memberType, out var memberKind)) return false;
            current = member.Name is "Length" or "Count" &&
                memberKind == SmtValueKind.Int &&
                SymbolicIndexingLowerer.TryCreateBuiltInLengthReferenceTerm(currentType, current, out var lengthTerm)
                ? lengthTerm
                : new SymbolicMemberTerm(current, member.Name, memberKind);
            if (index < memberNames.Count - 1) {
                if (current.Kind != SmtValueKind.Reference) return false;
                var nonNull = SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    current,
                    new SymbolicNullTerm(),
                    memberSyntax,
                    "ir.pattern.property-path.non-null");
                accessCondition = accessCondition == null
                    ? nonNull
                    : new SymbolicBinaryCondition(SymbolicConditionOperator.And, accessCondition, nonNull);
            }
            currentType = memberType;
        }
        term = current;
        return true;
    }
    private static ISymbol? ResolvePropertyPatternMember(ITypeSymbol? receiverType, string name) {
        if (receiverType is not INamedTypeSymbol namedType) return null;
        for (INamedTypeSymbol? current = namedType; current != null; current = current.BaseType) {
            var member = current.GetMembers(name)
                .FirstOrDefault(static candidate => candidate is IPropertySymbol or IFieldSymbol);
            if (member != null) return member;
        }
        return namedType.AllInterfaces
            .SelectMany(type => type.GetMembers(name))
            .FirstOrDefault(static candidate => candidate is IPropertySymbol or IFieldSymbol);
    }
    private static void CollectPropertyPatternMemberNames(ExpressionSyntax syntax, ICollection<SimpleNameSyntax> names) {
        switch (syntax) {
            case SimpleNameSyntax simpleName:
                names.Add(simpleName);
                break;
            case MemberAccessExpressionSyntax memberAccess:
                CollectPropertyPatternMemberNames(memberAccess.Expression, names);
                names.Add(memberAccess.Name);
                break;
        }
    }
    private static bool TryLowerListPatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        pattern = UnwrapPattern(pattern);
        return pattern is ListPatternSyntax listPattern &&
               TryLowerListPatternCondition(value, valueType, listPattern, sourceNode, context, 0, 0, out condition);
    }
    private static bool TryLowerListPatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        ListPatternSyntax listPattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        int basePrefixCount,
        int baseSuffixCount,
        out SymbolicCondition condition) {
        condition = null!;
        if (value.Kind != SmtValueKind.Reference ||
            !TryGetListPatternShape(value, valueType, out var length, out var elementType, out var elementKind))
            return false;
        SymbolicCondition combined = SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.NotEqual,
            value,
            new SymbolicNullTerm(),
            sourceNode,
            "ir.pattern.list.non-null");
        var sliceIndex = -1;
        for (var index = 0; index < listPattern.Patterns.Count; index++)
            if (listPattern.Patterns[index] is SlicePatternSyntax) {
                sliceIndex = index;
                break;
            }
        var fixedCount = listPattern.Patterns.Count - (sliceIndex >= 0 ? 1 : 0);
        if (sliceIndex < 0 || listPattern.Patterns[sliceIndex] is SlicePatternSyntax { Pattern: null }) {
            var requiredLength = basePrefixCount + baseSuffixCount + fixedCount;
            var lengthCondition = SymbolicIrLowerer.CreateRelationCondition(
                sliceIndex < 0 ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.GreaterThanOrEqual,
                length,
                new SymbolicIntegerConstantTerm(requiredLength),
                listPattern,
                sliceIndex < 0 ? "ir.pattern.list.exact-length" : "ir.pattern.list.minimum-length");
            combined = new SymbolicBinaryCondition(SymbolicConditionOperator.And, combined, lengthCondition);
        }
        for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++) {
            var elementPattern = listPattern.Patterns[patternIndex];
            if (elementPattern is SlicePatternSyntax slicePattern) {
                if (slicePattern.Pattern is ListPatternSyntax nestedList) {
                    if (!TryLowerListPatternCondition(
                            value,
                            valueType,
                            nestedList,
                            slicePattern,
                            context,
                            basePrefixCount + patternIndex,
                            baseSuffixCount + listPattern.Patterns.Count - patternIndex - 1,
                            out var nestedCondition))
                        return false;
                    combined = new SymbolicBinaryCondition(SymbolicConditionOperator.And, combined, nestedCondition);
                }
                continue;
            }
            SymbolicTerm indexTerm = sliceIndex < 0 || patternIndex < sliceIndex
                ? new SymbolicIntegerConstantTerm(basePrefixCount + patternIndex)
                : new SymbolicFromEndIndexTerm(new SymbolicIntegerConstantTerm(baseSuffixCount + listPattern.Patterns.Count
                    - patternIndex));
            var element = new SymbolicElementTerm(value, indexTerm, elementKind);
            if (!TryLowerPatternCondition(element, elementType, elementPattern, elementPattern, context, out var elementCondition))
                return false;
            combined = new SymbolicBinaryCondition(SymbolicConditionOperator.And, combined, elementCondition);
        }
        condition = combined;
        return true;
    }
    internal static bool TryGetListPatternShape(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        out SymbolicTerm length,
        out ITypeSymbol elementType,
        out SmtValueKind elementKind) {
        if (valueType is IArrayTypeSymbol { Rank: 1 } arrayType &&
            SymbolicTypeLowerer.TryGetValueKind(arrayType.ElementType, out elementKind)) {
            length = new SymbolicLengthTerm(value);
            elementType = arrayType.ElementType;
            return true;
        }
        if (valueType is INamedTypeSymbol namedType) {
            var candidateTypes = new[] { namedType }.Concat(namedType.AllInterfaces);
            var lengthProperty = candidateTypes
                .SelectMany(static type => type.GetMembers().OfType<IPropertySymbol>())
                .FirstOrDefault(static property =>
                    !property.IsStatic &&
                    !property.IsIndexer &&
                    property.Type.SpecialType == SpecialType.System_Int32 &&
                    property.Name is "Count" or "Length");
            var indexer = candidateTypes
                .SelectMany(static type => type.GetMembers().OfType<IPropertySymbol>())
                .FirstOrDefault(static property =>
                    !property.IsStatic &&
                    property.IsIndexer &&
                    property.Parameters.Length == 1 &&
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
    private static bool TryLowerTrivialPatternCondition(PatternSyntax pattern, out SymbolicCondition condition) {
        pattern = UnwrapPattern(pattern);
        if (pattern is DiscardPatternSyntax or VarPatternSyntax) {
            condition = new SymbolicConstantCondition(true);
            return true;
        }
        if (pattern is DeclarationPatternSyntax declarationPattern &&
            declarationPattern.Type.IsVar) {
            condition = new SymbolicConstantCondition(true);
            return true;
        }
        condition = null!;
        return false;
    }
    private static bool TryCombineBinaryPatternConditions(
        BinaryPatternSyntax binaryPattern,
        SymbolicCondition left,
        SymbolicCondition right,
        out SymbolicCondition condition) {
        if (binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword)) {
            condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, left, right);
            return true;
        }
        if (binaryPattern.OperatorToken.IsKind(SyntaxKind.OrKeyword)) {
            condition = new SymbolicBinaryCondition(SymbolicConditionOperator.Or, left, right);
            return true;
        }
        condition = null!;
        return false;
    }
    internal static bool TryLowerNullPatternCondition(
        SymbolicTerm value,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!TryLowerNullPattern(pattern, context, out var negate) ||
            value.Kind != SmtValueKind.Reference)
            return false;
        condition = SymbolicIrLowerer.CreateRelationCondition(
            negate ? SymbolicRelationOperator.NotEqual : SymbolicRelationOperator.Equal,
            value,
            new SymbolicNullTerm(),
            sourceNode,
            "ir.pattern.null");
        return true;
    }
    private static bool TryLowerNullPattern(PatternSyntax pattern, SymbolicLoweringContext context, out bool negate) {
        pattern = UnwrapPattern(pattern);
        negate = false;
        if (pattern is ConstantPatternSyntax constantPattern &&
            context.SemanticModel.GetConstantValue(constantPattern.Expression, context.CancellationToken) is {
                HasValue: true,
                Value: null
            })
            return true;
        if (pattern is UnaryPatternSyntax unaryPattern &&
            unaryPattern.IsKind(SyntaxKind.NotPattern) &&
            TryLowerNullPattern(unaryPattern.Pattern, context, out var nestedNegate)) {
            negate = !nestedNegate;
            return true;
        }
        return false;
    }
    internal static bool TryLowerConstantPatternCondition(
        SymbolicTerm value,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!TryLowerConstantPattern(pattern, out var constantExpression, out var negate) ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(constantExpression, context), out var constant) ||
            !SymbolicOperatorLowerer.CanCompareTerms(value, constant, SymbolicRelationOperator.Equal))
            return false;
        condition = SymbolicIrLowerer.CreateRelationCondition(
            negate ? SymbolicRelationOperator.NotEqual : SymbolicRelationOperator.Equal,
            value,
            constant,
            sourceNode,
            "ir.pattern.constant");
        return true;
    }
    private static bool TryLowerConstantPattern(PatternSyntax pattern, out ExpressionSyntax constantExpression, out bool negate) {
        pattern = UnwrapPattern(pattern);
        negate = false;
        if (pattern is ConstantPatternSyntax constantPattern &&
            !constantPattern.Expression.IsKind(SyntaxKind.NullLiteralExpression)) {
            constantExpression = constantPattern.Expression;
            return true;
        }
        if (pattern is UnaryPatternSyntax unaryPattern &&
            unaryPattern.IsKind(SyntaxKind.NotPattern) &&
            TryLowerConstantPattern(unaryPattern.Pattern, out constantExpression, out var nestedNegate)) {
            negate = !nestedNegate;
            return true;
        }
        constantExpression = null!;
        return false;
    }
    internal static bool TryLowerRelationalPatternCondition(
        SymbolicTerm value,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!TryLowerRelationalPattern(pattern, out var operatorKind, out var relationalExpression, out var negate) ||
            !SymbolicOperatorLowerer.TryGetRelationalPatternOperator(operatorKind, negate, out var relationOperator) ||
            value.Kind != SmtValueKind.Int ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(relationalExpression, context), out var relationalValue) ||
            relationalValue.Kind != SmtValueKind.Int)
            return false;
        condition = SymbolicIrLowerer.CreateRelationCondition(relationOperator, value, relationalValue, sourceNode,
            "ir.pattern.relational");
        return true;
    }
    private static bool TryLowerRelationalPattern(
        PatternSyntax pattern,
        out SyntaxKind operatorKind,
        out ExpressionSyntax relationalExpression,
        out bool negate) {
        pattern = UnwrapPattern(pattern);
        negate = false;
        if (pattern is RelationalPatternSyntax relationalPattern) {
            operatorKind = relationalPattern.OperatorToken.Kind();
            relationalExpression = relationalPattern.Expression;
            return true;
        }
        if (pattern is UnaryPatternSyntax unaryPattern &&
            unaryPattern.IsKind(SyntaxKind.NotPattern) &&
            TryLowerRelationalPattern(unaryPattern.Pattern, out operatorKind, out relationalExpression, out var nestedNegate)) {
            negate = !nestedNegate;
            return true;
        }
        operatorKind = default;
        relationalExpression = null!;
        return false;
    }
    internal static bool TryLowerEmptyRecursivePatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        out SymbolicCondition condition) {
        condition = null!;
        if (!TryLowerEmptyRecursivePattern(pattern, out var negate))
            return false;
        if (valueType is { IsValueType: true } &&
            valueType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T) {
            condition = new SymbolicConstantCondition(!negate);
            return true;
        }
        if (value.Kind != SmtValueKind.Reference) return false;
        condition = SymbolicIrLowerer.CreateRelationCondition(
            negate ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
            value,
            new SymbolicNullTerm(),
            sourceNode,
            "ir.pattern.recursive.empty");
        return true;
    }
    private static bool TryLowerEmptyRecursivePattern(PatternSyntax pattern, out bool negate) {
        pattern = UnwrapPattern(pattern);
        negate = false;
        if (pattern is RecursivePatternSyntax recursivePattern &&
            recursivePattern.PropertyPatternClause is not { Subpatterns.Count: > 0 } &&
            recursivePattern.PositionalPatternClause is not { Subpatterns.Count: > 0 })
            return true;
        if (pattern is UnaryPatternSyntax unaryPattern &&
            unaryPattern.IsKind(SyntaxKind.NotPattern) &&
            TryLowerEmptyRecursivePattern(unaryPattern.Pattern, out var nestedNegate)) {
            negate = !nestedNegate;
            return true;
        }
        return false;
    }
    internal static bool TryLowerTypePatternCondition(
        SymbolicTerm value,
        PatternSyntax pattern,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!TryLowerTypePattern(pattern, out var typeSyntax, out var negate)) return false;
        return TryLowerTypeTestCondition(value, typeSyntax, sourceNode, negate, context, out condition);
    }
    internal static bool TryLowerTypeTestCondition(
        SymbolicTerm value,
        TypeSyntax typeSyntax,
        SyntaxNode sourceNode,
        bool negate,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        var type = context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type;
        if (!SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey(type, out var typeKey) ||
            value.Kind != SmtValueKind.Reference)
            return false;
        var nonNull = SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.NotEqual,
            value,
            new SymbolicNullTerm(),
            sourceNode,
            "ir.pattern.type.non-null");
        var typeTest = SymbolicIrLowerer.CreateFactCondition(new SymbolicTypeTestAtom(value, typeKey), sourceNode, "ir.pattern.type.test");
        var positive = new SymbolicBinaryCondition(SymbolicConditionOperator.And, nonNull, typeTest);
        condition = negate ? new SymbolicNotCondition(positive) : positive;
        return true;
    }
    private static bool TryLowerTypePattern(PatternSyntax pattern, out TypeSyntax type, out bool negate) {
        pattern = UnwrapPattern(pattern);
        negate = false;
        if (pattern is TypePatternSyntax typePattern) {
            type = typePattern.Type;
            return true;
        }
        if (pattern is DeclarationPatternSyntax declarationPattern) {
            type = declarationPattern.Type;
            return true;
        }
        if (pattern is UnaryPatternSyntax unaryPattern &&
            unaryPattern.IsKind(SyntaxKind.NotPattern) &&
            TryLowerTypePattern(unaryPattern.Pattern, out type, out var nestedNegate)) {
            negate = !nestedNegate;
            return true;
        }
        type = null!;
        return false;
    }
    private static PatternSyntax UnwrapPattern(PatternSyntax pattern) {
        while (pattern is ParenthesizedPatternSyntax parenthesizedPattern) pattern = parenthesizedPattern.Pattern;
        return pattern;
    }
}
