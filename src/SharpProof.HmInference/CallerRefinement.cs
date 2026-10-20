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

        private void RefineInferenceFailures(Compilation changedCompilation, SyntaxTree originalTree, SyntaxTree changedTree, IMethodSymbol originalMethod, HmType[] parameters, HmType result, HashSet<(int Parameter, string Path)> pins)
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
            if (result is null)
            { throw new ArgumentNullException(nameof(result)); }
            var failures = changedCompilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            {
                if (diagnostic is null)
                { throw new ArgumentNullException(nameof(diagnostic)); }
                return IsCompilationError(diagnostic) && diagnostic.Id is "CS0411" or "CS0029" or "CS0407" or "CS0123" or "CS0452" or "CS0453" or "CS0310" or "CS0311" or "CS0315" or "CS8377"
                    || diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error && diagnostic.Id is "CS8714" or "CS8634" or "CS8631" or "CS8603" or "CS8604" or "CS8619" or "CS8620" or "CS8625" or "CS8621" or "CS8622";
            }).ToArray();
            if (failures.Length == 0)
            { return; }
            foreach (var tree in model.Compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var afterTree = replacementTrees.TryGetValue(tree, out var replacementTree) ? replacementTree : tree == originalTree ? changedTree : tree;
                var calls = tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
                var afterCalls = afterTree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
                var semantic = model.Compilation.GetSemanticModel(tree);
                for (var i = 0; i < calls.Length; i++)
                {
                    if (!failures.Any(diagnostic =>
                    {
                        if (diagnostic is null)
                        { throw new ArgumentNullException(nameof(diagnostic)); }
                        return diagnostic.Location.SourceTree == afterTree && afterCalls[i].Span.Contains(diagnostic.Location.SourceSpan);
                    }))
                    { continue; }
                    if (semantic.GetOperation(calls[i], cancellationToken) is not IInvocationOperation call
                        || !SameMethodDefinition(call.TargetMethod, originalMethod))
                    { continue; }
                    var signatureFailure = failures.Any(diagnostic =>
                    {
                        if (diagnostic is null)
                        { throw new ArgumentNullException(nameof(diagnostic)); }
                        return diagnostic.Id is "CS0452" or "CS0453" or "CS0310" or "CS0311" or "CS0315" or "CS8377" or "CS8714" or "CS8634" or "CS8631" or "CS8603" or "CS8604" or "CS8619" or "CS8620" or "CS8625"
                            && diagnostic.Location.SourceTree == afterTree && afterCalls[i].Span.Contains(diagnostic.Location.SourceSpan);
                    });
                    if (signatureFailure)
                    {
                        var afterSemantic = changedCompilation.GetSemanticModel(afterTree);
                        var operation = afterSemantic.GetOperation(afterCalls[i], cancellationToken) as IInvocationOperation;
                        var binding = afterSemantic.GetSymbolInfo(afterCalls[i].Expression, cancellationToken);
                        var selected = operation?.TargetMethod ?? binding.Symbol as IMethodSymbol;
                        var candidates = selected is null ? binding.CandidateSymbols.OfType<IMethodSymbol>() : [selected];
                        foreach (var candidate in candidates)
                        {
                            if (!ImplementationMethod(candidate).DeclaringSyntaxReferences.Any(reference =>
                            {
                                if (reference is null)
                                { throw new ArgumentNullException(nameof(reference)); }
                                return reference.SyntaxTree == changedTree && reference.GetSyntax(cancellationToken).GetAnnotations("HM.Candidate").Any();
                            }))
                            { continue; }
                            RefineSuppliedSignature(call.TargetMethod, candidate, parameters, result, pins);
                        }
                    }

                    foreach (var argument in call.Arguments)
                    {
                        if (argument.IsImplicit || argument.Parameter is null || argument.Syntax is not ArgumentSyntax syntax)
                        { continue; }
                        var ordinal = argument.Parameter.Ordinal;
                        if (call.TargetMethod.ReducedFrom is not null && SymbolEqualityComparer.Default.Equals(argument.Parameter.ContainingSymbol, call.TargetMethod))
                        { ordinal++; }
                        if (ordinal < parameters.Length)
                        { PinContextualArgument(syntax.Expression, argument.Parameter.Type, parameters[ordinal], semantic, pins); }
                    }
                }
                var groupSemantic = changedCompilation.GetSemanticModel(afterTree);
                var beforeGroups = MethodGroups(tree.GetRoot(cancellationToken), semantic);
                var afterGroups = MethodGroups(afterTree.GetRoot(cancellationToken), groupSemantic);
                if (beforeGroups.Length != afterGroups.Length)
                { continue; }
                for (var i = 0; i < beforeGroups.Length; i++)
                {
                    var groupSpan = afterGroups[i].Parent is CastExpressionSyntax cast ? cast.Span : afterGroups[i].Span;
                    if (!failures.Any(diagnostic =>
                    {
                        if (diagnostic is null)
                        { throw new ArgumentNullException(nameof(diagnostic)); }
                        return diagnostic.Location.SourceTree == afterTree && groupSpan.Contains(diagnostic.Location.SourceSpan);
                    }))
                    { continue; }
                    var originalBinding = semantic.GetSymbolInfo(beforeGroups[i], cancellationToken).Symbol as IMethodSymbol;
                    var before = originalBinding?.ReducedFrom is not null ? originalBinding
                        : (semantic.GetOperation(beforeGroups[i], cancellationToken) as IMethodReferenceOperation)?.Method ?? originalBinding;
                    var binding = groupSemantic.GetSymbolInfo(afterGroups[i], cancellationToken);
                    var reduced = binding.Symbol as IMethodSymbol;
                    var after = reduced?.ReducedFrom is not null ? reduced : (groupSemantic.GetOperation(afterGroups[i], cancellationToken) as IMethodReferenceOperation)?.Method
                        ?? binding.Symbol as IMethodSymbol
                        ?? binding.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault(candidate =>
                        {
                            if (candidate is null)
                            { throw new ArgumentNullException(nameof(candidate)); }
                            return ImplementationMethod(candidate).DeclaringSyntaxReferences.Any(reference =>
                            {
                                if (reference is null)
                                { throw new ArgumentNullException(nameof(reference)); }
                                return reference.SyntaxTree == changedTree && reference.GetSyntax(cancellationToken).GetAnnotations("HM.Candidate").Any();
                            });
                        });
                    if (before is null || after is null || !SameMethodDefinition(before, originalMethod)
                        || !ImplementationMethod(after).DeclaringSyntaxReferences.Any(reference =>
                        {
                            if (reference is null)
                            { throw new ArgumentNullException(nameof(reference)); }
                            return reference.SyntaxTree == changedTree && reference.GetSyntax(cancellationToken).GetAnnotations("HM.Candidate").Any();
                        }))
                    { continue; }
                    // A failed return conversion can expose an unconstructed candidate.
                    // Its open input parameters do not describe inferred caller arguments.
                    if (binding.Symbol is null && failures.Any(diagnostic =>
                    {
                        if (diagnostic is null)
                        { throw new ArgumentNullException(nameof(diagnostic)); }
                        return diagnostic.Id == "CS0407" && diagnostic.Location.SourceTree == afterTree && groupSpan.Contains(diagnostic.Location.SourceSpan);
                    }))
                    { CollectSignaturePins(result, CSharpTypes.Rigid(before.ReturnType), CSharpTypes.Rigid(after.ReturnType), null, pins); }
                    else
                    { RefineSuppliedSignature(before, after, parameters, result, pins); }
                    if (before.ReducedFrom is { } extension && afterGroups[i] is MemberAccessExpressionSyntax access
                        && groupSemantic.GetTypeInfo(access.Expression, cancellationToken).Type is { } capturedReceiver)
                    {
                        // A reduced group captures its receiver outside the
                        // delegate parameter list, including nullable annotations.
                        CollectSignaturePins(parameters[0], CSharpTypes.Rigid(extension.Parameters[0].Type), CSharpTypes.Rigid(capturedReceiver), 0, pins);
                    }
                    var delegateType = groupSemantic.GetTypeInfo(afterGroups[i], cancellationToken).ConvertedType
                        ?? (afterGroups[i].Parent is CastExpressionSyntax conversion ? groupSemantic.GetTypeInfo(conversion.Type, cancellationToken).Type : null);
                    // Method-group symbols omit nullable inference annotations. Use the
                    // delegate input contract when nullable conversion warnings expose them.
                    if (failures.Any(diagnostic =>
                    {
                        if (diagnostic is null)
                        { throw new ArgumentNullException(nameof(diagnostic)); }
                        return diagnostic.Id is "CS8621" or "CS8622"
                            && diagnostic.Location.SourceTree == afterTree && groupSpan.Contains(diagnostic.Location.SourceSpan);
                    }) && delegateType is INamedTypeSymbol { DelegateInvokeMethod: { } invoke }
                        && invoke.Parameters.Length == before.Parameters.Length)
                    {
                        var offset = before.ReducedFrom is null ? 0 : 1;
                        for (var parameter = 0; parameter < invoke.Parameters.Length; parameter++)
                        {
                            CollectSignaturePins(parameters[parameter + offset], CSharpTypes.Rigid(before.Parameters[parameter].Type),
                                CSharpTypes.Rigid(invoke.Parameters[parameter].Type), parameter + offset, pins);
                        }
                    }
                }
            }
        }

        private void PinContextualArgument(ExpressionSyntax expression, ITypeSymbol declared, HmType inferred, SemanticModel semantic, HashSet<(int Parameter, string Path)> pins)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            { PinContextualArgument(parenthesized.Expression, declared, inferred, semantic, pins); return; }
            if (expression is PostfixUnaryExpressionSyntax suppressed && suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            { PinContextualArgument(suppressed.Operand, declared, inferred, semantic, pins); return; }
            if (expression.IsKind(SyntaxKind.NullLiteralExpression) || expression.IsKind(SyntaxKind.DefaultLiteralExpression)
                || expression is ImplicitObjectCreationExpressionSyntax or CollectionExpressionSyntax { Elements.Count: 0 })
            { PinContextualShape(inferred, pins); return; }
            if (declared is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } optional
                && inferred.Root.Name == CSharpTypes.NullableValue)
            {
                if (semantic.GetTypeInfo(expression, cancellationToken).Type is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                { PinContextualShape(inferred, pins); }
                else
                { PinContextualArgument(expression, optional.TypeArguments[0], inferred.Root.Arguments[0], semantic, pins); }
                return;
            }
            if (expression is TupleExpressionSyntax tuple && declared is INamedTypeSymbol { IsTupleType: true } tupleType
                && inferred.Root.Name == "Tuple" && tuple.Arguments.Count == inferred.Root.Arguments.Length)
            {
                for (var i = 0; i < tuple.Arguments.Count; i++)
                { PinContextualArgument(tuple.Arguments[i].Expression, tupleType.TupleElements[i].Type, inferred.Root.Arguments[i], semantic, pins); }
                return;
            }
            if (expression is ConditionalExpressionSyntax or SwitchExpressionSyntax)
            {
                if (semantic.GetTypeInfo(expression, cancellationToken).Type is not { TypeKind: not TypeKind.Error })
                { PinContextualShape(inferred, pins); }
                return;
            }
            if (expression is CollectionExpressionSyntax collection)
            {
                var element = new Session(semantic, cancellationToken).CollectionElement(declared, inferred, expression.SpanStart);
                foreach (var item in collection.Elements.OfType<ExpressionElementSyntax>())
                { PinContextualArgument(item.Expression, element.Type, element.Shape, semantic, pins); }
                return;
            }

            if (declared is IFunctionPointerTypeSymbol pointerType && expression is PrefixUnaryExpressionSyntax address
                && address.IsKind(SyntaxKind.AddressOfExpression)
                && semantic.GetSymbolInfo(address.Operand, cancellationToken).Symbol is IMethodSymbol)
            {
                var pointerShape = ConversionCore(inferred);
                if (pointerShape.Arguments.Length != pointerType.Signature.Parameters.Length + 1)
                { return; }
                for (var i = 0; i < pointerType.Signature.Parameters.Length; i++)
                { PinContextualShape(pointerShape.Arguments[i], pins); }
                return;
            }
            if (declared is INamedTypeSymbol { TypeKind: TypeKind.Delegate } && ConversionCore(inferred).IsVariable)
            { PinContextualShape(inferred, pins); return; }
            if (declared is INamedTypeSymbol { TypeKind: TypeKind.Delegate } delegateType
                && expression is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax
                && semantic.GetSymbolInfo(expression, cancellationToken).Symbol is IMethodSymbol)
            {
                var groupPins = pins.Count;
                var groupSignature = DelegateSignature(delegateType, inferred);
                foreach (var input in groupSignature.Inputs)
                { PinContextualShape(input, pins); }
                if (pins.Count == groupPins)
                { PinContextualShape(inferred, pins); }
                return;
            }
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
            if (ConversionCore(inferred).IsVariable)
            { PinContextualShape(inferred, pins); return; }
            if (target.DelegateInvokeMethod is not { } invoke)
            { return; }
            var signature = DelegateSignature(target, inferred);
            ITypeSymbol outputType = invoke.ReturnType;
            var output = signature.Output;
            if (lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword) && outputType is INamedTypeSymbol { Arity: 1 } task)
            {
                outputType = task.TypeArguments[0];
                output = CSharpTypes.OwnArguments(ConversionCore(output))[0];
            }
            var previousPins = pins.Count;
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
            if (pins.Count != previousPins)
            { return; }
            if (lambda.Body is ExpressionSyntax body)
            { PinContextualReturn(body, outputType, output, semantic, pins); }
            else if (lambda.Body is BlockSyntax block)
            {
                foreach (var statement in block.DescendantNodes(node =>
                {
                    if (node is null)
                    { throw new ArgumentNullException(nameof(node)); }
                    return node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax;
                }).OfType<ReturnStatementSyntax>())
                {
                    if (statement.Expression is { } returned)
                    { PinContextualReturn(returned, outputType, output, semantic, pins); }
                }
            }
            if (pins.Count == previousPins)
            { PinContextualShape(inferred, pins); }
        }

        private void PinContextualReturn(ExpressionSyntax expression, ITypeSymbol declared, HmType inferred, SemanticModel semantic, HashSet<(int Parameter, string Path)> pins)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            cancellationToken.ThrowIfCancellationRequested();
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            { PinContextualReturn(parenthesized.Expression, declared, inferred, semantic, pins); return; }
            // Output inference needs a natural return type before it can examine
            // the delegate result; tuple argument inference can inspect each field.
            if (expression is AnonymousFunctionExpressionSyntax
                || expression is TupleExpressionSyntax or ConditionalExpressionSyntax or SwitchExpressionSyntax or CollectionExpressionSyntax
                && semantic.GetTypeInfo(expression, cancellationToken).Type is null)
            { PinContextualShape(inferred, pins); return; }
            PinContextualArgument(expression, declared, inferred, semantic, pins);
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
