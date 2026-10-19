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
                return inferred;
            }
            if (converted is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                && SymbolEqualityComparer.Default.Equals(info.Type, nullable.TypeArguments[0]))
            {
                return new(CSharpTypes.NullableValue, Expression(expression));
            }
            // Preserve widening, boxing and reference conversions at the return
            // boundary instead of narrowing the API to the expression's type.
            Anchor(expression);
            return CSharpTypes.Rigid(converted);
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
                && SymbolEqualityComparer.Default.Equals((before.ReducedFrom ?? before).OriginalDefinition, currentMethod?.OriginalDefinition)
                && after.DeclaringSyntaxReferences.Any(reference =>
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
            var inferred = BoundExpression(value).Root;
            expected = expected.Root;
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
                && model.GetOperation(binding, cancellationToken) is IArrayElementReferenceOperation)
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
                if (result.IsReferenceType && result.NullableAnnotation == NullableAnnotation.Annotated)
                {
                    if (array.ElementType.NullableAnnotation == NullableAnnotation.Annotated)
                    { HmType.Unify(element, CSharpTypes.Erase(array.ElementType)); return element; }
                    RequireKeyword(element, "class");
                    return new(CSharpTypes.NullableReference, element);
                }
                return element;
            }
            Anchor(expression.Expression);
            foreach (var argument in expression.WhenNotNull.DescendantNodesAndSelf().OfType<ArgumentSyntax>())
            {
                Anchor(argument.Expression);
            }
            return CSharpTypes.Rigid(model.GetTypeInfo(expression, cancellationToken).Type ?? throw new InferenceException("Untyped conditional access."));
        }

        private List<TypeParameterConstraintClauseSyntax> NullableConstraints(HmType[] parameters, HmType result, Dictionary<HmType, string> variables, bool nullableAnnotations)
        {
            if (parameters is null)
            { throw new ArgumentNullException(nameof(parameters)); }
            if (result is null)
            { throw new ArgumentNullException(nameof(result)); }
            if (variables is null)
            { throw new ArgumentNullException(nameof(variables)); }
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
            return BuildConstraints(variables, nullableAnnotations);
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
                if (warning.Severity == DiagnosticSeverity.Warning)
                {
                    var key = CompilerWarningKey(warning, model.Compilation);
                    existing.TryGetValue(key, out var count);
                    existing[key] = count + 1;
                }
            }
            foreach (var warning in changedDiagnostics ?? changed.GetDiagnostics(cancellationToken))
            {
                if (warning.Severity == DiagnosticSeverity.Warning)
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
