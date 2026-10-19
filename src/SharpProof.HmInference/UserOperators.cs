using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private static Dictionary<ITypeParameterSymbol, HmType> OperatorMapping(IMethodSymbol method, IEnumerable<(ITypeSymbol Type, HmType Shape)> values)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            if (values is null)
            { throw new ArgumentNullException(nameof(values)); }
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            foreach (var value in values)
            {
                var original = value.Type;
                var shape = ConversionCore(value.Shape);
                if (original is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } optional)
                { original = optional.TypeArguments[0]; if (shape.Name == CSharpTypes.NullableValue) { shape = shape.Arguments[0].Root; } }
                if (original is not INamedTypeSymbol named)
                { continue; }
                var matches = false;
                for (var ancestor = named; ancestor is not null; ancestor = ancestor.BaseType)
                { matches |= SymbolEqualityComparer.Default.Equals(ancestor.OriginalDefinition, method.ContainingType.OriginalDefinition); }
                matches |= named.AllInterfaces.Any(implemented => { if (implemented is null) { throw new ArgumentNullException(nameof(implemented)); } return SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, method.ContainingType.OriginalDefinition); });
                if (!matches)
                { continue; }
                var projected = ProjectDeconstructionReceiver(original, shape, method.ContainingType);
                if (projected is null)
                { continue; }
                var candidate = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
                if (!CSharpTypes.MapNamedArguments(method.ContainingType, projected, candidate))
                { continue; }
                foreach (var pair in candidate)
                {
                    if (mapping.TryGetValue(pair.Key, out var existing))
                    { HmType.Unify(existing, pair.Value); }
                    else
                    { mapping.Add(pair.Key, pair.Value); }
                }
            }
            for (var owner = method.ContainingType; owner is not null; owner = owner.ContainingType)
            {
                for (var i = 0; i < owner.Arity; i++)
                { if (!mapping.ContainsKey(owner.OriginalDefinition.TypeParameters[i])) { mapping.Add(owner.OriginalDefinition.TypeParameters[i], CSharpTypes.Rigid(owner.TypeArguments[i])); } }
            }
            return mapping;
        }

        private static (ITypeSymbol Type, HmType Shape) LiftedOperand(ITypeSymbol actual, ITypeSymbol parameter, HmType shape)
        {
            if (actual is null)
            { throw new ArgumentNullException(nameof(actual)); }
            if (parameter is null)
            { throw new ArgumentNullException(nameof(parameter)); }
            if (shape is null)
            { throw new ArgumentNullException(nameof(shape)); }
            if (actual is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                && parameter is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            {
                var inner = HmType.Variable();
                HmType.Unify(shape, new(CSharpTypes.NullableValue, inner));
                return (nullable.TypeArguments[0], inner);
            }
            return (actual, shape);
        }

        private static bool IsContextualOperatorOperand(IOperation operand)
        {
            if (operand is null)
            { throw new ArgumentNullException(nameof(operand)); }
            var syntax = operand.Syntax;
            while (syntax is ParenthesizedExpressionSyntax parentheses)
            { syntax = parentheses.Expression; }
            return syntax is LiteralExpressionSyntax literal && literal.Kind() is SyntaxKind.NullLiteralExpression or SyntaxKind.DefaultLiteralExpression;
        }

        private HmType UserOperator(IMethodSymbol method, IOperation[] operands, ITypeSymbol resultType, bool lifted)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            if (operands is null)
            { throw new ArgumentNullException(nameof(operands)); }
            if (resultType is null)
            { throw new ArgumentNullException(nameof(resultType)); }
            var values = operands.Select(operand => { if (operand is null) { throw new ArgumentNullException(nameof(operand)); } return IsContextualOperatorOperand(operand) ? CSharpTypes.Erase(operand.Type!) : BoundExpression(operand); }).ToArray();
            var mapping = OperatorMapping(method, operands.Select((operand, i) =>
            { if (operand is null) { throw new ArgumentNullException(nameof(operand)); } if (i < 0) { throw new ArgumentOutOfRangeException(nameof(i)); } return (model.GetTypeInfo(operand.Syntax, cancellationToken).Type ?? operand.Type!, values[i]); }));
            for (var i = 0; i < operands.Length; i++)
            {
                var expected = CSharpTypes.Rigid(method.OriginalDefinition.Parameters[i].Type, mapping);
                var actual = operands[i].Type ?? method.Parameters[i].Type;
                if (lifted && actual is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                { expected = new(CSharpTypes.NullableValue, expected); }
                if (IsContextualOperatorOperand(operands[i]))
                { HmType.Unify(values[i], expected); }
                else
                { BindNullableArgument(operands[i], expected); }
            }
            var result = CSharpTypes.Rigid(method.OriginalDefinition.ReturnType, mapping);
            if (lifted && resultType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                && method.ReturnType is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            { result = new(CSharpTypes.NullableValue, result); }
            return result;
        }

        private bool TryUserConversion(ITypeSymbol source, ITypeSymbol target, HmType value, HmType expected, IMethodSymbol? method)
        {
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            if (method is null || method.Parameters.Length != 1)
            { return false; }
            var input = LiftedOperand(source, method.Parameters[0].Type, value);
            var output = LiftedOperand(target, method.ReturnType, expected);
            var mapping = OperatorMapping(method, [(input.Type, input.Shape), (output.Type, output.Shape)]);
            var parameter = CSharpTypes.Rigid(method.OriginalDefinition.Parameters[0].Type, mapping);
            var result = CSharpTypes.Rigid(method.OriginalDefinition.ReturnType, mapping);
            BindConvertedArgument(input.Type, method.Parameters[0].Type, input.Shape, parameter, false);
            BindConvertedArgument(method.ReturnType, output.Type, result, output.Shape, false);
            return true;
        }

        private static string? UserOperationKey(IOperation operation)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            return operation switch
            {
                IBinaryOperation { OperatorMethod: not null } binary => "binary:" + binary.IsLifted + ":" + binary.IsChecked + ":" + ProtocolMember(binary.OperatorMethod),
                IUnaryOperation { OperatorMethod: not null } unary => "unary:" + unary.IsLifted + ":" + unary.IsChecked + ":" + ProtocolMember(unary.OperatorMethod),
                ICompoundAssignmentOperation { OperatorMethod: not null } compound => "compound:" + compound.IsLifted + ":" + compound.IsChecked + ":" + ProtocolMember(compound.OperatorMethod) + ":in:" + ProtocolMember(compound.InConversion.MethodSymbol) + ":out:" + ProtocolMember(compound.OutConversion.MethodSymbol),
                IIncrementOrDecrementOperation { OperatorMethod: not null } increment => "increment:" + increment.IsLifted + ":" + increment.IsChecked + ":" + ProtocolMember(increment.OperatorMethod),
                IConversionOperation { OperatorMethod: not null } conversion => "conversion:" + conversion.IsChecked + ":" + ProtocolMember(conversion.OperatorMethod),
                _ => null
            };
        }

        private static IEnumerable<string> UserOperationKeys(SyntaxNode root, SemanticModel semantic)
        {
            if (root is null)
            { throw new ArgumentNullException(nameof(root)); }
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            var roots = new List<IOperation>();
            foreach (var node in root.DescendantNodesAndSelf())
            { if (semantic.GetOperation(node) is { Parent: null } operation) { roots.Add(operation); } }
            var pending = new Stack<IOperation>(roots.AsEnumerable().Reverse());
            while (pending.Count != 0)
            {
                var operation = pending.Pop();
                if (UserOperationKey(operation) is { } key)
                { yield return key; }
                foreach (var child in operation.ChildOperations.Reverse())
                { pending.Push(child); }
            }
        }

        private static bool PreservesUserOperations(SyntaxNode before, SyntaxNode after, SemanticModel beforeModel, SemanticModel afterModel)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (beforeModel is null)
            { throw new ArgumentNullException(nameof(beforeModel)); }
            if (afterModel is null)
            { throw new ArgumentNullException(nameof(afterModel)); }
            return UserOperationKeys(before, beforeModel).SequenceEqual(UserOperationKeys(after, afterModel));
        }
    }
}
