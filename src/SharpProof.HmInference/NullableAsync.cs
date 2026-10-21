using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private INamedTypeSymbol? asyncReturn;

        private readonly Stack<(ITypeSymbol Type, HmType Value)> conditionalReceivers = new();

        private (ITypeSymbol Type, HmType Value) ConditionalReceiver()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (conditionalReceivers.Count == 0)
            { throw new InferenceException("Unresolved conditional receiver."); }
            return conditionalReceivers.Peek();
        }

        private HmType ConditionalBinding(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var receiver = ConditionalReceiver();
            var operation = model.GetOperation(expression, cancellationToken);
            if (expression is ElementBindingExpressionSyntax binding && receiver.Type is IArrayTypeSymbol indexedArray)
            {
                var element = HmType.Variable();
                HmType.Unify(receiver.Value, new("[" + new string(',', indexedArray.Rank - 1) + "]", element));
                foreach (var argument in binding.ArgumentList.Arguments)
                { Anchor(argument.Expression); }
                var indexType = binding.ArgumentList.Arguments.Count == 1 ? model.GetTypeInfo(binding.ArgumentList.Arguments[0].Expression, cancellationToken).Type : null;
                return indexType is INamedTypeSymbol { Name: "Range" } range && range.ContainingNamespace.ToDisplayString() == "System" ? receiver.Value : element;
            }
            if (operation is IEventReferenceOperation referencedEvent)
            {
                var mapping = PatternOwnerMapping(referencedEvent.Event.ContainingType, receiver.Value, receiver.Type);
                return CSharpTypes.Rigid(referencedEvent.Event.OriginalDefinition.Type, mapping);
            }
            if (operation is IPropertyReferenceOperation property)
            {
                if (receiver.Type is IArrayTypeSymbol array && property.Property.Name is "Length" or "LongLength" or "Rank")
                {
                    HmType.Unify(receiver.Value, new("[" + new string(',', array.Rank - 1) + "]", HmType.Variable()));
                    return CSharpTypes.Rigid(property.Type!);
                }
                var mapping = PatternOwnerMapping(property.Property.ContainingType, receiver.Value, receiver.Type);
                BindArguments(property.Arguments, property.Property.OriginalDefinition.Parameters.Select(parameter =>
                { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return CSharpTypes.Rigid(parameter.Type, mapping); }).ToArray());
                return CSharpTypes.Rigid(property.Property.OriginalDefinition.Type, mapping);
            }
            if (operation is IFieldReferenceOperation field)
            {
                if (receiver.Type is INamedTypeSymbol { IsTupleType: true } tuple)
                {
                    for (var i = 0; i < tuple.TupleElements.Length; i++)
                    { if (tuple.TupleElements[i].Name == field.Field.Name) { return receiver.Value.Root.Arguments[i]; } }
                }
                return PatternSymbolType(field.Field, receiver.Value, receiver.Type);
            }
            throw new InferenceException("Unresolved conditional member binding.");
        }



        private void SetAsyncReturn(IMethodSymbol method)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            asyncReturn = null;
            asyncBuilder = null;
            asyncBuilderResult = null;
            builderUsesResult = false;
            if (!method.IsAsync)
            { return; }
            if (method.ReturnsVoid)
            { ConfigureBuilder(method, null); return; }
            if (method.ReturnType is not INamedTypeSymbol { Arity: <= 1 } named)
            { throw new InferenceException("Invalid async return type."); }
            asyncReturn = named;
            ConfigureBuilder(method, named);
        }

        private HmType WrapAsyncResult(HmType value)
        {
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (asyncReturn is null)
            { return value; }
            var declared = CSharpTypes.Rigid(asyncReturn);
            HmType result = declared;
            if (asyncReturn.Arity == 1)
            {
                var template = declared.Name == CSharpTypes.NullableReference ? declared.Arguments[0] : declared;
                var constructed = CSharpTypes.ReplaceOwnArguments(template, value);
                result = declared.Name == CSharpTypes.NullableReference ? new(CSharpTypes.NullableReference, constructed) : constructed;
            }
            BindBuilderReturn(result, value);
            return result;
        }
        private HmType VoidBody(BlockSyntax body)
        {
            if (body is null)
            { throw new ArgumentNullException(nameof(body)); }
            var flow = Scoped(() => Statements(body.Statements.ToArray()));
            if (flow.ReturnType is not null || flow.Breaks || flow.Continues)
            {
                throw new InferenceException("Invalid void return flow.");
            }
            return new("void");
        }

        private HmType Returned(ExpressionSyntax expression, ITypeSymbol? target = null)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var info = model.GetTypeInfo(expression, cancellationToken);
            var converted = target ?? info.ConvertedType;
            if (target is null)
            {
                foreach (var owner in expression.Ancestors())
                {
                    if (owner is AnonymousFunctionExpressionSyntax lambda)
                    {
                        if (model.GetOperation(lambda, cancellationToken) is IAnonymousFunctionOperation function)
                        {
                            converted = function.Symbol.IsAsync && function.Symbol.ReturnType is INamedTypeSymbol { Arity: 1 } task
                                ? task.TypeArguments[0] : function.Symbol.ReturnType;
                        }
                        break;
                    }
                    if (owner is LocalFunctionStatementSyntax local)
                    {
                        var localSymbol = model.GetDeclaredSymbol(local, cancellationToken);
                        converted = localSymbol is { IsAsync: true, ReturnType: INamedTypeSymbol { Arity: 1 } localTask }
                            ? localTask.TypeArguments[0] : localSymbol?.ReturnType ?? converted;
                        break;
                    }
                    if (owner is MethodDeclarationSyntax)
                    {
                        converted = asyncReturn is null ? currentMethod!.ReturnType : asyncReturn.Arity == 1 ? asyncReturn.TypeArguments[0] : model.Compilation.GetSpecialType(SpecialType.System_Void);
                        break;
                    }
                }
            }
            var joined = expression;
            while (joined is ParenthesizedExpressionSyntax parenthesized)
            { joined = parenthesized.Expression; }
            if (converted is not null && joined is ConditionalExpressionSyntax or SwitchExpressionSyntax
                && IsFixedLocalReturn(expression) && model.GetOperation(joined, cancellationToken) is not IConditionalOperation { IsRef: true })
            { return JoinedExpression(joined, converted, true); }
            if (expression is TupleExpressionSyntax tupleExpression && converted is INamedTypeSymbol { IsTupleType: true } targetTuple)
            {
                var items = new List<HmType>();
                for (var i = 0; i < tupleExpression.Arguments.Count; i++)
                {
                    items.Add(Returned(tupleExpression.Arguments[i].Expression, targetTuple.TupleElements[i].Type));
                }
                return new("Tuple", items.ToArray());
            }
            if (info.Type is null || converted is null)
            { return Expression(expression); }
            if (IsTargetedInterpolation(expression))
            {
                var inferred = Interpolation(expression).Root;
                if (converted.IsReferenceType && converted.NullableAnnotation == NullableAnnotation.Annotated && inferred.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableParameter))
                { return new(CSharpTypes.NullableReference, inferred); }
                return inferred;
            }
            if (info.Type is IDynamicTypeSymbol || converted is IDynamicTypeSymbol)
            {
                _ = Expression(expression);
                return CSharpTypes.Rigid(converted);
            }
            if (IsDynamicIdentityBoundary(info.Type, converted))
            { return DynamicIdentityShape(CSharpTypes.Rigid(info.Type), CSharpTypes.Rigid(converted), Expression(expression)); }
            var sameTuple = info.Type is INamedTypeSymbol { IsTupleType: true } sourceTuple && converted is INamedTypeSymbol { IsTupleType: true } declaredTuple
                && SymbolEqualityComparer.Default.Equals(sourceTuple.TupleUnderlyingType, declaredTuple.TupleUnderlyingType);
            if (SymbolEqualityComparer.Default.Equals(info.Type, converted) || sameTuple || model.ClassifyConversion(expression, converted).IsIdentity)
            {
                var inferred = Expression(expression).Root;
                if (info.Type.IsReferenceType && converted.NullableAnnotation == NullableAnnotation.Annotated && inferred.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableParameter))
                {
                    return new(CSharpTypes.NullableReference, inferred);
                }
                if (info.Type.IsReferenceType && converted.NullableAnnotation != NullableAnnotation.Annotated && inferred.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter)
                {
                    while (inferred.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter)
                    { inferred = inferred.Arguments[0].Root; }
                    return inferred;
                }
                if (IsFixedLocalReturn(expression))
                {
                    var fixedReturn = CSharpTypes.Rigid(converted);
                    BindDeclaredLocalReturn(inferred, fixedReturn, converted, false);
                    return fixedReturn;
                }
                return inferred;
            }
            if (converted is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                && SymbolEqualityComparer.Default.Equals(info.Type, nullable.TypeArguments[0]))
            {
                return new(CSharpTypes.NullableValue, Expression(expression));
            }
            var returnConversion = model.ClassifyConversion(expression, converted);
            if (returnConversion.IsUserDefined)
            {
                var targetShape = CSharpTypes.Erase(converted);
                if (TryUserConversion(info.Type, converted, Expression(expression), targetShape, returnConversion.MethodSymbol))
                { return targetShape; }
            }
            // Preserve widening, boxing and reference conversions at the return
            // boundary instead of narrowing the API to the expression's type.
            Anchor(expression);
            return CSharpTypes.Rigid(converted);
        }

        private bool IsFixedLocalReturn(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            SyntaxNode slot = expression;
            while (true)
            {
                if (slot.Parent is ParenthesizedExpressionSyntax parentheses)
                { slot = parentheses; }
                else if (slot.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax tuple })
                { slot = tuple; }
                else
                { break; }
            }
            var declaration = slot.Parent switch
            {
                ArrowExpressionClauseSyntax { Parent: LocalFunctionStatementSyntax local } => local,
                ReturnStatementSyntax or YieldStatementSyntax => slot.Ancestors().FirstOrDefault(owner =>
                {
                    if (owner is null)
                    { throw new ArgumentNullException(nameof(owner)); }
                    return owner is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or MethodDeclarationSyntax;
                }) as LocalFunctionStatementSyntax,
                _ => null
            };
            return declaration is not null && (iteratorReturn is null || slot.Parent is YieldStatementSyntax)
                && model.GetDeclaredSymbol(declaration, cancellationToken) is { RefKind: RefKind.None };
        }

        private HmType JoinedExpression(ExpressionSyntax expression, ITypeSymbol? target = null, bool fixedResult = false)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var info = model.GetTypeInfo(expression, cancellationToken);
            target ??= info.Type ?? info.ConvertedType ?? throw new InferenceException("Unresolved expression join.");
            var result = fixedResult ? CSharpTypes.Rigid(target) : HmType.Variable();
            HmType Arm(ExpressionSyntax value)
            {
                if (value is null)
                { throw new ArgumentNullException(nameof(value)); }
                var unwrapped = value;
                while (unwrapped is ParenthesizedExpressionSyntax parentheses)
                { unwrapped = parentheses.Expression; }
                var inferred = fixedResult && unwrapped is ConditionalExpressionSyntax or SwitchExpressionSyntax
                    ? JoinedExpression(unwrapped, target, true) : Returned(value, target);
                if (fixedResult)
                { BindDeclaredLocalReturn(inferred, result, target, false); }
                else
                { HmType.Unify(result, inferred); }
                return inferred;
            }
            if (expression is ConditionalExpressionSyntax conditional)
            {
                BooleanCondition(conditional.Condition);
                if (model.GetOperation(conditional, cancellationToken) is IConditionalOperation { IsRef: true })
                {
                    var referent = Expression(conditional.WhenTrue);
                    HmType.Unify(referent, Expression(conditional.WhenFalse));
                    return referent;
                }
                var first = Arm(conditional.WhenTrue);
                var second = Arm(conditional.WhenFalse);
                // Earlier conditionals need a native operand conversion; the declared
                // local result cannot supply their type through target typing.
                if (fixedResult && model.SyntaxTree.Options is CSharpParseOptions { LanguageVersion: < LanguageVersion.CSharp9 })
                {
                    var firstType = model.GetTypeInfo(conditional.WhenTrue, cancellationToken).Type;
                    var secondType = model.GetTypeInfo(conditional.WhenFalse, cancellationToken).Type;
                    var firstMatches = SymbolEqualityComparer.Default.Equals(firstType, target);
                    var secondMatches = SymbolEqualityComparer.Default.Equals(secondType, target);
                    if (firstMatches && secondMatches)
                    { RelateNaturalJoin(first, second, target, false); }
                    else if (firstMatches && secondType is not null)
                    { BindConvertedArgument(secondType, target, CSharpTypes.Rigid(secondType), first, false); }
                    else if (secondMatches && firstType is not null)
                    { BindConvertedArgument(firstType, target, CSharpTypes.Rigid(firstType), second, false); }
                }
            }
            else if (expression is SwitchExpressionSyntax switched)
            {
                var governing = Expression(switched.GoverningExpression);
                foreach (var arm in switched.Arms)
                {
                    Pattern(arm.Pattern, governing);
                    if (arm.WhenClause is not null)
                    { BooleanCondition(arm.WhenClause.Condition); }
                    Arm(arm.Expression);
                }
            }
            else
            { throw new InferenceException("Unsupported expression join."); }
            return result;
        }

        private void RelateNaturalJoin(HmType first, HmType second, ITypeSymbol declared, bool referenceOnly)
        {
            if (first is null)
            { throw new ArgumentNullException(nameof(first)); }
            if (second is null)
            { throw new ArgumentNullException(nameof(second)); }
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            first = ConversionCore(first);
            second = ConversionCore(second);
            if (!first.IsVariable && second.IsVariable)
            { (first, second) = (second, first); }
            if (first.IsVariable)
            {
                conversionBounds.Add((first, second, declared, declared, referenceOnly));
                return;
            }
            if (first.Name == second.Name && first.Arguments.Length == second.Arguments.Length)
            {
                if (declared is IArrayTypeSymbol array && first.Arguments.Length == 1)
                {
                    RelateNaturalJoin(first.Arguments[0], second.Arguments[0], array.ElementType, true);
                    return;
                }
                if (declared is INamedTypeSymbol { IsTupleType: true } tuple && !referenceOnly)
                {
                    for (var i = 0; i < tuple.TupleElements.Length; i++)
                    { RelateNaturalJoin(first.Arguments[i], second.Arguments[i], tuple.TupleElements[i].Type, false); }
                    return;
                }
                if (declared is INamedTypeSymbol { IsGenericType: true, IsTupleType: false } named)
                {
                    if (first.NestedTypeName is not null)
                    { HmType.Unify(first.Arguments[0], second.Arguments[0]); }
                    var left = CSharpTypes.OwnArguments(first);
                    var right = CSharpTypes.OwnArguments(second);
                    for (var i = 0; i < left.Length; i++)
                    {
                        if (named.OriginalDefinition.TypeParameters[i].Variance == VarianceKind.Out)
                        { RelateNaturalJoin(left[i], right[i], named.TypeArguments[i], true); }
                        else
                        { HmType.Unify(left[i], right[i]); }
                    }
                    return;
                }
            }
            HmType.Unify(first, second);
        }

        private HmType Awaited(AwaitExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var info = model.GetAwaitExpressionInfo(expression);
            if (info.IsDynamic || info.GetResultMethod is null)
            {
                throw new InferenceException("Dynamic or unresolved await is unsupported.");
            }
            var operandType = model.GetTypeInfo(expression.Expression, cancellationToken).Type;
            if (operandType is INamedTypeSymbol named && named.ContainingType is null && named.Arity == 1 &&
                ((named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" && named.Name is "Task" or "ValueTask")
                || (named.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices" && named.Name is "ConfiguredTaskAwaitable" or "ConfiguredValueTaskAwaitable")))
            {
                var value = HmType.Variable();
                var operand = Expression(expression.Expression).Root;
                if (operand.Name == CSharpTypes.NullableReference)
                { operand = operand.Arguments[0].Root; }
                HmType.Unify(operand, new(CSharpTypes.Rigid(named.WithNullableAnnotation(NullableAnnotation.NotAnnotated)).Name, value));
                if (asyncBuilder is not null && info.GetAwaiterMethod is not null)
                { BindBuilderAwaiter(info.GetAwaiterMethod.ReturnType, AwaitProtocolResult(named, operand, info.GetAwaiterMethod)); }
                return value;
            }
            var selected = info.GetAwaiterMethod;
            if (selected is not null && operandType is not null)
            {
                var operand = Expression(expression.Expression);
                try
                {
                    var awaiter = AwaitProtocolResult(operandType, operand, selected);
                    BindBuilderAwaiter(selected.ReturnType, awaiter);
                    HmType.Unify(PatternSymbolType(info.IsCompletedProperty, awaiter, selected.ReturnType), new HmType("bool"));
                    return AwaitProtocolResult(selected.ReturnType, awaiter, info.GetResultMethod);
                }
                catch (InferenceException)
                {
                    // A protocol can contain nested/ref-like types that never appear
                    // in the public signature. Preserve the concrete await in that case.
                    HmType.Unify(operand, CSharpTypes.Rigid(operandType));
                    return CSharpTypes.Rigid(info.GetResultMethod.ReturnType);
                }
            }
            Anchor(expression.Expression);
            return CSharpTypes.Rigid(info.GetResultMethod.ReturnType);
        }

        private bool SameAwaitMethod(IMethodSymbol? before, IMethodSymbol? after, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
        {
            if (changedMethod is null)
            { throw new ArgumentNullException(nameof(changedMethod)); }
            if (changedTree is null)
            { throw new ArgumentNullException(nameof(changedTree)); }
            if (before is not null && after is not null
                && SameMethodDefinition(before, currentMethod)
                && ImplementationMethod(after).DeclaringSyntaxReferences.Any(reference =>
                {
                    if (reference is null)
                    { throw new ArgumentNullException(nameof(reference)); }
                    return reference.SyntaxTree == changedTree && reference.Span.Start == changedMethod.SpanStart;
                }))
            { return SameCallSignature(before.ReducedFrom ?? before, after.ReducedFrom ?? after); }
            return ProtocolMember(before) == ProtocolMember(after);
        }
        private HmType AwaitProtocolResult(ITypeSymbol receiverType, HmType receiver, IMethodSymbol selected)
        {
            if (receiverType is null)
            { throw new ArgumentNullException(nameof(receiverType)); }
            if (receiver is null)
            { throw new ArgumentNullException(nameof(receiver)); }
            if (selected is null)
            { throw new ArgumentNullException(nameof(selected)); }
            var full = selected.ReducedFrom ?? selected;
            var definition = full.OriginalDefinition;
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            if (full.IsStatic)
            {
                if (!full.IsExtensionMethod || definition.Parameters.Length != 1)
                { throw new InferenceException("Unsupported await extension signature."); }
                foreach (var parameter in definition.TypeParameters)
                { mapping.Add(parameter, HmType.Variable()); }
                var expected = CSharpTypes.Rigid(definition.Parameters[0].Type, mapping);
                var projected = definition.Parameters[0].Type is INamedTypeSymbol owner
                    ? ProjectDeconstructionReceiver(receiverType, receiver, owner) : receiver;
                if (projected is null)
                { HmType.Unify(receiver, CSharpTypes.Rigid(receiverType)); projected = CSharpTypes.Rigid(full.Parameters[0].Type); }
                UnifyDeconstructionReceiver(expected, projected);
                foreach (var parameter in definition.TypeParameters)
                { ImportConstraints(parameter, mapping[parameter], mapping, full.TypeArguments[parameter.Ordinal]); }
            }
            else
            {
                var projected = ProjectDeconstructionReceiver(receiverType, receiver, full.ContainingType);
                if (projected is null)
                { HmType.Unify(receiver, CSharpTypes.Rigid(receiverType)); return CSharpTypes.Rigid(selected.ReturnType); }
                if (!CSharpTypes.MapNamedArguments(full.ContainingType, projected, mapping))
                { throw new InferenceException("Invalid awaiter owner shape."); }
            }
            return CSharpTypes.Rigid(definition.ReturnType, mapping);
        }
        private void BindNullableArgument(IOperation value, HmType expected)
        {
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            if (value.Syntax is ExpressionSyntax interpolation && IsTargetedInterpolation(interpolation))
            { Interpolation(interpolation, expected); return; }
            var inferred = BoundExpression(value).Root;
            expected = expected.Root;
            if (TryBindDynamicArgument(value, expected, inferred))
            { return; }
            if (TryBindArgumentConversion(value, expected, inferred))
            { return; }
            if (expected.Name == CSharpTypes.NullableParameter)
            {
                // A T? formal accepts an unannotated actual without making that
                // actual nullable. The result scheme still retains its annotation.
                UnifyDeconstructionReceiver(expected.Arguments[0], inferred);
            }
            else if (expected.Name == CSharpTypes.NullableReference && inferred.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableParameter))
            {
                UnifyDeconstructionReceiver(expected.Arguments[0], inferred);
            }
            else if (!expected.IsVariable && inferred.Name == CSharpTypes.NullableReference && expected.Name != CSharpTypes.NullableReference)
            {
                HmType.Unify(inferred.Arguments[0], expected);
            }
            else if (expected.Name == CSharpTypes.NullableValue && value.Syntax is ExpressionSyntax expression
                && model.GetTypeInfo(expression, cancellationToken).Type is ITypeSymbol source && value.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                && SymbolEqualityComparer.Default.Equals(source, nullable.TypeArguments[0]))
            {
                HmType.Unify(inferred, expected.Arguments[0]);
            }
            else
            {
                HmType.Unify(inferred, expected);
            }
        }

        private HmType Coalesced(BinaryExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (model.GetOperation(expression, cancellationToken) is not ICoalesceOperation operation)
            {
                throw new InferenceException("Unresolved coalescing expression.");
            }
            var left = Expression(expression.Left).Root;
            while (left.Name == CSharpTypes.NullableParameter && left.Arguments[0].Root.Name is CSharpTypes.NullableReference or CSharpTypes.NullableValue or CSharpTypes.NullableParameter)
            { left = left.Arguments[0].Root; }
            if (!operation.ValueConversion.IsIdentity || left.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableValue or CSharpTypes.NullableParameter))
            {
                Anchor(expression.Left);
                Anchor(expression.Right);
                return CSharpTypes.Rigid(operation.Type!);
            }
            var right = Expression(expression.Right).Root;
            if (right.Name == left.Name)
            {
                HmType.Unify(left, right);
                return left;
            }
            HmType.Unify(left.Arguments[0], right);
            return right;
        }

        private HmType CoalesceAssigned(AssignmentExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var target = AssignmentTarget(expression.Left).Root;
            while (target.Name == CSharpTypes.NullableParameter && target.Arguments[0].Root.Name is CSharpTypes.NullableReference or CSharpTypes.NullableValue or CSharpTypes.NullableParameter)
            { target = target.Arguments[0].Root; }
            var targetType = model.GetTypeInfo(expression.Left, cancellationToken).Type!;
            var sourceType = model.GetTypeInfo(expression.Right, cancellationToken).Type;
            var resultType = model.GetTypeInfo(expression, cancellationToken).Type!;
            var underlying = targetType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                ? nullable.TypeArguments[0] : targetType;
            if (target.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableValue) || sourceType is null
                || (!SymbolEqualityComparer.Default.Equals(sourceType, targetType) && !SymbolEqualityComparer.Default.Equals(sourceType, underlying)))
            {
                Anchor(expression.Left);
                Anchor(expression.Right);
                return CSharpTypes.Rigid(resultType);
            }
            var right = Expression(expression.Right).Root;
            HmType.Unify(target, right.Name == target.Name ? right : new(target.Name, right));
            if (target.Name == CSharpTypes.NullableValue)
            {
                return resultType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } ? target : target.Arguments[0];
            }
            return resultType.NullableAnnotation == NullableAnnotation.Annotated ? target : target.Arguments[0];
        }
        private HmType ConditionalAccess(ConditionalAccessExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (expression.WhenNotNull is ElementBindingExpressionSyntax binding
                && model.GetTypeInfo(expression.Expression, cancellationToken).Type is IArrayTypeSymbol array
                && model.GetOperation(binding, cancellationToken) is IArrayElementReferenceOperation arrayAccess
                && SymbolEqualityComparer.Default.Equals(array.ElementType, arrayAccess.Type))
            {
                var element = HmType.Variable();
                var receiver = Expression(expression.Expression).Root;
                if (receiver.Name == CSharpTypes.NullableReference)
                { receiver = receiver.Arguments[0]; }
                HmType.Unify(receiver, new("[" + new string(',', array.Rank - 1) + "]", element));
                foreach (var argument in binding.ArgumentList.Arguments)
                { Anchor(argument.Expression); }
                var result = model.GetTypeInfo(expression, cancellationToken).Type!;
                if (result is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                    && array.ElementType is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                {
                    // The lifted temporary can disappear into ?? or another operation;
                    // retain its value-type requirement even when absent from the API.
                    RequireKeyword(element, "struct");
                    return new(CSharpTypes.NullableValue, element);
                }
                if (result.IsReferenceType)
                {
                    if (array.ElementType.NullableAnnotation == NullableAnnotation.Annotated)
                    { HmType.Unify(element, CSharpTypes.Erase(array.ElementType)); return element; }
                    RequireKeyword(element, "class");
                    return new(CSharpTypes.NullableReference, element);
                }
                return element;
            }
            var original = model.GetTypeInfo(expression.Expression, cancellationToken).Type
                ?? throw new InferenceException("Untyped conditional receiver.");
            var value = Expression(expression.Expression).Root;
            if (original is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                original = nullable.TypeArguments[0];
                if (value.Name != CSharpTypes.NullableValue)
                { throw new InferenceException("Invalid nullable receiver shape."); }
                value = value.Arguments[0];
            }
            else
            { value = ConversionCore(value); }
            HmType bound;
            conditionalReceivers.Push((original, value));
            try
            { bound = Expression(expression.WhenNotNull); }
            finally { conditionalReceivers.Pop(); }
            var resultType = model.GetTypeInfo(expression, cancellationToken).Type
                ?? throw new InferenceException("Untyped conditional access.");
            var boundType = model.GetTypeInfo(expression.WhenNotNull, cancellationToken).Type
                ?? throw new InferenceException("Untyped conditional member.");
            if (resultType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                && boundType is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            {
                RequireKeyword(bound, "struct");
                return new(CSharpTypes.NullableValue, bound);
            }
            if (boundType.IsReferenceType)
            {
                // Conditional access can produce null even when contextual type
                // info carries a non-annotated symbol (for example inside ??).
                var reference = ConversionCore(bound);
                if (reference.IsVariable)
                { RequireKeyword(reference, "class"); }
                return new(CSharpTypes.NullableReference, reference);
            }
            return bound;
        }

        private void ImportNullableConstraints(HmType[] parameters, HmType result)
        {
            if (parameters is null)
            { throw new ArgumentNullException(nameof(parameters)); }
            if (result is null)
            { throw new ArgumentNullException(nameof(result)); }
            var visited = new HashSet<HmType>();
            void Visit(HmType type)
            {
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                type = type.Root;
                if (!visited.Add(type))
                { return; }
                if (type.Name is CSharpTypes.NullableReference or CSharpTypes.NullableValue)
                {
                    var element = type.Arguments[0].Root;
                    while (element.Name == CSharpTypes.NullableParameter)
                    { element = element.Arguments[0].Root; }
                    if (element.IsVariable)
                    { RequireKeyword(element, type.Name == CSharpTypes.NullableReference ? "class" : "struct"); }
                }
                foreach (var argument in type.Arguments)
                { Visit(argument); }
            }
            foreach (var parameter in parameters)
            { Visit(parameter); }
            Visit(result);
        }

        private string CompilerWarningKey(Diagnostic warning, Compilation compilation)
        {
            if (warning is null)
            { throw new ArgumentNullException(nameof(warning)); }
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            var treeOrdinal = 0;
            foreach (var candidate in compilation.SyntaxTrees)
            {
                if (ReferenceEquals(candidate, warning.Location.SourceTree))
                { break; }
                treeOrdinal++;
            }
            var prefix = warning.Id + ":" + treeOrdinal + ":" + warning.Location.SourceTree?.FilePath + ":";
            if (warning.Location.IsInSource && warning.Location.SourceTree is SyntaxTree tree)
            {
                var root = tree.GetRoot(cancellationToken);
                var span = warning.Location.SourceSpan;
                var node = root.FindNode(span, getInnermostNodeForTie: true);
                foreach (var owner in node.AncestorsAndSelf())
                {
                    if (owner is not MethodDeclarationSyntax method)
                    { continue; }
                    if (warning.Id == "CS8826")
                    {
                        // The compiler embeds both complete partial signatures in this
                        // warning. Generic substitution changes that text even when
                        // the original per-part differences have been preserved.
                        var ordinal = 0;
                        foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                        {
                            if (declaration.SpanStart == method.SpanStart)
                            { return prefix + "partial-signature:" + ordinal; }
                            ordinal++;
                        }
                    }

                    SyntaxNode? body = method.Body is not null ? method.Body : method.ExpressionBody?.Expression;
                    if (body is not null && body.FullSpan.Contains(span))
                    {
                        var ordinal = 0;
                        foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                        {
                            if (declaration.SpanStart == method.SpanStart)
                            { return prefix + "body:" + ordinal + ":" + (span.Start - body.FullSpan.Start) + ":" + span.Length; }
                            ordinal++;
                        }
                    }
                    break;
                }
            }
            return prefix + warning.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        }

        private bool IntroducesCompilerWarnings(Compilation changed, IEnumerable<Diagnostic>? originalDiagnostics = null, IEnumerable<Diagnostic>? changedDiagnostics = null)
        {
            if (changed is null)
            { throw new ArgumentNullException(nameof(changed)); }
            var existing = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var warning in originalDiagnostics ?? model.Compilation.GetDiagnostics(cancellationToken))
            {
                if (warning.Severity == DiagnosticSeverity.Warning || warning.Severity == DiagnosticSeverity.Error && warning.DefaultSeverity == DiagnosticSeverity.Hidden)
                {
                    var key = CompilerWarningKey(warning, model.Compilation);
                    existing.TryGetValue(key, out var count);
                    existing[key] = count + 1;
                }
            }
            foreach (var warning in changedDiagnostics ?? changed.GetDiagnostics(cancellationToken))
            {
                if (warning.Severity == DiagnosticSeverity.Warning || warning.Severity == DiagnosticSeverity.Error && warning.DefaultSeverity == DiagnosticSeverity.Hidden)
                {
                    var key = CompilerWarningKey(warning, changed);
                    if (!existing.TryGetValue(key, out var count) || count == 0)
                    { return true; }
                    existing[key] = count - 1;
                }
            }
            return false;
        }
    }
}
