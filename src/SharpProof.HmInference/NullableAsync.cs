using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private HmType ApplyReturnNullability(IInvocationOperation operation, HmType value, bool forStorage, IReadOnlyDictionary<int, HmType>? argumentValues)
        {
            if (operation is null)
            { throw new ArgumentNullException(nameof(operation)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            var indirectAliasStore = operation.Syntax is ExpressionSyntax callSyntax && IsIndirectAliasStore(callSyntax);
            var result = ApplyNullabilityContract(operation.TargetMethod.ReturnType, value,
                operation.TargetMethod.GetReturnTypeAttributes(), forStorage, out var conditionalProved,
                operation.Arguments, argumentValues, allowBareRelational: indirectAliasStore);
            if (forStorage || operation.Syntax is not ExpressionSyntax syntax)
            { return result; }
            if (indirectAliasStore && conditionalProved && result.Root.IsVariable && !HasDefaultValueAnnotation(result))
            { conditionalGuaranteedCalls.Add(syntax.Span); }
            // A return occurrence can be nullable independently of its input variables.
            result = ApplyInvocationAnnotations(syntax, result);
            if (nullableReadFlow.TryGetValue(syntax.Span, out var state) && state == NullableFlowState.MaybeNull)
            { result = ApplyNullableReadFlow(syntax, operation.TargetMethod, result, forStorage); }
            invocationResultShapes[syntax.Span] = result;
            return result;
        }

        private readonly Dictionary<TextSpan, HmType> invocationResultShapes = [];
        private readonly HashSet<TextSpan> conditionalGuaranteedCalls = [];

        private bool IsIndirectAliasStore(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            foreach (var assignment in expression.Ancestors().OfType<AssignmentExpressionSyntax>())
            {
                if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                { continue; }
                foreach (var component in StorageComponents(assignment.Left, assignment.Right))
                {
                    if (model.GetSymbolInfo(StorageExpression(component.Target), cancellationToken).Symbol
                        is not ILocalSymbol { RefKind: not RefKind.None })
                    { continue; }
                    foreach (var alternative in StoredAlternatives(component.Value))
                    {
                        if (StorageExpression(alternative).Span == expression.Span)
                        { return true; }
                    }
                }
            }
            return false;
        }
        private readonly List<(TextSpan Call, int Argument, int Parameter, string Path, string TypePath)> nullableReceiverCandidates = [];

        private void ApplyNullableReceiverInputs(HmType[] parameters)
        {
            if (parameters is null)
            { throw new ArgumentNullException(nameof(parameters)); }
            if (nullableReceiverInputs.Count == 0)
            { return; }
            HmType Rewrite(HmType node, int parameter, string path)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                if (parameter < 0)
                { throw new ArgumentOutOfRangeException(nameof(parameter)); }
                if (path is null)
                { throw new ArgumentNullException(nameof(path)); }
                node = node.Root;
                if (nullableReceiverInputs.Contains((parameter, path)) && node.IsVariable)
                {
                    var optional = new HmType(CSharpTypes.NullableParameter, node);
                    defaultValueAnnotations.Add(optional);
                    return optional;
                }
                if (node.Arguments.Length == 0)
                { return node; }
                return node.WithArguments(node.Arguments.Select((argument, index) =>
                {
                    if (argument is null)
                    { throw new ArgumentNullException(nameof(argument)); }
                    if (index < 0)
                    { throw new ArgumentOutOfRangeException(nameof(index)); }
                    return Rewrite(argument, parameter, path + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }).ToArray());
            }
            for (var i = 0; i < parameters.Length; i++)
            { parameters[i] = Rewrite(parameters[i], i, ""); }
        }

        private void ApplyValueReceiverInputs(HmType[] parameters)
        {
            if (parameters is null)
            { throw new ArgumentNullException(nameof(parameters)); }
            foreach (var (parameter, path) in valueReceiverInputs)
            {
                if (path is null)
                { throw new InvalidOperationException("Missing value receiver path."); }
                if (parameter < 0 || parameter >= parameters.Length)
                { throw new InferenceException("Value receiver parameter is outside the signature."); }
                var node = parameters[parameter];
                foreach (var step in path.Split(ReceiverTypePathSeparators, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(step, System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var index)
                        || index >= ConversionCore(node).Arguments.Length)
                    { throw new InferenceException("Value receiver path no longer matches the signature."); }
                    node = ConversionCore(node).Arguments[index];
                }
                var variable = ConversionCore(node);
                if (!variable.IsVariable)
                { throw new InferenceException("Value receiver is fixed by the method body."); }
                variable.IsValueType = true;
                RequireKeyword(variable, "struct");
            }
        }

        private MethodResult? RefineNullableReceiverInputs(Compilation compilation, MethodDeclarationSyntax original,
            MethodDeclarationSyntax candidate, HashSet<(int Parameter, string Path)> pins)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (candidate is null)
            { throw new ArgumentNullException(nameof(candidate)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            if (nullableReceiverCandidates.Count == 0)
            { return null; }
            var warnings = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            {
                if (diagnostic is null)
                { throw new ArgumentNullException(nameof(diagnostic)); }
                return !diagnostic.IsSuppressed && diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error
                    && diagnostic.Id is ("CS8603" or "CS8604" or "CS8619" or "CS8620") && diagnostic.Location.SourceTree == candidate.SyntaxTree;
            }).ToArray();
            if (warnings.Length == 0)
            { return null; }
            var before = original.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            var after = candidate.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            if (before.Length != after.Length)
            { return null; }
            var next = new HashSet<(int Parameter, string Path)>(nullableReceiverInputs);
            var valueFallbacks = new HashSet<(int Parameter, string Path)>();
            var candidateModel = compilation.GetSemanticModel(candidate.SyntaxTree);
            var originalSymbol = model.GetDeclaredSymbol(original, cancellationToken);
            bool HasConcreteValueOrigin(int parameter, string path)
            {
                if (path is null)
                { throw new ArgumentNullException(nameof(path)); }
                if (originalSymbol is null || parameter < 0 || parameter >= originalSymbol.Parameters.Length)
                { return false; }
                var node = CSharpTypes.Rigid(originalSymbol.Parameters[parameter].Type);
                foreach (var step in path.Split(ReceiverTypePathSeparators, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(step, System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var slot)
                        || slot >= node.Root.Arguments.Length)
                    { return false; }
                    node = node.Root.Arguments[slot];
                }
                var source = node.Root.CSharpSymbol;
                return source is { IsValueType: true, IsRefLikeType: false }
                    && source is not ITypeParameterSymbol
                    && source is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
            }
            foreach (var input in nullableReceiverCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = Array.FindIndex(before, call =>
                {
                    if (call is null)
                    { throw new ArgumentNullException(nameof(call)); }
                    return call.Span == input.Call;
                });
                if (index < 0 || input.Argument >= after[index].ArgumentList.Arguments.Count
                    || !SyntaxFactory.AreEquivalent(before[index].Expression, after[index].Expression))
                { continue; }
                var argument = after[index].ArgumentList.Arguments[input.Argument];
                var expression = argument.Expression;
                if (pins.Contains((input.Parameter, input.Path)) || valueReceiverInputs.Contains((input.Parameter, input.Path)))
                { continue; }
                var matched = warnings.Where(warning =>
                    {
                        if (warning is null)
                        { throw new ArgumentNullException(nameof(warning)); }
                        if (warning.Id is "CS8603" or "CS8619")
                        {
                            return candidateModel.GetOperation(argument, cancellationToken) is IArgumentOperation { Parameter: { } lambdaParameter }
                            && IsLambdaReturnWarning(lambdaParameter.Type, expression, input.TypePath, warning,
                                candidateModel, compilation, cancellationToken);
                        }
                        return (warning.Location.SourceSpan == expression.Span
                                || warning.Location.SourceSpan == StorageExpression(expression).Span)
                            && (input.TypePath.Length == 0 || candidateModel.GetOperation(argument, cancellationToken) is IArgumentOperation { Parameter: { } parameter }
                                && candidateModel.GetTypeInfo(expression, cancellationToken).Type is { } actual
                                && HasAnnotatedReceiverLeaf(parameter.Type, actual, input.TypePath, compilation));
                    }).ToArray();
                if (matched.Length == 0)
                { continue; }
                var origin = (input.Parameter, input.Path);
                next.Add(origin);
                if (matched.Any(warning =>
                    { if (warning is null) { throw new ArgumentNullException(nameof(warning)); } return warning.Id == "CS8619"; })
                    && HasConcreteValueOrigin(origin.Parameter, origin.Path))
                { valueFallbacks.Add(origin); }
            }
            if (next.Count == nullableReceiverInputs.Count)
            { return null; }
            var widenedPins = new HashSet<(int Parameter, string Path)>(pins);
            var widened = new Session(model, cancellationToken, nullableReadFlow,
                nullableCollectionInputs: nullableCollectionInputs, nullableInvocationResults: nullableInvocationResults,
                nullableReceiverInputs: next, valueReceiverInputs: valueReceiverInputs).Analyze(original, widenedPins);
            if (widened.Status == "Suggestion")
            { pins.UnionWith(widenedPins); return widened; }
            foreach (var origin in valueFallbacks.OrderBy(item => item.Parameter).ThenBy(item => item.Path, StringComparer.Ordinal))
            {
                var trialValues = new HashSet<(int Parameter, string Path)>(valueReceiverInputs) { origin };
                var trialPins = new HashSet<(int Parameter, string Path)>(pins);
                MethodResult trial;
                try
                {
                    trial = new Session(model, cancellationToken, nullableReadFlow,
                        nullableCollectionInputs: nullableCollectionInputs, nullableInvocationResults: nullableInvocationResults,
                        nullableReceiverInputs: nullableReceiverInputs, valueReceiverInputs: trialValues).Analyze(original, trialPins);
                }
                catch (InferenceException)
                { continue; }
                if (trial.Status != "Suggestion")
                { continue; }
                pins.UnionWith(trialPins);
                return trial;
            }
            pins.UnionWith(widenedPins);
            return widened;
        }

        private static readonly char[] ReceiverTypePathSeparators = ['/'];

        private static bool IsLambdaReturnWarning(ITypeSymbol formal, ExpressionSyntax expression, string typePath, Diagnostic warning,
            SemanticModel candidateModel, Compilation compilation, CancellationToken cancellationToken)
        {
            if (formal is null)
            { throw new ArgumentNullException(nameof(formal)); }
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (typePath is null)
            { throw new ArgumentNullException(nameof(typePath)); }
            if (warning is null)
            { throw new ArgumentNullException(nameof(warning)); }
            if (candidateModel is null)
            { throw new ArgumentNullException(nameof(candidateModel)); }
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            var expressionDefinition = compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1");
            if (formal is INamedTypeSymbol quote
                && SymbolEqualityComparer.Default.Equals(quote.OriginalDefinition, expressionDefinition))
            {
                if (!typePath.StartsWith("/0/", StringComparison.Ordinal)
                    || quote.TypeArguments.Length != 1)
                { return false; }
                formal = quote.TypeArguments[0];
                typePath = typePath.Substring(2);
            }
            if (formal is not INamedTypeSymbol { TypeKind: TypeKind.Delegate } delegateType
                || delegateType.OriginalDefinition.DelegateInvokeMethod is not { ReturnsVoid: false, RefKind: RefKind.None } invoke
                || delegateType.DelegateInvokeMethod is not { ReturnsVoid: false, RefKind: RefKind.None } constructedInvoke)
            { return false; }
            while (expression is ParenthesizedExpressionSyntax parentheses)
            { expression = parentheses.Expression; }
            if (expression is not AnonymousFunctionExpressionSyntax lambda
                || lambda is ParenthesizedLambdaExpressionSyntax { ReturnType: not null })
            { return false; }
            var steps = typePath.Split(ReceiverTypePathSeparators, StringSplitOptions.RemoveEmptyEntries);
            var parameterOwner = delegateType.OriginalDefinition;
            var ownerDepth = 0;
            while (parameterOwner.ContainingType is { } containingOwner
                && ownerDepth < steps.Length && steps[ownerDepth] == "0")
            {
                parameterOwner = containingOwner.OriginalDefinition;
                ownerDepth++;
            }
            if (ownerDepth >= steps.Length
                || !int.TryParse(steps[ownerDepth], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var ownerSlot))
            { return false; }
            var returnIndex = ownerSlot - (parameterOwner.ContainingType is null ? 0 : 1);
            if (returnIndex < 0 || returnIndex >= parameterOwner.TypeParameters.Length)
            { return false; }
            var templatePaths = new List<string>();
            void FindTemplatePaths(ITypeSymbol node, string path)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                if (path is null)
                { throw new ArgumentNullException(nameof(path)); }
                if (node is ITypeParameterSymbol parameter)
                {
                    if (parameter.Ordinal == returnIndex
                        && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, parameterOwner))
                    { templatePaths.Add(path); }
                    return;
                }
                if (node is IArrayTypeSymbol array)
                { FindTemplatePaths(array.ElementType, path + "/0"); return; }
                if (node is not INamedTypeSymbol named)
                { return; }
                var arguments = named.IsTupleType ? named.TupleElements.Select(element =>
                {
                    if (element is null)
                    { throw new ArgumentNullException(nameof(element)); }
                    return element.Type;
                }).ToArray() : named.TypeArguments.ToArray();
                for (var index = 0; index < arguments.Length; index++)
                { FindTemplatePaths(arguments[index], path + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            }
            FindTemplatePaths(invoke.ReturnType, "");
            if (templatePaths.Count == 0)
            { return false; }
            ITypeSymbol expectedReturn = constructedInvoke.ReturnType;
            if (lambda.AsyncKeyword.RawKind != 0)
            {
                if (expectedReturn is not INamedTypeSymbol { TypeArguments.Length: 1 } task)
                { return false; }
                var standardTask = task.OriginalDefinition.MetadataName is "Task`1" or "ValueTask`1"
                    && task.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";
                if (!standardTask)
                {
                    var builderAttribute = candidateModel.GetOperation(lambda, cancellationToken)
                        is IAnonymousFunctionOperation function
                        ? BuilderAttribute(function.Symbol, task) : null;
                    if (builderAttribute is null)
                    { return false; }
                    try
                    {
                        var (_, usesResult) = ConstructBuilder(builderAttribute, task);
                        if (!usesResult)
                        { return false; }
                    }
                    catch (InferenceException)
                    { return false; }
                }
                expectedReturn = task.TypeArguments[0];
            }
            var returnPaths = new List<string>();
            foreach (var templatePath in templatePaths)
            {
                if (templatePath is null)
                { throw new InvalidOperationException("Missing delegate return template path."); }
                var returnPath = templatePath;
                for (var depth = ownerDepth + 1; depth < steps.Length; depth++)
                { returnPath += "/" + steps[depth]; }
                if (lambda.AsyncKeyword.RawKind != 0)
                {
                    if (returnPath == "/0")
                    { returnPath = ""; }
                    else if (returnPath.StartsWith("/0/", StringComparison.Ordinal))
                    { returnPath = returnPath.Substring(2); }
                    else
                    { continue; }
                }
                returnPaths.Add(returnPath);
            }
            if (returnPaths.Count == 0)
            { return false; }
            var body = lambda switch
            {
                LambdaExpressionSyntax lambdaExpression => lambdaExpression.Body,
                AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
                _ => null
            };
            if (body is ExpressionSyntax returned)
            { return HasReturnWarning(returned); }
            return body is BlockSyntax block && block.DescendantNodes(node =>
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                return node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
            }).OfType<ReturnStatementSyntax>().Any(statement => { if (statement is null) { throw new ArgumentNullException(nameof(statement)); } return statement.Expression is { } value && HasReturnWarning(value); });

            bool HasReturnWarning(ExpressionSyntax returned)
            {
                if (returned is null)
                { throw new ArgumentNullException(nameof(returned)); }
                while (returned is ParenthesizedExpressionSyntax wrapped)
                { returned = wrapped.Expression; }
                if (returned.Span == warning.Location.SourceSpan)
                {
                    if (warning.Id == "CS8603")
                    { return returnPaths.Contains(""); }
                    if (warning.Id != "CS8619" || candidateModel.GetTypeInfo(returned, cancellationToken).Type is not { } actualReturn)
                    { return false; }
                    foreach (var returnPath in returnPaths)
                    {
                        if (returnPath is null)
                        { throw new InvalidOperationException("Missing delegate return path."); }
                        if (returnPath.Length != 0 && HasAnnotatedReceiverLeaf(expectedReturn, actualReturn, returnPath, compilation))
                        { return true; }
                    }
                }
                if (warning.Id != "CS8619")
                { return false; }
                return returned switch
                {
                    ConditionalExpressionSyntax conditional => HasReturnWarning(conditional.WhenTrue)
                        || HasReturnWarning(conditional.WhenFalse),
                    SwitchExpressionSyntax selection => selection.Arms.Any(arm =>
                    { if (arm is null) { throw new ArgumentNullException(nameof(arm)); } return HasReturnWarning(arm.Expression); }),
                    _ => false
                };
            }
        }

        private static bool HasAnnotatedReceiverLeaf(ITypeSymbol formal, ITypeSymbol actual, string typePath, Compilation compilation)
        {
            if (formal is null)
            { throw new ArgumentNullException(nameof(formal)); }
            if (actual is null)
            { throw new ArgumentNullException(nameof(actual)); }
            if (typePath is null)
            { throw new ArgumentNullException(nameof(typePath)); }
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            var steps = typePath.Split(ReceiverTypePathSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (var depth = 0; depth < steps.Length; depth++)
            {
                if (!int.TryParse(steps[depth], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var index))
                { return false; }
                if (formal is IArrayTypeSymbol formalArray && actual is IArrayTypeSymbol actualArray
                    && formalArray.Rank == actualArray.Rank && index == 0)
                {
                    formal = formalArray.ElementType;
                    actual = actualArray.ElementType;
                    continue;
                }
                if (formal is not INamedTypeSymbol formalNamed)
                { return false; }
                if (formalNamed.IsTupleType && actual is INamedTypeSymbol { IsTupleType: true } actualTuple
                    && formalNamed.TupleElements.Length == actualTuple.TupleElements.Length
                    && index < formalNamed.TupleElements.Length)
                {
                    formal = formalNamed.TupleElements[index].Type;
                    actual = actualTuple.TupleElements[index].Type;
                    continue;
                }
                if (formalNamed.IsTupleType || !formalNamed.IsGenericType || index >= formalNamed.TypeArguments.Length)
                { return false; }
                if (actual is INamedTypeSymbol actualNamed)
                {
                    INamedTypeSymbol? projected = actualNamed;
                    while (projected is not null && !SymbolEqualityComparer.Default.Equals(formalNamed.OriginalDefinition, projected.OriginalDefinition))
                    { projected = projected.BaseType; }
                    if (projected is null)
                    {
                        foreach (var implemented in actualNamed.AllInterfaces)
                        {
                            if (SymbolEqualityComparer.Default.Equals(formalNamed.OriginalDefinition, implemented.OriginalDefinition))
                            { projected = implemented; break; }
                        }
                    }
                    if (projected is not null && index < projected.TypeArguments.Length
                        && compilation.ClassifyCommonConversion(actualNamed, formalNamed).IsImplicit)
                    {
                        formal = formalNamed.TypeArguments[index];
                        actual = projected.TypeArguments[index];
                        continue;
                    }
                }
                if (actual is IArrayTypeSymbol { Rank: 1 } sourceArray && index == 0
                    && formalNamed.TypeKind == TypeKind.Interface && formalNamed.TypeArguments.Length == 1
                    && compilation.ClassifyCommonConversion(sourceArray, formalNamed).IsImplicit)
                {
                    formal = formalNamed.TypeArguments[0];
                    actual = sourceArray.ElementType;
                    continue;
                }
                return false;
            }
            return CSharpTypes.Rigid(actual).Name is CSharpTypes.NullableParameter or CSharpTypes.NullableReference;
        }

        private HmType ApplyInvocationAnnotations(ExpressionSyntax syntax, HmType value)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (!nullableInvocationResults.TryGetValue(syntax.Span, out var paths))
            { return value; }
            return Rewrite(value, "");

            HmType Rewrite(HmType node, string path)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                if (path is null)
                { throw new ArgumentNullException(nameof(path)); }
                node = node.Root;
                var core = ConversionCore(node);
                var rebuilt = core.Arguments.Length == 0 ? core : core.WithArguments(core.Arguments.Select((argument, index) =>
                {
                    if (argument is null)
                    { throw new ArgumentNullException(nameof(argument)); }
                    if (index < 0)
                    { throw new ArgumentOutOfRangeException(nameof(index)); }
                    return Rewrite(argument, path + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }).ToArray());
                if (defaultValueAnnotations.Contains(core))
                { defaultValueAnnotations.Add(rebuilt); }
                if (node.Name is CSharpTypes.NullableParameter or CSharpTypes.NullableReference)
                {
                    var annotated = node.WithArguments(rebuilt);
                    if (defaultValueAnnotations.Contains(node))
                    { defaultValueAnnotations.Add(annotated); }
                    return annotated;
                }
                if (!paths.Contains(path) || rebuilt.IsValueType == true || rebuilt.Name == CSharpTypes.NullableValue)
                { return rebuilt; }
                var result = new HmType(rebuilt.IsVariable || rebuilt.CSharpSymbol is ITypeParameterSymbol ? CSharpTypes.NullableParameter : CSharpTypes.NullableReference, rebuilt);
                if (result.Name == CSharpTypes.NullableParameter)
                { defaultValueAnnotations.Add(result); }
                return result;
            }
        }

        private MethodResult? RefineInvocationResults(Compilation compilation, MethodDeclarationSyntax original, MethodDeclarationSyntax candidate, HashSet<(int Parameter, string Path)> pins)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (candidate is null)
            { throw new ArgumentNullException(nameof(candidate)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            if (invocationResultShapes.Count == 0)
            { return null; }
            var before = original.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            var after = candidate.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            if (before.Length != after.Length)
            { return null; }
            var next = nullableInvocationResults.ToDictionary(entry => entry.Key, entry => new HashSet<string>(entry.Value));
            var candidateModel = compilation.GetSemanticModel(candidate.SyntaxTree);
            var changed = false;
            for (var i = 0; i < before.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!invocationResultShapes.TryGetValue(before[i].Span, out var inferred)
                    || !SyntaxFactory.AreEquivalent(before[i].Expression, after[i].Expression)
                    || candidateModel.GetTypeInfo(after[i], cancellationToken).Type is not { } type)
                { continue; }
                var paths = new HashSet<string>();
                Collect(CSharpTypes.Rigid(type), inferred, "", paths);
                if (paths.Count == 0)
                { continue; }
                if (!next.TryGetValue(before[i].Span, out var retained))
                { retained = []; next.Add(before[i].Span, retained); }
                foreach (var path in paths)
                { changed |= retained.Add(path); }
            }
            return !changed ? null : new Session(model, cancellationToken, nullableReadFlow,
                nullableCollectionInputs: nullableCollectionInputs, nullableInvocationResults: next,
                nullableReceiverInputs: nullableReceiverInputs, valueReceiverInputs: valueReceiverInputs).Analyze(original, pins);

            static void Collect(HmType actual, HmType inferred, string path, HashSet<string> paths)
            {
                if (actual is null)
                { throw new ArgumentNullException(nameof(actual)); }
                if (inferred is null)
                { throw new ArgumentNullException(nameof(inferred)); }
                if (path is null)
                { throw new ArgumentNullException(nameof(path)); }
                if (paths is null)
                { throw new ArgumentNullException(nameof(paths)); }
                actual = actual.Root;
                inferred = inferred.Root;
                // Root flow is handled by return contracts; refine nested arguments here.
                if (path.Length != 0 && actual.Name is CSharpTypes.NullableParameter or CSharpTypes.NullableReference
                    && inferred.Name is not (CSharpTypes.NullableParameter or CSharpTypes.NullableReference or CSharpTypes.NullableValue))
                { paths.Add(path); }
                actual = ConversionCore(actual);
                inferred = ConversionCore(inferred);
                if (actual.Name != inferred.Name || actual.Arguments.Length != inferred.Arguments.Length)
                { return; }
                for (var i = 0; i < actual.Arguments.Length; i++)
                { Collect(actual.Arguments[i], inferred.Arguments[i], path + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), paths); }
            }
        }

        private HmType ApplyNullabilityContract(ITypeSymbol returnType, HmType value, IEnumerable<AttributeData> attributes, bool forStorage,
            IEnumerable<IArgumentOperation>? arguments = null, IReadOnlyDictionary<int, HmType>? argumentValues = null,
            bool preferNotNull = false, bool allowBareRelational = false)
        {
            if (returnType is null)
            { throw new ArgumentNullException(nameof(returnType)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (attributes is null)
            { throw new ArgumentNullException(nameof(attributes)); }
            return ApplyNullabilityContract(returnType, value, attributes, forStorage, out _, arguments, argumentValues,
                preferNotNull, allowBareRelational);
        }

        private HmType ApplyNullabilityContract(ITypeSymbol returnType, HmType value, IEnumerable<AttributeData> attributes, bool forStorage,
            out bool conditionalNotNull, IEnumerable<IArgumentOperation>? arguments = null,
            IReadOnlyDictionary<int, HmType>? argumentValues = null, bool preferNotNull = false, bool allowBareRelational = false)
        {
            if (returnType is null)
            { throw new ArgumentNullException(nameof(returnType)); }
            if (attributes is null)
            { throw new ArgumentNullException(nameof(attributes)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            conditionalNotNull = false;
            if (forStorage || returnType.SpecialType == SpecialType.System_Void)
            { return value; }
            var maybeNull = false;
            var notNull = false;
            var conditional = false;
            foreach (var attribute in attributes)
            {
                var name = attribute.AttributeClass?.ToDisplayString();
                maybeNull |= name == "System.Diagnostics.CodeAnalysis.MaybeNullAttribute";
                notNull |= name == "System.Diagnostics.CodeAnalysis.NotNullAttribute";
                if (name == "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute" && attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is string parameterName)
                {
                    conditional = true;
                    foreach (var argument in arguments ?? [])
                    {
                        if (argument.Parameter?.Name == parameterName
                            && (ConditionalReturnGuard(argument, argumentValues)
                                || allowBareRelational && argumentValues is not null && argument.Parameter is { } guardParameter
                                && argumentValues.TryGetValue(guardParameter.Ordinal, out var guardValue)
                                && guardValue.Root.IsVariable
                                && ReferenceEquals(ConversionCore(value), guardValue.Root)))
                        { conditionalNotNull = true; }
                    }
                }
            }
            if (conditionalNotNull || notNull && (preferNotNull || !maybeNull))
            { return ConversionCore(value); }
            if (!maybeNull)
            {
                if (conditional && value.Root.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter)
                { defaultValueAnnotations.Add(value.Root); }
                return value;
            }
            return ApplyMaybeNullContract(returnType, value);
        }

        private HmType ApplyNullableReadFlow(ExpressionSyntax syntax, ISymbol symbol, HmType value, bool forStorage)
        {
            if (syntax is null)
            { throw new ArgumentNullException(nameof(syntax)); }
            if (symbol is null)
            { throw new ArgumentNullException(nameof(symbol)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (forStorage || !nullableReadFlow.TryGetValue(syntax.Span, out var state))
            { return value; }
            var type = symbol switch { ILocalSymbol local => local.Type, IParameterSymbol parameter => parameter.Type, IFieldSymbol field => field.Type, IPropertySymbol property => property.Type, IMethodSymbol method => method.ReturnType, _ => null };
            return state == NullableFlowState.NotNull ? ConversionCore(value)
                : state == NullableFlowState.MaybeNull && type is not null ? ApplyMaybeNullContract(type, value) : value;
        }

        private static bool HasMemberPostconditions(ISymbol? symbol)
        {
            if (symbol is not (IMethodSymbol or IPropertySymbol))
            { return false; }
            IEnumerable<AttributeData> attributes = symbol.GetAttributes();
            if (symbol is IPropertySymbol { GetMethod: { } getter })
            { attributes = attributes.Concat(getter.GetAttributes()); }
            return attributes.Any(attribute =>
            {
                if (attribute is null)
                { throw new ArgumentNullException(nameof(attribute)); }
                return attribute.AttributeClass?.ToDisplayString() is "System.Diagnostics.CodeAnalysis.MemberNotNullAttribute"
                    or "System.Diagnostics.CodeAnalysis.MemberNotNullWhenAttribute";
            });
        }

        private readonly HashSet<int> forwardedDisallowNull = [];
        private readonly Dictionary<TextSpan, HmType> disallowNullArguments = [];
        private readonly HashSet<(HmType Shape, string Keyword)> constrainedDisallowNull = [];

        private void RecordDisallowNullArgument(IArgumentOperation argument, HmType value)
        {
            if (argument is null)
            { throw new ArgumentNullException(nameof(argument)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (argument.Parameter is { RefKind: not RefKind.Out } parameter && HasDisallowNull(parameter))
            { disallowNullArguments[(argument.Syntax is ArgumentSyntax syntax ? syntax.Expression : argument.Value.Syntax).Span] = value; }
        }

        private bool RefineDisallowNullInputs(Compilation compilation, MethodDeclarationSyntax original, MethodDeclarationSyntax candidate)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (candidate is null)
            { throw new ArgumentNullException(nameof(candidate)); }
            var diagnostics = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            {
                if (diagnostic is null)
                { throw new ArgumentNullException(nameof(diagnostic)); }
                return !diagnostic.IsSuppressed && diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error
                    && diagnostic.Id is "CS8601" or "CS8604" or "CS8607" && diagnostic.Location.SourceTree == candidate.SyntaxTree;
            }).ToArray();
            if (diagnostics.Length == 0)
            { return false; }
            var before = ContractInputSites(original);
            var after = ContractInputSites(candidate);
            if (before.Length != after.Length)
            { return false; }
            var changed = false;
            for (var i = 0; i < before.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!diagnostics.Any(diagnostic =>
                {
                    if (diagnostic is null)
                    { throw new ArgumentNullException(nameof(diagnostic)); }
                    return after[i].Value.Span.Contains(diagnostic.Location.SourceSpan) || after[i].Target?.Span.Contains(diagnostic.Location.SourceSpan) == true || after[i].Target is not null && diagnostic.Location.SourceSpan.Contains(after[i].Value.Span);
                }) || !disallowNullArguments.TryGetValue(before[i].Value.Span, out var actual))
                { continue; }
                if (StorageExpression(before[i].Value) is IdentifierNameSyntax identifier
                    && model.GetSymbolInfo(identifier, cancellationToken).Symbol is IParameterSymbol source
                    && SymbolEqualityComparer.Default.Equals(source.ContainingSymbol, currentMethod))
                {
                    if (!HasDisallowNull(source))
                    { changed |= forwardedDisallowNull.Add(source.Ordinal); }
                }
                else
                {
                    if (ForwardAliasedDisallowNull(compilation, candidate, i, actual))
                    { return true; }
                    var required = actual.Root;
                    var keyword = "notnull";
                    if (required.Name == CSharpTypes.NullableParameter && HasDefaultValueAnnotation(required)
                        && model.GetTypeInfo(before[i].Value, cancellationToken).Type is { IsValueType: true } originalType
                        && originalType is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                    {
                        required = required.Arguments[0].Root;
                        keyword = "struct";
                    }
                    if (required.IsVariable && constrainedDisallowNull.Add((required, keyword)))
                    {
                        RequireKeyword(required, keyword);
                        changed = true;
                    }
                }
                if (changed)
                { break; }
            }
            return changed;
        }

        private bool ForwardAliasedDisallowNull(Compilation compilation, MethodDeclarationSyntax candidate, int argumentIndex, HmType actual)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (candidate is null)
            { throw new ArgumentNullException(nameof(candidate)); }
            if (actual is null)
            { throw new ArgumentNullException(nameof(actual)); }
            if (argumentIndex < 0)
            { throw new ArgumentOutOfRangeException(nameof(argumentIndex)); }
            if (!actual.Root.IsVariable || currentMethod is null)
            { return false; }
            var inputs = new HashSet<int>(currentMethod.Parameters.Where(parameter =>
            {
                if (parameter is null)
                { throw new ArgumentNullException(nameof(parameter)); }
                return parameter.RefKind != RefKind.Out && !HasDisallowNull(parameter)
                    && !forwardedDisallowNull.Contains(parameter.Ordinal)
                    && environment.TryGetValue(parameter, out var shape) && shape.Root == actual.Root;
            }).Select(parameter =>
            {
                if (parameter is null)
                { throw new ArgumentNullException(nameof(parameter)); }
                return parameter.Ordinal;
            }));
            if (inputs.Count == 0 || !Accepts(inputs))
            { return false; }
            // Compiler flow, rather than type equality alone, must establish which
            // input contracts actually reach this argument. Remove redundant inputs.
            foreach (var input in inputs.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                inputs.Remove(input);
                if (!Accepts(inputs))
                { inputs.Add(input); }
            }
            forwardedDisallowNull.UnionWith(inputs);
            return inputs.Count != 0;

            bool Accepts(HashSet<int> contracts)
            {
                if (contracts is null)
                { throw new ArgumentNullException(nameof(contracts)); }
                var marker = new SyntaxAnnotation("HM.InputTrial");
                var trial = candidate.WithParameterList(candidate.ParameterList.WithParameters(SyntaxFactory.SeparatedList(candidate.Parameters().Select((parameter, index) =>
                {
                    if (parameter is null)
                    { throw new ArgumentNullException(nameof(parameter)); }
                    if (index < 0)
                    { throw new ArgumentOutOfRangeException(nameof(index)); }
                    return !contracts.Contains(index) ? parameter : parameter.AddAttributeLists(
                        SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Attribute(SyntaxFactory.ParseName("global::System.Diagnostics.CodeAnalysis.DisallowNull")))));
                })))).WithAdditionalAnnotations(marker);
                var tree = candidate.SyntaxTree;
                var replacement = tree.WithRootAndOptions(tree.GetRoot(cancellationToken).ReplaceNode(candidate, trial), tree.Options);
                var rewritten = compilation.ReplaceSyntaxTree(tree, replacement);
                if (rewritten.Options.SyntaxTreeOptionsProvider is { } sourceOptions)
                { rewritten = rewritten.WithOptions(rewritten.Options.WithSyntaxTreeOptionsProvider(new CandidateTreeOptions(sourceOptions, tree, replacement))); }
                var argument = ContractInputSites((MethodDeclarationSyntax)replacement.GetRoot(cancellationToken).GetAnnotatedNodes(marker).Single())[argumentIndex];
                return !rewritten.GetDiagnostics(cancellationToken).Any(diagnostic =>
                {
                    if (diagnostic is null)
                    { throw new ArgumentNullException(nameof(diagnostic)); }
                    return !diagnostic.IsSuppressed && diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error
                        && diagnostic.Id is "CS8601" or "CS8604" or "CS8607" && diagnostic.Location.SourceTree == replacement
                        && (argument.Value.Span.Contains(diagnostic.Location.SourceSpan) || argument.Target?.Span.Contains(diagnostic.Location.SourceSpan) == true || argument.Target is not null && diagnostic.Location.SourceSpan.Contains(argument.Value.Span));
                });
            }
        }

        private static (ExpressionSyntax Value, ExpressionSyntax? Target)[] ContractInputSites(MethodDeclarationSyntax method)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            var sites = new List<(ExpressionSyntax Value, ExpressionSyntax? Target)>();
            foreach (var node in method.DescendantNodes())
            {
                if (node is ArgumentSyntax argument)
                { sites.Add((argument.Expression, null)); }
                else if (node is AssignmentExpressionSyntax assignment && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    foreach (var component in StorageComponents(assignment.Left, assignment.Right))
                    { sites.Add((component.Value, component.Target)); }
                }
            }
            return sites.GroupBy(site =>
            {
                return site.Value.Span;
            }).Select(group =>
            {
                if (group is null)
                { throw new ArgumentNullException(nameof(group)); }
                return group.OrderByDescending(site => site.Target is not null).First();
            }).ToArray();
        }

        private static bool HasDisallowNull(IParameterSymbol parameter)
        {
            if (parameter is null)
            { throw new ArgumentNullException(nameof(parameter)); }
            return parameter.GetAttributes().Any(attribute =>
            {
                if (attribute is null)
                { throw new ArgumentNullException(nameof(attribute)); }
                return attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.DisallowNullAttribute";
            });
        }

        private static bool HasNullableOutput(IParameterSymbol parameter)
        {
            if (parameter is null)
            { throw new ArgumentNullException(nameof(parameter)); }
            if (parameter.RefKind is not (RefKind.Ref or RefKind.Out) || parameter.OriginalDefinition.Type is ITypeParameterSymbol { HasValueTypeConstraint: true }
                || parameter.OriginalDefinition.Type is { IsValueType: true } and not ITypeParameterSymbol)
            { return false; }
            var maybeNull = false;
            foreach (var attribute in parameter.GetAttributes())
            {
                if (attribute is null)
                { throw new InvalidOperationException("Unexpected null output attribute."); }
                var name = attribute.AttributeClass?.ToDisplayString();
                maybeNull |= name is "System.Diagnostics.CodeAnalysis.MaybeNullAttribute" or "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute";
            }
            return maybeNull;
        }

        private static HmType OutputStorageType(IParameterSymbol parameter, HmType value)
        {
            if (parameter is null)
            { throw new ArgumentNullException(nameof(parameter)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (!HasNullableOutput(parameter))
            { return value; }
            var shape = value.Root;
            if (shape.Name is CSharpTypes.NullableParameter or CSharpTypes.NullableReference or CSharpTypes.NullableValue)
            { return value; }
            if (shape.IsVariable || shape.CSharpSymbol is ITypeParameterSymbol)
            { return new HmType(CSharpTypes.NullableParameter, shape); }
            return parameter.Type.IsReferenceType ? new HmType(CSharpTypes.NullableReference, shape) : value;
        }

        private static ISymbol NullableReadTarget(ISymbol symbol)
        {
            if (symbol is null)
            { throw new ArgumentNullException(nameof(symbol)); }
            return symbol is IFieldSymbol { CorrespondingTupleField: { } element } ? element : symbol;
        }

        private MethodResult? RefineNullablePostconditions(Compilation compilation, MethodDeclarationSyntax original,
            MethodDeclarationSyntax candidate, HashSet<(int Parameter, string Path)> pins)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (candidate is null)
            { throw new ArgumentNullException(nameof(candidate)); }
            if (pins is null)
            { throw new ArgumentNullException(nameof(pins)); }
            var body = (SyntaxNode?)original.Body ?? original.ExpressionBody;
            var changedBody = (SyntaxNode?)candidate.Body ?? candidate.ExpressionBody;
            if (body is null || changedBody is null)
            { return null; }
            var targets = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var syntax in body.DescendantNodesAndSelf().OfType<ArgumentSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetOperation(syntax, cancellationToken) is not IArgumentOperation { Parameter: { } parameter } argument
                    || !parameter.GetAttributes().Any(attribute =>
                    {
                        if (attribute is null)
                        { throw new ArgumentNullException(nameof(attribute)); }
                        return attribute.AttributeClass?.ToDisplayString() is
                            "System.Diagnostics.CodeAnalysis.MaybeNullAttribute" or "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute"
                            or "System.Diagnostics.CodeAnalysis.NotNullAttribute" or "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute";
                    }))
                { continue; }
                var operand = argument.Value;
                while (operand is IConversionOperation or IDeclarationExpressionOperation)
                { operand = operand is IConversionOperation conversion ? conversion.Operand : ((IDeclarationExpressionOperation)operand).Expression; }
                if (operand is ILocalReferenceOperation local)
                { targets.Add(local.Local); }
                else if (operand is IParameterReferenceOperation input)
                { targets.Add(input.Parameter); }
                else if (operand is IMemberReferenceOperation member)
                { targets.Add(NullableReadTarget(member.Member)); }
            }
            foreach (var assignment in body.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
            {
                if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                { continue; }
                foreach (var component in StorageComponents(assignment.Left, assignment.Right))
                {
                    if (MemberWriteContract(component.Target).AllowNull
                        && model.GetSymbolInfo(component.Target, cancellationToken).Symbol is { } member)
                    { targets.Add(NullableReadTarget(member)); }
                }
            }
            static ExpressionSyntax[] ReadSites(SyntaxNode root)
            {
                if (root is null)
                { throw new ArgumentNullException(nameof(root)); }
                return root.DescendantNodesAndSelf().OfType<ExpressionSyntax>().Where(expression =>
                {
                    if (expression is null)
                    { throw new ArgumentNullException(nameof(expression)); }
                    return expression is SimpleNameSyntax or MemberAccessExpressionSyntax or MemberBindingExpressionSyntax
                        or ElementAccessExpressionSyntax or ElementBindingExpressionSyntax or InvocationExpressionSyntax;
                }).ToArray();
            }
            var before = ReadSites(body);
            var symbols = before.Select(expression =>
            {
                if (expression is null)
                { throw new ArgumentNullException(nameof(expression)); }
                return model.GetSymbolInfo(expression, cancellationToken).Symbol;
            }).ToArray();
            var members = symbols.Any(HasMemberPostconditions);
            var calls = symbols.Any(symbol => symbol is IMethodSymbol { OriginalDefinition.ReturnType: ITypeParameterSymbol });
            if (targets.Count == 0 && !members && !calls)
            { return null; }
            var after = ReadSites(changedBody);
            if (before.Length != after.Length)
            { throw new InferenceException("Cannot correlate nullable postcondition reads."); }
            var candidateModel = compilation.GetSemanticModel(candidate.SyntaxTree);
            var next = new Dictionary<TextSpan, NullableFlowState>();
            for (var i = 0; i < before.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SyntaxFactory.AreEquivalent(before[i], after[i]) && !(before[i] is InvocationExpressionSyntax originalCall && after[i] is InvocationExpressionSyntax candidateCall && SyntaxFactory.AreEquivalent(originalCall.Expression, candidateCall.Expression) && originalCall.ArgumentList.Arguments.Count == candidateCall.ArgumentList.Arguments.Count))
                { throw new InferenceException("Cannot correlate nullable postcondition reads."); }
                if (symbols[i] is not { } symbol || !targets.Contains(NullableReadTarget(symbol)) && !(members && symbol is IFieldSymbol or IPropertySymbol) && !(calls && before[i] is InvocationExpressionSyntax && symbol is IMethodSymbol { OriginalDefinition.ReturnType: ITypeParameterSymbol }))
                { continue; }
                var info = candidateModel.GetTypeInfo(after[i], cancellationToken);
                var state = info.Nullability.FlowState;
                // Unconstrained T can be MaybeNull within its unannotated domain.
                // Only an annotated read introduces optionality beyond that domain.
                if (state == NullableFlowState.MaybeNull && info.Nullability.Annotation == NullableAnnotation.NotAnnotated)
                { state = NullableFlowState.NotNull; }
                // A conditional contract already discharged by inference can
                // still appear MaybeNull in the candidate's Roslyn flow state.
                // Keep the proved bare result instead of adding optionality.
                if (state == NullableFlowState.MaybeNull && conditionalGuaranteedCalls.Contains(before[i].Span)
                    && invocationResultShapes.TryGetValue(before[i].Span, out var inferredCall)
                    && !HasDefaultValueAnnotation(inferredCall)
                    && inferredCall.Root.IsVariable)
                { state = NullableFlowState.NotNull; }
                // Roslyn can expose the pre-flow annotation of a direct conditional
                // operand even when its joined expression retains unannotated T.
                SyntaxNode branch = after[i];
                while (branch.Parent is ParenthesizedExpressionSyntax parentheses)
                { branch = parentheses; }
                if (before[i] is not InvocationExpressionSyntax && branch.Parent is ConditionalExpressionSyntax conditional && conditional.Condition != branch)
                {
                    var joined = candidateModel.GetTypeInfo(conditional, cancellationToken);
                    if (joined.Nullability.Annotation == NullableAnnotation.NotAnnotated
                        && SymbolEqualityComparer.Default.Equals(info.Type, joined.Type))
                    { state = NullableFlowState.NotNull; }
                }
                // A representability bound added for C# 8 must not erase the flow
                // evidence that required that bound in the preceding pass.
                if (state == NullableFlowState.NotNull && nullableReadFlow.TryGetValue(before[i].Span, out var previous)
                    && previous == NullableFlowState.MaybeNull && (info.Type?.IsValueType == true
                        || info.Type is ITypeParameterSymbol { HasValueTypeConstraint: true }))
                { state = previous; }
                if (state != NullableFlowState.None && (before[i] is not InvocationExpressionSyntax || state == NullableFlowState.MaybeNull))
                { next[before[i].Span] = state; }
            }
            static bool SameFlow(Dictionary<TextSpan, NullableFlowState> left, Dictionary<TextSpan, NullableFlowState> right)
            {
                if (left is null)
                { throw new ArgumentNullException(nameof(left)); }
                if (right is null)
                { throw new ArgumentNullException(nameof(right)); }
                return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
            }
            if (SameFlow(nullableReadFlow, next))
            { return null; }
            if (readFlowHistory.Any(previous =>
            {
                if (previous is null)
                { throw new ArgumentNullException(nameof(previous)); }
                return SameFlow(previous, next);
            }))
            { return new(original.Identifier.ValueText, "Rejected", "Nullable postcondition flow refinement does not converge."); }
            readFlowHistory.Add(nullableReadFlow);
            return new Session(model, cancellationToken, next, readFlowHistory, nullableCollectionInputs,
                nullableInvocationResults, nullableReceiverInputs, valueReceiverInputs).Analyze(original, pins);
        }

        private static HmType ApplyInputNullability(IParameterSymbol parameter, HmType value)
        {
            if (parameter is null)
            { throw new ArgumentNullException(nameof(parameter)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (!parameter.GetAttributes().Any(attribute =>
            {
                if (attribute is null)
                { throw new ArgumentNullException(nameof(attribute)); }
                return attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.AllowNullAttribute";
            }))
            { return value; }
            var shape = value.Root;
            if (shape.Name is CSharpTypes.NullableParameter or CSharpTypes.NullableReference or CSharpTypes.NullableValue)
            { return value; }
            // This annotation describes argument acceptance, not a nullable result
            // to emit. It must not constrain an ordinary C# 8 input to struct/class.
            if (shape.IsVariable || shape.CSharpSymbol is ITypeParameterSymbol)
            { return new HmType(CSharpTypes.NullableParameter, shape); }
            return parameter.Type.IsReferenceType ? new HmType(CSharpTypes.NullableReference, shape) : value;
        }

        private HmType ApplyMaybeNullContract(ITypeSymbol returnType, HmType value)
        {
            if (returnType is null)
            { throw new ArgumentNullException(nameof(returnType)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            value = value.Root;
            if (value.Name == CSharpTypes.NullableValue)
            { return value; }
            if (value.Name is CSharpTypes.NullableParameter or CSharpTypes.NullableReference)
            { defaultValueAnnotations.Add(value); return value; }
            if (!value.IsVariable && returnType.IsValueType && returnType is not ITypeParameterSymbol)
            { return value; }
            if (value.IsVariable || value.CSharpSymbol is ITypeParameterSymbol)
            {
                var position = currentMethod?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is MethodDeclarationSyntax declaration
                    ? declaration.ReturnType.SpanStart : 0;
                var legacy = model.SyntaxTree.Options is CSharpParseOptions { LanguageVersion: < LanguageVersion.CSharp9 }
                    && (model.GetNullableContext(position) & NullableContext.AnnotationsEnabled) != 0;
                if (!value.IsVariable && value.CSharpSymbol is ITypeParameterSymbol existing)
                {
                    if (existing.HasValueTypeConstraint || existing.HasUnmanagedTypeConstraint || legacy && !existing.HasReferenceTypeConstraint)
                    { return value; }
                    var annotated = new HmType(CSharpTypes.NullableParameter, value);
                    defaultValueAnnotations.Add(annotated);
                    return annotated;
                }
                if (legacy && returnType.IsReferenceType)
                { RequireKeyword(value, "class"); }
                return DefaultValueAnnotation(value, legacy && !returnType.IsReferenceType);
            }
            if (returnType.IsReferenceType)
            {
                var nullable = new HmType(CSharpTypes.NullableReference, value);
                defaultValueAnnotations.Add(nullable);
                return nullable;
            }
            return value;
        }

        private static bool ConditionalReturnGuard(IArgumentOperation argument, IReadOnlyDictionary<int, HmType>? argumentValues)
        {
            if (argument is null)
            { throw new ArgumentNullException(nameof(argument)); }
            if (argument.ArgumentKind is ArgumentKind.ParamArray or ArgumentKind.ParamCollection)
            { return false; }
            if (argument.Parameter is not { RefKind: not RefKind.Out } parameter)
            { return false; }
            if (argument.ArgumentKind == ArgumentKind.DefaultValue
                || argument.Value.Syntax is ExpressionSyntax defaultSyntax && StorageExpression(defaultSyntax).IsKind(SyntaxKind.DefaultLiteralExpression))
            {
                var declared = parameter.OriginalDefinition.Type;
                if (declared is ITypeParameterSymbol)
                { return false; }
                return argument.Value.ConstantValue.HasValue ? argument.Value.ConstantValue.Value is not null
                    : declared.IsValueType && declared is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
            }
            var sourceArgument = argument.Syntax as ArgumentSyntax
                ?? argument.Syntax.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault();
            if ((sourceArgument?.Expression ?? argument.Syntax as ExpressionSyntax ?? argument.Value.Syntax as ExpressionSyntax) is { } syntax
                && StorageExpression(syntax) is PostfixUnaryExpressionSyntax suppression
                && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            { return true; }
            if (argument.Value.ConstantValue.HasValue)
            { return argument.Value.ConstantValue.Value is not null; }
            if (argumentValues is null || !argumentValues.TryGetValue(parameter.Ordinal, out var value))
            { return false; }
            value = value.Root;
            if (value.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter or CSharpTypes.NullableValue
                || value.CSharpSymbol is IDynamicTypeSymbol)
            { return false; }
            if (value.CSharpSymbol is ITypeParameterSymbol existing)
            {
                return existing.HasValueTypeConstraint || existing.HasUnmanagedTypeConstraint || existing.HasNotNullConstraint
                    || existing.HasReferenceTypeConstraint && existing.ReferenceTypeConstraintNullableAnnotation != NullableAnnotation.Annotated;
            }
            return !value.IsVariable || value.IsValueType == true;
        }

        private INamedTypeSymbol? asyncReturn;

        private readonly Stack<(ITypeSymbol Type, HmType Value)> conditionalReceivers = new();

        private (ITypeSymbol Type, HmType Value) ConditionalReceiver()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (conditionalReceivers.Count == 0)
            { throw new InferenceException("Unresolved conditional receiver."); }
            return conditionalReceivers.Peek();
        }

        private HmType ConditionalBinding(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var receiver = ConditionalReceiver();
            var operation = model.GetOperation(expression, cancellationToken);
            if (expression is ElementBindingExpressionSyntax binding && receiver.Type is IArrayTypeSymbol indexedArray)
            {
                var element = HmType.Variable();
                HmType.Unify(receiver.Value, new("[" + new string(',', indexedArray.Rank - 1) + "]", element));
                foreach (var argument in binding.ArgumentList.Arguments)
                { Anchor(argument.Expression); }
                var indexType = binding.ArgumentList.Arguments.Count == 1 ? model.GetTypeInfo(binding.ArgumentList.Arguments[0].Expression, cancellationToken).Type : null;
                return indexType is INamedTypeSymbol { Name: "Range" } range && range.ContainingNamespace.ToDisplayString() == "System" ? receiver.Value : element;
            }
            if (expression is ElementBindingExpressionSyntax inlineBinding && operation is IInlineArrayAccessOperation inlineAccess)
            { return IndexedInlineArray(inlineBinding.ArgumentList, inlineAccess, receiver.Value); }
            if (operation is IEventReferenceOperation referencedEvent)
            {
                var mapping = PatternOwnerMapping(referencedEvent.Event.ContainingType, receiver.Value, receiver.Type);
                return CSharpTypes.Rigid(referencedEvent.Event.OriginalDefinition.Type, mapping);
            }
            if (operation is IPropertyReferenceOperation property)
            {
                if (receiver.Type is IArrayTypeSymbol array && property.Property.Name is "Length" or "LongLength" or "Rank")
                {
                    HmType.Unify(receiver.Value, new("[" + new string(',', array.Rank - 1) + "]", HmType.Variable()));
                    return CSharpTypes.Rigid(property.Type!);
                }
                var mapping = PatternOwnerMapping(property.Property.ContainingType, receiver.Value, receiver.Type);
                Dictionary<int, HmType> argumentValues = [];
                BindArguments(property.Arguments, property.Property.OriginalDefinition.Parameters.Select(parameter =>
                { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return CSharpTypes.Rigid(parameter.Type, mapping); }).ToArray(), argumentValues);
                return ApplyMemberNullability(property.Property, CSharpTypes.Rigid(property.Property.OriginalDefinition.Type, mapping), arguments: property.Arguments, argumentValues: argumentValues, syntax: expression);
            }
            if (operation is IFieldReferenceOperation field)
            {
                if (receiver.Type is INamedTypeSymbol { IsTupleType: true } tuple)
                {
                    for (var i = 0; i < tuple.TupleElements.Length; i++)
                    { if (tuple.TupleElements[i].Name == field.Field.Name) { return receiver.Value.Root.Arguments[i]; } }
                }
                return ApplyMemberNullability(field.Field, PatternSymbolType(field.Field, receiver.Value, receiver.Type), syntax: expression);
            }
            throw new InferenceException("Unresolved conditional member binding.");
        }



        private void SetAsyncReturn(IMethodSymbol method)
        {
            if (method is null)
            { throw new ArgumentNullException(nameof(method)); }
            asyncReturn = null;
            asyncBuilder = null;
            asyncBuilderResult = null;
            builderUsesResult = false;
            if (!method.IsAsync)
            { return; }
            if (method.ReturnsVoid)
            { ConfigureBuilder(method, null); return; }
            if (method.ReturnType is not INamedTypeSymbol { Arity: <= 1 } named)
            { throw new InferenceException("Invalid async return type."); }
            asyncReturn = named;
            ConfigureBuilder(method, named);
        }

        private HmType WrapAsyncResult(HmType value)
        {
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (asyncReturn is null)
            { return value; }
            var declared = CSharpTypes.Rigid(asyncReturn);
            HmType result = declared;
            if (asyncReturn.Arity == 1)
            {
                var template = declared.Name == CSharpTypes.NullableReference ? declared.Arguments[0] : declared;
                var constructed = CSharpTypes.ReplaceOwnArguments(template, value);
                result = declared.Name == CSharpTypes.NullableReference ? new(CSharpTypes.NullableReference, constructed) : constructed;
            }
            BindBuilderReturn(result, value);
            return result;
        }
        private HmType VoidBody(BlockSyntax body)
        {
            if (body is null)
            { throw new ArgumentNullException(nameof(body)); }
            var flow = Scoped(() => Statements(body.Statements.ToArray()));
            if (flow.ReturnType is not null || flow.Breaks || flow.Continues)
            {
                throw new InferenceException("Invalid void return flow.");
            }
            return new("void");
        }

        private HmType Returned(ExpressionSyntax expression, ITypeSymbol? target = null)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var info = model.GetTypeInfo(expression, cancellationToken);
            var converted = target ?? info.ConvertedType;
            if (target is null)
            {
                foreach (var owner in expression.Ancestors())
                {
                    if (owner is AnonymousFunctionExpressionSyntax lambda)
                    {
                        if (model.GetOperation(lambda, cancellationToken) is IAnonymousFunctionOperation function)
                        {
                            converted = function.Symbol.IsAsync && function.Symbol.ReturnType is INamedTypeSymbol { Arity: 1 } task
                                ? task.TypeArguments[0] : function.Symbol.ReturnType;
                        }
                        break;
                    }
                    if (owner is LocalFunctionStatementSyntax local)
                    {
                        var localSymbol = model.GetDeclaredSymbol(local, cancellationToken);
                        converted = localSymbol is { IsAsync: true, ReturnType: INamedTypeSymbol { Arity: 1 } localTask }
                            ? localTask.TypeArguments[0] : localSymbol?.ReturnType ?? converted;
                        break;
                    }
                    if (owner is MethodDeclarationSyntax)
                    {
                        converted = asyncReturn is null ? currentMethod!.ReturnType : asyncReturn.Arity == 1 ? asyncReturn.TypeArguments[0] : model.Compilation.GetSpecialType(SpecialType.System_Void);
                        break;
                    }
                }
            }
            if (converted is not null && NullReturnHole(expression, converted) is { } hole)
            { return hole; }
            if (converted is not null && ScalarDefaultValue(expression, converted) is { } defaultValue)
            { return BindDeclaredReturn(expression, defaultValue, converted); }
            var joined = expression;
            while (joined is ParenthesizedExpressionSyntax parenthesized)
            { joined = parenthesized.Expression; }
            if (converted is INamedTypeSymbol { TypeKind: TypeKind.Delegate } && joined is AnonymousFunctionExpressionSyntax fixedLambda
                && IsFixedDeclaredReturn(expression))
            { return Lambda(fixedLambda, true); }
            if (converted is not null && joined is ConditionalExpressionSyntax or SwitchExpressionSyntax
                && IsFixedDeclaredReturn(expression) && model.GetOperation(joined, cancellationToken) is not IConditionalOperation { IsRef: true })
            { return JoinedExpression(joined, converted, true); }
            if (expression is TupleExpressionSyntax tupleExpression && converted is INamedTypeSymbol { IsTupleType: true } targetTuple)
            {
                var items = new List<HmType>();
                for (var i = 0; i < tupleExpression.Arguments.Count; i++)
                {
                    items.Add(Returned(tupleExpression.Arguments[i].Expression, targetTuple.TupleElements[i].Type));
                }
                return new("Tuple", items.ToArray());
            }
            if (info.Type is null || converted is null)
            { return Expression(expression); }
            if (IsTargetedInterpolation(expression))
            {
                var inferred = Interpolation(expression).Root;
                if (converted.IsReferenceType && converted.NullableAnnotation == NullableAnnotation.Annotated && inferred.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableParameter))
                { inferred = new(CSharpTypes.NullableReference, inferred); }
                return BindDeclaredReturn(expression, inferred, converted);
            }
            if (info.Type is IDynamicTypeSymbol || converted is IDynamicTypeSymbol)
            {
                _ = Expression(expression);
                return CSharpTypes.Rigid(converted);
            }
            if (IsDynamicIdentityBoundary(info.Type, converted))
            { return DynamicIdentityShape(CSharpTypes.Rigid(info.Type), CSharpTypes.Rigid(converted), Expression(expression)); }
            var sameTuple = info.Type is INamedTypeSymbol { IsTupleType: true } sourceTuple && converted is INamedTypeSymbol { IsTupleType: true } declaredTuple
                && SymbolEqualityComparer.Default.Equals(sourceTuple.TupleUnderlyingType, declaredTuple.TupleUnderlyingType);
            if (SymbolEqualityComparer.Default.Equals(info.Type, converted) || sameTuple || model.ClassifyConversion(expression, converted).IsIdentity)
            {
                var inferred = Expression(expression).Root;
                if (info.Type.IsReferenceType && converted.NullableAnnotation == NullableAnnotation.Annotated && inferred.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableParameter))
                {
                    inferred = new(CSharpTypes.NullableReference, inferred);
                }
                else if (info.Type.IsReferenceType && converted.NullableAnnotation != NullableAnnotation.Annotated
                    && inferred.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter && !HasDefaultValueAnnotation(inferred))
                {
                    while (inferred.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter)
                    { inferred = inferred.Arguments[0].Root; }
                }
                return BindDeclaredReturn(expression, inferred, converted);
            }
            if (converted is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                && SymbolEqualityComparer.Default.Equals(info.Type, nullable.TypeArguments[0]))
            {
                return BindDeclaredReturn(expression, new(CSharpTypes.NullableValue, Expression(expression)), converted);
            }
            if (joined is AnonymousFunctionExpressionSyntax && converted.TypeKind != TypeKind.Delegate)
            {
                // Natural delegates and expression trees retain their observable closed type.
                Anchor(expression);
                return CSharpTypes.Rigid(converted);
            }
            var returnConversion = model.ClassifyConversion(expression, converted);
            var value = Expression(expression);
            var targetShape = IsFixedDeclaredReturn(expression) ? CSharpTypes.Rigid(converted) : CSharpTypes.Erase(converted);
            if (!returnConversion.IsUserDefined || !TryUserConversion(info.Type, converted, value, targetShape, returnConversion.MethodSymbol))
            { BindConvertedArgument(info.Type, converted, value, targetShape, false); }
            conversionTargets.Add((targetShape, converted));
            return BindDeclaredReturn(expression, targetShape, converted);
        }
        private readonly HashSet<HmType> defaultValueAnnotations = [];
        private readonly HashSet<ISymbol> defaultAssignedStorages = new(SymbolEqualityComparer.Default);
        private Dictionary<ISymbol, List<ExpressionSyntax>>? defaultStores;
        private Dictionary<ISymbol, HashSet<ISymbol>>? storageAliases;
        private readonly HashSet<ISymbol> preparedIndirectAliases = new(SymbolEqualityComparer.Default);
        private Dictionary<ISymbol, HmType>? storageLambdaInputs;
        private readonly List<(HmType Shape, ExpressionSyntax Store, InvocationExpressionSyntax Target, bool Exact)> refReturnDefaultDomains = [];

        private bool ConstrainRefReturnDefaults(Compilation changed, MethodDeclarationSyntax original, MethodDeclarationSyntax replacement)
        {
            if (changed is null)
            { throw new ArgumentNullException(nameof(changed)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (replacement is null)
            { throw new ArgumentNullException(nameof(replacement)); }
            var before = (SyntaxNode?)original.Body ?? original.ExpressionBody;
            var after = (SyntaxNode?)replacement.Body ?? replacement.ExpressionBody;
            if (before is null || after is null)
            { return false; }
            var offset = after.SpanStart - before.SpanStart;
            var constrained = new HashSet<HmType>();
            SemanticModel? rewrittenModel = null;
            foreach (var diagnostic in changed.GetDiagnostics(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (diagnostic.Id is not ("CS8600" or "CS8601") || diagnostic.IsSuppressed || diagnostic.Severity is not (DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
                    || diagnostic.Location.SourceTree != replacement.SyntaxTree)
                { continue; }
                var position = diagnostic.Location.SourceSpan;
                foreach (var domain in refReturnDefaultDomains)
                {
                    var start = domain.Store.SpanStart + offset;
                    var shape = ConversionCore(domain.Shape);
                    if (position.Start < start || position.End > start + domain.Store.Span.Length || shape.IsValueType == true)
                    { continue; }
                    if (domain.Exact)
                    {
                        // Tuple conversions report one warning on the enclosing branch.
                        // Inspect each generated ref destination before restricting its domain.
                        if (position.Start != start || position.Length != domain.Store.Span.Length)
                        { continue; }
                        rewrittenModel ??= changed.GetSemanticModel(replacement.SyntaxTree);
                        var targetSpan = new Microsoft.CodeAnalysis.Text.TextSpan(domain.Target.SpanStart + offset, domain.Target.Span.Length);
                        var target = replacement.SyntaxTree.GetRoot(cancellationToken).FindNode(targetSpan, getInnermostNodeForTie: true);
                        if (rewrittenModel.GetOperation(target, cancellationToken) is not IInvocationOperation { TargetMethod.ReturnType.NullableAnnotation: NullableAnnotation.NotAnnotated })
                        { continue; }
                    }
                    if (!constrained.Add(shape))
                    { continue; }
                    RequireKeyword(shape, "struct");
                }
            }
            return constrained.Count != 0;
        }

        private void PrepareIndirectStorage(HmType[] parameters)
        {
            if (parameters is null)
            { throw new ArgumentNullException(nameof(parameters)); }
            if (currentMethod?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
            { return; }
            var candidates = new List<(ExpressionSyntax Target, ExpressionSyntax Literal, ITypeSymbol Type, ISymbol? Alias)>();
            foreach (var variable in declaration.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (variable.Initializer?.Value is RefExpressionSyntax reference && model.GetDeclaredSymbol(variable, cancellationToken) is ILocalSymbol alias
                    && ReferenceStorageOwners(alias).Count == 0 && defaultStores is not null && defaultStores.TryGetValue(alias, out var literals))
                { foreach (var literal in literals) { candidates.Add((reference.Expression, literal, alias.Type, alias)); } }
            }
            foreach (var assignment in declaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                { continue; }
                if (assignment.Right is RefExpressionSyntax reference && model.GetSymbolInfo(StorageExpression(assignment.Left), cancellationToken).Symbol is ILocalSymbol alias
                    && ReferenceStorageOwners(alias).Count == 0 && defaultStores is not null && defaultStores.TryGetValue(alias, out var literals))
                { foreach (var literal in literals) { candidates.Add((reference.Expression, literal, alias.Type, alias)); } }
                else
                {
                    foreach (var component in StorageComponents(assignment.Left, assignment.Right))
                    {
                        if (component.Target is not (MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or InvocationExpressionSyntax))
                        { continue; }
                        if (MemberWriteContract(component.Target).AllowNull)
                        { continue; }
                        var type = StorageType(component.Target);
                        foreach (var literal in StoredAlternatives(component.Value))
                        { if (StoredValueShape(literal, type) is not null) { candidates.Add((component.Target, literal, type, null)); } }
                    }
                }
            }
            foreach (var syntax in declaration.DescendantNodes().OfType<ArgumentSyntax>())
            {
                var target = StorageExpression(syntax.Expression);
                var alias = model.GetSymbolInfo(target, cancellationToken).Symbol as ILocalSymbol;
                if (target is not (MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or InvocationExpressionSyntax)
                    && alias is not { RefKind: not RefKind.None }
                    || model.GetOperation(syntax, cancellationToken) is not IArgumentOperation { Parameter: { } parameter }
                    || !HasNullableOutput(parameter))
                { continue; }
                candidates.Add((target, syntax.Expression, StorageType(target), alias));
            }
            if (candidates.Count == 0 || parameters.Length == 0)
            { return; }
            var template = HmType.Function(parameters, new("void"));
            var fresh = new HmScheme(template, template.FreeVariables()).Instantiate();
            var probe = new Session(model, cancellationToken, nullableCollectionInputs: nullableCollectionInputs,
                nullableInvocationResults: nullableInvocationResults, nullableReceiverInputs: nullableReceiverInputs,
                valueReceiverInputs: valueReceiverInputs)
            { currentMethod = currentMethod, storageLambdaInputs = new(SymbolEqualityComparer.Default) };
            probe.SetIteratorReturn(declaration, currentMethod);
            if (probe.iteratorReturn is null)
            { probe.SetAsyncReturn(currentMethod); }
            var probeParameters = fresh.Arguments.Take(parameters.Length).ToArray();
            probe.currentFunction = HmType.Function(probeParameters, CSharpTypes.Erase(currentMethod.ReturnType));
            for (var i = 0; i < parameters.Length; i++)
            { probe.environment.Add(currentMethod.Parameters[i], probeParameters[i]); }
            var resolving = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var boundDeconstructions = new HashSet<int>();
            var boundStorageQueries = new HashSet<int>();
            var boundPatterns = new HashSet<int>();
            void BindCallbackParameter(IParameterSymbol parameter)
            {
                if (parameter is null)
                { throw new ArgumentNullException(nameof(parameter)); }
                if (!probe.storageLambdaInputs.TryGetValue(parameter, out var shape))
                {
                    if (!resolving.Add(parameter))
                    { throw new InferenceException("Cyclic callback storage context."); }
                    var site = parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
                    var lambda = site?.AncestorsAndSelf().OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault();
                    var context = lambda?.Ancestors().OfType<ExpressionSyntax>().FirstOrDefault(expression =>
                    { if (expression is null) { throw new ArgumentNullException(nameof(expression)); } return expression is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax; })
                        ?? throw new InferenceException("Callback storage has no call context.");
                    foreach (var identifier in context.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                    {
                        var dependency = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
                        if (dependency is not (ILocalSymbol or IParameterSymbol))
                        { continue; }
                        var declared = dependency.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
                        if (declared?.SyntaxTree == context.SyntaxTree && context.Span.Contains(declared.Span))
                        { continue; }
                        BindLocals(identifier);
                    }
                    if (!probe.storageLambdaInputs.TryGetValue(parameter, out shape))
                    {
                        foreach (var owned in probe.environment.Keys.ToArray())
                        {
                            var declared = owned.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
                            if (declared?.SyntaxTree == context.SyntaxTree && context.Span.Contains(declared.Span))
                            { probe.environment.Remove(owned); }
                        }
                        _ = probe.Expression(context);
                    }
                    if (!probe.storageLambdaInputs.TryGetValue(parameter, out shape))
                    { throw new InferenceException("Unresolved callback storage parameter."); }
                    resolving.Remove(parameter);
                }
                probe.environment[parameter] = shape;
            }
            bool BindPatternOwner(SyntaxNode site)
            {
                if (site is null)
                { throw new ArgumentNullException(nameof(site)); }
                foreach (var ancestor in site.AncestorsAndSelf())
                {
                    var binding = ancestor switch
                    {
                        IsPatternExpressionSyntax test => (test.Pattern, test.Expression),
                        SwitchExpressionArmSyntax { Parent: SwitchExpressionSyntax selection } arm => (arm.Pattern, selection.GoverningExpression),
                        CasePatternSwitchLabelSyntax { Parent.Parent: SwitchStatementSyntax selection } label => (label.Pattern, selection.Expression),
                        _ => ((PatternSyntax?)null, (ExpressionSyntax?)null)
                    };
                    if (binding.Item1 is not { } pattern || binding.Item2 is not { } input || !pattern.Span.Contains(site.Span))
                    { continue; }
                    if (boundPatterns.Add(pattern.SpanStart))
                    { BindLocals(input); probe.Pattern(pattern, probe.Expression(input)); }
                    return true;
                }
                return false;
            }
            void BindDeconstructed(AssignmentExpressionSyntax assignment)
            {
                if (assignment is null)
                { throw new ArgumentNullException(nameof(assignment)); }
                if (!boundDeconstructions.Add(assignment.SpanStart))
                { return; }
                BindLocals(assignment.Right);
                BindLocals(assignment.Left);
                var original = probe.DeconstructionSourceType(assignment);
                probe.BindDeconstruction(assignment.Left, probe.DeconstructionValue(assignment.Right, original), original, AssignmentDeconstructionBinding(model, assignment));
            }
            void BindLocals(SyntaxNode syntax)
            {
                if (syntax is null)
                { throw new ArgumentNullException(nameof(syntax)); }
                foreach (var identifier in syntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                {
                    var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
                    if (symbol is not null && probe.environment.ContainsKey(symbol))
                    { continue; }
                    if (symbol is IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } } callback)
                    { BindCallbackParameter(callback); continue; }
                    if (symbol is not ILocalSymbol local)
                    { continue; }
                    if (!resolving.Add(local))
                    { throw new InferenceException("Cyclic storage initializer."); }
                    var site = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)
                        ?? throw new InferenceException("Storage owner has no declaration.");
                    var loop = site.AncestorsAndSelf().OfType<CommonForEachStatementSyntax>().FirstOrDefault(candidate =>
                    {
                        if (candidate is null)
                        { throw new ArgumentNullException(nameof(candidate)); }
                        return candidate is ForEachStatementSyntax simple && SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(simple, cancellationToken), local)
                            || candidate is ForEachVariableStatementSyntax deconstructed && deconstructed.Variable.Span.Contains(site.Span);
                    });
                    var deconstruction = site.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault(candidate =>
                    {
                        if (candidate is null)
                        { throw new ArgumentNullException(nameof(candidate)); }
                        return candidate.Left is DeclarationExpressionSyntax or TupleExpressionSyntax && candidate.Left.Span.Contains(site.Span);
                    });
                    var output = site.AncestorsAndSelf().OfType<DeclarationExpressionSyntax>().FirstOrDefault();
                    if (loop is not null)
                    { BindLocals(loop.Expression); probe.BindIteration(loop); }
                    else if (deconstruction is not null)
                    { BindDeconstructed(deconstruction); }
                    else if (BindPatternOwner(site))
                    { /* Pattern binding supplies each declared receiver's structural shape. */ }
                    else if (output?.Parent is ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.OutKeyword, Parent.Parent: ExpressionSyntax call })
                    { BindLocals(call); _ = probe.Expression(call); }
                    else if (site is VariableDeclaratorSyntax { Initializer: not null, Parent: VariableDeclarationSyntax localDeclaration } variable)
                    {
                        BindLocals(variable.Initializer.Value);
                        var inferred = localDeclaration.Type is RefTypeSyntax referenceType ? referenceType.Type.IsVar : localDeclaration.Type.IsVar;
                        probe.environment.Add(local, inferred ? probe.Expression(variable.Initializer.Value) : CSharpTypes.Rigid(local.Type));
                    }
                    else
                    { throw new InferenceException("Storage owner has no initializer."); }
                    resolving.Remove(local);
                    // Receiver copies can be reassigned to another input container.
                    foreach (var assignment in declaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    {
                        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || assignment.Right is RefExpressionSyntax)
                        { continue; }
                        if (assignment.Left is TupleExpressionSyntax && assignment.Left.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(target =>
                            { if (target is null) { throw new ArgumentNullException(nameof(target)); } return SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(target, cancellationToken).Symbol, local); }))
                        { BindDeconstructed(assignment); continue; }
                        if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(StorageExpression(assignment.Left), cancellationToken).Symbol, local)
                            || model.GetTypeInfo(assignment.Right, cancellationToken).Type is not { IsReferenceType: true })
                        { continue; }
                        BindLocals(assignment.Right);
                        probe.AssignValue(assignment.Left, probe.Expression(assignment.Right), model.GetTypeInfo(assignment.Right, cancellationToken).Type);
                    }
                }
            }
            var plans = new List<(int Parameter, int[] Path, ExpressionSyntax Literal, ITypeSymbol Type, ISymbol? Alias)>();
            void Find(HmType node, HmType slot, int parameter, List<int> path, ExpressionSyntax literal, ITypeSymbol type, ISymbol? alias)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                if (slot is null)
                { throw new ArgumentNullException(nameof(slot)); }
                if (parameter < 0)
                { throw new ArgumentOutOfRangeException(nameof(parameter)); }
                if (path is null)
                { throw new ArgumentNullException(nameof(path)); }
                if (literal is null)
                { throw new ArgumentNullException(nameof(literal)); }
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                node = node.Root;
                if (ReferenceEquals(node, slot.Root))
                { plans.Add((parameter, path.ToArray(), literal, type, alias)); return; }
                for (var i = 0; i < node.Arguments.Length; i++)
                { path.Add(i); Find(node.Arguments[i], slot, parameter, path, literal, type, alias); path.RemoveAt(path.Count - 1); }
            }
            void Discover(ExpressionSyntax target, ExpressionSyntax literal, ITypeSymbol type, ISymbol? alias)
            {
                if (target is null)
                { throw new ArgumentNullException(nameof(target)); }
                if (literal is null)
                { throw new ArgumentNullException(nameof(literal)); }
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                while (target is ParenthesizedExpressionSyntax or RefExpressionSyntax)
                { target = target is ParenthesizedExpressionSyntax parentheses ? parentheses.Expression : ((RefExpressionSyntax)target).Expression; }
                if (target is ConditionalExpressionSyntax conditional)
                { Discover(conditional.WhenTrue, literal, type, alias); Discover(conditional.WhenFalse, literal, type, alias); return; }
                if ((probe.StoredValueShape(literal, type) ?? probe.IndirectNullableStoredValue(literal, type)) is null)
                { return; }
                try
                {
                    BindLocals(target);
                    if (target.Ancestors().OfType<QueryExpressionSyntax>().LastOrDefault() is { } query && boundStorageQueries.Add(query.SpanStart))
                    { BindLocals(query); _ = probe.Query(query, retainStorageReferences: true); }
                    var slot = probe.AssignmentTarget(target);
                    if (slot.FreeVariables().Count == 0)
                    { return; }
                    for (var i = 0; i < parameters.Length; i++)
                    { Find(probeParameters[i], slot, i, [], literal, type, alias); }
                }
                catch (InferenceException)
                { /* An unresolved owner retains the normal fixed/ref constraints. */ }
            }
            foreach (var candidate in candidates)
            { Discover(candidate.Target, candidate.Literal, candidate.Type, candidate.Alias); }
            HmType Rewrite(HmType node, int parameter, List<int> path)
            {
                if (node is null)
                { throw new ArgumentNullException(nameof(node)); }
                if (parameter < 0)
                { throw new ArgumentOutOfRangeException(nameof(parameter)); }
                if (path is null)
                { throw new ArgumentNullException(nameof(path)); }
                node = node.Root;
                var arguments = node.Arguments.ToArray();
                for (var i = 0; i < arguments.Length; i++)
                { path.Add(i); arguments[i] = Rewrite(arguments[i], parameter, path); path.RemoveAt(path.Count - 1); }
                if (arguments.Length != 0)
                { node = node.WithArguments(arguments); }
                foreach (var plan in plans)
                {
                    if (plan.Parameter == parameter && plan.Path.SequenceEqual(path) && (StoredValueShape(plan.Literal, plan.Type) ?? IndirectNullableStoredValue(plan.Literal, plan.Type)) is { } literal)
                    {
                        node = MergeDefaultValues(node, literal);
                        defaultValueAnnotations.Add(node.Root);
                        if (plan.Alias is not null)
                        { preparedIndirectAliases.Add(plan.Alias); }
                    }
                }
                return node;
            }
            if (plans.Count == 0)
            { return; }
            // Apply children before parent annotations so paths do not shift.
            for (var i = 0; i < parameters.Length; i++)
            { parameters[i] = Rewrite(parameters[i], i, []); environment[currentMethod.Parameters[i]] = parameters[i]; }
        }

        private static IEnumerable<ExpressionSyntax> StoredAlternatives(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            while (expression is ParenthesizedExpressionSyntax parentheses)
            { expression = parentheses.Expression; }
            if (expression is ConditionalExpressionSyntax conditional)
            {
                foreach (var value in StoredAlternatives(conditional.WhenTrue))
                { yield return value; }
                foreach (var value in StoredAlternatives(conditional.WhenFalse))
                { yield return value; }
            }
            else if (expression is SwitchExpressionSyntax switched)
            {
                foreach (var arm in switched.Arms)
                { foreach (var value in StoredAlternatives(arm.Expression)) { yield return value; } }
            }
            else
            { yield return expression; }
        }

        private IEnumerable<ISymbol> ReferenceTargets(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            while (expression is ParenthesizedExpressionSyntax or RefExpressionSyntax)
            { expression = expression is ParenthesizedExpressionSyntax parentheses ? parentheses.Expression : ((RefExpressionSyntax)expression).Expression; }
            if (expression is ConditionalExpressionSyntax conditional)
            {
                foreach (var target in ReferenceTargets(conditional.WhenTrue))
                { yield return target; }
                foreach (var target in ReferenceTargets(conditional.WhenFalse))
                { yield return target; }
            }
            else if (model.GetSymbolInfo(expression, cancellationToken).Symbol is ILocalSymbol or IParameterSymbol)
            { yield return model.GetSymbolInfo(expression, cancellationToken).Symbol!; }
        }

        private HashSet<ISymbol> ReferenceStorageOwners(ISymbol storage)
        {
            if (storage is null)
            { throw new ArgumentNullException(nameof(storage)); }
            if (storageAliases is null)
            {
                // Rebindings contribute possible owners across the alias lifetime.
                // Ref invariance separately relates their stored value types.
                storageAliases = new(SymbolEqualityComparer.Default);
                void Add(ISymbol alias, ExpressionSyntax reference)
                {
                    if (alias is null)
                    { throw new ArgumentNullException(nameof(alias)); }
                    if (reference is null)
                    { throw new ArgumentNullException(nameof(reference)); }
                    if (!storageAliases.TryGetValue(alias, out var targets))
                    { targets = new(SymbolEqualityComparer.Default); storageAliases.Add(alias, targets); }
                    targets.UnionWith(ReferenceTargets(reference));
                }
                if (currentMethod?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is { } declaration)
                {
                    foreach (var variable in declaration.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                    {
                        if (variable.Initializer?.Value is RefExpressionSyntax reference && model.GetDeclaredSymbol(variable, cancellationToken) is ILocalSymbol alias)
                        { Add(alias, reference); }
                    }
                    foreach (var assignment in declaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    {
                        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Right is RefExpressionSyntax reference
                            && model.GetSymbolInfo(StorageExpression(assignment.Left), cancellationToken).Symbol is ILocalSymbol { RefKind: not RefKind.None } alias)
                        { Add(alias, reference); }
                    }
                }
            }
            var owners = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var pending = new Stack<ISymbol>();
            pending.Push(storage);
            while (pending.Count != 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current))
                { continue; }
                if (storageAliases.TryGetValue(current, out var targets))
                { foreach (var target in targets) { pending.Push(target); } }
                else if (current is IParameterSymbol or ILocalSymbol { RefKind: RefKind.None })
                { owners.Add(current); }
            }
            return owners;
        }

        private bool WriteReferenceStorage(ISymbol storage, HmType value)
        {
            if (storage is null)
            { throw new ArgumentNullException(nameof(storage)); }
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            var owners = ReferenceStorageOwners(storage);
            if (owners.Count == 0)
            { return false; }
            var targets = new HashSet<ISymbol>(owners, SymbolEqualityComparer.Default) { storage };
            foreach (var alias in storageAliases!.Keys)
            {
                if (ReferenceStorageOwners(alias).Overlaps(owners))
                { targets.Add(alias); }
            }
            foreach (var target in targets)
            {
                // Ordinary value copies sharing a type are not storage aliases.
                if (environment.TryGetValue(target, out var previous))
                { environment[target] = MergeDefaultValues(previous, value); defaultAssignedStorages.Add(target); }
            }
            return true;
        }

        private HmType CapturedDefaultValue(IdentifierNameSyntax identifier, ISymbol symbol, HmType type)
        {
            if (identifier is null)
            { throw new ArgumentNullException(nameof(identifier)); }
            if (symbol is null)
            { throw new ArgumentNullException(nameof(symbol)); }
            if (type is null)
            { throw new ArgumentNullException(nameof(type)); }
            if (symbol is not (ILocalSymbol or IParameterSymbol)
                || defaultStores is not null && !defaultStores.ContainsKey(symbol))
            { return type; }
            // Captured writes may run after their body was analyzed. Join their
            // lifetime even when compiler flow still assumes the earlier value.
            return DefaultStoredValue(symbol, type, SymbolEqualityComparer.Default.Equals(symbol.ContainingSymbol, model.GetEnclosingSymbol(identifier.SpanStart, cancellationToken)));
        }

        private HmType DefaultStoredValue(ISymbol symbol, HmType type, bool onlyCapturedWrites = false)
        {
            if (symbol is null)
            { throw new ArgumentNullException(nameof(symbol)); }
            if (type is null)
            { throw new ArgumentNullException(nameof(type)); }
            if (defaultStores is null)
            {
                defaultStores = new(SymbolEqualityComparer.Default);
                if (currentMethod?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is { } declaration)
                {
                    foreach (var assignment in declaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    {
                        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                        { continue; }
                        foreach (var component in StorageComponents(assignment.Left, assignment.Right))
                        {
                            if (component.Target is not IdentifierNameSyntax || model.GetSymbolInfo(component.Target, cancellationToken).Symbol is not { } assigned || assigned is not (ILocalSymbol or IParameterSymbol))
                            { continue; }
                            foreach (var literal in StoredAlternatives(component.Value))
                            {
                                if (StoredValueShape(literal, StorageType(component.Target)) is null
                                    && (assigned is not ILocalSymbol { RefKind: not RefKind.None } || IndirectNullableStoredValue(literal, StorageType(component.Target)) is null))
                                { continue; }
                                var targets = ReferenceStorageOwners(assigned);
                                targets.Add(assigned);
                                foreach (var target in targets)
                                {
                                    if (!defaultStores.TryGetValue(target, out var stores))
                                    { stores = []; defaultStores.Add(target, stores); }
                                    stores.Add(literal);
                                }
                            }
                        }
                    }
                }
            }
            var storageType = symbol switch { ILocalSymbol local => local.Type, IParameterSymbol parameter => parameter.Type, IFieldSymbol field => field.Type, IPropertySymbol property => property.Type, IMethodSymbol method => method.ReturnType, _ => null };
            if (storageType is null || !defaultStores.TryGetValue(symbol, out var literals))
            { return type; }
            foreach (var literal in literals)
            {
                if ((!onlyCapturedWrites || !SymbolEqualityComparer.Default.Equals(symbol.ContainingSymbol, model.GetEnclosingSymbol(literal.SpanStart, cancellationToken)))
                    && (StoredValueShape(literal, storageType) ?? IndirectNullableStoredValue(literal, storageType)) is { } value)
                { type = MergeDefaultValues(type, value); }
            }
            return type;
        }

        private HmType? StoredValueShape(ExpressionSyntax expression, ITypeSymbol target)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (expression.Parent is ArgumentSyntax syntax && model.GetOperation(syntax, cancellationToken) is IArgumentOperation { Parameter: { } parameter }
                && HasNullableOutput(parameter))
            { return ApplyMaybeNullContract(target, CSharpTypes.Erase(target)); }
            if (ScalarDefaultValue(expression, target) is { } scalar)
            { return scalar; }
            if (!IsNullLiteralConversion(expression, target))
            { return null; }
            var literal = expression;
            var suppressed = false;
            while (true)
            {
                if (literal is ParenthesizedExpressionSyntax parentheses)
                { literal = parentheses.Expression; }
                else if (literal is PostfixUnaryExpressionSyntax suppression && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                { suppressed = true; literal = suppression.Operand; }
                else
                { break; }
            }
            var shape = NullLiteralShape(target.IsReferenceType
                ? target.WithNullableAnnotation(suppressed ? NullableAnnotation.NotAnnotated : NullableAnnotation.Annotated) : target);
            defaultValueAnnotations.Add(shape);
            return shape;
        }

        private HmType? ScalarDefaultValue(ExpressionSyntax expression, ITypeSymbol target)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (!target.IsValueType || target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                || IsFixedDeclaredReturn(expression))
            { return null; }
            var literal = expression;
            var suppressed = false;
            while (true)
            {
                if (literal is ParenthesizedExpressionSyntax parentheses)
                { literal = parentheses.Expression; }
                else if (literal is PostfixUnaryExpressionSyntax suppression && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                { suppressed = true; literal = suppression.Operand; }
                else
                { break; }
            }
            if (!literal.IsKind(SyntaxKind.DefaultLiteralExpression)
                || !SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(literal, cancellationToken).ConvertedType, target))
            { return null; }
            var shape = CSharpTypes.Erase(target);
            var position = expression.SpanStart;
            if (currentMethod?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is MethodDeclarationSyntax declaration)
            { position = declaration.ReturnType.SpanStart; }
            var valueDomain = model.SyntaxTree.Options is CSharpParseOptions { LanguageVersion: < LanguageVersion.CSharp9 }
                && (model.GetNullableContext(position) & NullableContext.AnnotationsEnabled) != 0;
            return suppressed ? shape : DefaultValueAnnotation(shape, valueDomain);
        }

        private HmType DefaultValueAnnotation(HmType shape, bool valueDomain)
        {
            if (shape is null)
            { throw new ArgumentNullException(nameof(shape)); }
            shape = shape.Root;
            if (shape.Name == "Tuple")
            {
                return shape.WithArguments(shape.Arguments.Select(argument =>
                { if (argument is null) { throw new ArgumentNullException(nameof(argument)); } return DefaultValueAnnotation(argument, valueDomain); }).ToArray());
            }
            if (!shape.IsVariable)
            { return shape; }
            if (valueDomain)
            { RequireKeyword(shape, "struct"); return shape; }
            var annotated = new HmType(CSharpTypes.NullableParameter, shape);
            defaultValueAnnotations.Add(annotated);
            return annotated;
        }

        private bool HasDefaultValueAnnotation(HmType shape)
        {
            if (shape is null)
            { throw new ArgumentNullException(nameof(shape)); }
            if (defaultValueAnnotations.Count == 0)
            { return false; }
            shape = shape.Root;
            return defaultValueAnnotations.Contains(shape) || shape.Arguments.Any(HasDefaultValueAnnotation);
        }

        private HmType MergeDefaultValues(HmType first, HmType second)
        {
            if (first is null)
            { throw new ArgumentNullException(nameof(first)); }
            if (second is null)
            { throw new ArgumentNullException(nameof(second)); }
            if (!HasDefaultValueAnnotation(first) && !HasDefaultValueAnnotation(second))
            { HmType.Unify(first, second); return first; }
            first = first.Root;
            second = second.Root;
            var reference = first.Name == CSharpTypes.NullableReference || second.Name == CSharpTypes.NullableReference;
            if (first.Name == CSharpTypes.NullableReference)
            { first = first.Arguments[0].Root; }
            if (second.Name == CSharpTypes.NullableReference)
            { second = second.Arguments[0].Root; }
            var annotated = first.Name == CSharpTypes.NullableParameter || second.Name == CSharpTypes.NullableParameter;
            if (first.Name == CSharpTypes.NullableParameter)
            { first = first.Arguments[0].Root; }
            if (second.Name == CSharpTypes.NullableParameter)
            { second = second.Arguments[0].Root; }
            HmType merged;
            if (!first.IsVariable && !second.IsVariable && first.Name == second.Name && first.NestedTypeName == second.NestedTypeName
                && first.Arguments.Length == second.Arguments.Length && (first.CSharpSymbol is null || second.CSharpSymbol is null
                || CSharpTypes.ConstructorIdentity(first.CSharpSymbol) == CSharpTypes.ConstructorIdentity(second.CSharpSymbol)))
            {
                merged = first.WithArguments(first.Arguments.Select((argument, index) =>
                {
                    if (argument is null)
                    { throw new ArgumentNullException(nameof(argument)); }
                    if (index < 0)
                    { throw new ArgumentOutOfRangeException(nameof(index)); }
                    return MergeDefaultValues(argument, second.Arguments[index]);
                }).ToArray());
            }
            else
            { HmType.Unify(first, second); merged = first.Root; }
            if (!annotated && !reference)
            { return merged; }
            var result = new HmType(reference ? CSharpTypes.NullableReference : CSharpTypes.NullableParameter, merged);
            defaultValueAnnotations.Add(result);
            return result;
        }
        private HmType? NullReturnHole(ExpressionSyntax expression, ITypeSymbol converted)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (converted is null)
            { throw new ArgumentNullException(nameof(converted)); }
            if (!IsNullLiteralConversion(expression, converted))
            { return null; }
            ExpressionSyntax site = expression;
            while (true)
            {
                if (site.Parent is ParenthesizedExpressionSyntax parentheses)
                { site = parentheses; }
                else if (site.Parent is PostfixUnaryExpressionSyntax suppressed && suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                { site = suppressed; }
                else if (site.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax tuple })
                { site = tuple; }
                else if (site.Parent is ConditionalExpressionSyntax conditional && (conditional.WhenTrue == site || conditional.WhenFalse == site))
                { site = conditional; }
                else if (site.Parent is SwitchExpressionArmSyntax { Parent: SwitchExpressionSyntax switched })
                { site = switched; }
                else
                { break; }
            }
            if (IsFixedDeclaredReturn(site))
            { return null; }
            SyntaxNode? owner = site.Parent switch
            {
                ArrowExpressionClauseSyntax arrow => arrow.Parent,
                AnonymousFunctionExpressionSyntax anonymous => anonymous,
                ReturnStatementSyntax or YieldStatementSyntax => site.Ancestors().FirstOrDefault(node =>
                {
                    if (node is null)
                    { throw new ArgumentNullException(nameof(node)); }
                    return node is MethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax;
                }),
                _ => null
            };
            var referenceKind = owner switch
            {
                MethodDeclarationSyntax method => model.GetDeclaredSymbol(method, cancellationToken)?.RefKind,
                AnonymousFunctionExpressionSyntax anonymous when model.GetOperation(anonymous, cancellationToken) is IAnonymousFunctionOperation operation => operation.Symbol.RefKind,
                null when site.Parent is SelectOrGroupClauseSyntax or QueryClauseSyntax or OrderingSyntax => querySelectorReturn?.RefKind,
                _ => null
            };
            if (referenceKind != RefKind.None)
            { return null; }
            return NullLiteralShape(converted);
        }

        private static bool IsNullLiteralConversion(ExpressionSyntax expression, ITypeSymbol converted)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (converted is null)
            { throw new ArgumentNullException(nameof(converted)); }
            var literal = expression;
            while (true)
            {
                if (literal is ParenthesizedExpressionSyntax parentheses)
                { literal = parentheses.Expression; }
                else if (literal is PostfixUnaryExpressionSyntax suppressed && suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                { literal = suppressed.Operand; }
                else
                { break; }
            }
            if (!literal.IsKind(SyntaxKind.NullLiteralExpression) && !literal.IsKind(SyntaxKind.DefaultLiteralExpression))
            { return false; }
            if (!converted.IsReferenceType && converted is not (IPointerTypeSymbol or IFunctionPointerTypeSymbol)
                && converted is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            { return false; }
            return true;
        }

        private HmType NullLiteralShape(ITypeSymbol converted)
        {
            if (converted is null)
            { throw new ArgumentNullException(nameof(converted)); }
            var inferred = CSharpTypes.Erase(converted);
            var core = ConversionCore(inferred);
            if (converted.IsReferenceType && core.IsVariable)
            { RequireKeyword(core, "class"); }
            else if (core.Name == CSharpTypes.NullableValue && core.Arguments[0].Root.IsVariable)
            { RequireKeyword(core.Arguments[0], "struct"); }
            return inferred;
        }

        private HmType BindDeclaredReturn(ExpressionSyntax expression, HmType inferred, ITypeSymbol converted)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (inferred is null)
            { throw new ArgumentNullException(nameof(inferred)); }
            if (converted is null)
            { throw new ArgumentNullException(nameof(converted)); }
            if (!IsFixedDeclaredReturn(expression))
            { return inferred; }
            var fixedReturn = CSharpTypes.Rigid(converted);
            BindFixedConversion(inferred, fixedReturn, converted, false);
            return fixedReturn;
        }

        private bool IsFixedDeclaredReturn(ExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            SyntaxNode slot = expression;
            while (true)
            {
                if (slot.Parent is ParenthesizedExpressionSyntax parentheses)
                { slot = parentheses; }
                else if (slot.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax tuple })
                { slot = tuple; }
                else
                { break; }
            }
            var declaration = slot.Parent switch
            {
                ArrowExpressionClauseSyntax { Parent: LocalFunctionStatementSyntax local } => (SyntaxNode)local,
                AnonymousFunctionExpressionSyntax lambda => lambda,
                ReturnStatementSyntax or YieldStatementSyntax => slot.Ancestors().FirstOrDefault(owner =>
                {
                    if (owner is null)
                    { throw new ArgumentNullException(nameof(owner)); }
                    return owner is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or MethodDeclarationSyntax;
                }),
                _ => null
            };
            if (declaration is LocalFunctionStatementSyntax localDeclaration)
            {
                return (iteratorReturn is null || slot.Parent is YieldStatementSyntax)
                    && model.GetDeclaredSymbol(localDeclaration, cancellationToken) is { RefKind: RefKind.None };
            }
            return declaration is AnonymousFunctionExpressionSyntax anonymous && fixedLambdaReturn is { RefKind: RefKind.None }
                && model.GetOperation(anonymous, cancellationToken) is IAnonymousFunctionOperation operation
                && SymbolEqualityComparer.Default.Equals(operation.Symbol, fixedLambdaReturn);
        }

        private HmType JoinedExpression(ExpressionSyntax expression, ITypeSymbol? target = null, bool fixedResult = false)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var info = model.GetTypeInfo(expression, cancellationToken);
            target ??= info.Type ?? info.ConvertedType ?? throw new InferenceException("Unresolved expression join.");
            var result = fixedResult ? CSharpTypes.Rigid(target) : HmType.Variable();
            if (fixedResult && info.Type is { IsValueType: true } natural && !SymbolEqualityComparer.Default.Equals(natural, target)
                && expression.DescendantNodesAndSelf(node =>
                    { if (node is null) { throw new ArgumentNullException(nameof(node)); } return node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax); })
                    .OfType<LiteralExpressionSyntax>().Any(literal =>
                    {
                        if (literal is null)
                        { throw new ArgumentNullException(nameof(literal)); }
                        var type = model.GetTypeInfo(literal, cancellationToken).ConvertedType;
                        return literal.IsKind(SyntaxKind.DefaultLiteralExpression) && type is { IsValueType: true }
                            && type is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
                    }))
            {
                var inferred = JoinedExpression(expression, natural, false);
                BindConvertedArgument(natural, target, inferred, result);
                conversionTargets.Add((result, target));
                return result;
            }
            HmType Arm(ExpressionSyntax value)
            {
                if (value is null)
                { throw new ArgumentNullException(nameof(value)); }
                var unwrapped = value;
                while (unwrapped is ParenthesizedExpressionSyntax parentheses)
                { unwrapped = parentheses.Expression; }
                var inferred = fixedResult && unwrapped is ConditionalExpressionSyntax or SwitchExpressionSyntax
                    ? JoinedExpression(unwrapped, target, true) : Returned(value, target);
                if (fixedResult)
                { BindFixedConversion(inferred, result, target, false); }
                else
                { result = MergeDefaultValues(result, inferred); }
                return inferred;
            }
            if (expression is ConditionalExpressionSyntax conditional)
            {
                BooleanCondition(conditional.Condition);
                if (model.GetOperation(conditional, cancellationToken) is IConditionalOperation { IsRef: true })
                {
                    var referent = Expression(conditional.WhenTrue);
                    HmType.Unify(referent, Expression(conditional.WhenFalse));
                    return referent;
                }
                var first = Arm(conditional.WhenTrue);
                var second = Arm(conditional.WhenFalse);
                // Earlier conditionals need a native operand conversion; the declared
                // local result cannot supply their type through target typing.
                if (fixedResult && model.SyntaxTree.Options is CSharpParseOptions { LanguageVersion: < LanguageVersion.CSharp9 })
                {
                    var firstType = model.GetTypeInfo(conditional.WhenTrue, cancellationToken).Type;
                    var secondType = model.GetTypeInfo(conditional.WhenFalse, cancellationToken).Type;
                    var firstMatches = SymbolEqualityComparer.Default.Equals(firstType, target);
                    var secondMatches = SymbolEqualityComparer.Default.Equals(secondType, target);
                    if (firstMatches && secondMatches)
                    { RelateNaturalJoin(first, second, target, false); }
                    else if (firstMatches && secondType is not null)
                    { BindConvertedArgument(secondType, target, CSharpTypes.Rigid(secondType), first, false); }
                    else if (secondMatches && firstType is not null)
                    { BindConvertedArgument(firstType, target, CSharpTypes.Rigid(firstType), second, false); }
                }
            }
            else if (expression is SwitchExpressionSyntax switched)
            {
                var governing = Expression(switched.GoverningExpression);
                foreach (var arm in switched.Arms)
                {
                    Pattern(arm.Pattern, governing);
                    if (arm.WhenClause is not null)
                    { BooleanCondition(arm.WhenClause.Condition); }
                    Arm(arm.Expression);
                }
            }
            else
            { throw new InferenceException("Unsupported expression join."); }
            return result;
        }

        private void RelateNaturalJoin(HmType first, HmType second, ITypeSymbol declared, bool referenceOnly)
        {
            if (first is null)
            { throw new ArgumentNullException(nameof(first)); }
            if (second is null)
            { throw new ArgumentNullException(nameof(second)); }
            if (declared is null)
            { throw new ArgumentNullException(nameof(declared)); }
            first = ConversionCore(first);
            second = ConversionCore(second);
            if (!first.IsVariable && second.IsVariable)
            { (first, second) = (second, first); }
            if (first.IsVariable)
            {
                conversionBounds.Add((first, second, declared, declared, referenceOnly));
                return;
            }
            if (first.Name == second.Name && first.Arguments.Length == second.Arguments.Length)
            {
                if (declared is IArrayTypeSymbol array && first.Arguments.Length == 1)
                {
                    RelateNaturalJoin(first.Arguments[0], second.Arguments[0], array.ElementType, true);
                    return;
                }
                if (declared is INamedTypeSymbol { IsTupleType: true } tuple && !referenceOnly)
                {
                    for (var i = 0; i < tuple.TupleElements.Length; i++)
                    { RelateNaturalJoin(first.Arguments[i], second.Arguments[i], tuple.TupleElements[i].Type, false); }
                    return;
                }
                if (declared is INamedTypeSymbol { IsGenericType: true, IsTupleType: false } named)
                {
                    if (first.NestedTypeName is not null)
                    { HmType.Unify(first.Arguments[0], second.Arguments[0]); }
                    var left = CSharpTypes.OwnArguments(first);
                    var right = CSharpTypes.OwnArguments(second);
                    for (var i = 0; i < left.Length; i++)
                    {
                        if (named.OriginalDefinition.TypeParameters[i].Variance == VarianceKind.Out)
                        { RelateNaturalJoin(left[i], right[i], named.TypeArguments[i], true); }
                        else
                        { HmType.Unify(left[i], right[i]); }
                    }
                    return;
                }
            }
            HmType.Unify(first, second);
        }

        private HmType Awaited(AwaitExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var info = model.GetAwaitExpressionInfo(expression);
            if (info.IsDynamic || info.GetResultMethod is null)
            {
                throw new InferenceException("Dynamic or unresolved await is unsupported.");
            }
            var operandType = model.GetTypeInfo(expression.Expression, cancellationToken).Type;
            if (operandType is INamedTypeSymbol named && named.ContainingType is null && named.Arity == 1 &&
                ((named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" && named.Name is "Task" or "ValueTask")
                || (named.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices" && named.Name is "ConfiguredTaskAwaitable" or "ConfiguredValueTaskAwaitable")))
            {
                var value = HmType.Variable();
                var operand = Expression(expression.Expression).Root;
                if (operand.Name == CSharpTypes.NullableReference)
                { operand = operand.Arguments[0].Root; }
                HmType.Unify(operand, new(CSharpTypes.Rigid(named.WithNullableAnnotation(NullableAnnotation.NotAnnotated)).Name, value));
                if (asyncBuilder is not null && info.GetAwaiterMethod is not null)
                { BindBuilderAwaiter(info.GetAwaiterMethod.ReturnType, AwaitProtocolResult(named, operand, info.GetAwaiterMethod)); }
                return value;
            }
            var selected = info.GetAwaiterMethod;
            if (selected is not null && operandType is not null)
            {
                var operand = Expression(expression.Expression);
                try
                {
                    var awaiter = AwaitProtocolResult(operandType, operand, selected);
                    BindBuilderAwaiter(selected.ReturnType, awaiter);
                    HmType.Unify(PatternSymbolType(info.IsCompletedProperty, awaiter, selected.ReturnType), new HmType("bool"));
                    return AwaitProtocolResult(selected.ReturnType, awaiter, info.GetResultMethod);
                }
                catch (InferenceException)
                {
                    // A protocol can contain nested/ref-like types that never appear
                    // in the public signature. Preserve the concrete await in that case.
                    HmType.Unify(operand, CSharpTypes.Rigid(operandType));
                    return CSharpTypes.Rigid(info.GetResultMethod.ReturnType);
                }
            }
            Anchor(expression.Expression);
            return CSharpTypes.Rigid(info.GetResultMethod.ReturnType);
        }

        private bool SameAwaitMethod(IMethodSymbol? before, IMethodSymbol? after, MethodDeclarationSyntax changedMethod, SyntaxTree changedTree)
        {
            if (changedMethod is null)
            { throw new ArgumentNullException(nameof(changedMethod)); }
            if (changedTree is null)
            { throw new ArgumentNullException(nameof(changedTree)); }
            if (before is not null && after is not null
                && SameMethodDefinition(before, currentMethod)
                && ImplementationMethod(after).DeclaringSyntaxReferences.Any(reference =>
                {
                    if (reference is null)
                    { throw new ArgumentNullException(nameof(reference)); }
                    return reference.SyntaxTree == changedTree && reference.Span.Start == changedMethod.SpanStart;
                }))
            { return SameCallSignature(before.ReducedFrom ?? before, after.ReducedFrom ?? after); }
            return ProtocolMember(before) == ProtocolMember(after);
        }
        private HmType AwaitProtocolResult(ITypeSymbol receiverType, HmType receiver, IMethodSymbol selected)
        {
            if (receiverType is null)
            { throw new ArgumentNullException(nameof(receiverType)); }
            if (receiver is null)
            { throw new ArgumentNullException(nameof(receiver)); }
            if (selected is null)
            { throw new ArgumentNullException(nameof(selected)); }
            var full = selected.ReducedFrom ?? selected;
            var definition = full.OriginalDefinition;
            var mapping = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            if (full.IsStatic)
            {
                if (!full.IsExtensionMethod || definition.Parameters.Length != 1)
                { throw new InferenceException("Unsupported await extension signature."); }
                foreach (var parameter in definition.TypeParameters)
                { mapping.Add(parameter, HmType.Variable()); }
                var expected = CSharpTypes.Rigid(definition.Parameters[0].Type, mapping);
                var projected = definition.Parameters[0].Type is INamedTypeSymbol owner
                    ? ProjectDeconstructionReceiver(receiverType, receiver, owner) : receiver;
                if (projected is null)
                { HmType.Unify(receiver, CSharpTypes.Rigid(receiverType)); projected = CSharpTypes.Rigid(full.Parameters[0].Type); }
                UnifyDeconstructionReceiver(expected, projected);
                foreach (var parameter in definition.TypeParameters)
                { ImportConstraints(parameter, mapping[parameter], mapping, full.TypeArguments[parameter.Ordinal]); }
            }
            else
            {
                var projected = ProjectDeconstructionReceiver(receiverType, receiver, full.ContainingType);
                if (projected is null)
                { HmType.Unify(receiver, CSharpTypes.Rigid(receiverType)); return CSharpTypes.Rigid(selected.ReturnType); }
                if (!CSharpTypes.MapNamedArguments(full.ContainingType, projected, mapping))
                { throw new InferenceException("Invalid awaiter owner shape."); }
            }
            return CSharpTypes.Rigid(definition.ReturnType, mapping);
        }
        private HmType BindNullableArgument(IOperation value, HmType expected)
        {
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            if (value.Syntax is ExpressionSyntax interpolation && IsTargetedInterpolation(interpolation))
            { return Interpolation(interpolation, expected); }
            if (value.Syntax is ExpressionSyntax literal)
            {
                literal = StorageExpression(literal);
                while (literal is PostfixUnaryExpressionSyntax suppression && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                { literal = StorageExpression(suppression.Operand); }
                if (literal.IsKind(SyntaxKind.DefaultLiteralExpression))
                { return expected; }
            }
            if (TryBindFixedArgument(value, expected))
            { return value.Type is { } fixedType ? CSharpTypes.Rigid(fixedType) : expected; }
            var inferred = BoundExpression(value).Root;
            expected = expected.Root;
            if (TryBindDynamicArgument(value, expected, inferred))
            { return inferred; }
            if (TryBindArgumentConversion(value, expected, inferred))
            { return inferred; }
            if (expected.Name == CSharpTypes.NullableValue && value.Syntax is ExpressionSyntax expression
                && model.GetTypeInfo(expression, cancellationToken).Type is ITypeSymbol source && value.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                && SymbolEqualityComparer.Default.Equals(source, nullable.TypeArguments[0]))
            {
                HmType.Unify(inferred, expected.Arguments[0]);
            }
            else
            {
                BindByValueInput(expected, inferred);
            }
            return inferred;
        }

        private HmType Coalesced(BinaryExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (model.GetOperation(expression, cancellationToken) is not ICoalesceOperation operation)
            {
                throw new InferenceException("Unresolved coalescing expression.");
            }
            var left = Expression(expression.Left).Root;
            var resultType = model.GetTypeInfo(expression, cancellationToken).Type ?? operation.Type;
            if (resultType is not null && IsNullLiteralConversion(expression.Right, resultType))
            {
                var result = NullLiteralShape(resultType);
                HmType.Unify(ConversionCore(left), ConversionCore(result));
                return result;
            }
            if (resultType is not null && ConvertedCoalescing(expression.Left, left, expression.Right, resultType) is { } convertedResult)
            { return convertedResult; }
            while (left.Name == CSharpTypes.NullableParameter && left.Arguments[0].Root.Name is CSharpTypes.NullableReference or CSharpTypes.NullableValue or CSharpTypes.NullableParameter)
            { left = left.Arguments[0].Root; }
            if (!operation.ValueConversion.IsIdentity || left.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableValue or CSharpTypes.NullableParameter))
            {
                Anchor(expression.Left);
                Anchor(expression.Right);
                return CSharpTypes.Rigid(operation.Type!);
            }
            var right = Expression(expression.Right).Root;
            if (right.Name == left.Name)
            {
                HmType.Unify(left, right);
                return left;
            }
            HmType.Unify(left.Arguments[0], right);
            return right;
        }

        private HmType CoalesceAssigned(AssignmentExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            var target = AssignmentTarget(expression.Left).Root;
            while (target.Name == CSharpTypes.NullableParameter && target.Arguments[0].Root.Name is CSharpTypes.NullableReference or CSharpTypes.NullableValue or CSharpTypes.NullableParameter)
            { target = target.Arguments[0].Root; }
            var targetType = model.GetTypeInfo(expression.Left, cancellationToken).Type!;
            var sourceType = model.GetTypeInfo(expression.Right, cancellationToken).Type;
            var resultType = model.GetTypeInfo(expression, cancellationToken).Type!;
            if (IsNullLiteralConversion(expression.Right, resultType))
            {
                var result = NullLiteralShape(resultType);
                HmType.Unify(ConversionCore(target), ConversionCore(result));
                return result;
            }
            if (ConvertedCoalescing(expression.Left, target, expression.Right, resultType) is { } convertedResult)
            { return convertedResult; }
            var underlying = targetType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                ? nullable.TypeArguments[0] : targetType;
            if (target.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableValue) || sourceType is null
                || (!SymbolEqualityComparer.Default.Equals(sourceType, targetType) && !SymbolEqualityComparer.Default.Equals(sourceType, underlying)))
            {
                Anchor(expression.Left);
                Anchor(expression.Right);
                return CSharpTypes.Rigid(resultType);
            }
            var right = Expression(expression.Right).Root;
            HmType.Unify(target, right.Name == target.Name ? right : new(target.Name, right));
            if (target.Name == CSharpTypes.NullableValue)
            {
                return resultType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } ? target : target.Arguments[0];
            }
            return resultType.NullableAnnotation == NullableAnnotation.Annotated ? target : target.Arguments[0];
        }
        private HmType? ConvertedCoalescing(ExpressionSyntax leftSyntax, HmType left, ExpressionSyntax rightSyntax, ITypeSymbol resultType)
        {
            if (leftSyntax is null)
            { throw new ArgumentNullException(nameof(leftSyntax)); }
            if (left is null)
            { throw new ArgumentNullException(nameof(left)); }
            if (rightSyntax is null)
            { throw new ArgumentNullException(nameof(rightSyntax)); }
            if (resultType is null)
            { throw new ArgumentNullException(nameof(resultType)); }
            var source = model.GetTypeInfo(leftSyntax, cancellationToken).Type;
            var fallback = model.GetTypeInfo(rightSyntax, cancellationToken).Type;
            if (source is null || fallback is null || source is IDynamicTypeSymbol || fallback is IDynamicTypeSymbol || resultType is IDynamicTypeSymbol)
            { return null; }
            var selected = ConversionCore(left);
            if (source is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                if (selected.Name != CSharpTypes.NullableValue)
                { return null; }
                selected = selected.Arguments[0];
                source = nullable.TypeArguments[0];
            }
            else
            { source = source.WithNullableAnnotation(NullableAnnotation.NotAnnotated); }
            var result = CSharpTypes.Erase(resultType);
            BindConvertedArgument(source, resultType, selected, result);
            BindConvertedArgument(fallback, resultType, ScalarDefaultValue(rightSyntax, resultType) ?? Expression(rightSyntax), result);
            conversionTargets.Add((result, resultType));
            return result;
        }
        private HmType ConditionalAccess(ConditionalAccessExpressionSyntax expression)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (expression.WhenNotNull is ElementBindingExpressionSyntax binding
                && model.GetTypeInfo(expression.Expression, cancellationToken).Type is IArrayTypeSymbol array
                && model.GetOperation(binding, cancellationToken) is IArrayElementReferenceOperation arrayAccess
                && SymbolEqualityComparer.Default.Equals(array.ElementType, arrayAccess.Type))
            {
                var element = HmType.Variable();
                var receiver = Expression(expression.Expression).Root;
                if (receiver.Name == CSharpTypes.NullableReference)
                { receiver = receiver.Arguments[0]; }
                HmType.Unify(receiver, new("[" + new string(',', array.Rank - 1) + "]", element));
                foreach (var argument in binding.ArgumentList.Arguments)
                { Anchor(argument.Expression); }
                var result = model.GetTypeInfo(expression, cancellationToken).Type!;
                if (result is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                    && array.ElementType is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                {
                    // The lifted temporary can disappear into ?? or another operation;
                    // retain its value-type requirement even when absent from the API.
                    RequireKeyword(element, "struct");
                    return new(CSharpTypes.NullableValue, element);
                }
                if (result.IsReferenceType)
                {
                    if (array.ElementType.NullableAnnotation == NullableAnnotation.Annotated)
                    { HmType.Unify(element, CSharpTypes.Erase(array.ElementType)); return element; }
                    RequireKeyword(element, "class");
                    return new(CSharpTypes.NullableReference, element);
                }
                return element;
            }
            var original = model.GetTypeInfo(expression.Expression, cancellationToken).Type
                ?? throw new InferenceException("Untyped conditional receiver.");
            var value = Expression(expression.Expression).Root;
            if (original is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            {
                original = nullable.TypeArguments[0];
                if (value.Name != CSharpTypes.NullableValue)
                { throw new InferenceException("Invalid nullable receiver shape."); }
                value = value.Arguments[0];
            }
            else
            { value = ConversionCore(value); }
            HmType bound;
            conditionalReceivers.Push((original, value));
            try
            { bound = Expression(expression.WhenNotNull); }
            finally { conditionalReceivers.Pop(); }
            var resultType = model.GetTypeInfo(expression, cancellationToken).Type
                ?? throw new InferenceException("Untyped conditional access.");
            var boundType = model.GetTypeInfo(expression.WhenNotNull, cancellationToken).Type
                ?? throw new InferenceException("Untyped conditional member.");
            if (resultType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                && boundType is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            {
                RequireKeyword(bound, "struct");
                return new(CSharpTypes.NullableValue, bound);
            }
            if (boundType.IsReferenceType)
            {
                // Conditional access can produce null even when contextual type
                // info carries a non-annotated symbol (for example inside ??).
                var reference = ConversionCore(bound);
                if (reference.IsVariable)
                { RequireKeyword(reference, "class"); }
                return new(CSharpTypes.NullableReference, reference);
            }
            return bound;
        }

        private void ImportNullableConstraints(HmType[] parameters, HmType result)
        {
            if (parameters is null)
            { throw new ArgumentNullException(nameof(parameters)); }
            if (result is null)
            { throw new ArgumentNullException(nameof(result)); }
            var visited = new HashSet<HmType>();
            void Visit(HmType type)
            {
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                type = type.Root;
                if (!visited.Add(type))
                { return; }
                if (type.Name is CSharpTypes.NullableReference or CSharpTypes.NullableValue)
                {
                    var element = type.Arguments[0].Root;
                    while (element.Name == CSharpTypes.NullableParameter)
                    { element = element.Arguments[0].Root; }
                    if (element.IsVariable)
                    { RequireKeyword(element, type.Name == CSharpTypes.NullableReference ? "class" : "struct"); }
                }
                foreach (var argument in type.Arguments)
                { Visit(argument); }
            }
            foreach (var parameter in parameters)
            { Visit(parameter); }
            Visit(result);
        }

        private string CompilerWarningKey(Diagnostic warning, Compilation compilation)
        {
            if (warning is null)
            { throw new ArgumentNullException(nameof(warning)); }
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            var treeOrdinal = 0;
            foreach (var candidate in compilation.SyntaxTrees)
            {
                if (ReferenceEquals(candidate, warning.Location.SourceTree))
                { break; }
                treeOrdinal++;
            }
            var prefix = warning.Id + ":" + treeOrdinal + ":" + warning.Location.SourceTree?.FilePath + ":";
            if (warning.Location.IsInSource && warning.Location.SourceTree is SyntaxTree tree)
            {
                var root = tree.GetRoot(cancellationToken);
                var span = warning.Location.SourceSpan;
                var node = root.FindNode(span, getInnermostNodeForTie: true);
                foreach (var owner in node.AncestorsAndSelf())
                {
                    if (owner is not MethodDeclarationSyntax method)
                    { continue; }
                    if (warning.Id == "CS8826")
                    {
                        // The compiler embeds both complete partial signatures in this
                        // warning. Generic substitution changes that text even when
                        // the original per-part differences have been preserved.
                        var ordinal = 0;
                        foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                        {
                            if (declaration.SpanStart == method.SpanStart)
                            { return prefix + "partial-signature:" + ordinal; }
                            ordinal++;
                        }
                    }

                    SyntaxNode? body = method.Body is not null ? method.Body : method.ExpressionBody?.Expression;
                    if (body is not null && body.FullSpan.Contains(span))
                    {
                        var ordinal = 0;
                        foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                        {
                            if (declaration.SpanStart == method.SpanStart)
                            { return prefix + "body:" + ordinal + ":" + (span.Start - body.FullSpan.Start) + ":" + span.Length; }
                            ordinal++;
                        }
                    }
                    break;
                }
            }
            return prefix + warning.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal bool IntroducesCompilerWarnings(Compilation changed, IEnumerable<Diagnostic>? originalDiagnostics = null, IEnumerable<Diagnostic>? changedDiagnostics = null)
        {
            if (changed is null)
            { throw new ArgumentNullException(nameof(changed)); }
            var existing = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var warning in originalDiagnostics ?? model.Compilation.GetDiagnostics(cancellationToken))
            {
                if (warning.Severity == DiagnosticSeverity.Warning || warning.Severity == DiagnosticSeverity.Error && warning.DefaultSeverity == DiagnosticSeverity.Hidden)
                {
                    var key = CompilerWarningKey(warning, model.Compilation);
                    existing.TryGetValue(key, out var count);
                    existing[key] = count + 1;
                }
            }
            foreach (var warning in changedDiagnostics ?? changed.GetDiagnostics(cancellationToken))
            {
                if (warning.Severity == DiagnosticSeverity.Warning || warning.Severity == DiagnosticSeverity.Error && warning.DefaultSeverity == DiagnosticSeverity.Hidden)
                {
                    var key = CompilerWarningKey(warning, changed);
                    if (!existing.TryGetValue(key, out var count) || count == 0)
                    { return true; }
                    existing[key] = count - 1;
                }
            }
            return false;
        }
    }
}
