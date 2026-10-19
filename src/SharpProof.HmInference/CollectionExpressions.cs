using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private string CollectionTypeName(ITypeSymbol type, int position)
        {
            if (type is null)
            { throw new ArgumentNullException(nameof(type)); }
            return CSharpTypes.Render(CSharpTypes.Rigid(type), type, [], true, model, position);
        }

        private IMethodSymbol? CollectionEnumerator(ITypeSymbol type, int position)
        {
            if (type is null)
            { throw new ArgumentNullException(nameof(type)); }
            try
            {
                var expression = SyntaxFactory.ParseExpression("default(" + CollectionTypeName(type, position) + ")!.GetEnumerator()");
                return model.GetSpeculativeSymbolInfo(position, expression, SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol;
            }
            catch (InferenceException) { return null; }
        }

        private (ITypeSymbol Type, HmType Shape) CollectionElement(ITypeSymbol original, HmType shape, int position)
        {
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (shape is null)
            { throw new ArgumentNullException(nameof(shape)); }
            if (original is IArrayTypeSymbol array)
            {
                var element = HmType.Variable();
                HmType.Unify(shape, new("[" + new string(',', array.Rank - 1) + "]", element));
                return (array.ElementType, element);
            }
            var enumerator = CollectionEnumerator(original, position);
            if (enumerator?.ReturnType is INamedTypeSymbol returned)
            {
                IPropertySymbol? current = null;
                for (var owner = returned; owner is not null && current is null; owner = owner.BaseType)
                { current = owner.GetMembers("Current").OfType<IPropertySymbol>().FirstOrDefault(); }
                if (current is not null)
                {
                    var receiver = AwaitProtocolResult(original, shape, enumerator);
                    return (current.Type, PatternSymbolType(current, receiver, returned));
                }
            }
            if (original is INamedTypeSymbol named)
            {
                var enumerable = named.AllInterfaces.Prepend(named).FirstOrDefault(candidate =>
                { if (candidate is null) { throw new ArgumentNullException(nameof(candidate)); } return candidate.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T; });
                if (enumerable is not null && ProjectDeconstructionReceiver(original, shape, enumerable) is { } projected)
                { return (enumerable.TypeArguments[0], CSharpTypes.OwnArguments(ConversionCore(projected))[0]); }
            }
            throw new InferenceException("Unresolved collection element protocol.");
        }

        private IMethodSymbol? CollectionAdd(ICollectionExpressionOperation operation, ExpressionSyntax value)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (operation.ConstructMethod is not { MethodKind: MethodKind.Constructor } || operation.Type is null)
            { return null; }
            var invocation = SyntaxFactory.ParseExpression("default(" + CollectionTypeName(operation.Type, operation.Syntax.SpanStart) + ")!.Add(" + value.WithoutTrivia() + ")");
            return model.GetSpeculativeSymbolInfo(operation.Syntax.SpanStart, invocation, SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol;
        }

        private void BindCollectionAdd(ICollectionExpressionOperation operation, ITypeSymbol source, HmType value, ExpressionSyntax syntax, HmType collection)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            if (source is null)
            { throw new ArgumentNullException(nameof(source)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            if (collection is null)
            { throw new ArgumentNullException(nameof(collection)); }
            var add = CollectionAdd(operation, syntax);
            if (add is null)
            { return; }
            if (add.Parameters.Length != 1)
            { throw new InferenceException("Unsupported collection Add shape."); }
            var mapping = PatternOwnerMapping(add.ContainingType, collection, operation.Type!);
            foreach (var parameter in add.OriginalDefinition.TypeParameters)
            { mapping[parameter] = HmType.Variable(); }
            var expected = CSharpTypes.Rigid(add.OriginalDefinition.Parameters[0].Type, mapping);
            BindConvertedArgument(source, add.Parameters[0].Type, value, expected);
            foreach (var parameter in add.OriginalDefinition.TypeParameters)
            { ImportConstraints(parameter, mapping[parameter], mapping, add.TypeArguments[parameter.Ordinal]); }
        }

        private HmType CollectionExpression(ICollectionExpressionOperation operation, HmType? expected = null, Func<IOperation, (ITypeSymbol Type, HmType Shape, ExpressionSyntax Syntax)>? elementSource = null)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            var original = operation.Type ?? throw new InferenceException("Unresolved collection expression target.");
            var shape = CSharpTypes.Erase(original);
            if (ConversionCore(shape).IsVariable)
            { HmType.Unify(shape, CSharpTypes.Rigid(original)); }
            if (expected is not null)
            { UnifyDeconstructionReceiver(expected, shape); }
            ImportConstructedConstraints(original, shape);
            var element = CollectionElement(original, shape, operation.Syntax.SpanStart);
            if (operation.ConstructMethod is { IsStatic: true } builder)
            {
                var mapping = ReceiverMapping(builder, null);
                foreach (var parameter in builder.OriginalDefinition.TypeParameters)
                { mapping[parameter] = HmType.Variable(); }
                HmType.Unify(shape, CSharpTypes.Rigid(builder.OriginalDefinition.ReturnType, mapping));
                if (builder.Parameters.Length != 1)
                { throw new InferenceException("Unsupported collection builder signature."); }
                var input = CSharpTypes.Rigid(builder.OriginalDefinition.Parameters[0].Type, mapping);
                HmType.Unify(element.Shape, CSharpTypes.OwnArguments(ConversionCore(input))[0]);
                foreach (var parameter in builder.OriginalDefinition.TypeParameters)
                { ImportConstraints(parameter, mapping[parameter], mapping, builder.TypeArguments[parameter.Ordinal]); }
            }
            foreach (var item in operation.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item is ISpreadOperation spread)
                {
                    var source = spread.Operand.Type ?? throw new InferenceException("Unresolved spread source.");
                    var values = CollectionElement(source, BoundExpression(spread.Operand), spread.Syntax.SpanStart);
                    BindConvertedArgument(values.Type, element.Type, values.Shape, element.Shape);
                    if (operation.ConstructMethod is { MethodKind: MethodKind.Constructor })
                    { BindCollectionAdd(operation, values.Type, values.Shape, SyntaxFactory.ParseExpression("default(" + CollectionTypeName(values.Type, spread.Syntax.SpanStart) + ")!"), shape); }
                }
                else
                {
                    if (elementSource is not null)
                    {
                        var resolved = elementSource(item);
                        BindConvertedArgument(resolved.Type, element.Type, resolved.Shape, element.Shape);
                        BindCollectionAdd(operation, resolved.Type, resolved.Shape, resolved.Syntax, shape);
                    }
                    else
                    {
                        BindNullableArgument(item, element.Shape);
                        var source = model.GetTypeInfo(item.Syntax, cancellationToken).Type ?? item.Type ?? element.Type;
                        var value = BoundExpression(item);
                        BindCollectionAdd(operation, source, value, (ExpressionSyntax)item.Syntax, shape);
                    }
                }
            }
            return shape;
        }

        private static IEnumerable<ICollectionExpressionOperation> CollectionOperations(SyntaxNode root, SemanticModel semantic)
        {
            if (root is null)
            { throw new ArgumentNullException(nameof(root)); }
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            var seen = new HashSet<ICollectionExpressionOperation>();
            foreach (var expression in root.DescendantNodes().OfType<ExpressionSyntax>())
            {
                var operation = semantic.GetOperation(expression);
                if (operation is ICollectionExpressionOperation collection)
                { if (seen.Add(collection)) { yield return collection; } }
                if (InterpolationHandler(operation)?.HandlerCreation is IObjectCreationOperation handlerConstruction)
                {
                    foreach (var argument in handlerConstruction.Arguments)
                    {
                        if (argument.ArgumentKind == ArgumentKind.ParamCollection && argument.Value is ICollectionExpressionOperation pack && seen.Add(pack))
                        { yield return pack; }
                    }
                }
                var arguments = operation switch { IInvocationOperation call => call.Arguments, IObjectCreationOperation creation => creation.Arguments, IPropertyReferenceOperation property => property.Arguments, _ => default };
                if (arguments.IsDefault)
                { continue; }
                foreach (var argument in arguments)
                { if (argument.ArgumentKind == ArgumentKind.ParamCollection && argument.Value is ICollectionExpressionOperation expanded && seen.Add(expanded)) { yield return expanded; } }
            }
        }

        private static ExpressionSyntax CollectionValueSyntax(ICollectionExpressionOperation collection, IOperation value)
        {
            if (collection is null)
            { throw new ArgumentNullException(nameof(collection)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            for (var current = (IOperation)collection; current.Parent is not null; current = current.Parent)
            {
                if (current.Parent is IInterpolatedStringHandlerCreationOperation handler)
                {
                    if (ReferenceEquals(current, handler.HandlerCreation))
                    {
                        value = ForwardedHandlerArgument(handler, value) ?? throw new InferenceException("Unresolved collection constructor placeholder.");
                        if (value is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance })
                        { return SyntaxFactory.ThisExpression(); }
                    }
                    break;
                }
            }
            return value.Syntax as ExpressionSyntax ?? throw new InferenceException("Unresolved collection element syntax.");
        }

        private string CollectionFacts(ICollectionExpressionOperation operation)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            var facts = new List<string?> { ProtocolMember(operation.ConstructMethod) };
            foreach (var item in operation.Elements)
            {
                if (item is ISpreadOperation spread)
                {
                    var source = spread.Operand.Type!;
                    var selected = CollectionEnumerator(source, spread.Syntax.SpanStart);
                    facts.Add(ProtocolMember(selected));
                    if (selected?.ReturnType is INamedTypeSymbol returned)
                    {
                        foreach (var name in new[] { "Current", "MoveNext", "Dispose" })
                        {
                            ISymbol? member = null;
                            for (var owner = returned; owner is not null && member is null; owner = owner.BaseType)
                            { member = owner.GetMembers(name).FirstOrDefault(); }
                            facts.Add(ProtocolMember(member));
                        }
                    }
                    var type = spread.ElementType!;
                    facts.Add(operation.ConstructMethod is { MethodKind: MethodKind.Constructor }
                        ? ProtocolMember(CollectionAdd(operation, SyntaxFactory.ParseExpression("default(" + CollectionTypeName(type, spread.Syntax.SpanStart) + ")!"))) : null);
                }
                else
                { facts.Add(ProtocolMember(CollectionAdd(operation, CollectionValueSyntax(operation, item)))); }
            }
            return string.Join("|", facts);
        }

        private bool PreservesCollectionBindings(SyntaxNode before, SyntaxNode after, SemanticModel beforeModel, SemanticModel afterModel)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (beforeModel is null)
            { throw new ArgumentNullException(nameof(beforeModel)); }
            if (afterModel is null)
            { throw new ArgumentNullException(nameof(afterModel)); }
            var left = CollectionOperations(before, beforeModel).ToArray();
            var right = CollectionOperations(after, afterModel).ToArray();
            if (left.Length != right.Length)
            { return false; }
            var original = new Session(beforeModel, cancellationToken);
            var candidate = new Session(afterModel, cancellationToken);
            for (var i = 0; i < left.Length; i++)
            { if (original.CollectionFacts(left[i]) != candidate.CollectionFacts(right[i])) { return false; } }
            return true;
        }
    }
}
