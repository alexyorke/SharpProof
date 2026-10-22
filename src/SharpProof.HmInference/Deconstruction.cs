using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private static ExpressionSyntax StorageExpression(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            while (expression is ParenthesizedExpressionSyntax parentheses)
            { expression = parentheses.Expression; }
            return expression;
        }

        private static IEnumerable<(ExpressionSyntax Target, ExpressionSyntax Value)> StorageComponents(ExpressionSyntax target, ExpressionSyntax value)
        {
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            target = StorageExpression(target);
            if (target is not TupleExpressionSyntax targets)
            { yield return (target, value); yield break; }
            foreach (var alternative in StoredAlternatives(value))
            {
                if (StorageExpression(alternative) is not TupleExpressionSyntax values || values.Arguments.Count != targets.Arguments.Count)
                { continue; }
                for (var i = 0; i < targets.Arguments.Count; i++)
                {
                    foreach (var component in StorageComponents(targets.Arguments[i].Expression, values.Arguments[i].Expression))
                    { yield return component; }
                }
            }
        }

        private static ExpressionSyntax StoreValueLocation(ExpressionSyntax target, AssignmentExpressionSyntax assignment)
        {
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (assignment is null)
            { throw new ArgumentNullException(nameof(assignment)); }
            var path = new Stack<int>();
            foreach (var node in target.AncestorsAndSelf())
            {
                if (node == assignment.Left)
                { break; }
                if (node is ArgumentSyntax argument && argument.Parent is TupleExpressionSyntax tuple)
                { path.Push(tuple.Arguments.IndexOf(argument)); }
            }
            var value = assignment.Right;
            foreach (var index in path)
            {
                if (StorageExpression(value) is not TupleExpressionSyntax tuple || index >= tuple.Arguments.Count)
                { return assignment.Right; }
                value = tuple.Arguments[index].Expression;
            }
            return value;
        }
        private ITypeSymbol DeconstructionSourceType(AssignmentExpressionSyntax assignment)
        {
            if (assignment is null)
            { throw new ArgumentNullException(nameof(assignment)); }
            var info = model.GetTypeInfo(assignment.Right, cancellationToken);
            return info.Type ?? info.ConvertedType ?? (model.GetOperation(assignment, cancellationToken) as IDeconstructionAssignmentOperation)?.Value.Type
                ?? throw new InferenceException("Unresolved deconstruction source.");
        }

        private HmType DeconstructionValue(ExpressionSyntax expression, ITypeSymbol original)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            var unwrapped = StorageExpression(expression);
            if (unwrapped is TupleExpressionSyntax tuple && original is INamedTypeSymbol { IsTupleType: true } type && tuple.Arguments.Count == type.TupleElements.Length)
            {
                var values = new HmType[tuple.Arguments.Count];
                for (var i = 0; i < values.Length; i++)
                { values[i] = DeconstructionValue(tuple.Arguments[i].Expression, type.TupleElements[i].Type); }
                return new("Tuple", values);
            }
            return StoredLiteralValue(expression, original) ?? Returned(expression, original);
        }

        private static HmType? ProjectDeconstructionReceiver(ITypeSymbol original, HmType value, INamedTypeSymbol target)
        {
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (original is not INamedTypeSymbol source)
            { return null; }
            value = value.Root;
            while (value.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter)
            { value = value.Arguments[0].Root; }
            if (value.IsVariable && !source.IsGenericType)
            { HmType.Unify(value, CSharpTypes.Rigid(source.WithNullableAnnotation(NullableAnnotation.NotAnnotated))); }
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            if (!CSharpTypes.MapNamedArguments(source, value, mapping))
            { HmType.Unify(value, CSharpTypes.Rigid(source.WithNullableAnnotation(NullableAnnotation.NotAnnotated))); CSharpTypes.MapNamedArguments(source, value, mapping); }
            INamedTypeSymbol? ancestor = source.OriginalDefinition;
            while (ancestor is not null && !SymbolEqualityComparer.Default.Equals(ancestor.OriginalDefinition, target.OriginalDefinition))
            { ancestor = ancestor.BaseType; }
            if (ancestor is null)
            {
                foreach (var implemented in source.OriginalDefinition.AllInterfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, target.OriginalDefinition))
                    { ancestor = implemented; break; }
                }
            }
            return ancestor is null ? null : CSharpTypes.Rigid(ancestor, mapping);
        }

        private static void UnifyDeconstructionReceiver(HmType expected, HmType value)
        {
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            expected = expected.Root;
            value = value.Root;
            if (expected.Name == CSharpTypes.NullableReference && value.Name != CSharpTypes.NullableReference)
            { expected = expected.Arguments[0]; }
            else if (value.Name == CSharpTypes.NullableReference && expected.Name != CSharpTypes.NullableReference)
            { value = value.Arguments[0]; }
            HmType.Unify(expected, value);
        }

        private (HmType[] Values, ITypeSymbol[] Types, DeconstructionInfo[] Steps) DeconstructionParts(HmType value, ITypeSymbol original, DeconstructionInfo info, int count)
        {
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (count < 0)
            { throw new ArgumentOutOfRangeException(nameof(count)); }
            var values = new HmType[count];
            var types = new ITypeSymbol[count];
            if (original is INamedTypeSymbol { IsTupleType: true } tuple)
            {
                if (tuple.TupleElements.Length != count)
                { throw new InferenceException("Tuple deconstruction arity mismatch."); }
                for (var i = 0; i < count; i++)
                { values[i] = HmType.Variable(); types[i] = tuple.TupleElements[i].Type; }
                HmType.Unify(value, new("Tuple", values));
            }
            else
            {
                var selected = info.Method ?? throw new InferenceException("Unresolved custom Deconstruct method.");
                var definition = selected.OriginalDefinition;
                var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
                var offset = selected.IsStatic ? 1 : 0;
                if (definition.Parameters.Length != count + offset)
                { throw new InferenceException("Custom Deconstruct arity mismatch."); }
                if (!selected.IsStatic)
                {
                    var projected = ProjectDeconstructionReceiver(original, value, selected.ContainingType);
                    if (projected is null)
                    {
                        HmType.Unify(value, CSharpTypes.Rigid(original));
                        AddNamedArguments(selected.ContainingType, mapping);
                    }
                    else
                    {
                        if (!CSharpTypes.MapNamedArguments(selected.ContainingType, projected, mapping))
                        { throw new InferenceException("Invalid deconstructor owner shape."); }
                    }
                }
                foreach (var parameter in definition.TypeParameters)
                { mapping.Add(parameter, HmType.Variable()); }
                if (selected.IsStatic)
                {
                    var receiver = definition.Parameters[0].Type;
                    var projected = receiver is INamedTypeSymbol named ? ProjectDeconstructionReceiver(original, value, named) : value;
                    if (projected is null)
                    {
                        HmType.Unify(value, CSharpTypes.Rigid(original));
                        for (var i = 0; i < definition.TypeParameters.Length; i++)
                        { HmType.Unify(mapping[definition.TypeParameters[i]], CSharpTypes.Rigid(selected.TypeArguments[i])); }
                    }
                    else
                    { UnifyDeconstructionReceiver(CSharpTypes.Rigid(receiver, mapping), projected); }
                }
                for (var i = 0; i < count; i++)
                {
                    if (definition.Parameters[i + offset].RefKind != RefKind.Out)
                    { throw new InferenceException("Deconstruct outputs must be out parameters."); }
                    values[i] = CSharpTypes.Rigid(definition.Parameters[i + offset].Type, mapping);
                    types[i] = selected.Parameters[i + offset].Type;
                }
                foreach (var parameter in definition.TypeParameters)
                { ImportConstraints(parameter, mapping[parameter], mapping, selected.TypeArguments[parameter.Ordinal]); }
            }
            var steps = info.Nested.IsDefaultOrEmpty ? new DeconstructionInfo[count] : info.Nested.ToArray();
            if (steps.Length != count)
            { throw new InferenceException("Nested deconstruction arity mismatch."); }
            return (values, types, steps);
        }

        private static SyntaxNode[] DeconstructionSites(SyntaxNode root)
        {
            if (root is null)
            { throw new ArgumentNullException(nameof(root)); }
            var sites = new List<SyntaxNode>();
            foreach (var node in root.DescendantNodes())
            {
                if (node is AssignmentExpressionSyntax { Left: TupleExpressionSyntax or DeclarationExpressionSyntax }
                    or ForEachVariableStatementSyntax)
                { sites.Add(node); }
            }
            return sites.ToArray();
        }

        private static DeconstructionInfo DeconstructionBinding(SemanticModel semantic, SyntaxNode node)
        {
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            if (node is null)
            { throw new ArgumentNullException(nameof(node)); }
            return node switch
            {
                AssignmentExpressionSyntax assignment => AssignmentDeconstructionBinding(semantic, assignment),
                ForEachVariableStatementSyntax loop => semantic.GetDeconstructionInfo(loop),
                _ => throw new InferenceException("Invalid deconstruction binding site.")
            };
        }

        private static DeconstructionInfo AssignmentDeconstructionBinding(SemanticModel semantic, AssignmentExpressionSyntax assignment)
        {
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            if (assignment is null)
            { throw new ArgumentNullException(nameof(assignment)); }
            var info = semantic.GetDeconstructionInfo(assignment);
            if (info.Method is not null || !info.Nested.IsDefaultOrEmpty)
            { return info; }
            // Value-context tuple conversions can hide deconstruction metadata. Bind the
            // same assignment as a statement at its original scope using the public API.
            var statement = SyntaxFactory.ExpressionStatement(assignment.WithoutTrivia());
            if (!semantic.IsSpeculativeSemanticModel && semantic.TryGetSpeculativeSemanticModel(assignment.SpanStart, statement, out var speculative))
            { return speculative.GetDeconstructionInfo((AssignmentExpressionSyntax)statement.Expression); }
            return info;
        }

        private bool PreservesDeconstructionBindings(SyntaxNode before, SyntaxNode after, SemanticModel beforeModel, SemanticModel afterModel, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (beforeModel is null)
            { throw new ArgumentNullException(nameof(beforeModel)); }
            if (afterModel is null)
            { throw new ArgumentNullException(nameof(afterModel)); }
            if (changedMethod is null)
            { throw new ArgumentNullException(nameof(changedMethod)); }
            if (changedTree is null)
            { throw new ArgumentNullException(nameof(changedTree)); }
            var originals = DeconstructionSites(before);
            var candidates = DeconstructionSites(after);
            if (originals.Length != candidates.Length)
            { return false; }
            for (var i = 0; i < originals.Length; i++)
            {
                if (!SameDeconstructionBinding(DeconstructionBinding(beforeModel, originals[i]), DeconstructionBinding(afterModel, candidates[i]), changedMethod, changedTree))
                { return false; }
            }
            return true;
        }

        private bool SameDeconstructionBinding(DeconstructionInfo before, DeconstructionInfo after, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
        {
            if (changedMethod is null)
            { throw new ArgumentNullException(nameof(changedMethod)); }
            if (changedTree is null)
            { throw new ArgumentNullException(nameof(changedTree)); }
            if (before.Method is not null && after.Method is not null && SameMethodDefinition(before.Method, currentMethod)
                && ImplementationMethod(after.Method).DeclaringSyntaxReferences.Any(reference =>
                {
                    if (reference is null)
                    { throw new ArgumentNullException(nameof(reference)); }
                    return reference.SyntaxTree == changedTree && reference.Span.Start == changedMethod.SpanStart;
                }))
            {
                if (!SameCallSignature(before.Method, after.Method))
                { return false; }
            }
            else if (ProtocolMember(before.Method) != ProtocolMember(after.Method))
            { return false; }
            if (before.Conversion.HasValue != after.Conversion.HasValue)
            { return false; }
            if (before.Conversion.HasValue && !SameProtocolConversion(before.Conversion.Value, after.Conversion!.Value))
            { return false; }
            if (before.Nested.IsDefault || after.Nested.IsDefault)
            { return before.Nested.IsDefault && after.Nested.IsDefault; }
            if (before.Nested.Length != after.Nested.Length)
            { return false; }
            for (var i = 0; i < before.Nested.Length; i++)
            { if (!SameDeconstructionBinding(before.Nested[i], after.Nested[i], changedMethod, changedTree)) { return false; } }
            return true;
        }
    }
}
