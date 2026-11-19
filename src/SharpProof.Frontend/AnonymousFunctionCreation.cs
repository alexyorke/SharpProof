using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Frontend.Host;

namespace SharpProof.Frontend;

// Converting a lambda or anonymous method to a delegate runs none of its body
// (C# specification, anonymous function conversions). The conversion makes a
// delegate: a non-capturing one is cached by the compiler in a static field,
// so only its first evaluation allocates; a capturing one is a new delegate
// on every evaluation, over a closure frame the compiler allocates when the
// scope of a captured variable is entered. The body runs only when the
// delegate is invoked, which is an unknown call.
//
// A captured variable is shared between the method and the closure. If the
// method's own locals and parameters are its IR variables, an invocation the
// method does not see (an unknown call that got the delegate) must not change
// them. So no anonymous or local function in the declaring method may write,
// take a reference to, or call a mutating member on a variable it captures.
// The method's own writes need nothing: they change what a later invocation
// reads, and every invocation is an unknown call.
internal static class AnonymousFunctionCreation
{
    private const int MaximumWork = 4096;

    // Null when the creation is not admitted; otherwise whether the delegate
    // captures (a fresh delegate each time) or not (a compiler-cached one).
    internal static bool? Captures(IDelegateCreationOperation creation, Compilation? compilation, CancellationToken cancellationToken)
    {
        creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
        var (symbol, syntax) = creation.Target switch
        {
            IAnonymousFunctionOperation anonymous => (anonymous.Symbol, anonymous.Syntax),
            IFlowAnonymousFunctionOperation anonymous => (anonymous.Symbol, anonymous.Syntax),
            _ => default((IMethodSymbol?, SyntaxNode?))
        };
        // An expression tree is not a delegate: building one runs library code.
        if (symbol == null || syntax is not AnonymousFunctionExpressionSyntax expression || creation.Type?.TypeKind != TypeKind.Delegate ||
            compilation == null || !compilation.ContainsSyntaxTree(expression.SyntaxTree) ||
            symbol.ContainingSymbol is not IMethodSymbol owner || !IsAdmittedOwner(owner) ||
            owner.DeclaringSyntaxReferences.Length != 1)
        { return null; }
        var model = CompilationModelProvider.GetSemanticModel(compilation, expression.SyntaxTree);
        var remaining = MaximumWork;
        var usesThis = false;
        if (model.GetOperation(expression, cancellationToken) is not { } function)
        { return null; }
        foreach (var operation in function.Descendants())
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A local function's captures are not the lambda's in compiler
            // data flow, yet calling one shares its frame.
            if (--remaining < 0 || operation is ILocalFunctionOperation ||
                operation is IInvocationOperation { TargetMethod.MethodKind: MethodKind.LocalFunction } ||
                operation is IMethodReferenceOperation { Method.MethodKind: MethodKind.LocalFunction })
            { return null; }
            if (operation is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance })
            { usesThis = true; }
        }
        if (model.AnalyzeDataFlow(expression) is not { Succeeded: true } flow)
        { return null; }
        var captured = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var capture in flow.CapturedInside)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DeclaredWithin(capture, symbol))
            { continue; }
            if (capture is IParameterSymbol { IsThis: true })
            { usesThis = true; continue; }
            if (capture is not (ILocalSymbol or IParameterSymbol) || !SymbolEqualityComparer.Default.Equals(capture.ContainingSymbol, owner))
            { return null; }
            captured.Add(capture);
        }
        // A constructor's receiver is its own fresh object; a delegate over
        // it would publish that object unseen.
        if (usesThis && owner.MethodKind == MethodKind.Constructor)
        { return null; }
        if (captured.Count != 0)
        {
            var declaration = owner.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
            if (declaration.SyntaxTree != expression.SyntaxTree || model.GetOperation(declaration, cancellationToken) is not { } body)
            { return null; }
            foreach (var operation in body.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { return null; }
                var variable = operation switch
                {
                    ILocalReferenceOperation local => (ISymbol)local.Local,
                    IParameterReferenceOperation parameter => parameter.Parameter,
                    _ => null
                };
                if (variable != null && captured.Contains(variable) && WithinFunction(operation, body) && MayChange(operation, 0))
                { return null; }
            }
        }
        return usesThis || captured.Count != 0;
    }

    // Whether a method declares variables a closure frame holds: any of its
    // anonymous functions captures one of its own locals or parameters.
    // The compiler allocates that frame where the variable's scope begins,
    // whether or not the function is ever converted. A call the compiler
    // omits, such as one to a [Conditional] method, is removed before
    // closures are formed, so its lambdas hold no frame.
    internal static IOperation? FrameCapture(IOperation body, IMethodSymbol owner, Func<IOperation, bool>? elided, CancellationToken cancellationToken)
    {
        body = RepositoryParameterGuard.NotNull(body, nameof(body));
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        if (body.SemanticModel is not { } model)
        { return null; }
        foreach (var expression in body.Syntax.DescendantNodes().OfType<AnonymousFunctionExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var function = model.GetOperation(expression, cancellationToken);
            // A function inside an omitted call, at any depth, is removed with it.
            if (expression.AncestorsAndSelf().OfType<AnonymousFunctionExpressionSyntax>().Any(enclosing =>
                    model.GetOperation(enclosing, cancellationToken)?.Parent is IDelegateCreationOperation creation &&
                    elided?.Invoke(creation) == true))
            { continue; }
            // A failed analysis is taken to capture.
            if (model.AnalyzeDataFlow(expression) is not { Succeeded: true } flow ||
                flow.CapturedInside.Any(capture => capture is ILocalSymbol or IParameterSymbol { IsThis: false } &&
                    SymbolEqualityComparer.Default.Equals(capture.ContainingSymbol, owner.OriginalDefinition)))
            { return function ?? body; }
        }
        return null;
    }

    private static bool IsAdmittedOwner(IMethodSymbol owner)
    {
        return owner.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.PropertyGet or MethodKind.PropertySet or
            MethodKind.UserDefinedOperator or MethodKind.Conversion or MethodKind.ExplicitInterfaceImplementation;
    }

    private static bool DeclaredWithin(ISymbol capture, IMethodSymbol function)
    {
        for (var container = capture.ContainingSymbol; container is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction } nested;
            container = nested.ContainingSymbol)
        {
            if (SymbolEqualityComparer.Default.Equals(nested, function))
            { return true; }
        }
        return false;
    }

    private static bool WithinFunction(IOperation operation, IOperation body)
    {
        for (var parent = operation.Parent; parent != null && !ReferenceEquals(parent, body); parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            { return true; }
        }
        return false;
    }

    // Whether a use of a variable can change it: a store, a by-reference use,
    // or a member of a mutable struct value reached through it.
    private static bool MayChange(IOperation use, int depth)
    {
        if (depth > 32 || use.Parent is not { } parent || use.Syntax.Parent is RefExpressionSyntax)
        { return true; }
        if (parent is IAssignmentOperation assignment && ReferenceEquals(assignment.Target, use) ||
            parent is IIncrementOrDecrementOperation or IAddressOfOperation ||
            parent is ISimpleAssignmentOperation { IsRef: true } ||
            parent is IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None } })
        { return true; }
        if (parent is IArgumentOperation argument)
        { return argument.Parameter is not { RefKind: RefKind.None or RefKind.In }; }
        // A dynamic call binds at run time and may take the argument by
        // reference; Roslyn gives its arguments no IArgumentOperation.
        if (parent is IDynamicInvocationOperation or IDynamicObjectCreationOperation or IDynamicIndexerAccessOperation)
        { return true; }
        // A deconstruction stores into each element of its target tuple.
        if (parent is ITupleOperation)
        { return MayChange(parent, depth + 1); }
        if (use.Type is not { } type || type.IsReferenceType || IsImmutableValue(type))
        { return false; }
        return parent switch
        {
            IFieldReferenceOperation field when ReferenceEquals(field.Instance, use) => MayChange(field, depth + 1),
            IPropertyReferenceOperation property when ReferenceEquals(property.Instance, use) =>
                property.Property.GetMethod is not { IsReadOnly: true } || MayChange(property, depth + 1),
            IInvocationOperation invocation when ReferenceEquals(invocation.Instance, use) => !invocation.TargetMethod.IsReadOnly,
            IConversionOperation or IBinaryOperation or IUnaryOperation or IReturnOperation or IVariableInitializerOperation or
                ISimpleAssignmentOperation or IIsPatternOperation or IIsTypeOperation or IInterpolationOperation => false,
            IFieldReferenceOperation or IPropertyReferenceOperation or IInvocationOperation => false,
            _ => true
        };
    }

    private static bool IsImmutableValue(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { IsReadOnly: true } || type.TypeKind == TypeKind.Enum ||
            type.SpecialType is not SpecialType.None;
    }
}
