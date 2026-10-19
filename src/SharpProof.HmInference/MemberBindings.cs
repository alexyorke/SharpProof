using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private HmType WithExpression(WithExpressionSyntax syntax)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            var operation = model.GetOperation(syntax, cancellationToken) as IWithOperation
                ?? throw new InferenceException("Unresolved with expression.");
            var original = operation.Operand.Type ?? throw new InferenceException("Unresolved clone receiver.");
            var receiver = Expression(syntax.Expression);
            var core = ConversionCore(receiver);
            if (core.IsVariable)
            {
                if (original.IsValueType && syntax.Initializer.Expressions.Count == 0)
                { RequireKeyword(core, "struct"); }
                else
                { HmType.Unify(core, CSharpTypes.Rigid(original.WithNullableAnnotation(NullableAnnotation.NotAnnotated))); }
            }
            foreach (var entry in syntax.Initializer.Expressions)
            {
                if (model.GetOperation(entry, cancellationToken) is not ISimpleAssignmentOperation assignment
                    || assignment.Target is not IMemberReferenceOperation member)
                { throw new InferenceException("Unsupported with initializer."); }
                var expected = PatternSymbolType(member.Member, receiver, original);
                BindNullableArgument(assignment.Value, expected);
            }
            return receiver;
        }
        private HmType IndexedProperty(ElementAccessExpressionSyntax syntax, IPropertyReferenceOperation indexer)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            if (indexer is null)
            { throw new ArgumentNullException(nameof(indexer)); }
            var original = model.GetTypeInfo(syntax.Expression, cancellationToken).Type ?? throw new InferenceException("Unresolved indexer receiver.");
            var mapping = PatternOwnerMapping(indexer.Property.ContainingType, Expression(syntax.Expression), original);
            var definition = indexer.Property.OriginalDefinition;
            BindArguments(indexer.Arguments, definition.Parameters.Select(parameter =>
            { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return CSharpTypes.Rigid(parameter.Type, mapping); }).ToArray());
            return CSharpTypes.Rigid(definition.Type, mapping);
        }

        private HmType IndexedImplicitly(ElementAccessExpressionSyntax syntax, IImplicitIndexerReferenceOperation indexer)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            if (indexer is null)
            { throw new ArgumentNullException(nameof(indexer)); }
            foreach (var argument in syntax.ArgumentList.Arguments)
            { Anchor(argument.Expression); }
            var original = model.GetTypeInfo(syntax.Expression, cancellationToken).Type ?? throw new InferenceException("Unresolved implicit indexer receiver.");
            return PatternSymbolType(indexer.IndexerSymbol, Expression(syntax.Expression), original);
        }

        private static string? MemberSymbol(ISymbol symbol)
        {
            if (symbol is null)
            { throw new ArgumentNullException(nameof(symbol)); }
            if (symbol is IPropertySymbol { ContainingType.IsAnonymousType: true } anonymous)
            { return CSharpTypes.AnonymousName(anonymous.ContainingType) + "." + anonymous.Name; }
            return symbol is IFieldSymbol { ContainingType.IsTupleType: true } field
                ? "tuple:" + (field.CorrespondingTupleField ?? field).Name : ProtocolMember(symbol);
        }

        private bool PreservesMemberBindings(SyntaxNode before, SyntaxNode after, SemanticModel beforeModel, SemanticModel afterModel)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (beforeModel is null)
            { throw new ArgumentNullException(nameof(beforeModel)); }
            if (afterModel is null)
            { throw new ArgumentNullException(nameof(afterModel)); }
            var originals = before.DescendantNodes().Where(node => { if (node is null) { throw new ArgumentNullException(nameof(node)); } return node is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or WithExpressionSyntax; }).ToArray();
            var candidates = after.DescendantNodes().Where(node => { if (node is null) { throw new ArgumentNullException(nameof(node)); } return node is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or WithExpressionSyntax; }).ToArray();
            if (originals.Length != candidates.Length)
            { return false; }
            for (var i = 0; i < originals.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var left = beforeModel.GetOperation(originals[i], cancellationToken);
                var right = afterModel.GetOperation(candidates[i], cancellationToken);
                if (left is IWithOperation withOperation)
                { if (right is not IWithOperation otherWith || ProtocolMember(withOperation.CloneMethod) != ProtocolMember(otherWith.CloneMethod)) { return false; } }
                else if (left is IMemberReferenceOperation member && member.Member is IPropertySymbol or IFieldSymbol)
                { if (right is not IMemberReferenceOperation other || MemberSymbol(member.Member) != MemberSymbol(other.Member)) { return false; } }
                else if (left is IImplicitIndexerReferenceOperation implicitIndexer)
                {
                    if (right is not IImplicitIndexerReferenceOperation other || ProtocolMember(implicitIndexer.LengthSymbol) != ProtocolMember(other.LengthSymbol)
                    || ProtocolMember(implicitIndexer.IndexerSymbol) != ProtocolMember(other.IndexerSymbol))
                    { return false; }
                }
            }
            return true;
        }
    }
}
