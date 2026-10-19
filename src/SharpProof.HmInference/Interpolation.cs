using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private bool requiresInterpolationEmission;

        private static IInterpolatedStringHandlerCreationOperation? InterpolationHandler(IOperation? operation)
        {
            while (operation?.Parent is { Kind: OperationKind.InterpolatedStringAddition } addition)
            { operation = addition; }
            return operation?.Parent as IInterpolatedStringHandlerCreationOperation;
        }

        private static IEnumerable<IOperation> InterpolationOperations(IOperation operation)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            var pending = new Stack<IOperation>();
            pending.Push(operation);
            while (pending.Count != 0)
            {
                var current = pending.Pop();
                yield return current;
                foreach (var child in current.ChildOperations.Reverse().Where(child => { if (child is null) { throw new ArgumentNullException(nameof(child)); } return child is not IInterpolatedStringHandlerCreationOperation; }))
                { pending.Push(child); }
            }
        }

        private static bool IsFormattableTarget(ITypeSymbol? target)
        { return target is INamedTypeSymbol named && named.Arity == 0 && named.ContainingNamespace.ToDisplayString() == "System" && named.Name is "FormattableString" or "IFormattable"; }

        private bool IsTargetedInterpolation(ExpressionSyntax syntax)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            var target = model.GetTypeInfo(syntax, cancellationToken).ConvertedType;
            while (syntax is ParenthesizedExpressionSyntax parentheses)
            { syntax = parentheses.Expression; }
            return syntax is InterpolatedStringExpressionSyntax && IsFormattableTarget(target)
                || syntax is InterpolatedStringExpressionSyntax or BinaryExpressionSyntax && InterpolationHandler(model.GetOperation(syntax, cancellationToken)) is not null;
        }

        private static IOperation? ForwardedHandlerArgument(IInterpolatedStringHandlerCreationOperation handler, IOperation value)
        {
            if (handler is null)
            { throw new ArgumentNullException(nameof(handler)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            while (value is IConversionOperation conversion)
            { value = conversion.Operand; }
            if (value is not IInterpolatedStringHandlerArgumentPlaceholderOperation placeholder)
            { return value; }
            IOperation? context = handler.Parent;
            while (context is not null && context is not (IInvocationOperation or IObjectCreationOperation))
            { context = context.Parent; }
            if (placeholder.ArgumentIndex < 0)
            { return (context as IInvocationOperation)?.Instance; }
            var arguments = context is IInvocationOperation invocation ? invocation.Arguments
                : context is IObjectCreationOperation creation ? creation.Arguments : default;
            return !arguments.IsDefault && placeholder.ArgumentIndex < arguments.Length ? arguments[placeholder.ArgumentIndex].Value : null;
        }

        private void BindHandlerConstructorValue(IInterpolatedStringHandlerCreationOperation handler, IOperation value, ITypeSymbol target, HmType expected)
        {
            if (handler is null)
            { throw new ArgumentNullException(nameof(handler)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            while (value is IConversionOperation conversion)
            { value = conversion.Operand; }
            if (value is IArrayCreationOperation { Initializer: not null } array && target is IArrayTypeSymbol arrayTarget)
            {
                var element = HmType.Variable();
                HmType.Unify(expected, new("[]", element));
                foreach (var item in array.Initializer.ElementValues)
                { BindHandlerConstructorValue(handler, item, arrayTarget.ElementType, element); }
                return;
            }
            var forwarded = ForwardedHandlerArgument(handler, value);
            if (forwarded is null || ReferenceEquals(forwarded, value) && value.IsImplicit && value.Syntax == handler.Syntax)
            { throw new InferenceException("Unsupported synthetic interpolation constructor argument."); }
            var containingInstance = forwarded is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance };
            var source = (containingInstance ? forwarded.Type : model.GetTypeInfo(forwarded.Syntax, cancellationToken).Type ?? forwarded.Type)
                ?? throw new InferenceException("Unresolved forwarded interpolation type.");
            BindConvertedArgument(source, target, containingInstance ? CSharpTypes.Rigid(source) : BoundExpression(forwarded), expected);
        }

        private void BindInterpolationCall(IInvocationOperation call, HmType handlerShape, ITypeSymbol handlerType)
        {
            if (call is null)
            { throw new ArgumentNullException(nameof(call)); }
            if (handlerShape is null)
            { throw new ArgumentNullException(nameof(handlerShape)); }
            if (handlerType is null)
            { throw new ArgumentNullException(nameof(handlerType)); }
            var mapping = PatternOwnerMapping(call.TargetMethod.ContainingType, handlerShape, handlerType);
            foreach (var parameter in call.TargetMethod.OriginalDefinition.TypeParameters)
            { mapping[parameter] = HmType.Variable(); }
            foreach (var argument in call.Arguments)
            {
                if (argument.ArgumentKind == ArgumentKind.DefaultValue)
                { continue; }
                var parameter = argument.Parameter ?? throw new InferenceException("Unbound interpolation append argument.");
                var expected = CSharpTypes.Rigid(call.TargetMethod.OriginalDefinition.Parameters[parameter.Ordinal].Type, mapping);
                if (argument.ArgumentKind == ArgumentKind.ParamArray || argument.Value is ICollectionExpressionOperation)
                {
                    BindArguments([argument], call.TargetMethod.OriginalDefinition.Parameters.Select(p =>
                    { if (p is null) { throw new ArgumentNullException(nameof(p)); } return CSharpTypes.Rigid(p.Type, mapping); }).ToArray());
                    continue;
                }
                if (BindReferenceArgument(argument, expected))
                { continue; }
                if (argument.Value.Syntax is ExpressionSyntax)
                { BindNullableArgument(argument.Value, expected); }
                else if (argument.Value.Type is { } actual)
                { BindConvertedArgument(actual, parameter.Type, CSharpTypes.Rigid(actual), expected); }
            }
            foreach (var parameter in call.TargetMethod.OriginalDefinition.TypeParameters)
            { ImportConstraints(parameter, mapping[parameter], mapping, call.TargetMethod.TypeArguments[parameter.Ordinal]); }
        }

        private HmType Interpolation(ExpressionSyntax syntax, HmType? expected = null)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            var target = model.GetTypeInfo(syntax, cancellationToken).ConvertedType;
            while (syntax is ParenthesizedExpressionSyntax parentheses)
            { syntax = parentheses.Expression; }
            var operation = model.GetOperation(syntax, cancellationToken);
            HmType shape;
            if (InterpolationHandler(operation) is { Type: not null } handler)
            {
                shape = CSharpTypes.Erase(handler.Type);
                if (ConversionCore(shape).IsVariable)
                { HmType.Unify(shape, CSharpTypes.Rigid(handler.Type)); }
                if (expected is not null)
                { UnifyDeconstructionReceiver(expected, shape); }
                ImportConstructedConstraints(handler.Type, shape);
                if (handler.HandlerCreation is not IObjectCreationOperation { Constructor: not null } construction)
                { throw new InferenceException("Unresolved interpolation handler constructor."); }
                var mapping = PatternOwnerMapping(construction.Constructor.ContainingType, shape, handler.Type);
                foreach (var argument in construction.Arguments)
                {
                    var parameter = argument.Parameter ?? throw new InferenceException("Unbound interpolation constructor argument.");
                    if (parameter.Ordinal < 2 || parameter.RefKind == RefKind.Out || argument.ArgumentKind == ArgumentKind.DefaultValue)
                    { continue; }
                    BindHandlerConstructorValue(handler, argument.Value, parameter.Type, CSharpTypes.Rigid(construction.Constructor.OriginalDefinition.Parameters[parameter.Ordinal].Type, mapping));
                }
                foreach (var append in InterpolationOperations(handler.Content).OfType<IInterpolatedStringAppendOperation>())
                {
                    if (append.AppendCall is not IInvocationOperation call)
                    { throw new InferenceException("Unresolved interpolation append call."); }
                    BindInterpolationCall(call, shape, handler.Type);
                }
                return shape;
            }
            // FormattableString stores boxed arguments. Ordinary string lowering
            // hides formatting overloads, so retain its original hole types.
            foreach (var hole in (syntax as InterpolatedStringExpressionSyntax)?.Contents.OfType<InterpolationSyntax>() ?? Enumerable.Empty<InterpolationSyntax>())
            {
                if (IsFormattableTarget(target))
                { Expression(hole.Expression); }
                else
                { Anchor(hole.Expression); }
                if (hole.AlignmentClause is not null)
                { HmType.Unify(Expression(hole.AlignmentClause.Value), new("int")); }
            }
            shape = IsFormattableTarget(target) ? CSharpTypes.Rigid(target!) : new("string");
            if (expected is not null)
            { UnifyDeconstructionReceiver(expected, shape); }
            return shape;
        }

        private static IEnumerable<(string Kind, IMethodSymbol? Method)> InterpolationBindings(SyntaxNode root, SemanticModel semantic)
        {
            if (root is null)
            { throw new ArgumentNullException(nameof(root)); }
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            foreach (var syntax in root.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>())
            {
                var handler = InterpolationHandler(semantic.GetOperation(syntax));
                if (handler is null)
                { yield return ("plain:" + IsFormattableTarget(semantic.GetTypeInfo(syntax).ConvertedType), null); continue; }
                yield return ("handler:" + handler.HandlerCreationHasSuccessParameter + ":" + handler.HandlerAppendCallsReturnBool,
                    (handler.HandlerCreation as IObjectCreationOperation)?.Constructor);
                foreach (var append in InterpolationOperations(handler.Content).OfType<IInterpolatedStringAppendOperation>())
                { yield return ("append:" + append.Kind, (append.AppendCall as IInvocationOperation)?.TargetMethod); }
            }
        }

        private string? ValidateInterpolationEmission(SemanticModel changedModel)
        {
            if (changedModel is null)
            { throw new ArgumentNullException(nameof(changedModel)); }
            using var originalImage = new System.IO.MemoryStream();
            var original = model.Compilation.Emit(originalImage, cancellationToken: cancellationToken);
            if (!original.Success)
            { throw new InferenceException("Original interpolation-handler compilation cannot be emitted."); }
            using var candidateImage = new System.IO.MemoryStream();
            var candidate = changedModel.Compilation.Emit(candidateImage, cancellationToken: cancellationToken);
            if (!candidate.Success)
            { return "Generalization fails interpolation-handler lowering."; }
            return IntroducesCompilerWarnings(changedModel.Compilation, original.Diagnostics, candidate.Diagnostics)
                ? "Generalization introduces interpolation-handler emission warnings." : null;
        }

        private bool PreservesInterpolationBindings(SyntaxNode before, SyntaxNode after, SemanticModel beforeModel, SemanticModel afterModel, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (beforeModel is null)
            { throw new ArgumentNullException(nameof(beforeModel)); }
            if (afterModel is null)
            { throw new ArgumentNullException(nameof(afterModel)); }
            var left = InterpolationBindings(before, beforeModel).ToArray();
            var right = InterpolationBindings(after, afterModel).ToArray();
            if (left.Length != right.Length)
            { return false; }
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i].Method is { } selected && SymbolEqualityComparer.Default.Equals((selected.ReducedFrom ?? selected).OriginalDefinition, currentMethod?.OriginalDefinition))
                { requiresInterpolationEmission = true; }
                if (left[i].Kind != right[i].Kind || !SameAwaitMethod(left[i].Method, right[i].Method, changedMethod, changedTree))
                { return false; }
            }
            return true;
        }
    }
}
