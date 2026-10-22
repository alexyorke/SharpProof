using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
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
                BindArguments(property.Arguments, property.Property.OriginalDefinition.Parameters.Select(parameter =>
                { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return CSharpTypes.Rigid(parameter.Type, mapping); }).ToArray());
                return CSharpTypes.Rigid(property.Property.OriginalDefinition.Type, mapping);
            }
            if (operation is IFieldReferenceOperation field)
            {
                if (receiver.Type is INamedTypeSymbol { IsTupleType: true } tuple)
                {
                    for (var i = 0; i < tuple.TupleElements.Length; i++)
                    { if (tuple.TupleElements[i].Name == field.Field.Name) { return receiver.Value.Root.Arguments[i]; } }
                }
                return PatternSymbolType(field.Field, receiver.Value, receiver.Type);
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
        private readonly List<(HmType Shape, ExpressionSyntax Store)> refReturnDefaultDomains = [];

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
                    if (position.Start < start || position.End > start + domain.Store.Span.Length || shape.IsValueType == true || !constrained.Add(shape))
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
                else if (StorageExpression(assignment.Left) is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax)
                {
                    var type = StorageType(assignment.Left);
                    foreach (var literal in StoredAlternatives(assignment.Right))
                    { if (StoredLiteralValue(literal, type) is not null) { candidates.Add((assignment.Left, literal, type, null)); } }
                }
            }
            if (candidates.Count == 0 || parameters.Length == 0)
            { return; }
            var template = HmType.Function(parameters, new("void"));
            var fresh = new HmScheme(template, template.FreeVariables()).Instantiate();
            var probe = new Session(model, cancellationToken) { currentMethod = currentMethod, storageLambdaInputs = new(SymbolEqualityComparer.Default) };
            probe.SetIteratorReturn(declaration, currentMethod);
            if (probe.iteratorReturn is null)
            { probe.SetAsyncReturn(currentMethod); }
            var probeParameters = fresh.Arguments.Take(parameters.Length).ToArray();
            probe.currentFunction = HmType.Function(probeParameters, CSharpTypes.Erase(currentMethod.ReturnType));
            for (var i = 0; i < parameters.Length; i++)
            { probe.environment.Add(currentMethod.Parameters[i], probeParameters[i]); }
            var resolving = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var boundDeconstructions = new HashSet<int>();
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
                if (probe.StoredLiteralValue(literal, type) is null)
                { return; }
                try
                {
                    BindLocals(target);
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
                    if (plan.Parameter == parameter && plan.Path.SequenceEqual(path) && StoredLiteralValue(plan.Literal, plan.Type) is { } literal)
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
                        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && StorageExpression(assignment.Left) is IdentifierNameSyntax
                            && model.GetSymbolInfo(StorageExpression(assignment.Left), cancellationToken).Symbol is { } assigned
                            && assigned is ILocalSymbol or IParameterSymbol)
                        {
                            foreach (var literal in StoredAlternatives(assignment.Right))
                            {
                                if (StoredLiteralValue(literal, StorageType(assignment.Left)) is null)
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
            var storageType = symbol switch { ILocalSymbol local => local.Type, IParameterSymbol parameter => parameter.Type, _ => null };
            if (storageType is null || !defaultStores.TryGetValue(symbol, out var literals))
            { return type; }
            foreach (var literal in literals)
            {
                if ((!onlyCapturedWrites || !SymbolEqualityComparer.Default.Equals(symbol.ContainingSymbol, model.GetEnclosingSymbol(literal.SpanStart, cancellationToken)))
                    && StoredLiteralValue(literal, storageType) is { } value)
                { type = MergeDefaultValues(type, value); }
            }
            return type;
        }

        private HmType? StoredLiteralValue(ExpressionSyntax expression, ITypeSymbol target)
        {
            if (expression is null)
            { throw new ArgumentNullException(nameof(expression)); }
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
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
        private void BindNullableArgument(IOperation value, HmType expected)
        {
            if (value is null)
            { throw new ArgumentNullException(nameof(value)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            if (value.Syntax is ExpressionSyntax interpolation && IsTargetedInterpolation(interpolation))
            { Interpolation(interpolation, expected); return; }
            if (TryBindFixedArgument(value, expected))
            { return; }
            var inferred = BoundExpression(value).Root;
            expected = expected.Root;
            if (TryBindDynamicArgument(value, expected, inferred))
            { return; }
            if (TryBindArgumentConversion(value, expected, inferred))
            { return; }
            if (expected.Name == CSharpTypes.NullableParameter)
            {
                // A T? formal accepts an unannotated actual without making that
                // actual nullable. The result scheme still retains its annotation.
                UnifyDeconstructionReceiver(expected.Arguments[0], inferred);
            }
            else if (expected.Name == CSharpTypes.NullableReference && inferred.Name is not (CSharpTypes.NullableReference or CSharpTypes.NullableParameter))
            {
                UnifyDeconstructionReceiver(expected.Arguments[0], inferred);
            }
            else if (!expected.IsVariable && inferred.Name == CSharpTypes.NullableReference && expected.Name != CSharpTypes.NullableReference)
            {
                HmType.Unify(inferred.Arguments[0], expected);
            }
            else if (expected.Name == CSharpTypes.NullableValue && value.Syntax is ExpressionSyntax expression
                && model.GetTypeInfo(expression, cancellationToken).Type is ITypeSymbol source && value.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
                && SymbolEqualityComparer.Default.Equals(source, nullable.TypeArguments[0]))
            {
                HmType.Unify(inferred, expected.Arguments[0]);
            }
            else
            {
                HmType.Unify(inferred, expected);
            }
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

        private bool IntroducesCompilerWarnings(Compilation changed, IEnumerable<Diagnostic>? originalDiagnostics = null, IEnumerable<Diagnostic>? changedDiagnostics = null)
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
