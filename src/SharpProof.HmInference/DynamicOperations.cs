using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private HmType? DynamicExpression(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var operation = model.GetOperation(expression, cancellationToken);
            if (!IsDynamicOperation(operation))
            { return null; }
            foreach (var child in operation!.ChildOperations)
            { AnchorDynamicOperand(child); }
            return CSharpTypes.Rigid(model.GetTypeInfo(expression, cancellationToken).Type ?? operation.Type
                ?? throw new InferenceException("Unresolved dynamic expression type."));
        }

        private static bool IsDynamicOperation(IOperation? operation)
        {
            return operation switch
            {
                IDynamicInvocationOperation or IDynamicMemberReferenceOperation or IDynamicIndexerAccessOperation or IDynamicObjectCreationOperation => true,
                IBinaryOperation binary => binary.LeftOperand.Type is IDynamicTypeSymbol || binary.RightOperand.Type is IDynamicTypeSymbol,
                IUnaryOperation unary => unary.Operand.Type is IDynamicTypeSymbol,
                IIncrementOrDecrementOperation increment => increment.Target.Type is IDynamicTypeSymbol,
                ICompoundAssignmentOperation compound => compound.Target.Type is IDynamicTypeSymbol || compound.Value.Type is IDynamicTypeSymbol,
                ISimpleAssignmentOperation assignment => assignment.Target.Type is IDynamicTypeSymbol || assignment.Value.Type is IDynamicTypeSymbol,
                ICoalesceAssignmentOperation assignment => assignment.Target.Type is IDynamicTypeSymbol || assignment.Value.Type is IDynamicTypeSymbol,
                ICoalesceOperation coalesce => coalesce.Value.Type is IDynamicTypeSymbol || coalesce.WhenNull.Type is IDynamicTypeSymbol,
                IConditionalOperation conditional => conditional.Type is IDynamicTypeSymbol,
                IConditionalAccessOperation conditional => conditional.Operation.Type is IDynamicTypeSymbol,
                IAwaitOperation awaited => awaited.Operation.Type is IDynamicTypeSymbol,
                IConversionOperation conversion => conversion.Operand.Type is IDynamicTypeSymbol || conversion.Type is IDynamicTypeSymbol,
                _ => false
            };
        }

        private IEnumerable<(ExpressionSyntax Syntax, IOperation Operation)> DynamicSites(SyntaxNode root, SemanticModel semantic)
        {
            if (root is null)
            { throw new ArgumentNullException(nameof(root)); }
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            foreach (var expression in root.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (semantic.GetOperation(expression, cancellationToken) is { } operation && IsDynamicOperation(operation))
                { yield return (expression, operation); }
            }
        }

        private bool PreservesDynamicBindings(SyntaxNode before, SyntaxNode after, SemanticModel beforeModel, SemanticModel afterModel)
        {
            var originals = DynamicSites(before, beforeModel).ToArray();
            var candidates = DynamicSites(after, afterModel).ToArray();
            if (originals.Length != candidates.Length)
            { return false; }
            for (var i = 0; i < originals.Length; i++)
            {
                var original = originals[i];
                var candidate = candidates[i];
                if (original.Operation.Kind != candidate.Operation.Kind)
                { return false; }
                var originalChildren = original.Operation.ChildOperations.ToArray();
                var candidateChildren = candidate.Operation.ChildOperations.ToArray();
                if (originalChildren.Length != candidateChildren.Length)
                { return false; }
                for (var j = 0; j < originalChildren.Length; j++)
                {
                    var originalType = originalChildren[j].Type;
                    var candidateType = candidateChildren[j].Type;
                    if ((originalType is null) != (candidateType is null)
                        || originalType is not null && !CSharpTypes.SameSignatureType(originalType, candidateType!))
                    { return false; }
                }
                if (original.Operation is IDynamicMemberReferenceOperation originalMember
                    && candidate.Operation is IDynamicMemberReferenceOperation candidateMember)
                {
                    if (originalMember.TypeArguments.Length != candidateMember.TypeArguments.Length)
                    { return false; }
                    for (var j = 0; j < originalMember.TypeArguments.Length; j++)
                    { if (!CSharpTypes.SameSignatureType(originalMember.TypeArguments[j], candidateMember.TypeArguments[j])) { return false; } }
                }
                if (original.Syntax is InvocationExpressionSyntax originalInvocation && candidate.Syntax is InvocationExpressionSyntax candidateInvocation)
                {
                    var originalGroup = beforeModel.GetMemberGroup(originalInvocation.Expression, cancellationToken).OfType<IMethodSymbol>().ToArray();
                    var candidateGroup = afterModel.GetMemberGroup(candidateInvocation.Expression, cancellationToken).OfType<IMethodSymbol>().ToArray();
                    if (originalGroup.Length != candidateGroup.Length)
                    { return false; }
                    foreach (var method in originalGroup)
                    {
                        if (!candidateGroup.Any(changed =>
                        {
                            if (changed is null)
                            { throw new ArgumentNullException(nameof(changed)); }
                            return method.Arity == changed.Arity && ProtocolMember(method.OriginalDefinition) == ProtocolMember(changed.OriginalDefinition)
                                && SameCallSignature(method, changed);
                        }))
                        { return false; }
                    }
                }
            }
            return true;
        }


        private bool TryBindDynamicArgument(IOperation operation, HmType expected, HmType inferred)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (!ContainsDynamic(expected) || operation.Syntax is not ExpressionSyntax expression
                || model.GetTypeInfo(expression, cancellationToken).Type is not ITypeSymbol source || source is IDynamicTypeSymbol)
            { return false; }
            UnifyDeconstructionReceiver(CSharpTypes.Rigid(source), inferred);
            return true;
        }

        private static bool ContainsDynamic(HmType shape)
        {
            if (shape is null)
            { throw new ArgumentNullException(nameof(shape)); }
            shape = shape.Root;
            return shape.Name == "dynamic" || shape.Arguments.Any(argument =>
            {
                if (argument is null)
                { throw new ArgumentNullException(nameof(argument)); }
                return ContainsDynamic(argument);
            });
        }


        private void AnchorDynamicOperand(IOperation operation)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (operation is IConditionalAccessInstanceOperation)
            {
                if (conditionalReceivers.Count != 0)
                {
                    var receiver = ConditionalReceiver();
                    UnifyDeconstructionReceiver(CSharpTypes.Rigid(receiver.Type), receiver.Value);
                }
                return;
            }
            if (operation is IInstanceReferenceOperation)
            { return; }
            if (operation.Syntax is DeclarationExpressionSyntax declaration && operation.Type is ITypeSymbol declared)
            {
                BindDesignation(declaration.Designation, CSharpTypes.Rigid(declared), declared);
                return;
            }
            if (operation is IConversionOperation conversion)
            { AnchorDynamicOperand(conversion.Operand); return; }
            if (operation.Type is not null && operation.Syntax is ExpressionSyntax expression
                && expression is not InitializerExpressionSyntax)
            { Anchor(expression); return; }
            foreach (var child in operation.ChildOperations)
            { AnchorDynamicOperand(child); }
        }
    }
}
