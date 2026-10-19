using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private static (HmType[] Inputs, HmType Output) DelegateSignature(INamedTypeSymbol declared, HmType inferred)
        {
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            var invoke = declared.OriginalDefinition.DelegateInvokeMethod ?? throw new InferenceException("Unresolved delegate signature.");
            if (invoke.ReturnsByRef || invoke.ReturnsByRefReadonly)
            { throw new InferenceException("Ref delegate returns are unsupported."); }
            var shape = inferred.Root;
            if (shape.Name == CSharpTypes.NullableReference)
            { shape = shape.Arguments[0].Root; }
            if (shape.IsVariable)
            { HmType.Unify(shape, CSharpTypes.Rigid(declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated))); shape = shape.Root; }
            if (shape.Arguments.Length != declared.TypeArguments.Length)
            { throw new InferenceException("Delegate type argument mismatch."); }
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            for (var i = 0; i < declared.TypeArguments.Length; i++)
            { mapping.Add(declared.OriginalDefinition.TypeParameters[i], shape.Arguments[i]); }
            var inputs = new List<HmType>();
            foreach (var parameter in invoke.Parameters)
            {
                if (parameter.RefKind != RefKind.None)
                { throw new InferenceException("Ref delegate parameters are unsupported."); }
                inputs.Add(CSharpTypes.Rigid(parameter.Type, mapping));
            }
            return (inputs.ToArray(), CSharpTypes.Rigid(invoke.ReturnType, mapping));
        }

        private ExpressionSyntax[] MethodGroups(SyntaxNode root, SemanticModel semantic)
        {
            if (root is null)
            { throw new ArgumentNullException(nameof(root)); }
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            var groups = new List<ExpressionSyntax>();
            foreach (var expression in root.DescendantNodes().OfType<ExpressionSyntax>())
            {
                if (expression is not (IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax))
                { continue; }
                if (semantic.GetSymbolInfo(expression, cancellationToken).Symbol is IMethodSymbol
                    && semantic.GetTypeInfo(expression, cancellationToken).ConvertedType is INamedTypeSymbol { TypeKind: TypeKind.Delegate })
                { groups.Add(expression); }
            }
            return groups.ToArray();
        }

        private Dictionary<ITypeParameterSymbol, HmType> ReceiverMapping(IMethodSymbol method, ExpressionSyntax? receiver)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            var owner = method.ContainingType;
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            for (var i = 0; i < owner.TypeArguments.Length; i++)
            { mapping.Add(owner.OriginalDefinition.TypeParameters[i], CSharpTypes.Rigid(owner.TypeArguments[i])); }
            if (receiver is null)
            { return mapping; }
            if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol { IsGenericType: true } declared)
            { Anchor(receiver); return mapping; }
            var shape = Expression(receiver).Root;
            if (shape.Name == CSharpTypes.NullableReference)
            { shape = shape.Arguments[0].Root; }
            if (shape.Arguments.Length != declared.TypeArguments.Length)
            { Anchor(receiver); return mapping; }
            var receiverVariables = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            for (var i = 0; i < declared.TypeArguments.Length; i++)
            { receiverVariables.Add(declared.OriginalDefinition.TypeParameters[i], shape.Arguments[i]); }
            INamedTypeSymbol? ancestor = declared.OriginalDefinition;
            while (ancestor is not null && !SymbolEqualityComparer.Default.Equals(ancestor.OriginalDefinition, owner.OriginalDefinition))
            { ancestor = ancestor.BaseType; }
            if (ancestor is null)
            {
                foreach (var implemented in declared.OriginalDefinition.AllInterfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, owner.OriginalDefinition))
                    { ancestor = implemented; break; }
                }
            }
            if (ancestor is null)
            { Anchor(receiver); return mapping; }
            for (var i = 0; i < owner.TypeArguments.Length; i++)
            { mapping[owner.OriginalDefinition.TypeParameters[i]] = CSharpTypes.Rigid(ancestor.TypeArguments[i], receiverVariables); }
            return mapping;
        }
        private HmType MethodGroup(ExpressionSyntax expression, IMethodSymbol method, INamedTypeSymbol target)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            var invoke = target.DelegateInvokeMethod ?? throw new InferenceException("Missing method group delegate signature.");
            var identical = method.Parameters.Length == invoke.Parameters.Length
                && SymbolEqualityComparer.IncludeNullability.Equals(method.ReturnType, invoke.ReturnType);
            for (var i = 0; identical && i < method.Parameters.Length; i++)
            {
                identical &= SymbolEqualityComparer.IncludeNullability.Equals(method.Parameters[i].Type, invoke.Parameters[i].Type)
                    && method.Parameters[i].RefKind == invoke.Parameters[i].RefKind;
            }
            var receiver = !method.IsStatic && expression is MemberAccessExpressionSyntax member ? member.Expression : null;
            if (!identical || method.ReducedFrom is not null)
            {
                // Preserve delegate variance, parameter adaptation and reduced extension receiver conversions.
                if (receiver is not null)
                { Anchor(receiver); }
                return CSharpTypes.Rigid(target);
            }
            var nominal = CSharpTypes.Erase(target);
            var signature = DelegateSignature(target, nominal);
            var mapping = ReceiverMapping(method, receiver);
            var definition = method.OriginalDefinition;
            var explicitArguments = expression is GenericNameSyntax generic ? generic.TypeArgumentList.Arguments
                : expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax name } ? name.TypeArgumentList.Arguments : default;
            for (var i = 0; i < definition.TypeParameters.Length; i++)
            {
                mapping.Add(definition.TypeParameters[i], explicitArguments.Count == 0 ? HmType.Variable()
                    : CSharpTypes.Rigid(model.GetTypeInfo(explicitArguments[i], cancellationToken).Type!));
            }
            for (var i = 0; i < definition.Parameters.Length; i++)
            { HmType.Unify(signature.Inputs[i], CSharpTypes.Rigid(definition.Parameters[i].Type, mapping)); }
            HmType.Unify(signature.Output, CSharpTypes.Rigid(definition.ReturnType, mapping));
            foreach (var parameter in definition.TypeParameters)
            { ImportConstraints(parameter, mapping[parameter], mapping, method.TypeArguments[parameter.Ordinal]); }
            ImportConstructedConstraints(target, nominal);
            return nominal;
        }
        private HmType Lambda(AnonymousFunctionExpressionSyntax lambda)
        {
            if (lambda is null)
            { throw new ArgumentNullException(nameof(lambda)); }
            if (model.GetOperation(lambda, cancellationToken) is not IAnonymousFunctionOperation operation
                || model.GetTypeInfo(lambda, cancellationToken).ConvertedType is not INamedTypeSymbol { TypeKind: TypeKind.Delegate } target)
            { throw new InferenceException("Lambda requires a resolved delegate target."); }
            var parameters = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => new[] { simple.Parameter },
                ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.ToArray(),
                AnonymousMethodExpressionSyntax anonymous => anonymous.ParameterList?.Parameters.ToArray() ?? [],
                _ => throw new InferenceException("Unsupported lambda.")
            };
            if (lambda is LambdaExpressionSyntax attributed && attributed.AttributeLists.Count != 0)
            { throw new InferenceException("Attributed lambdas are unsupported."); }
            var nominal = CSharpTypes.Erase(target);
            var signature = DelegateSignature(target, nominal);
            var previousAsync = asyncReturn;
            var previousBuilder = asyncBuilder;
            var previousBuilderUsesResult = builderUsesResult;
            try
            {
                SetAsyncReturn(operation.Symbol);
                Scoped(() =>
                {
                    for (var i = 0; i < parameters.Length; i++)
                    {
                        var parameter = parameters[i];
                        if (parameter.Modifiers.Count != 0 || parameter.AttributeLists.Count != 0)
                        { throw new InferenceException("Modified lambda parameters are unsupported."); }
                        if (parameter.Type is not null)
                        { HmType.Unify(signature.Inputs[i], CSharpTypes.Rigid(model.GetTypeInfo(parameter.Type, cancellationToken).Type!)); }
                        environment.Add(model.GetDeclaredSymbol(parameter, cancellationToken)!, signature.Inputs[i]);
                    }
                    var hasVoidBody = operation.Symbol.ReturnsVoid || asyncReturn is { Arity: 0 };
                    HmType result;
                    var body = lambda is AnonymousMethodExpressionSyntax anonymous ? anonymous.Block : ((LambdaExpressionSyntax)lambda).Body;
                    if (body is ExpressionSyntax expression)
                    {
                        if (hasVoidBody)
                        { Expression(expression); result = new("void"); }
                        else
                        { result = Returned(expression); }
                    }
                    else
                    { result = hasVoidBody ? VoidBody((BlockSyntax)body) : Block((BlockSyntax)body); }
                    if (lambda is ParenthesizedLambdaExpressionSyntax { ReturnType: not null } explicitReturn)
                    { HmType.Unify(WrapAsyncResult(result), CSharpTypes.Rigid(model.GetTypeInfo(explicitReturn.ReturnType, cancellationToken).Type!)); }
                    HmType.Unify(signature.Output, WrapAsyncResult(result));
                    return new(null, true);
                });
                ImportConstructedConstraints(target, nominal);
                return nominal;
            }
            finally { asyncReturn = previousAsync; asyncBuilder = previousBuilder; builderUsesResult = previousBuilderUsesResult; }
        }
    }
}
