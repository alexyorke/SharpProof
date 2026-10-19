using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private void AnalyzeLocalFunction(LocalFunctionStatementSyntax declaration)
        {
            if (declaration is null)
            { throw new ArgumentNullException(nameof(declaration)); }
            var symbol = model.GetDeclaredSymbol(declaration, cancellationToken) as IMethodSymbol
                ?? throw new InferenceException("Unresolved local function.");
            // The declared local scheme remains fixed. Isolate body inference so an
            // unsupported nested construct can retain the compiler-validated path.
            var copies = new Dictionary<HmType, HmType>();
            HmType Copy(HmType type)
            {
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                type = type.Root;
                if (copies.TryGetValue(type, out var copy))
                { return copy; }
                copy = type.WithArguments(type.Arguments.Select(Copy).ToArray());
                copies.Add(type, copy);
                return copy;
            }
            var nested = new Session(model, cancellationToken) { currentMethod = currentMethod };
            foreach (var entry in environment)
            { nested.environment.Add(entry.Key, Copy(entry.Value)); }
            if (currentFunction is not null)
            { nested.currentFunction = Copy(currentFunction); }
            try
            {
                foreach (var parameter in symbol.Parameters)
                { nested.environment.Add(parameter, CSharpTypes.Rigid(parameter.Type)); }
                nested.SetIteratorReturn(declaration, symbol);
                if (nested.iteratorReturn is null)
                { nested.SetAsyncReturn(symbol); }
                else
                { HmType.Unify(nested.iteratorElement!, CSharpTypes.Rigid(nested.iteratorDeclaredElement!)); }
                HmType result;
                var noValue = symbol.ReturnsVoid || nested.asyncReturn is { Arity: 0 };
                if (declaration.ExpressionBody is { } expressionBody)
                {
                    if (noValue)
                    { nested.Expression(expressionBody.Expression); result = new("void"); }
                    else
                    { result = nested.Returned(expressionBody.Expression); }
                }
                else if (declaration.Body is { } body)
                { result = nested.iteratorReturn is not null ? nested.IteratorBody(body) : noValue ? nested.VoidBody(body) : nested.Block(body); }
                else
                { return; }
                result = nested.iteratorReturn is not null ? nested.WrapIteratorResult() : nested.WrapAsyncResult(result);
                HmType.Unify(result, CSharpTypes.Rigid(symbol.ReturnType));
            }
            catch (InferenceException)
            { return; }
            foreach (var entry in copies)
            { HmType.Unify(entry.Key, entry.Value); }
            inferredConstraints.AddRange(nested.inferredConstraints);
            conversionBounds.AddRange(nested.conversionBounds);
            needsBuilderEmission |= nested.needsBuilderEmission;
        }
        private static (HmType[] Inputs, HmType Output) DelegateSignature(INamedTypeSymbol declared, HmType inferred)
        {
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            var invoke = declared.OriginalDefinition.DelegateInvokeMethod ?? throw new InferenceException("Unresolved delegate signature.");
            var shape = inferred.Root;
            if (shape.Name == CSharpTypes.NullableReference)
            { shape = shape.Arguments[0].Root; }
            if (shape.IsVariable)
            { HmType.Unify(shape, CSharpTypes.Rigid(declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated))); shape = shape.Root; }
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            if (!CSharpTypes.MapNamedArguments(declared, shape, mapping))
            { throw new InferenceException("Delegate type argument mismatch."); }
            var inputs = new List<HmType>();
            foreach (var parameter in invoke.Parameters)
            {
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
            AddNamedArguments(owner, mapping);
            if (receiver is null)
            { return mapping; }
            if (model.GetTypeInfo(receiver, cancellationToken).Type is not INamedTypeSymbol { IsGenericType: true } declared)
            { Anchor(receiver); return mapping; }
            var shape = Expression(receiver).Root;
            if (shape.Name == CSharpTypes.NullableReference)
            { shape = shape.Arguments[0].Root; }
            var receiverVariables = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            if (!CSharpTypes.MapNamedArguments(declared, shape, receiverVariables))
            { Anchor(receiver); return mapping; }
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
            if (!CSharpTypes.MapNamedArguments(ancestor, CSharpTypes.Rigid(ancestor, receiverVariables), mapping))
            { Anchor(receiver); }
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
            var identical = method.RefKind == invoke.RefKind && method.Parameters.Length == invoke.Parameters.Length
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
            if (model.GetOperation(lambda, cancellationToken) is not IAnonymousFunctionOperation operation)
            { throw new InferenceException("Lambda requires a resolved operation."); }
            var expressionDefinition = model.Compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1");
            INamedTypeSymbol? DelegateTarget(ITypeSymbol? candidate)
            {
                if (candidate is INamedTypeSymbol { TypeKind: TypeKind.Delegate } direct)
                { return direct; }
                return candidate is INamedTypeSymbol quoted && SymbolEqualityComparer.Default.Equals(quoted.OriginalDefinition, expressionDefinition)
                    && quoted.TypeArguments[0] is INamedTypeSymbol { TypeKind: TypeKind.Delegate } nested ? nested : null;
            }
            var info = model.GetTypeInfo(lambda, cancellationToken);
            var container = info.ConvertedType as INamedTypeSymbol;
            var target = DelegateTarget(container);
            if (target is null)
            { container = info.Type as INamedTypeSymbol; target = DelegateTarget(container); }
            if (target is null || container is null)
            { throw new InferenceException("Lambda requires a resolved delegate or expression-tree target."); }
            var parameters = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => new[] { simple.Parameter },
                ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.ToArray(),
                AnonymousMethodExpressionSyntax anonymous => anonymous.ParameterList?.Parameters.ToArray() ?? [],
                _ => throw new InferenceException("Unsupported lambda.")
            };
            var nominal = CSharpTypes.Erase(container);
            var shape = nominal.Root;
            if (shape.Name == CSharpTypes.NullableReference)
            { shape = shape.Arguments[0].Root; }
            var signature = DelegateSignature(target, SymbolEqualityComparer.Default.Equals(container.OriginalDefinition, expressionDefinition)
                ? CSharpTypes.OwnArguments(shape)[0] : nominal);
            var previousAsync = asyncReturn;
            var previousBuilder = asyncBuilder;
            var previousBuilderResult = asyncBuilderResult;
            var previousBuilderUsesResult = builderUsesResult;
            try
            {
                SetAsyncReturn(operation.Symbol);
                Scoped(() =>
                {
                    for (var i = 0; i < parameters.Length; i++)
                    {
                        var parameter = parameters[i];
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
                    { HmType.Unify(WrapAsyncResult(result), CSharpTypes.Rigid(operation.Symbol.ReturnType)); }
                    HmType.Unify(signature.Output, WrapAsyncResult(result));
                    return new(null, true);
                });
                ImportConstructedConstraints(container, nominal);
                return nominal;
            }
            finally { asyncReturn = previousAsync; asyncBuilder = previousBuilder; asyncBuilderResult = previousBuilderResult; builderUsesResult = previousBuilderUsesResult; }
        }
    }
}
