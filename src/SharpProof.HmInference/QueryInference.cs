using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private Dictionary<SyntaxNode, HmType>? queryReferences;
        private IMethodSymbol? querySelectorReturn;

        private HmType Query(QueryExpressionSyntax syntax, bool retainStorageReferences = false)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            if (model.GetOperation(syntax, cancellationToken) is not ITranslatedQueryOperation operation)
            { throw new InferenceException("Unresolved query translation."); }
            var previous = queryReferences;
            queryReferences = previous is null ? [] : new(previous);
            try
            { return QueryValue(operation.Operation); }
            finally
            { if (!retainStorageReferences) { queryReferences = previous; } }
        }

        private HmType QueryValue(IOperation operation)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            cancellationToken.ThrowIfCancellationRequested();
            switch (operation)
            {
                case ITranslatedQueryOperation query:
                    return QueryValue(query.Operation);
                case IParameterReferenceOperation parameter when environment.TryGetValue(parameter.Parameter, out var input):
                    return parameter.Syntax is IdentifierNameSyntax parameterRead ? CapturedDefaultValue(parameterRead, parameter.Parameter, input) : input;
                case ILocalReferenceOperation local when environment.TryGetValue(local.Local, out var value):
                    return local.Syntax is IdentifierNameSyntax localRead ? CapturedDefaultValue(localRead, local.Local, value) : value;
                case IParameterReferenceOperation:
                    throw new InferenceException("Unbound query parameter.");
                case IPropertyReferenceOperation { Instance: not null, Arguments.Length: 0 } property:
                    return ApplyMemberNullability(property.Property, PatternSymbolType(property.Property, QueryValue(property.Instance!), property.Instance!.Type!), syntax: property.Syntax as ExpressionSyntax);
                case IFieldReferenceOperation { Instance: not null } field:
                    return ApplyMemberNullability(field.Field, PatternSymbolType(field.Field, QueryValue(field.Instance!), field.Instance!.Type!), syntax: field.Syntax as ExpressionSyntax);
                case IInvocationOperation { IsImplicit: true } call:
                    return QueryCall(call);
                case IConversionOperation { IsImplicit: true, Type: not null } conversion:
                    var operand = QueryValue(conversion.Operand);
                    var converted = CSharpTypes.Erase(conversion.Type!);
                    if (conversion.Operand.Type is { } source)
                    { BindConvertedArgument(source, conversion.Type!, operand, converted); }
                    else
                    { HmType.Unify(operand, converted); }
                    return converted;
                case IAnonymousObjectCreationOperation { Type: INamedTypeSymbol anonymous } creation:
                    return new(CSharpTypes.AnonymousName(anonymous), creation.Initializers.Select(initializer =>
                    {
                        if (initializer is null)
                        { throw new ArgumentNullException(nameof(initializer)); }
                        return initializer is ISimpleAssignmentOperation assignment ? QueryValue(assignment.Value)
                            : throw new InferenceException("Unresolved query carrier initializer.");
                    }).ToArray());
                case ITupleOperation tuple:
                    return new("Tuple", tuple.Elements.Select(QueryValue).ToArray());
            }
            if (operation.Syntax is not ExpressionSyntax expression || expression is QueryExpressionSyntax)
            { throw new InferenceException("Unsupported translated query operation: " + operation.Kind); }
            SeedQueryReferences(operation);
            return Expression(expression);
        }

        private void SeedQueryReferences(IOperation operation)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            foreach (var child in operation.ChildOperations)
            { SeedQueryReferences(child); }
            if (!operation.IsImplicit && operation.Syntax is IdentifierNameSyntax identifier
                && operation is IParameterReferenceOperation or IPropertyReferenceOperation or IFieldReferenceOperation)
            {
                try
                { queryReferences![identifier] = QueryValue(operation); }
                catch (InferenceException)
                { /* Explicit nested lambdas bind their own parameters later. */ }
            }
        }

        private HmType QueryCall(IInvocationOperation call)
        {
            if (call is null)
            { throw new ArgumentNullException(nameof(call)); }
            var called = call.TargetMethod;
            var mapping = call.Instance is null ? new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default)
                : PatternOwnerMapping(called.ContainingType, QueryValue(call.Instance), call.Instance.Type!);
            var definition = called.OriginalDefinition;
            var explicitCast = call.Syntax is QueryClauseSyntax clause && model.GetQueryClauseInfo(clause, cancellationToken).CastInfo.Symbol is IMethodSymbol cast
                && SameMethodDefinition(cast, called);
            foreach (var parameter in definition.TypeParameters)
            { mapping.Add(parameter, explicitCast ? CSharpTypes.Rigid(called.TypeArguments[parameter.Ordinal]) : HmType.Variable()); }
            foreach (var argument in call.Arguments)
            {
                if (argument.ArgumentKind == ArgumentKind.DefaultValue)
                { continue; }
                var parameter = argument.Parameter ?? throw new InferenceException("Unresolved query argument.");
                QueryArgument(argument.Value, CSharpTypes.Rigid(definition.Parameters[parameter.Ordinal].Type, mapping));
            }
            foreach (var parameter in definition.TypeParameters)
            { ImportConstraints(parameter, mapping[parameter], mapping, called.TypeArguments[parameter.Ordinal]); }
            return CSharpTypes.Rigid(definition.ReturnType, mapping);
        }

        private void QueryArgument(IOperation argument, HmType expected)
        {
            if (argument is null)
            { throw new ArgumentNullException(nameof(argument)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            var target = argument.Type as INamedTypeSymbol;
            var operand = argument;
            while (operand is IConversionOperation conversion)
            { operand = conversion.Operand; }
            if (operand is IDelegateCreationOperation creation)
            { operand = creation.Target; }
            if (operand is IAnonymousFunctionOperation lambda && target is not null)
            {
                var nominal = expected;
                if (SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, model.Compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1")))
                { target = (INamedTypeSymbol)target.TypeArguments[0]; nominal = CSharpTypes.OwnArguments(ConversionCore(expected))[0]; }
                var signature = DelegateSignature(target, nominal);
                var previousReturn = querySelectorReturn;
                querySelectorReturn = lambda.Symbol;
                try
                {
                    Scoped(() =>
                    {
                        for (var i = 0; i < lambda.Symbol.Parameters.Length; i++)
                        { environment.Add(lambda.Symbol.Parameters[i], signature.Inputs[i]); }
                        var returned = lambda.Body.Operations.OfType<IReturnOperation>().SingleOrDefault()?.ReturnedValue
                            ?? throw new InferenceException("Unresolved query selector body.");
                        HmType.Unify(signature.Output, QueryValue(returned));
                        return new(null, true);
                    });
                }
                finally
                { querySelectorReturn = previousReturn; }
                return;
            }
            HmType.Unify(expected, QueryValue(argument));
        }

        private IEnumerable<IOperation> QueryOperations(IOperation operation)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            cancellationToken.ThrowIfCancellationRequested();
            yield return operation;
            foreach (var child in operation.ChildOperations)
            { foreach (var descendant in QueryOperations(child)) { yield return descendant; } }
        }

        private bool PreservesQueryBindings(SyntaxNode before, SyntaxNode after, SemanticModel beforeModel, SemanticModel afterModel, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
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
            var originals = before.DescendantNodes().OfType<QueryExpressionSyntax>().ToArray();
            var candidates = after.DescendantNodes().OfType<QueryExpressionSyntax>().ToArray();
            if (originals.Length != candidates.Length)
            { return false; }
            for (var i = 0; i < originals.Length; i++)
            {
                var left = QueryOperations(beforeModel.GetOperation(originals[i], cancellationToken)!).OfType<IInvocationOperation>().Where(call =>
                { if (call is null) { throw new ArgumentNullException(nameof(call)); } return call.IsImplicit; }).ToArray();
                var right = QueryOperations(afterModel.GetOperation(candidates[i], cancellationToken)!).OfType<IInvocationOperation>().Where(call =>
                { if (call is null) { throw new ArgumentNullException(nameof(call)); } return call.IsImplicit; }).ToArray();
                if (left.Length != right.Length)
                { return false; }
                for (var j = 0; j < left.Length; j++)
                {
                    if (!SamePatternMember(left[j].TargetMethod, right[j].TargetMethod, changedMethod, changedTree))
                    { return false; }
                    var oldDefaults = left[j].Arguments.Where(argument =>
                    { if (argument is null) { throw new ArgumentNullException(nameof(argument)); } return argument.ArgumentKind == ArgumentKind.DefaultValue; }).ToArray();
                    var newDefaults = right[j].Arguments.Where(argument =>
                    { if (argument is null) { throw new ArgumentNullException(nameof(argument)); } return argument.ArgumentKind == ArgumentKind.DefaultValue; }).ToArray();
                    if (oldDefaults.Length != newDefaults.Length)
                    { return false; }
                    for (var k = 0; k < oldDefaults.Length; k++)
                    { if (!Equals(oldDefaults[k].Value.ConstantValue, newDefaults[k].Value.ConstantValue)) { return false; } }
                }
            }
            return true;
        }
    }
}
