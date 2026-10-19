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
            if (!method.IsAsync)
            { return; }
            if (method.ReturnType is not INamedTypeSymbol named || named.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks"
                || named.Name is not ("Task" or "ValueTask") || named.Arity > 1)
            {
                throw new InferenceException("Custom async return builders and async iterators are unsupported.");
            }
            asyncReturn = named;
        }

        private HmType WrapAsyncResult(HmType value)
        {
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (asyncReturn is null)
            { return value; }
            var declared = CSharpTypes.Rigid(asyncReturn);
            if (asyncReturn.Arity == 0)
            { return declared; }
            return declared.Name == CSharpTypes.NullableReference
                ? new(CSharpTypes.NullableReference, new HmType(declared.Arguments[0].Name, value))
                : new(declared.Name, value);
        }

        private HmType AsyncBody(BlockSyntax body)
        {
            if (body is null)
            { throw new ArgumentNullException(nameof(body)); }
            var flow = Scoped(() => Statements(body.Statements.ToArray()));
            if (flow.ReturnType is not null || flow.Breaks || flow.Continues)
            {
                throw new InferenceException("Invalid non-generic async return flow.");
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
                    if (owner is LambdaExpressionSyntax lambda)
                    {
                        converted = (model.GetTypeInfo(lambda, cancellationToken).ConvertedType as INamedTypeSymbol)?.DelegateInvokeMethod?.ReturnType ?? converted;
                        break;
                    }
                    if (owner is LocalFunctionStatementSyntax local)
                    {
                        converted = model.GetDeclaredSymbol(local, cancellationToken)?.ReturnType ?? converted;
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
                if (info.Type.IsReferenceType && converted.NullableAnnotation == NullableAnnotation.Annotated && inferred.Name != CSharpTypes.NullableReference)
                {
                    return new(CSharpTypes.NullableReference, inferred);
                }
                if (info.Type.IsReferenceType && converted.NullableAnnotation != NullableAnnotation.Annotated && inferred.Name == CSharpTypes.NullableReference)
                {
                    return inferred.Arguments[0];
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
            if (operandType is INamedTypeSymbol named && named.Arity == 1 &&
                ((named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" && named.Name is "Task" or "ValueTask")
                || (named.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices" && named.Name is "ConfiguredTaskAwaitable" or "ConfiguredValueTaskAwaitable")))
            {
                var value = HmType.Variable();
                var operand = Expression(expression.Expression).Root;
                if (operand.Name == CSharpTypes.NullableReference)
                { operand = operand.Arguments[0].Root; }
                HmType.Unify(operand, new(CSharpTypes.Rigid(named.WithNullableAnnotation(NullableAnnotation.NotAnnotated)).Name, value));
                return value;
            }
            // Non-generic framework awaitables and custom awaiters retain their
            // operand types. Their result still participates in surrounding flow.
            Anchor(expression.Expression);
            return CSharpTypes.Rigid(info.GetResultMethod.ReturnType);
        }

        private void BindNullableArgument(IOperation value, HmType expected)
        {
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            var inferred = BoundExpression(value).Root;
            expected = expected.Root;
            if (expected.Name == CSharpTypes.NullableReference && inferred.Name != CSharpTypes.NullableReference)
            {
                HmType.Unify(inferred, expected.Arguments[0]);
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
            if (!operation.ValueConversion.IsIdentity || left.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableValue))
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

        private HmType ConditionalAccess(ConditionalAccessExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            Anchor(expression.Expression);
            foreach (var argument in expression.WhenNotNull.DescendantNodesAndSelf().OfType<ArgumentSyntax>())
            {
                Anchor(argument.Expression);
            }
            return CSharpTypes.Rigid(model.GetTypeInfo(expression, cancellationToken).Type ?? throw new InferenceException("Untyped conditional access."));
        }

        private static List<TypeParameterConstraintClauseSyntax> NullableConstraints(HmType[] parameters, HmType result, Dictionary<HmType, string> variables)
        {
            if (parameters is null)
            { throw new ArgumentNullException(nameof(parameters)); }
            if (result is null)
            { throw new ArgumentNullException(nameof(result)); }
            if (variables is null)
            { throw new ArgumentNullException(nameof(variables)); }
            var requirements = new Dictionary<HmType, string>();
            var visited = new HashSet<HmType>();
            void Visit(HmType type)
            {
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                type = type.Root;
                if (!visited.Add(type))
                { return; }
                if (type.Name is CSharpTypes.NullableReference or CSharpTypes.NullableValue && type.Arguments[0].Root.IsVariable)
                {
                    var variable = type.Arguments[0].Root;
                    var constraint = type.Name == CSharpTypes.NullableReference ? "class" : "struct";
                    if (requirements.TryGetValue(variable, out var existing) && existing != constraint)
                    {
                        throw new InferenceException("Conflicting nullable reference/value constraints.");
                    }
                    requirements[variable] = constraint;
                }
                foreach (var argument in type.Arguments)
                { Visit(argument); }
            }
            foreach (var parameter in parameters)
            { Visit(parameter); }
            Visit(result);
            var clauses = new List<TypeParameterConstraintClauseSyntax>();
            foreach (var variable in variables)
            {
                if (requirements.TryGetValue(variable.Key, out var constraint))
                {
                    var shape = (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration($"void C<{variable.Value}>() where {variable.Value} : {constraint} {{}}")!;
                    clauses.Add(shape.ConstraintClauses[0]);
                }
            }
            return clauses;
        }

        private bool IntroducesCompilerWarnings(Compilation changed)
        {
            if (changed is null)
            { throw new ArgumentNullException(nameof(changed)); }
            var existing = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var warning in model.Compilation.GetDiagnostics(cancellationToken))
            {
                if (warning.Severity == DiagnosticSeverity.Warning)
                {
                    var key = warning.Id + ":" + warning.Location.SourceTree?.FilePath + ":" + warning.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
                    existing.TryGetValue(key, out var count);
                    existing[key] = count + 1;
                }
            }
            foreach (var warning in changed.GetDiagnostics(cancellationToken))
            {
                if (warning.Severity == DiagnosticSeverity.Warning)
                {
                    var key = warning.Id + ":" + warning.Location.SourceTree?.FilePath + ":" + warning.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
                    if (!existing.TryGetValue(key, out var count) || count == 0)
                    { return true; }
                    existing[key] = count - 1;
                }
            }
            return false;
        }
    }
}
