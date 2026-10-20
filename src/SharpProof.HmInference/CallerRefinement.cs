using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private readonly List<(HmType Variable, (int Parameter, string Path) Key)> signatureOrigins = [];

        private void RegisterSignatureOrigins(HmType inferred, HmType declared, int parameter, string path, HashSet<(int Parameter, string Path)> pins)
        {
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            if (parameter < 0)
            { throw new ArgumentOutOfRangeException(nameof(parameter)); }
            if (path is null)
            { throw new ArgumentNullException(nameof(path)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (inferred.IsVariable)
            {
                var key = (parameter, path);
                signatureOrigins.Add((inferred, key));
                if (pins.Contains(key))
                { HmType.Unify(inferred, declared); }
                return;
            }
            if (inferred.Arguments.Length != declared.Arguments.Length)
            { return; }
            for (var i = 0; i < inferred.Arguments.Length; i++)
            { RegisterSignatureOrigins(inferred.Arguments[i], declared.Arguments[i], parameter, path + "/" + i.ToString(CultureInfo.InvariantCulture), pins); }
        }

        private static bool SameRigidSignature(HmType before, HmType after)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            before = before.Root;
            after = after.Root;
            if (before.CSharpSymbol is { } original && after.CSharpSymbol is { } changed)
            { return CSharpTypes.SameSignatureType(original, changed); }
            if (before.Name != after.Name || before.NestedTypeName != after.NestedTypeName || before.Arguments.Length != after.Arguments.Length)
            { return false; }
            for (var i = 0; i < before.Arguments.Length; i++)
            { if (!SameRigidSignature(before.Arguments[i], after.Arguments[i])) { return false; } }
            return true;
        }

        private bool CollectSignaturePins(HmType inferred, HmType before, HmType after, int? parameter, HashSet<(int Parameter, string Path)> pins)
        {
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (parameter is < 0)
            { throw new ArgumentOutOfRangeException(nameof(parameter)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (SameRigidSignature(before, after))
            { return false; }
            var added = false;
            var direct = false;
            foreach (var origin in signatureOrigins)
            {
                if (ReferenceEquals(origin.Variable, inferred) && (parameter is null || origin.Key.Parameter == parameter))
                { direct = true; added |= pins.Add(origin.Key); }
            }
            if (direct)
            { return added; }
            inferred = inferred.Root;
            before = before.Root;
            after = after.Root;
            if (inferred.IsVariable || before.Name != after.Name || inferred.Arguments.Length != before.Arguments.Length || before.Arguments.Length != after.Arguments.Length)
            {
                var free = inferred.FreeVariables();
                foreach (var origin in signatureOrigins)
                {
                    if ((parameter is null || origin.Key.Parameter == parameter) && free.Contains(origin.Variable.Root))
                    { added |= pins.Add(origin.Key); }
                }
                return added;
            }
            for (var i = 0; i < inferred.Arguments.Length; i++)
            { added |= CollectSignaturePins(inferred.Arguments[i], before.Arguments[i], after.Arguments[i], parameter, pins); }
            return added;
        }

        private void RefineSuppliedSignature(IMethodSymbol before, IMethodSymbol after, HmType[] parameters, HmType result, HashSet<(int Parameter, string Path)> pins)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (parameters is null)
            { throw new ArgumentNullException(nameof(parameters)); }
            if (result is null)
            { throw new ArgumentNullException(nameof(result)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            var originalOffset = before.ReducedFrom is null ? 0 : 1;
            var changedOffset = after.ReducedFrom is null ? 0 : 1;
            if (before.Parameters.Length + originalOffset != parameters.Length || after.Parameters.Length + changedOffset != parameters.Length)
            { return; }
            for (var i = 0; i < parameters.Length; i++)
            {
                var original = i == 0 && originalOffset != 0 ? before.ReceiverType : before.Parameters[i - originalOffset].Type;
                var changed = i == 0 && changedOffset != 0 ? after.ReceiverType : after.Parameters[i - changedOffset].Type;
                if (original is not null && changed is not null)
                { CollectSignaturePins(parameters[i], CSharpTypes.Rigid(original), CSharpTypes.Rigid(changed), i, pins); }
            }
            CollectSignaturePins(result, CSharpTypes.Rigid(before.ReturnType), CSharpTypes.Rigid(after.ReturnType), null, pins);
        }

        private void RefineInferenceFailures(Compilation changedCompilation, SyntaxTree originalTree, SyntaxTree changedTree, IMethodSymbol originalMethod, HmType[] parameters, HashSet<(int Parameter, string Path)> pins)
        {
            if (changedCompilation is null)
            { throw new ArgumentNullException(nameof(changedCompilation)); }
            if (originalTree is null)
            { throw new ArgumentNullException(nameof(originalTree)); }
            if (changedTree is null)
            { throw new ArgumentNullException(nameof(changedTree)); }
            if (originalMethod is null)
            { throw new ArgumentNullException(nameof(originalMethod)); }
            if (parameters is null)
            { throw new ArgumentNullException(nameof(parameters)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            var failures = changedCompilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            {
                if (diagnostic is null)
                { throw new ArgumentNullException(nameof(diagnostic)); }
                return diagnostic.Id == "CS0411";
            }).ToArray();
            if (failures.Length == 0)
            { return; }
            foreach (var tree in model.Compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var afterTree = tree == originalTree ? changedTree : tree;
                var calls = tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
                var afterCalls = afterTree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
                var semantic = model.Compilation.GetSemanticModel(tree);
                for (var i = 0; i < calls.Length; i++)
                {
                    if (!failures.Any(diagnostic =>
                    {
                        if (diagnostic is null)
                        { throw new ArgumentNullException(nameof(diagnostic)); }
                        return diagnostic.Location.SourceTree == afterTree && afterCalls[i].Expression.Span.Contains(diagnostic.Location.SourceSpan);
                    }))
                    { continue; }
                    if (semantic.GetOperation(calls[i], cancellationToken) is not IInvocationOperation call
                        || !SymbolEqualityComparer.Default.Equals((call.TargetMethod.ReducedFrom ?? call.TargetMethod).OriginalDefinition, originalMethod.OriginalDefinition))
                    { continue; }
                    foreach (var argument in call.Arguments)
                    {
                        if (argument.IsImplicit || argument.Parameter is null || argument.Syntax is not ArgumentSyntax syntax)
                        { continue; }
                        var ordinal = argument.Parameter.Ordinal;
                        if (call.TargetMethod.ReducedFrom is not null && SymbolEqualityComparer.Default.Equals(argument.Parameter.ContainingSymbol, call.TargetMethod))
                        { ordinal++; }
                        if (ordinal < parameters.Length)
                        { PinContextualArgument(syntax.Expression, argument.Parameter.Type, parameters[ordinal], pins); }
                    }
                }
            }
        }

        private void PinContextualArgument(ExpressionSyntax expression, ITypeSymbol declared, HmType inferred, HashSet<(int Parameter, string Path)> pins)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            { PinContextualArgument(parenthesized.Expression, declared, inferred, pins); return; }
            if (expression is PostfixUnaryExpressionSyntax suppressed && suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            { PinContextualArgument(suppressed.Operand, declared, inferred, pins); return; }
            if (expression.IsKind(SyntaxKind.NullLiteralExpression) || expression.IsKind(SyntaxKind.DefaultLiteralExpression)
                || expression is ImplicitObjectCreationExpressionSyntax or CollectionExpressionSyntax { Elements.Count: 0 })
            { PinContextualShape(inferred, pins); return; }
            if (expression is not AnonymousFunctionExpressionSyntax lambda || declared is not INamedTypeSymbol target)
            { return; }
            if (target.OriginalDefinition.ToDisplayString() == "System.Linq.Expressions.Expression<TDelegate>")
            {
                target = (INamedTypeSymbol)target.TypeArguments[0];
                inferred = inferred.Root;
                if (inferred.Name == CSharpTypes.NullableReference)
                { inferred = inferred.Arguments[0].Root; }
                if (inferred.Arguments.Length != 1)
                { return; }
                inferred = inferred.Arguments[0];
            }
            if (target.DelegateInvokeMethod is not { } invoke)
            { return; }
            var signature = DelegateSignature(target, inferred);
            for (var i = 0; i < signature.Inputs.Length; i++)
            {
                var implicitInput = lambda switch
                {
                    SimpleLambdaExpressionSyntax simple => simple.Parameter.Type is null,
                    ParenthesizedLambdaExpressionSyntax list => i < list.ParameterList.Parameters.Count && list.ParameterList.Parameters[i].Type is null,
                    AnonymousMethodExpressionSyntax anonymous => anonymous.ParameterList is null,
                    _ => false
                };
                if (implicitInput)
                { PinContextualShape(signature.Inputs[i], pins); }
            }
            if (lambda.Body is AnonymousFunctionExpressionSyntax)
            { PinContextualShape(signature.Output, pins); }
            else if (lambda.Body is ExpressionSyntax body)
            { PinContextualArgument(body, invoke.ReturnType, signature.Output, pins); }
            else if (lambda.Body is BlockSyntax block)
            {
                foreach (var statement in block.DescendantNodes(node =>
                {
                    if (node is null)
                    { throw new ArgumentNullException(nameof(node)); }
                    return node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax;
                }).OfType<ReturnStatementSyntax>())
                {
                    if (statement.Expression is AnonymousFunctionExpressionSyntax)
                    { PinContextualShape(signature.Output, pins); }
                    else if (statement.Expression is { } returned)
                    { PinContextualArgument(returned, invoke.ReturnType, signature.Output, pins); }
                }
            }
        }

        private void PinContextualShape(HmType inferred, HashSet<(int Parameter, string Path)> pins)
        {
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            cancellationToken.ThrowIfCancellationRequested();
            var free = inferred.FreeVariables();
            foreach (var origin in signatureOrigins)
            { if (free.Contains(origin.Variable.Root)) { pins.Add(origin.Key); } }
        }

    }
}
