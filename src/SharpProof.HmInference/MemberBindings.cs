using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private HmType ApplyMemberNullability(ISymbol member, HmType value, bool forStorage = false,
            IEnumerable<IArgumentOperation>? arguments = null, IReadOnlyDictionary<int, HmType>? argumentValues = null, ExpressionSyntax? syntax = null)
        {
            if (member is null)
            { throw new ArgumentNullException(nameof(member)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            var type = member switch { IFieldSymbol field => field.Type, IPropertySymbol property => property.Type, _ => null };
            if (type is null)
            { return value; }
            // The pinned compiler reads conditional property contracts from getter return attributes.
            var attributes = member.GetAttributes().Where(attribute =>
            {
                if (attribute is null)
                { throw new ArgumentNullException(nameof(attribute)); }
                return member is not IPropertySymbol || attribute.AttributeClass?.ToDisplayString() != "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute";
            });
            // Source property flow annotations override accessor-return annotations.
            if (member is IPropertySymbol { GetMethod: { } getter } && !attributes.Any(attribute =>
            {
                if (attribute is null)
                { throw new ArgumentNullException(nameof(attribute)); }
                return attribute.AttributeClass?.ToDisplayString() is "System.Diagnostics.CodeAnalysis.MaybeNullAttribute" or "System.Diagnostics.CodeAnalysis.NotNullAttribute";
            }))
            { attributes = attributes.Concat(getter.GetReturnTypeAttributes()); }
            var result = ApplyNullabilityContract(type, value, attributes, forStorage, arguments, argumentValues, preferNotNull: true);
            return syntax is null ? result : ApplyNullableReadFlow(syntax, member, result, forStorage);
        }

        private (bool AllowNull, bool DisallowNull) MemberWriteContract(ExpressionSyntax target)
        {
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            ISymbol? member = model.GetOperation(StorageExpression(target), cancellationToken) switch
            {
                IFieldReferenceOperation field => field.Field,
                IPropertyReferenceOperation { Property: { SetMethod: not null, ReturnsByRef: false, ReturnsByRefReadonly: false } property } => property,
                _ => null
            };
            if (member is null)
            { return default; }
            IEnumerable<AttributeData> attributes = member.GetAttributes();
            // Source binding reads property annotations. Imported property input
            // annotations are encoded on the setter's value parameter.
            if (member is IPropertySymbol propertySymbol && !propertySymbol.Locations.Any(location =>
            {
                if (location is null)
                { throw new ArgumentNullException(nameof(location)); }
                return location.IsInSource;
            }) && propertySymbol.SetMethod is { Parameters.Length: > 0 } setter)
            { attributes = attributes.Concat(setter.Parameters[setter.Parameters.Length - 1].GetAttributes()); }
            var allow = false;
            var disallow = false;
            foreach (var attribute in attributes)
            {
                allow |= attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.AllowNullAttribute";
                disallow |= attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.DisallowNullAttribute";
            }
            return (allow && !disallow, disallow);
        }

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
        private HmType IndexedProperty(ElementAccessExpressionSyntax syntax, IPropertyReferenceOperation indexer, bool forStorage = false)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            if (indexer is null)
            { throw new ArgumentNullException(nameof(indexer)); }
            var original = model.GetTypeInfo(syntax.Expression, cancellationToken).Type ?? throw new InferenceException("Unresolved indexer receiver.");
            var mapping = PatternOwnerMapping(indexer.Property.ContainingType, Expression(syntax.Expression), original);
            var definition = indexer.Property.OriginalDefinition;
            Dictionary<int, HmType>? argumentValues = forStorage ? null : [];
            BindArguments(indexer.Arguments, definition.Parameters.Select(parameter =>
            { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return CSharpTypes.Rigid(parameter.Type, mapping); }).ToArray(), argumentValues);
            return ApplyMemberNullability(indexer.Property, CSharpTypes.Rigid(definition.Type, mapping), forStorage, indexer.Arguments, argumentValues, syntax);
        }

        private HmType IndexedImplicitly(ElementAccessExpressionSyntax syntax, IImplicitIndexerReferenceOperation indexer, bool forStorage = false)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            if (indexer is null)
            { throw new ArgumentNullException(nameof(indexer)); }
            foreach (var argument in syntax.ArgumentList.Arguments)
            { Anchor(argument.Expression); }
            var original = model.GetTypeInfo(syntax.Expression, cancellationToken).Type ?? throw new InferenceException("Unresolved implicit indexer receiver.");
            return ApplyMemberNullability(indexer.IndexerSymbol, PatternSymbolType(indexer.IndexerSymbol, Expression(syntax.Expression), original), forStorage, syntax: syntax);
        }

        private HmType IndexedInlineArray(ElementAccessExpressionSyntax syntax, IInlineArrayAccessOperation access, bool forStorage)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            if (access is null)
            { throw new ArgumentNullException(nameof(access)); }
            return IndexedInlineArray(syntax.ArgumentList, access, Expression(syntax.Expression, forStorage));
        }

        private HmType IndexedInlineArray(BracketedArgumentListSyntax arguments, IInlineArrayAccessOperation access, HmType receiver)
        {
            if (arguments is null)
            { throw new ArgumentNullException(nameof(arguments)); }
            if (access is null)
            { throw new ArgumentNullException(nameof(access)); }
            if (receiver is null)
            { throw new ArgumentNullException(nameof(receiver)); }
            if (arguments.Arguments.Count != 1 || access.Instance.Type is not INamedTypeSymbol owner)
            { throw new InferenceException("Unresolved inline-array access."); }
            var fields = owner.GetMembers().OfType<IFieldSymbol>().Where(field =>
            { if (field is null) { throw new ArgumentNullException(nameof(field)); } return !field.IsStatic; }).ToArray();
            if (fields.Length != 1)
            { throw new InferenceException("Invalid inline-array element field."); }
            var element = PatternSymbolType(fields[0], receiver, owner);
            Anchor(arguments.Arguments[0].Expression);
            var range = model.Compilation.GetTypeByMetadataName("System.Range");
            if (range is not null && SymbolEqualityComparer.Default.Equals(access.Argument.Type, range))
            {
                if (access.Type is not INamedTypeSymbol { TypeArguments.Length: 1 } slice)
                { throw new InferenceException("Unresolved inline-array slice type."); }
                return CSharpTypes.Rigid(slice).WithArguments(element);
            }
            return element;
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
            var originals = before.DescendantNodes().Where(node => { if (node is null) { throw new ArgumentNullException(nameof(node)); } return node is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or ElementBindingExpressionSyntax or WithExpressionSyntax; }).ToArray();
            var candidates = after.DescendantNodes().Where(node => { if (node is null) { throw new ArgumentNullException(nameof(node)); } return node is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or ElementBindingExpressionSyntax or WithExpressionSyntax; }).ToArray();
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
                else if (left is IInlineArrayAccessOperation inlineArray)
                {
                    if (right is not IInlineArrayAccessOperation other
                        || inlineArray.Instance.Type is null || other.Instance.Type is null
                        || CSharpTypes.ConstructorIdentity(inlineArray.Instance.Type) != CSharpTypes.ConstructorIdentity(other.Instance.Type)
                        || inlineArray.Argument.Type is null || other.Argument.Type is null
                        || CSharpTypes.ConstructorIdentity(inlineArray.Argument.Type) != CSharpTypes.ConstructorIdentity(other.Argument.Type))
                    { return false; }
                    var range = beforeModel.Compilation.GetTypeByMetadataName("System.Range");
                    if (range is not null && SymbolEqualityComparer.Default.Equals(inlineArray.Argument.Type, range)
                        && (inlineArray.Type is null || other.Type is null
                            || CSharpTypes.ConstructorIdentity(inlineArray.Type) != CSharpTypes.ConstructorIdentity(other.Type)))
                    { return false; }
                }
            }
            return true;
        }
    }
}
