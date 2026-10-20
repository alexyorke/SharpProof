using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public sealed record MethodResult(string Method, string Status, string Message, string? Signature = null)
{
    public string Method { get; } = Method ?? throw new ArgumentNullException(nameof(Method));
    public string Status { get; } = Status ?? throw new ArgumentNullException(nameof(Status));
    public string Message { get; } = Message ?? throw new ArgumentNullException(nameof(Message));
}

public static partial class Inference
{
    // Hidden-by-default compiler diagnostics describe editor style, such as unused
    // imports. Existing style errors do not invalidate type binding; newly introduced
    // style errors are still rejected by the diagnostic comparison below.
    public static bool IsCompilationError(Diagnostic diagnostic)
    {
        if (diagnostic is null)
        { throw new ArgumentNullException(nameof(diagnostic)); }
        return diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.DefaultSeverity != DiagnosticSeverity.Hidden;
    }
    public static MethodResult Analyze(MethodDeclarationSyntax method, SemanticModel model, CancellationToken cancellationToken)
    {
        if (method is null)
        {
            throw new global::System.ArgumentNullException(nameof(method));
        }

        if (model is null)
        {
            throw new global::System.ArgumentNullException(nameof(model));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new Session(model, cancellationToken).Analyze(method);
        }
        catch (InferenceException exception)
        {
            return new(method.Identifier.ValueText, "Skipped", exception.Message);
        }
    }

    private sealed partial class Session(SemanticModel model, CancellationToken cancellationToken)
    {
        private readonly SemanticModel model = model ?? throw new ArgumentNullException(nameof(model));
        private readonly Dictionary<ISymbol, HmType> environment = new(SymbolEqualityComparer.Default);
        private IMethodSymbol? currentMethod;
        private HmType? currentFunction;
        public MethodResult Analyze(MethodDeclarationSyntax method)
        {
            var symbol = model.GetDeclaredSymbol(method, cancellationToken) ?? throw new InferenceException("Method has no symbol.");
            if (symbol.IsVararg)
            { return new(symbol.Name, "Concrete", "C# vararg methods cannot declare type parameters."); }
            if (symbol.IsExtern || method.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                throw new InferenceException("Extern and partial root methods are unsupported.");
            }

            if (IsRequiredBuilderMember(symbol) && IsUsedBuilderMember(symbol))
            { return new(symbol.Name, "Concrete", "Compiler-required async-builder members retain their declared signatures."); }

            SetIteratorReturn(method, symbol);
            if (iteratorReturn is null)
            { SetAsyncReturn(symbol); }
            needsBuilderEmission |= HasNestedAsyncBuilder(method);
            var parameters = symbol.Parameters.Select(p =>
            {
                if (p is null)
                {
                    throw new global::System.ArgumentNullException(nameof(p));
                }

                return CSharpTypes.Erase(p.Type, method.SyntaxTree.Options is CSharpParseOptions { LanguageVersion: >= LanguageVersion.CSharp13 }
                    && model.Compilation.SupportsRuntimeCapability(RuntimeCapability.ByRefLikeGenerics));
            }).ToArray();
            for (var i = 0; i < parameters.Length; i++)
            {
                if (method.Parameters()[i].Default is not null)
                {
                    HmType.Unify(parameters[i], CSharpTypes.Rigid(symbol.Parameters[i].Type));
                }
            }

            for (var i = 0; i < parameters.Length; i++)
            { ImportConstructedConstraints(symbol.Parameters[i].Type, parameters[i]); }
            currentMethod = symbol;
            var recursiveReturn = HmType.Variable();
            currentFunction = HmType.Function(parameters, recursiveReturn);
            for (var i = 0; i < parameters.Length; i++)
            {
                environment.Add(symbol.Parameters[i], parameters[i]);
            }

            HmType result;
            if (method.ExpressionBody is not null)
            {
                if (symbol.ReturnsVoid || asyncReturn is { Arity: 0 })
                { Expression(method.ExpressionBody.Expression); result = new("void"); }
                else
                { result = Returned(method.ExpressionBody.Expression); }
            }
            else if (method.Body is not null)
            {
                result = iteratorReturn is not null ? IteratorBody(method.Body)
                    : symbol.ReturnsVoid || asyncReturn is { Arity: 0 } ? VoidBody(method.Body) : Block(method.Body);
            }
            else
            {
                throw new InferenceException("No method body.");
            }

            result = iteratorReturn is not null ? WrapIteratorResult() : WrapAsyncResult(result);
            HmType.Unify(recursiveReturn, result);
            var inferable = new HashSet<HmType>(parameters.SelectMany(p =>
            {
                if (p is null)
                {
                    throw new global::System.ArgumentNullException(nameof(p));
                }

                return p.FreeVariables();
            }));
            PreserveResultOnly(result, CSharpTypes.Rigid(symbol.ReturnType), inferable);
            CompleteArgumentConversions(inferable);
            ImportConstructedConstraints(symbol.ReturnType, result);
            ImportNullableConstraints(parameters, result);
            NormalizeConstraints();
            var variables = new Dictionary<HmType, string>();
            var reserved = new HashSet<string>(method.DescendantTokens().Where((SyntaxToken /* SPINT001: all values valid - IsKind accepts every SyntaxToken including default. */ token) => token.IsKind(SyntaxKind.IdentifierToken)).Select((SyntaxToken /* SPINT001: all values valid - Text accepts every SyntaxToken including default. */ token) => token.ValueText));
            for (var owner = symbol.ContainingType; owner is not null; owner = owner.ContainingType)
            {
                foreach (var parameter in owner.TypeParameters)
                {
                    reserved.Add(parameter.Name);
                }
            }

            var nextName = 1;
            void NameVariables(HmType type)
            {
                if (type is null)
                {
                    throw new global::System.ArgumentNullException(nameof(type));
                }

                type = type.Root;
                if (type.IsVariable && !variables.ContainsKey(type))
                {
                    string name;
                    do
                    {
                        name = "T" + nextName++;
                    }
                    while (reserved.Contains(name));
                    variables.Add(type, name);
                }

                foreach (var argument in type.Arguments)
                {
                    NameVariables(argument);
                }
            }

            foreach (var parameter in parameters)
            {
                NameVariables(parameter);
            }

            NameVariables(result);
            if (variables.Count == 0)
            {
                return new(symbol.Name, "Concrete", "Body requires concrete types; no generic suggestion.");
            }

            // A type variable only present in the result cannot be inferred by C# callers.
            var inputVariables = new Dictionary<HmType, string>();
            foreach (var parameter in parameters)
            {
                parameter.Format(inputVariables);
            }

            if (variables.Keys.Any(v =>
            {
                if (v is null)
                {
                    throw new global::System.ArgumentNullException(nameof(v));
                }

                return !inputVariables.ContainsKey(v);
            }))
            {
                throw new InferenceException("A result-only type variable cannot be inferred at a C# call site.");
            }

            var constraintPosition = method.ConstraintClauses.Count == 0 ? method.ParameterList.Span.End : method.ConstraintClauses[method.ConstraintClauses.Count - 1].Span.End;
            var nullableAnnotations = (model.GetNullableContext(constraintPosition) & NullableContext.AnnotationsEnabled) != 0;
            var nullableConstraints = BuildConstraints(variables, nullableAnnotations);
            var parameterNames = parameters.Select((t, i) =>
            {
                if (t is null)
                {
                    throw new global::System.ArgumentNullException(nameof(t));
                }

                if (i < 0)
                {
                    throw new global::System.ArgumentOutOfRangeException(nameof(i));
                }

                return CSharpTypes.Render(t, symbol.Parameters[i].Type, variables,
                    (model.GetNullableContext(method.Parameters()[i].Type!.SpanStart) & NullableContext.AnnotationsEnabled) != 0, model, method.ParameterList.Span.End);
            }).ToArray();
            var returnName = CSharpTypes.Render(result, symbol.ReturnType, variables,
                (model.GetNullableContext(method.ReturnType.SpanStart) & NullableContext.AnnotationsEnabled) != 0, model, method.ParameterList.Span.End);
            TypeSyntax returnSyntax = returnName == "void" ? SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)) : SyntaxFactory.ParseTypeName(returnName);
            returnSyntax = method.ReturnType is RefTypeSyntax refReturn ? refReturn.WithType(returnSyntax.WithTriviaFrom(refReturn.Type)) : returnSyntax.WithTriviaFrom(method.ReturnType);
            var candidate = method.WithReturnType(returnSyntax).WithTypeParameterList(SyntaxFactory.TypeParameterList(SyntaxFactory.SeparatedList((method.TypeParameterList?.Parameters ?? default).Concat(variables.Values.Select(SyntaxFactory.TypeParameter))))).WithParameterList(method.ParameterList.WithParameters(SyntaxFactory.SeparatedList(method.Parameters().Select((p, i) =>
            {
                if (p is null)
                {
                    throw new global::System.ArgumentNullException(nameof(p));
                }

                if (i < 0)
                {
                    throw new global::System.ArgumentOutOfRangeException(nameof(i));
                }

                return p.WithType(SyntaxFactory.ParseTypeName(parameterNames[i]).WithTriviaFrom(p.Type!));
            }))));
            candidate = candidate.WithConstraintClauses(method.ConstraintClauses.AddRange(nullableConstraints));
            // Recheck the original compilation with just this signature changed. This catches
            // explicit local types, conversions, name collisions and broken call sites.
            var tree = method.SyntaxTree;
            var changed = tree.WithRootAndOptions(tree.GetRoot(cancellationToken).ReplaceNode(method, candidate), tree.Options);
            Compilation ReplaceCandidate(SyntaxTree replacement)
            {
                if (replacement is null)
                { throw new ArgumentNullException(nameof(replacement)); }
                var rewritten = model.Compilation.ReplaceSyntaxTree(tree, replacement);
                if (model.Compilation.Options.SyntaxTreeOptionsProvider is { } sourceOptions)
                { rewritten = rewritten.WithOptions(rewritten.Options.WithSyntaxTreeOptionsProvider(new CandidateTreeOptions(sourceOptions, tree, replacement))); }
                return rewritten;
            }
            var changedCompilation = ReplaceCandidate(changed);
            if (asyncBuilder is not null && symbol.IsAsync)
            {
                var layoutMethod = changed.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(declaration =>
                { if (declaration is null) { throw new ArgumentNullException(nameof(declaration)); } return declaration.SpanStart == method.SpanStart; });
                if (InferBuilderLayoutConstraints(changedCompilation, layoutMethod, variables))
                {
                    candidate = candidate.WithConstraintClauses(method.ConstraintClauses.AddRange(BuildConstraints(variables, nullableAnnotations)));
                    changed = tree.WithRootAndOptions(tree.GetRoot(cancellationToken).ReplaceNode(method, candidate), tree.Options);
                    changedCompilation = ReplaceCandidate(changed);
                }
            }
            if (changedCompilation.GetDiagnostics(cancellationToken).Any(d =>
            {
                if (d is null)
                {
                    throw new global::System.ArgumentNullException(nameof(d));
                }

                return IsCompilationError(d);
            }))
            {
                return new(symbol.Name, "Rejected", "Inferred signature fails C# compilation or breaks a call site.");
            }

            if (IntroducesCompilerWarnings(changedCompilation))
            {
                return new(symbol.Name, "Rejected", "Generalization introduces compiler warnings or style errors.");
            }

            var changedMethod = changed.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m =>
            {
                if (m is null)
                {
                    throw new global::System.ArgumentNullException(nameof(m));
                }

                return m.SpanStart == method.SpanStart;
            });
            var changedModel = changedCompilation.GetSemanticModel(changed);
            var changedSymbol = changedModel.GetDeclaredSymbol(changedMethod, cancellationToken)!;
            var signatureCopies = new Dictionary<HmType, HmType>();
            HmType CopySignature(HmType type)
            {
                if (type is null)
                { throw new ArgumentNullException(nameof(type)); }
                type = type.Root;
                if (signatureCopies.TryGetValue(type, out var copied))
                { return copied; }
                copied = type.WithArguments(type.Arguments.Select(CopySignature).ToArray());
                signatureCopies.Add(type, copied);
                return copied;
            }
            try
            {
                foreach (var variable in variables)
                { HmType.Unify(CopySignature(variable.Key), CSharpTypes.Rigid(changedSymbol.TypeParameters.Single(p => { if (p is null) { throw new ArgumentNullException(nameof(p)); } return p.Name == variable.Value; }))); }
                for (var i = 0; i < parameters.Length; i++)
                { HmType.Unify(CopySignature(parameters[i]), CSharpTypes.Rigid(changedSymbol.Parameters[i].Type)); }
                HmType.Unify(CopySignature(result), CSharpTypes.Rigid(changedSymbol.ReturnType));
            }
            catch (InferenceException)
            { return new(symbol.Name, "Rejected", "Rendered signature changes a nominal constructor or generic parameter scope."); }
            // Validate statically resolved calls in every supplied file. Merely
            // compiling callers does not preserve conversions or overload selection
            // when this method becomes generic or its result narrows.
            foreach (var sourceTree in model.Compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var beforeModel = model.Compilation.GetSemanticModel(sourceTree);
                var afterTree = sourceTree == tree ? changed : sourceTree;
                var afterModel = changedCompilation.GetSemanticModel(afterTree);
                var beforeRoot = sourceTree.GetRoot(cancellationToken);
                var afterRoot = afterTree.GetRoot(cancellationToken);
                if (!PreservesPinningBindings(beforeRoot, afterRoot, beforeModel, afterModel, changedMethod, changed))
                { return new(symbol.Name, "Rejected", "Generalization changes the selected pinning pattern."); }
                if (!PreservesForEachBindings(beforeRoot, afterRoot, beforeModel, afterModel, changedMethod, changed))
                { return new(symbol.Name, "Rejected", "Generalization changes the selected enumeration protocol or implicit await."); }
                if (!PreservesCollectionBindings(beforeRoot, afterRoot, beforeModel, afterModel))
                { return new(symbol.Name, "Rejected", "Generalization changes collection construction, Add or spread protocol selection."); }
                var beforeAwaits = beforeRoot.DescendantNodes().OfType<AwaitExpressionSyntax>().ToArray();
                var afterAwaits = afterRoot.DescendantNodes().OfType<AwaitExpressionSyntax>().ToArray();
                for (var i = 0; i < beforeAwaits.Length; i++)
                {
                    var beforeAwait = beforeModel.GetAwaitExpressionInfo(beforeAwaits[i]);
                    var afterAwait = afterModel.GetAwaitExpressionInfo(afterAwaits[i]);
                    if (!SameAwaitMethod(beforeAwait.GetAwaiterMethod, afterAwait.GetAwaiterMethod, changedMethod, changed)
                        || !SameAwaitMethod(beforeAwait.GetResultMethod, afterAwait.GetResultMethod, changedMethod, changed)
                        || ProtocolMember(beforeAwait.IsCompletedProperty) != ProtocolMember(afterAwait.IsCompletedProperty)
                        || beforeAwait.IsDynamic != afterAwait.IsDynamic)
                    { return new(symbol.Name, "Rejected", "Generalization changes the selected await pattern."); }
                }
                var originalCalls = beforeRoot.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
                var changedCalls = afterRoot.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
                for (var i = 0; i < originalCalls.Length; i++)
                {
                    var before = (beforeModel.GetOperation(originalCalls[i], cancellationToken) as IInvocationOperation)?.TargetMethod
                        ?? beforeModel.GetSymbolInfo(originalCalls[i], cancellationToken).Symbol as IMethodSymbol;
                    var after = (afterModel.GetOperation(changedCalls[i], cancellationToken) as IInvocationOperation)?.TargetMethod
                        ?? afterModel.GetSymbolInfo(changedCalls[i], cancellationToken).Symbol as IMethodSymbol;
                    var definition = (before?.ReducedFrom ?? before)?.OriginalDefinition;
                    if (SymbolEqualityComparer.Default.Equals(definition, symbol.OriginalDefinition) && after?.DeclaringSyntaxReferences.Any(reference =>
                    {
                        if (reference is null)
                        { throw new ArgumentNullException(nameof(reference)); }
                        return reference.SyntaxTree == changed && reference.Span.Start == changedMethod.SpanStart;
                    }) == true)
                    {
                        var insideMethod = sourceTree == tree && method.Span.Contains(originalCalls[i].Span);
                        if (!insideMethod && !SameCallSignature(before!, after))
                        {
                            return new(symbol.Name, "Rejected", "Generalization changes a supplied call's instantiated signature.");
                        }
                        continue;
                    }
                    if (definition?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                        != (after?.ReducedFrom ?? after)?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                    {
                        return new(symbol.Name, "Rejected", "Generalization changes a selected method overload.");
                    }
                }
                var beforeGroups = MethodGroups(beforeRoot, beforeModel);
                var afterGroups = MethodGroups(afterRoot, afterModel);
                if (beforeGroups.Length != afterGroups.Length)
                { return new(symbol.Name, "Rejected", "Generalization changes method group resolution."); }
                for (var i = 0; i < beforeGroups.Length; i++)
                {
                    var before = (beforeModel.GetOperation(beforeGroups[i], cancellationToken) as IMethodReferenceOperation)?.Method
                        ?? (IMethodSymbol)beforeModel.GetSymbolInfo(beforeGroups[i], cancellationToken).Symbol!;
                    var after = (afterModel.GetOperation(afterGroups[i], cancellationToken) as IMethodReferenceOperation)?.Method
                        ?? (IMethodSymbol)afterModel.GetSymbolInfo(afterGroups[i], cancellationToken).Symbol!;
                    if (SymbolEqualityComparer.Default.Equals(before.OriginalDefinition, symbol.OriginalDefinition)
                        && after.DeclaringSyntaxReferences.Any(reference =>
                        {
                            if (reference is null)
                            { throw new ArgumentNullException(nameof(reference)); }
                            return reference.SyntaxTree == changed && reference.Span.Start == changedMethod.SpanStart;
                        }))
                    {
                        if (!(sourceTree == tree && method.Span.Contains(beforeGroups[i].Span)) && !SameCallSignature(before, after))
                        { return new(symbol.Name, "Rejected", "Generalization changes a supplied method group signature."); }
                    }
                    else if ((before.ReducedFrom ?? before).OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                        != (after.ReducedFrom ?? after).OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                    { return new(symbol.Name, "Rejected", "Generalization changes a selected method group overload."); }
                }
                if (!PreservesMemberBindings(beforeRoot, afterRoot, beforeModel, afterModel))
                { return new(symbol.Name, "Rejected", "Generalization changes a selected field, property or indexer."); }
                if (!PreservesPatternBindings(beforeRoot, afterRoot, beforeModel, afterModel, changedMethod, changed))
                { return new(symbol.Name, "Rejected", "Generalization changes an implicit pattern member."); }
                if (!PreservesDeconstructionBindings(beforeRoot, afterRoot, beforeModel, afterModel, changedMethod, changed))
                { return new(symbol.Name, "Rejected", "Generalization changes a deconstruction binding or conversion."); }
                var originalCreations = beforeRoot.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().ToArray();
                var changedCreations = afterRoot.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().ToArray();
                for (var i = 0; i < originalCreations.Length; i++)
                {
                    if (beforeModel.GetSymbolInfo(originalCreations[i], cancellationToken).Symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                        != afterModel.GetSymbolInfo(changedCreations[i], cancellationToken).Symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                    {
                        return new(symbol.Name, "Rejected", "Generalization changes a selected constructor.");
                    }
                }
                if (!PreservesResourceBindings(beforeRoot, afterRoot, beforeModel, afterModel, changedMethod, changed))
                { return new(symbol.Name, "Rejected", "Generalization changes resource disposal or its implicit await."); }
                if (!PreservesInterpolationBindings(beforeRoot, afterRoot, beforeModel, afterModel, changedMethod, changed))
                { return new(symbol.Name, "Rejected", "Generalization changes interpolation handler construction or append selection."); }
                if (!PreservesUserOperations(beforeRoot, afterRoot, beforeModel, afterModel))
                { return new(symbol.Name, "Rejected", "Generalization changes a selected user operator or conversion."); }
            }

            var originalIndexers = method.DescendantNodes().OfType<ElementAccessExpressionSyntax>().ToArray();
            var changedIndexers = changedMethod.DescendantNodes().OfType<ElementAccessExpressionSyntax>().ToArray();
            for (var i = 0; i < originalIndexers.Length; i++)
            {
                if (model.GetSymbolInfo(originalIndexers[i], cancellationToken).Symbol?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    != changedModel.GetSymbolInfo(changedIndexers[i], cancellationToken).Symbol?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                {
                    return new(symbol.Name, "Rejected", "Generalization changes a selected indexer.");
                }
            }
            var originalInitializers = CollectionElements(method);
            var changedInitializers = CollectionElements(changedMethod);
            for (var i = 0; i < originalInitializers.Length; i++)
            {
                if (model.GetCollectionInitializerSymbolInfo(originalInitializers[i], cancellationToken).Symbol?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    != changedModel.GetCollectionInitializerSymbolInfo(changedInitializers[i], cancellationToken).Symbol?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                {
                    return new(symbol.Name, "Rejected", "Generalization changes a collection initializer Add method.");
                }
            }

            var builderEmission = needsBuilderEmission || IsUsedBuilderMember(symbol);
            if (!builderEmission && (requiresPinningEmission || method.DescendantNodes().OfType<FixedStatementSyntax>().Any())
                && ValidateProtocolEmission(changedModel, "pinning") is string pinningFailure)
            { return new(symbol.Name, "Rejected", pinningFailure); }
            if (builderEmission && ValidateBuilderEmission(method, changedMethod, changedModel) is string builderFailure)
            { return new(symbol.Name, "Rejected", builderFailure); }
            if (!builderEmission && (requiresInterpolationEmission || method.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>().Any(interpolation =>
                { if (interpolation is null) { throw new ArgumentNullException(nameof(interpolation)); } return InterpolationHandler(model.GetOperation(interpolation, cancellationToken)) is not null; }))
                && ValidateProtocolEmission(changedModel, "interpolation-handler") is string interpolationFailure)
            { return new(symbol.Name, "Rejected", interpolationFailure); }

            if (!builderEmission && method.DescendantNodes().Any(node =>
                { if (node is null) { throw new ArgumentNullException(nameof(node)); } return node is UsingStatementSyntax or LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 }; })
                && ValidateProtocolEmission(changedModel, "resource-disposal") is string resourceFailure)
            { return new(symbol.Name, "Rejected", resourceFailure); }
            var typeParameterNames = (method.TypeParameterList?.Parameters.Select(p =>
            {
                if (p is null)
                {
                    throw new global::System.ArgumentNullException(nameof(p));
                }

                return p.Identifier.Text;
            }) ?? Enumerable.Empty<string>()).Concat(variables.Values);
            var renderedParameters = method.Parameters().Select((p, i) =>
            {
                if (p is null)
                {
                    throw new global::System.ArgumentNullException(nameof(p));
                }

                if (i < 0)
                {
                    throw new global::System.ArgumentOutOfRangeException(nameof(i));
                }

                return (p.AttributeLists.Count == 0 ? "" : string.Join(" ", p.AttributeLists.Select(attribute =>
                {
                    if (attribute is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(attribute));
                    }

                    return attribute.NormalizeWhitespace().ToFullString();
                })) + " ") + (p.Modifiers.Count == 0 ? "" : string.Join(" ", p.Modifiers.Select((SyntaxToken /* SPINT001: all values valid - Text accepts every SyntaxToken including default. */ token) => token.Text)) + " ") + parameterNames[i] + " " + p.Identifier.Text + (p.Default is null ? "" : " = " + p.Default.Value.NormalizeWhitespace().ToFullString());
            });
            var signatureReturn = method.ReturnType is RefTypeSyntax refResult ? "ref " + (refResult.ReadOnlyKeyword == default ? "" : "readonly ") + returnName : returnName;
            var signature = $"{signatureReturn} {method.Identifier.Text}<{string.Join(", ", typeParameterNames)}>({string.Join(", ", renderedParameters)})";
            if (candidate.ConstraintClauses.Count != 0)
            {
                signature += " " + string.Join(" ", candidate.ConstraintClauses.Select(clause =>
                {
                    if (clause is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(clause));
                    }

                    return clause.NormalizeWhitespace().ToFullString();
                }));
            }

            return new(symbol.Name, "Suggestion", "Review as an API change; the candidate compiles in the current compilation.", signature);
        }

        private HmType Block(BlockSyntax block)
        {
            var flow = Scoped(() => Statements(block.Statements.ToArray()));
            if (flow.Breaks || flow.Continues)
            {
                throw new InferenceException("Body has an invalid escaping loop exit.");
            }

            return flow.ReturnType ?? HmType.Variable();
        }

        private readonly record struct Flow(HmType? ReturnType, bool FallsThrough, bool Breaks = false, bool Continues = false);
        private Flow Scoped(Func<Flow> analyze)
        {
            if (analyze is null)
            {
                throw new global::System.ArgumentNullException(nameof(analyze));
            }

            var locals = new HashSet<ISymbol>(environment.Keys, SymbolEqualityComparer.Default);
            try
            {
                return analyze();
            }
            finally
            {
                foreach (var local in environment.Keys.Where((ISymbol /* SPINT001: all values valid - Dictionary keys are nonnull symbols from the semantic model. */ k) => !locals.Contains(k)).ToArray())
                {
                    environment.Remove(local);
                }
            }
        }

        private Flow Statements(StatementSyntax[] statements)
        {
            HmType? returns = null;
            var localFunctions = new List<LocalFunctionStatementSyntax>();
            var fallsThrough = true;
            var breaks = false;
            var continues = false;
            for (var index = 0; index < statements.Length; index++)
            {
                // The candidate compiler checks reachability, return completeness and definite
                // assignment. Unreachable syntax still contributes C# typing constraints.

                var statement = statements[index];
                cancellationToken.ThrowIfCancellationRequested();
                var flow = new Flow(null, true);
                switch (statement)
                {
                    case LabeledStatementSyntax labeled:
                        flow = Statements([labeled.Statement]);
                        break;
                    case GotoStatementSyntax:
                        // Label/case destinations and constant expressions are compiler checked.
                        flow = new(null, false);
                        break;
                    case LocalDeclarationStatementSyntax local when local.Modifiers.Any(SyntaxKind.ConstKeyword):
                        Declare(local.Declaration);
                        break;
                    case LocalDeclarationStatementSyntax local when local.Modifiers.Count == 0 && local.UsingKeyword == default:
                        Declare(local.Declaration);
                        break;
                    case LocalDeclarationStatementSyntax local when local.Modifiers.Count == 0:
                        DeclareResource(local.Declaration, local, local.AwaitKeyword != default);
                        break;
                    case UsingStatementSyntax resource:
                        flow = Scoped(() =>
                        {
                            if (resource.Declaration is not null)
                            { DeclareResource(resource.Declaration, resource, resource.AwaitKeyword != default); }
                            if (resource.Expression is not null)
                            { InferResource(resource.Expression, resource, resource.AwaitKeyword != default); }
                            return Branch(resource.Statement);
                        });
                        break;
                    case UnsafeStatementSyntax unsafeBlock:
                        flow = Branch(unsafeBlock.Block);
                        break;
                    case FixedStatementSyntax pinned:
                        flow = Scoped(() => { DeclarePinned(pinned.Declaration); return Branch(pinned.Statement); });
                        break;
                    case LockStatementSyntax locked:
                        // Retain Monitor versus System.Threading.Lock lowering and receiver conversions.
                        Anchor(locked.Expression);
                        flow = Branch(locked.Statement);
                        break;
                    case TryStatementSyntax attempted:
                        flow = TryFlow(attempted);
                        break;
                    case ThrowStatementSyntax { Expression: null }:
                        flow = new(null, false);
                        break;
                    case ExpressionStatementSyntax expression:
                        Expression(expression.Expression);
                        break;
                    case LocalFunctionStatementSyntax localFunction:
                        localFunctions.Add(localFunction);
                        break;
                    case ReturnStatementSyntax { Expression: null }:
                        flow = new(null, false);
                        break;
                    case ReturnStatementSyntax { Expression: not null } returned:
                        flow = new(Returned(returned.Expression), false);
                        break;
                    case IfStatementSyntax conditional:
                        BooleanCondition(conditional.Condition);
                        var yes = Branch(conditional.Statement);
                        var no = conditional.Else is not null ? Branch(conditional.Else.Statement) : new Flow(null, true);
                        flow = new(MergeReturns(yes.ReturnType, no.ReturnType), yes.FallsThrough || no.FallsThrough, yes.Breaks || no.Breaks, yes.Continues || no.Continues);
                        break;
                    case BlockSyntax nested:
                        flow = Scoped(() => Statements(nested.Statements.ToArray()));
                        break;
                    case WhileStatementSyntax loop:
                        BooleanCondition(loop.Condition);
                        flow = LoopFlow(Branch(loop.Statement));
                        break;
                    case DoStatementSyntax loop:
                        flow = LoopFlow(Branch(loop.Statement));
                        BooleanCondition(loop.Condition);
                        break;
                    case ForStatementSyntax loop:
                        flow = Scoped(() =>
                        {
                            if (loop.Declaration is not null)
                            {
                                Declare(loop.Declaration);
                            }

                            foreach (var initializer in loop.Initializers)
                            {
                                Expression(initializer);
                            }

                            if (loop.Condition is not null)
                            {
                                BooleanCondition(loop.Condition);
                            }

                            var body = Branch(loop.Statement);
                            foreach (var increment in loop.Incrementors)
                            {
                                Expression(increment);
                            }

                            return LoopFlow(body);
                        });
                        break;
                    case CommonForEachStatementSyntax loop:
                        flow = ForEachFlow(loop);
                        break;
                    case YieldStatementSyntax yielded:
                        if (iteratorElement is null)
                        { throw new InferenceException("Yield outside the current iterator."); }
                        if (yielded.Expression is null)
                        { flow = new(null, false); }
                        else
                        { HmType.Unify(iteratorElement, Returned(yielded.Expression, iteratorDeclaredElement)); }
                        break;
                    case BreakStatementSyntax:
                        flow = new(null, false, Breaks: true);
                        break;
                    case ContinueStatementSyntax:
                        flow = new(null, false, Continues: true);
                        break;
                    case SwitchStatementSyntax switched:
                        flow = Switch(switched);
                        break;
                    case CheckedStatementSyntax checkedStatement:
                        flow = Scoped(() => Statements(checkedStatement.Block.Statements.ToArray()));
                        break;
                    case ThrowStatementSyntax { Expression: not null } thrown:
                        Anchor(thrown.Expression);
                        flow = new(null, false);
                        break;
                    case EmptyStatementSyntax:
                        break;
                    default:
                        throw new InferenceException($"Unsupported statement: {statement.Kind()}.");
                }

                returns = MergeReturns(returns, flow.ReturnType);
                fallsThrough = flow.FallsThrough;
                breaks |= flow.Breaks;
                continues |= flow.Continues;
            }

            foreach (var localFunction in localFunctions)
            { AnalyzeLocalFunction(localFunction); }
            return new(returns, fallsThrough, breaks, continues);
        }

        private static Flow LoopFlow(Flow /* SPINT001: all values valid - Every flow flag combination is valid input to loop exit normalization. */ body)
        {
            return new(body.ReturnType, true);
        }

        private Flow Switch(SwitchStatementSyntax statement)
        {
            if (statement is null)
            {
                throw new global::System.ArgumentNullException(nameof(statement));
            }

            Expression(statement.Expression);
            return Scoped(() =>
            {
                HmType? returns = null;
                var hasDefault = false;
                var fallsThrough = false;
                var continues = false;
                foreach (var section in statement.Sections)
                {
                    foreach (var label in section.Labels)
                    {
                        switch (label)
                        {
                            case DefaultSwitchLabelSyntax:
                                hasDefault = true;
                                break;
                            case CaseSwitchLabelSyntax:
                                Anchor(statement.Expression);
                                break;
                            case CasePatternSwitchLabelSyntax pattern:
                                Pattern(pattern.Pattern, Expression(statement.Expression));
                                if (pattern.WhenClause is not null)
                                {
                                    BooleanCondition(pattern.WhenClause.Condition);
                                }

                                break;
                            default:
                                throw new InferenceException("Only constant/discard switch labels are supported.");
                        }
                    }

                    // Switch sections share a C# declaration scope. Retain their
                    // bindings until the entire switch has been analyzed.
                    var sectionFlow = Statements(section.Statements.ToArray());
                    returns = MergeReturns(returns, sectionFlow.ReturnType);
                    fallsThrough |= sectionFlow.FallsThrough || sectionFlow.Breaks;
                    continues |= sectionFlow.Continues;
                }

                return new Flow(returns, !hasDefault || fallsThrough, Continues: continues);
            });
        }

        private static HmType? MergeReturns(HmType? first, HmType? second)
        {
            if (first is null)
            {
                return second;
            }

            if (second is not null)
            {
                HmType.Unify(first, second);
            }

            return first;
        }

        private void Declare(VariableDeclarationSyntax declaration)
        {
            if (declaration is null)
            {
                throw new global::System.ArgumentNullException(nameof(declaration));
            }

            foreach (var variable in declaration.Variables)
            {
                var symbol = model.GetDeclaredSymbol(variable, cancellationToken) as ILocalSymbol
                    ?? throw new InferenceException("Unresolved local declaration.");
                if (variable.Initializer is null)
                {
                    environment.Add(symbol, CSharpTypes.Rigid(symbol.Type));
                }
                else if (declaration.Type is RefTypeSyntax referenceType ? referenceType.Type.IsVar : declaration.Type.IsVar)
                {
                    environment.Add(symbol, Expression(variable.Initializer.Value));
                }
                else
                {
                    // Retain the explicit local type and its conversion from the initializer.
                    Anchor(variable.Initializer.Value);
                    environment.Add(symbol, CSharpTypes.Rigid(symbol.Type));
                }
            }
        }

        private Flow Branch(StatementSyntax statement)
        {
            if (statement is null)
            {
                throw new global::System.ArgumentNullException(nameof(statement));
            }

            return Scoped(() => statement is BlockSyntax block ? Statements(block.Statements.ToArray()) : Statements([statement]));
        }

        private HmType Expression(ExpressionSyntax expression)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expression is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax
                && model.GetSymbolInfo(expression, cancellationToken).Symbol is IMethodSymbol group
                && model.GetTypeInfo(expression, cancellationToken).ConvertedType is INamedTypeSymbol { TypeKind: TypeKind.Delegate } target)
            { return MethodGroup(expression, group, target); }
            switch (expression)
            {
                case PrefixUnaryExpressionSyntax pointerRead when pointerRead.IsKind(SyntaxKind.PointerIndirectionExpression):
                    return PointerElement(pointerRead.Operand);
                case PrefixUnaryExpressionSyntax address when address.IsKind(SyntaxKind.AddressOfExpression):
                    return AddressOf(address);
                case StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax:
                    return StackAllocation(expression);
                case SizeOfExpressionSyntax:
                    return new("int");
                case RefExpressionSyntax reference:
                    return Expression(reference.Expression);
                case RangeExpressionSyntax range:
                    if (range.LeftOperand is not null)
                    { Anchor(range.LeftOperand); }
                    if (range.RightOperand is not null)
                    { Anchor(range.RightOperand); }
                    return CSharpTypes.Rigid(model.GetTypeInfo(range, cancellationToken).Type!);
                case PrefixUnaryExpressionSyntax fromEnd when fromEnd.IsKind(SyntaxKind.IndexExpression):
                    Anchor(fromEnd.Operand);
                    return CSharpTypes.Rigid(model.GetTypeInfo(fromEnd, cancellationToken).Type!);
                case AwaitExpressionSyntax awaited:
                    return Awaited(awaited);
                case IsPatternExpressionSyntax pattern:
                    Pattern(pattern.Pattern, Expression(pattern.Expression));
                    return new("bool");
                case ConditionalAccessExpressionSyntax conditionalAccess:
                    return ConditionalAccess(conditionalAccess);
                case PostfixUnaryExpressionSyntax suppressed when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    var nullableOperand = Expression(suppressed.Operand).Root;
                    return nullableOperand.Name is CSharpTypes.NullableReference or CSharpTypes.NullableParameter ? nullableOperand.Arguments[0] : nullableOperand;
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression) || literal.IsKind(SyntaxKind.DefaultLiteralExpression):
                    return CSharpTypes.Rigid(model.GetTypeInfo(literal, cancellationToken).ConvertedType ?? throw new InferenceException("Untyped null/default literal."));
                case ParenthesizedExpressionSyntax parentheses:
                    return Expression(parentheses.Expression);
                case ThrowExpressionSyntax thrown:
                    Anchor(thrown.Expression);
                    return HmType.Variable();
                case AssignmentExpressionSyntax assignment:
                    if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        if (assignment.Left is TupleExpressionSyntax or DeclarationExpressionSyntax)
                        {
                            var original = model.GetTypeInfo(assignment.Right, cancellationToken).Type;
                            if (original is null)
                            { throw new InferenceException("Unresolved deconstruction source."); }
                            return BindDeconstruction(assignment.Left, Expression(assignment.Right), original, AssignmentDeconstructionBinding(model, assignment));
                        }

                        if (model.GetOperation(assignment.Left, cancellationToken) is IDiscardOperation)
                        { return Expression(assignment.Right); }
                        return AssignValue(assignment.Left, Expression(assignment.Right), IsTargetedInterpolation(assignment.Right)
                            ? model.GetTypeInfo(assignment.Right, cancellationToken).ConvertedType : model.GetTypeInfo(assignment.Right, cancellationToken).Type);
                    }

                    if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
                    {
                        return CoalesceAssigned(assignment);
                    }
                    if (model.GetOperation(assignment, cancellationToken) is ICompoundAssignmentOperation { OperatorMethod: not null } userCompound)
                    {
                        var calculatedType = userCompound.IsLifted ? userCompound.Type! : userCompound.OperatorMethod.ReturnType;
                        var calculated = UserOperator(userCompound.OperatorMethod, [userCompound.Target, userCompound.Value], calculatedType, userCompound.IsLifted);
                        return AssignValue(assignment.Left, calculated, calculatedType);
                    }
                    if (model.GetOperation(assignment, cancellationToken) is not ICompoundAssignmentOperation { OperatorMethod: null })
                    {
                        throw new InferenceException("User-defined and coalescing assignments are unsupported.");
                    }

                    if (model.GetTypeInfo(assignment.Left, cancellationToken).Type is IPointerTypeSymbol)
                    { Anchor(assignment.Right); return AssignmentTarget(assignment.Left); }
                    AssignmentTarget(assignment.Left);
                    Anchor(assignment.Left);
                    Anchor(assignment.Right);
                    return CSharpTypes.Rigid(model.GetTypeInfo(assignment, cancellationToken).Type!);
                case PrefixUnaryExpressionSyntax increment when increment.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression:
                    return Increment(increment, increment.Operand);
                case PostfixUnaryExpressionSyntax increment when increment.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression:
                    return Increment(increment, increment.Operand);
                case IdentifierNameSyntax identifier:
                    var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
                    if (symbol is not null && environment.TryGetValue(symbol, out var type))
                    {
                        return type;
                    }

                    // Captured constructor parameters retain their instance storage type.
                    if (symbol is IParameterSymbol captured && captured.DeclaringSyntaxReferences.Any(reference =>
                    {
                        if (reference is null)
                        { throw new ArgumentNullException(nameof(reference)); }
                        return reference.GetSyntax(cancellationToken) is ParameterSyntax { Parent.Parent: TypeDeclarationSyntax };
                    }))
                    { return CSharpTypes.Rigid(captured.Type); }
                    if (symbol is IFieldSymbol field)
                    {
                        return CSharpTypes.Rigid(field.Type);
                    }

                    if (symbol is IPropertySymbol property)
                    {
                        return CSharpTypes.Rigid(property.Type);
                    }

                    throw new InferenceException("Only local and parameter references are supported.");
                case ThisExpressionSyntax or BaseExpressionSyntax:
                    return CSharpTypes.Rigid(model.GetTypeInfo(expression, cancellationToken).Type!);
                case LiteralExpressionSyntax literal when !literal.IsKind(SyntaxKind.NullLiteralExpression) && !literal.IsKind(SyntaxKind.DefaultLiteralExpression):
                    return CSharpTypes.Rigid(model.GetTypeInfo(literal, cancellationToken).Type!);
                case ConditionalExpressionSyntax conditional:
                    BooleanCondition(conditional.Condition);
                    var yes = Expression(conditional.WhenTrue);
                    HmType.Unify(yes, Expression(conditional.WhenFalse));
                    return yes;
                case TupleExpressionSyntax tuple:
                    return new("Tuple", tuple.Arguments.Select(a =>
                    {
                        if (a is null)
                        {
                            throw new global::System.ArgumentNullException(nameof(a));
                        }

                        return Expression(a.Expression);
                    }).ToArray());
                case ImplicitArrayCreationExpressionSyntax implicitArray:
                    var inferredElement = HmType.Variable();
                    foreach (var item in implicitArray.Initializer.Expressions)
                    {
                        HmType.Unify(inferredElement, Expression(item));
                    }

                    return new("[" + new string(',', implicitArray.Commas.Count) + "]", inferredElement);
                case ArrayCreationExpressionSyntax explicitArray:
                    var createdArray = (IArrayTypeSymbol)model.GetTypeInfo(explicitArray, cancellationToken).Type!;
                    foreach (var rank in explicitArray.Type.RankSpecifiers)
                    {
                        foreach (var size in rank.Sizes.Where(size =>
                        {
                            if (size is null)
                            {
                                throw new global::System.ArgumentNullException(nameof(size));
                            }

                            return size is not OmittedArraySizeExpressionSyntax;
                        }))
                        {
                            HmType.Unify(Expression(size), new("int"));
                        }
                    }

                    if (explicitArray.Initializer is not null)
                    {
                        AnchorInitializer(explicitArray.Initializer, createdArray.ElementType);
                    }

                    return CSharpTypes.Rigid(createdArray);
                case CollectionExpressionSyntax collectionSyntax:
                    return CollectionExpression(model.GetOperation(collectionSyntax, cancellationToken) as ICollectionExpressionOperation ?? throw new InferenceException("Unresolved collection expression."));
                case AnonymousObjectCreationExpressionSyntax anonymous:
                    var anonymousType = model.GetTypeInfo(anonymous, cancellationToken).Type as INamedTypeSymbol
                        ?? throw new InferenceException("Unresolved anonymous object.");
                    return new HmType(CSharpTypes.AnonymousName(anonymousType), anonymous.Initializers.Select(initializer =>
                    { if (initializer is null) { throw new ArgumentNullException(nameof(initializer)); } return Expression(initializer.Expression); }).ToArray())
                    { CSharpSymbol = anonymousType, IsValueType = false };
                case WithExpressionSyntax cloned:
                    return WithExpression(cloned);
                case AnonymousFunctionExpressionSyntax lambda:
                    return Lambda(lambda);
                case InvocationExpressionSyntax invocation:
                    if (model.GetOperation(invocation, cancellationToken) is INameOfOperation)
                    {
                        return new("string");
                    }

                    return MethodCall(invocation);
                case BaseObjectCreationExpressionSyntax creation:
                    var constructed = model.GetOperation(creation, cancellationToken) as IObjectCreationOperation ?? throw new InferenceException("Unsupported object construction.");
                    if (constructed.Constructor is null)
                    { throw new InferenceException("Unresolved constructor."); }

                    BindArguments(constructed.Arguments, constructed.Constructor.Parameters.Select(p =>
                    {
                        if (p is null)
                        {
                            throw new global::System.ArgumentNullException(nameof(p));
                        }

                        return CSharpTypes.Rigid(p.Type);
                    }).ToArray());
                    if (creation.Initializer is not null)
                    {
                        AnchorObjectInitializer(creation.Initializer);
                    }
                    return CSharpTypes.Rigid(constructed.Type!);
                case InterpolatedStringExpressionSyntax interpolated:
                    return Interpolation(interpolated);
                case BinaryExpressionSyntax binary:
                    return InterpolationHandler(model.GetOperation(binary, cancellationToken)) is not null ? Interpolation(binary) : Binary(binary);
                case PrefixUnaryExpressionSyntax unary when unary.Kind() is SyntaxKind.UnaryMinusExpression or SyntaxKind.UnaryPlusExpression or SyntaxKind.LogicalNotExpression or SyntaxKind.BitwiseNotExpression:
                    if (model.GetOperation(unary, cancellationToken) is IUnaryOperation { OperatorMethod: not null } userUnary)
                    { return UserOperator(userUnary.OperatorMethod, [userUnary.Operand], userUnary.Type!, userUnary.IsLifted); }

                    Anchor(unary.Operand);
                    return CSharpTypes.Rigid(model.GetTypeInfo(unary, cancellationToken).Type!);
                case ElementAccessExpressionSyntax element:
                    if (model.GetTypeInfo(element.Expression, cancellationToken).Type is IPointerTypeSymbol)
                    {
                        foreach (var index in element.ArgumentList.Arguments)
                        { Anchor(index.Expression); }
                        return PointerElement(element.Expression);
                    }
                    if (model.GetOperation(element, cancellationToken) is IPropertyReferenceOperation indexer)
                    {
                        return IndexedProperty(element, indexer);
                    }
                    if (model.GetOperation(element, cancellationToken) is IImplicitIndexerReferenceOperation implicitIndex)
                    { return IndexedImplicitly(element, implicitIndex); }
                    var array = model.GetTypeInfo(element.Expression, cancellationToken).Type as IArrayTypeSymbol ?? throw new InferenceException("Only array indexing is supported.");
                    var elementType = HmType.Variable();
                    HmType.Unify(Expression(element.Expression), new("[" + new string(',', array.Rank - 1) + "]", elementType));
                    if (element.ArgumentList.Arguments.Count != array.Rank)
                    {
                        throw new InferenceException("Array index count mismatch.");
                    }

                    if (array.Rank == 1 && element.ArgumentList.Arguments.Count == 1
                        && (model.GetTypeInfo(element.ArgumentList.Arguments[0].Expression, cancellationToken).ConvertedType ?? model.GetTypeInfo(element.ArgumentList.Arguments[0].Expression, cancellationToken).Type) is INamedTypeSymbol specialIndex
                        && specialIndex.ContainingType is null && specialIndex.ContainingNamespace.ToDisplayString() == "System" && specialIndex.Name is "Index" or "Range")
                    {
                        Anchor(element.ArgumentList.Arguments[0].Expression);
                        return specialIndex.Name == "Range" ? new HmType("[]", elementType) : elementType;
                    }
                    foreach (var index in element.ArgumentList.Arguments)
                    {
                        Anchor(index.Expression);
                    }

                    return elementType;
                case MemberAccessExpressionSyntax member:
                    return Member(member);
                case CastExpressionSyntax cast:
                    if (model.GetOperation(cast, cancellationToken) is IConversionOperation { OperatorMethod: not null } userCast)
                    {
                        var targetType = model.GetTypeInfo(cast.Type, cancellationToken).Type!;
                        var fixedTarget = CSharpTypes.Rigid(targetType);
                        if (TryUserConversion(model.GetTypeInfo(cast.Expression, cancellationToken).Type!, targetType, Expression(cast.Expression), fixedTarget, userCast.OperatorMethod))
                        { return fixedTarget; }
                    }
                    Anchor(cast.Expression);
                    return CSharpTypes.Rigid(model.GetTypeInfo(cast.Type, cancellationToken).Type!);
                case DefaultExpressionSyntax @default:
                    return CSharpTypes.Rigid(model.GetTypeInfo(@default.Type, cancellationToken).Type!);
                case CheckedExpressionSyntax checkedExpression:
                    return Expression(checkedExpression.Expression);
                case SwitchExpressionSyntax switched:
                    Expression(switched.GoverningExpression);
                    var switchedResult = HmType.Variable();
                    foreach (var arm in switched.Arms)
                    {
                        Pattern(arm.Pattern, Expression(switched.GoverningExpression));

                        if (arm.WhenClause is not null)
                        {
                            BooleanCondition(arm.WhenClause.Condition);
                        }

                        HmType.Unify(switchedResult, Expression(arm.Expression));
                    }

                    return switchedResult;
                default:
                    throw new InferenceException($"Unsupported expression: {expression.Kind()}.");
            }
        }

        private void Anchor(ExpressionSyntax expression)
        {
            if (IsTargetedInterpolation(expression))
            { UnifyDeconstructionReceiver(CSharpTypes.Rigid(model.GetTypeInfo(expression, cancellationToken).ConvertedType!), Interpolation(expression)); return; }
            // Local declaration annotations and flow-state annotations can differ.
            // Retain the declared wrapper while fixing the same runtime receiver type.
            UnifyDeconstructionReceiver(CSharpTypes.Rigid(model.GetTypeInfo(expression, cancellationToken).Type ?? model.GetTypeInfo(expression, cancellationToken).ConvertedType ?? throw new InferenceException("Untyped expression.")), Expression(expression));
        }

        private static bool SameCallSignature(IMethodSymbol before, IMethodSymbol after)
        {
            if (before is null)
            { throw new ArgumentNullException(nameof(before)); }
            if (after is null)
            { throw new ArgumentNullException(nameof(after)); }
            if (before.RefKind != after.RefKind || before.Parameters.Length != after.Parameters.Length || !CSharpTypes.SameSignatureType(before.ReturnType, after.ReturnType))
            {
                return false;
            }
            for (var i = 0; i < before.Parameters.Length; i++)
            {
                if (before.Parameters[i].RefKind != after.Parameters[i].RefKind || !CSharpTypes.SameSignatureType(before.Parameters[i].Type, after.Parameters[i].Type))
                {
                    return false;
                }
            }
            return true;
        }

        private static ExpressionSyntax[] CollectionElements(MethodDeclarationSyntax method)
        {
            if (method is null)
            {
                throw new ArgumentNullException(nameof(method));
            }
            var elements = new List<ExpressionSyntax>();
            foreach (var initializer in method.DescendantNodes().OfType<InitializerExpressionSyntax>())
            {
                if (initializer.IsKind(SyntaxKind.CollectionInitializerExpression))
                {
                    elements.AddRange(initializer.Expressions);
                }
            }
            return elements.ToArray();
        }

        // Construction fixes the receiver type. Pin initializer source types too,
        // preserving overload selection and boxing/widening conversions while
        // allowing unrelated method parameters to generalize.
        private void AnchorObjectInitializer(InitializerExpressionSyntax initializer)
        {
            if (initializer is null)
            {
                throw new ArgumentNullException(nameof(initializer));
            }
            foreach (var entry in initializer.Expressions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry is InitializerExpressionSyntax nested)
                {
                    AnchorObjectInitializer(nested);
                }
                else if (entry is AssignmentExpressionSyntax assignment)
                {
                    if (assignment.Left is ImplicitElementAccessSyntax index)
                    {
                        foreach (var argument in index.ArgumentList.Arguments)
                        {
                            Anchor(argument.Expression);
                        }
                    }
                    else if (model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not IFieldSymbol and not IPropertySymbol)
                    {
                        throw new InferenceException("Unsupported object initializer member.");
                    }
                    if (assignment.Right is InitializerExpressionSyntax memberInitializer)
                    {
                        AnchorObjectInitializer(memberInitializer);
                    }
                    else
                    {
                        Anchor(assignment.Right);
                    }
                }
                else
                {
                    Anchor(entry);
                }
            }
        }

        private HmType AssignmentTarget(ExpressionSyntax target)
        {
            if (target is InvocationExpressionSyntax pointerInvocation
                && model.GetOperation(pointerInvocation, cancellationToken) is IFunctionPointerInvocationOperation { Target.Type: IFunctionPointerTypeSymbol { Signature.ReturnsByRef: true } })
            { return Expression(pointerInvocation); }
            if (target is InvocationExpressionSyntax invocation && model.GetOperation(invocation, cancellationToken) is IInvocationOperation { TargetMethod.ReturnsByRef: true })
            { return Expression(invocation); }
            if (target is PrefixUnaryExpressionSyntax pointerTarget && pointerTarget.IsKind(SyntaxKind.PointerIndirectionExpression))
            { return Expression(target); }
            if (target is not IdentifierNameSyntax and not ElementAccessExpressionSyntax and not MemberAccessExpressionSyntax)
            {
                throw new InferenceException("Unsupported assignment target.");
            }

            return Expression(target);
        }

        private HmType AssignValue(ExpressionSyntax targetSyntax, HmType value, ITypeSymbol? sourceType)
        {
            if (targetSyntax is null)
            {
                throw new global::System.ArgumentNullException(nameof(targetSyntax));
            }

            if (value is null)
            {
                throw new global::System.ArgumentNullException(nameof(value));
            }

            var target = AssignmentTarget(targetSyntax);
            var targetType = model.GetTypeInfo(targetSyntax, cancellationToken).Type!;
            if (sourceType is not null && !SymbolEqualityComparer.Default.Equals(targetType, sourceType))
            {
                // Assignment conversions are not HM equality. Keep both original
                // endpoints so widening/boxing still happens at the assignment.
                HmType.Unify(target, CSharpTypes.Rigid(targetType));
                HmType.Unify(value, CSharpTypes.Rigid(sourceType));
            }
            else if (target.Root.Name == CSharpTypes.NullableReference && value.Root.Name != CSharpTypes.NullableReference)
            {
                HmType.Unify(target.Root.Arguments[0], value);
            }
            else if (value.Root.Name == CSharpTypes.NullableReference && target.Root.Name != CSharpTypes.NullableReference)
            {
                HmType.Unify(target, value.Root.Arguments[0]);
            }
            else
            {
                HmType.Unify(target, value);
            }

            return target;
        }

        private HmType Increment(ExpressionSyntax expression, ExpressionSyntax operand)
        {
            if (operand is null)
            {
                throw new global::System.ArgumentNullException(nameof(operand));
            }

            if (model.GetTypeInfo(operand, cancellationToken).Type is IPointerTypeSymbol)
            { return AssignmentTarget(operand); }
            if (model.GetOperation(expression, cancellationToken) is IIncrementOrDecrementOperation { OperatorMethod: not null } userIncrement)
            {
                var result = UserOperator(userIncrement.OperatorMethod, [userIncrement.Target], userIncrement.Type!, userIncrement.IsLifted);
                HmType.Unify(AssignmentTarget(operand), result);
                return result;
            }
            if (model.GetOperation(expression, cancellationToken) is not IIncrementOrDecrementOperation { OperatorMethod: null })
            {
                throw new InferenceException("User-defined increments are unsupported.");
            }

            AssignmentTarget(operand);
            Anchor(operand);
            return CSharpTypes.Rigid(model.GetTypeInfo(expression, cancellationToken).Type!);
        }

        private HmType BindDeconstruction(ExpressionSyntax target, HmType value, ITypeSymbol original, DeconstructionInfo /* SPINT001: all values valid - Default metadata supports tuple binding; unresolved custom bindings are reported explicitly. */ info = default)
        {
            if (target is null)
            { throw new ArgumentNullException(nameof(target)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (value is null)
            {
                throw new global::System.ArgumentNullException(nameof(value));
            }

            switch (target)
            {
                case DeclarationExpressionSyntax declaration:
                    if (!declaration.Type.IsVar)
                    {
                        var declaredType = model.GetTypeInfo(declaration.Type, cancellationToken).Type!;
                        if (!SymbolEqualityComparer.IncludeNullability.Equals(original, declaredType))
                        {
                            // Explicit deconstruction conversions keep separate source and local types.
                            HmType.Unify(value, CSharpTypes.Rigid(original));
                            value = CSharpTypes.Rigid(declaredType);
                            original = declaredType;
                        }
                        else
                        { HmType.Unify(value, CSharpTypes.Rigid(declaredType)); }
                    }

                    return BindDesignation(declaration.Designation, value, original, info);
                case TupleExpressionSyntax tuple:
                    var parts = DeconstructionParts(value, original, info, tuple.Arguments.Count);
                    var assigned = new HmType[parts.Values.Length];
                    for (var i = 0; i < assigned.Length; i++)
                    { assigned[i] = BindDeconstruction(tuple.Arguments[i].Expression, parts.Values[i], parts.Types[i], parts.Steps[i]); }
                    return new("Tuple", assigned);
                case IdentifierNameSyntax identifier when model.GetOperation(identifier, cancellationToken) is IDiscardOperation:
                    return value;
                case IdentifierNameSyntax or MemberAccessExpressionSyntax or ElementAccessExpressionSyntax:
                    return AssignValue(target, value, original);
                default:
                    throw new InferenceException("Mixed declarations and assignments in deconstruction are unsupported.");
            }
        }

        private HmType BindDesignation(VariableDesignationSyntax designation, HmType value, ITypeSymbol original, DeconstructionInfo /* SPINT001: all values valid - Default metadata supports tuple binding; unresolved custom bindings are reported explicitly. */ info = default)
        {
            if (designation is null)
            { throw new ArgumentNullException(nameof(designation)); }
            if (original is null)
            { throw new ArgumentNullException(nameof(original)); }
            if (value is null)
            {
                throw new global::System.ArgumentNullException(nameof(value));
            }

            cancellationToken.ThrowIfCancellationRequested();
            switch (designation)
            {
                case SingleVariableDesignationSyntax variable:
                    environment.Add(model.GetDeclaredSymbol(variable, cancellationToken)!, value);
                    return value;
                case DiscardDesignationSyntax:
                    return value;
                case ParenthesizedVariableDesignationSyntax tuple:
                    var parts = DeconstructionParts(value, original, info, tuple.Variables.Count);
                    var assigned = new HmType[parts.Values.Length];
                    for (var i = 0; i < assigned.Length; i++)
                    { assigned[i] = BindDesignation(tuple.Variables[i], parts.Values[i], parts.Types[i], parts.Steps[i]); }
                    return new("Tuple", assigned);
                default:
                    throw new InferenceException("Unsupported deconstruction designation.");
            }
        }

        private void AnchorInitializer(InitializerExpressionSyntax initializer, ITypeSymbol element)
        {
            if (initializer is null)
            {
                throw new global::System.ArgumentNullException(nameof(initializer));
            }

            if (element is null)
            {
                throw new global::System.ArgumentNullException(nameof(element));
            }

            foreach (var item in initializer.Expressions)
            {
                if (item is InitializerExpressionSyntax nested)
                {
                    AnchorInitializer(nested, element);
                }
                else
                {
                    HmType.Unify(Expression(item), CSharpTypes.Rigid(element));
                }
            }
        }

        private static void PreserveResultOnly(HmType inferred, HmType declared, HashSet<HmType> inferable)
        {
            if (declared is null)
            {
                throw new global::System.ArgumentNullException(nameof(declared));
            }

            inferred = inferred.Root;
            declared = declared.Root;
            if (inferred.IsVariable && !inferable.Contains(inferred))
            {
                HmType.Unify(inferred, declared);
                return;
            }

            if (inferred.Name == declared.Name && inferred.Arguments.Length == declared.Arguments.Length)
            {
                for (var i = 0; i < inferred.Arguments.Length; i++)
                {
                    PreserveResultOnly(inferred.Arguments[i], declared.Arguments[i], inferable);
                }
            }
        }

        private HmType Binary(BinaryExpressionSyntax binary)
        {
            if (model.GetTypeInfo(binary.Left, cancellationToken).Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol
                || model.GetTypeInfo(binary.Right, cancellationToken).Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
            { return PointerBinary(binary); }
            if (binary.IsKind(SyntaxKind.CoalesceExpression))
            {
                return Coalesced(binary);
            }
            if (model.GetOperation(binary, cancellationToken) is IBinaryOperation { OperatorMethod: not null } userBinary)
            { return UserOperator(userBinary.OperatorMethod, [userBinary.LeftOperand, userBinary.RightOperand], userBinary.Type!, userBinary.IsLifted); }
            if (binary.Kind() is SyntaxKind.AsExpression or SyntaxKind.IsExpression)
            {
                throw new InferenceException("User-defined, null-coalescing and type-test operators are unsupported.");
            }

            // Retain original operand types so numeric promotion, boxing and string
            // concatenation keep their C# meaning. Equality is not unconstrained HM equality.
            Anchor(binary.Left);
            Anchor(binary.Right);
            return CSharpTypes.Rigid(model.GetTypeInfo(binary, cancellationToken).Type!);
        }

        private HmType Member(MemberAccessExpressionSyntax member)
        {
            var receiverType = model.GetTypeInfo(member.Expression, cancellationToken).Type;
            if (receiverType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } && member.Name.Identifier.ValueText is "Value" or "HasValue")
            {
                var nullableElement = HmType.Variable();
                HmType.Unify(Expression(member.Expression), new(CSharpTypes.NullableValue, nullableElement));
                return member.Name.Identifier.ValueText == "Value" ? nullableElement : new("bool");
            }
            if (receiverType is INamedTypeSymbol { IsTupleType: true } tuple && model.GetSymbolInfo(member, cancellationToken).Symbol is IFieldSymbol tupleField)
            {
                var index = tuple.TupleElements.IndexOf(tupleField);
                if (index < 0)
                {
                    index = tuple.TupleElements.IndexOf(tupleField.CorrespondingTupleField!);
                }

                if (index < 0)
                {
                    index = tuple.TupleElements.Select((field, i) =>
                    {
                        if (field is null)
                        {
                            throw new global::System.ArgumentNullException(nameof(field));
                        }

                        return (field, i);
                    }).First(p => p.field.CorrespondingTupleField?.Name == tupleField.Name).i;
                }

                var items = tuple.TupleElements.Select(_ => HmType.Variable()).ToArray();
                HmType.Unify(Expression(member.Expression), new("Tuple", items));
                return items[index];
            }

            if (receiverType is IArrayTypeSymbol array && member.Name.Identifier.ValueText is "Length" or "LongLength" or "Rank")
            {
                HmType.Unify(Expression(member.Expression), new("[" + new string(',', array.Rank - 1) + "]", HmType.Variable()));
                return CSharpTypes.Rigid(model.GetTypeInfo(member, cancellationToken).Type!);
            }

            var selected = model.GetSymbolInfo(member, cancellationToken).Symbol;
            if (selected is not IPropertySymbol and not IFieldSymbol)
            {
                throw new InferenceException("Unsupported member access.");
            }

            if (selected is IPropertySymbol { IsStatic: false } or IFieldSymbol { IsStatic: false })
            {
                return receiverType is IPointerTypeSymbol pointer
                    ? PatternSymbolType(selected, PointerElement(member.Expression), pointer.PointedAtType)
                    : PatternSymbolType(selected, Expression(member.Expression), receiverType ?? throw new InferenceException("Unresolved member receiver."));
            }

            return CSharpTypes.Rigid(model.GetTypeInfo(member, cancellationToken).Type!);
        }

        private HmType MethodCall(InvocationExpressionSyntax invocation)
        {
            if (model.GetOperation(invocation, cancellationToken) is IFunctionPointerInvocationOperation pointerCall)
            {
                var pointer = Expression(invocation.Expression).Root;
                if (pointerCall.Target.Type is not IFunctionPointerTypeSymbol declared || pointer.Name != CSharpTypes.FunctionPointerName(declared.Signature))
                { throw new InferenceException("Unresolved function pointer invocation."); }
                // A default literal takes its type from the already inferred
                // function pointer parameter, rather than fixing that parameter.
                BindArguments(pointerCall.Arguments.Where(argument =>
                { if (argument is null) { throw new ArgumentNullException(nameof(argument)); } return !argument.Value.Syntax.IsKind(SyntaxKind.DefaultLiteralExpression); }),
                    pointer.Arguments.Take(pointer.Arguments.Length - 1).ToArray());
                return pointer.Arguments[pointer.Arguments.Length - 1];
            }
            var operation = model.GetOperation(invocation, cancellationToken) as IInvocationOperation ?? throw new InferenceException("Unresolved or dynamic invocation.");
            var called = operation.TargetMethod;
            if (called.MethodKind == MethodKind.DelegateInvoke)
            {
                var receiver = invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke" } member
                    ? member.Expression : invocation.Expression;
                var signature = DelegateSignature(called.ContainingType, Expression(receiver));
                BindArguments(operation, signature.Inputs);
                return signature.Output;
            }
            if (SymbolEqualityComparer.Default.Equals(called, currentMethod))
            {
                BindArguments(operation, currentFunction!.Arguments.Take(currentFunction.Arguments.Length - 1).ToArray());
                return currentFunction.Arguments[currentFunction.Arguments.Length - 1];
            }

            ExpressionSyntax? instanceReceiver = null;
            if (!called.IsStatic && called.MethodKind != MethodKind.LocalFunction)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax member)
                { instanceReceiver = member.Expression; }
                else if (operation.Instance is not IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance })
                { throw new InferenceException("Implicit instance calls are unsupported."); }
            }
            var variables = ReceiverMapping(called, instanceReceiver);
            var definition = called.OriginalDefinition;
            var explicitArguments = invocation.Expression is GenericNameSyntax generic ? generic.TypeArgumentList.Arguments
                : invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax name } ? name.TypeArgumentList.Arguments : default;
            for (var i = 0; i < definition.TypeParameters.Length; i++)
            {
                variables.Add(definition.TypeParameters[i], explicitArguments.Count == 0 ? HmType.Variable()
                    : CSharpTypes.Rigid(model.GetTypeInfo(explicitArguments[i], cancellationToken).Type!));
            }
            BindArguments(operation, definition.Parameters.Select(p =>
            {
                if (p is null)
                {
                    throw new global::System.ArgumentNullException(nameof(p));
                }

                return CSharpTypes.Rigid(p.Type, variables);
            }).ToArray());
            foreach (var parameter in definition.TypeParameters)
            { ImportConstraints(parameter, variables[parameter], variables, called.TypeArguments[parameter.Ordinal]); }
            return CSharpTypes.Rigid(definition.ReturnType, variables);
        }

        private void BindArguments(IInvocationOperation operation, HmType[] parameters)
        {
            if (operation is null)
            {
                throw new global::System.ArgumentNullException(nameof(operation));
            }

            if (parameters is null)
            {
                throw new global::System.ArgumentNullException(nameof(parameters));
            }

            BindArguments(operation.Arguments, parameters);
        }

        private void BindArguments(IEnumerable<IArgumentOperation> arguments, HmType[] parameters)
        {
            if (arguments is null)
            {
                throw new global::System.ArgumentNullException(nameof(arguments));
            }

            if (parameters is null)
            {
                throw new global::System.ArgumentNullException(nameof(parameters));
            }

            foreach (var argument in arguments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parameter = argument.Parameter ?? throw new InferenceException("Unbound argument.");
                var expected = parameters[parameter.Ordinal];
                // Omitted defaults are independent of the caller's expressions.
                // In particular, default(T) must not pin a generic T to the type
                // from the original constructed call.
                if (argument.ArgumentKind == ArgumentKind.DefaultValue)
                {
                    continue;
                }

                if (BindReferenceArgument(argument, expected))
                { continue; }

                if (argument.Value is ICollectionExpressionOperation collection)
                { CollectionExpression(collection, expected); continue; }

                if (argument.ArgumentKind == ArgumentKind.ParamArray)
                {
                    if (argument.Value is not IArrayCreationOperation { Initializer: not null } array)
                    {
                        throw new InferenceException("Only array-based expanded params are supported.");
                    }

                    var element = HmType.Variable();
                    HmType.Unify(expected, new("[]", element));
                    foreach (var value in array.Initializer.ElementValues)
                    {
                        BindNullableArgument(value, element);
                    }
                }
                else
                {
                    BindNullableArgument(argument.Value, expected);
                }
            }
        }

        private bool BindReferenceArgument(IArgumentOperation argument, HmType expected)
        {
            if (argument is null)
            { throw new ArgumentNullException(nameof(argument)); }
            if (expected is null)
            { throw new ArgumentNullException(nameof(expected)); }
            var parameter = argument.Parameter ?? throw new InferenceException("Unresolved reference argument.");
            var explicitReference = argument.Syntax is ArgumentSyntax syntax && syntax.RefKindKeyword != default;
            if (parameter.RefKind is not (RefKind.Ref or RefKind.Out) && !(parameter.RefKind != RefKind.None && explicitReference))
            { return false; }
            if (argument.Value is IDiscardOperation)
            { return true; }
            if (argument.Value.Syntax is DeclarationExpressionSyntax declaration)
            {
                var original = argument.Value.Type ?? parameter.Type;
                if (!declaration.Type.IsVar)
                { HmType.Unify(expected, CSharpTypes.Rigid(model.GetTypeInfo(declaration.Type, cancellationToken).Type ?? original)); }
                BindDesignation(declaration.Designation, expected, original);
                return true;
            }
            // Storage passed by reference is invariant. Omitted readonly arguments
            // continue through ordinary conversion binding, including temporaries.
            HmType.Unify(expected, BoundExpression(argument.Value));
            return true;
        }
        private HmType BoundExpression(IOperation value)
        {
            return value.Syntax is ExpressionSyntax expression ? Expression(expression) : throw new InferenceException("Unsupported synthesized argument.");
        }
    }

    private static SeparatedSyntaxList<ParameterSyntax> Parameters(this MethodDeclarationSyntax method)
    {
        if (method is null)
        {
            throw new global::System.ArgumentNullException(nameof(method));
        }

        return method.ParameterList.Parameters;
    }
    private sealed class CandidateTreeOptions : SyntaxTreeOptionsProvider
    {
        private readonly SyntaxTreeOptionsProvider sourceOptions;
        private readonly SyntaxTree source;
        private readonly SyntaxTree candidate;
        internal CandidateTreeOptions(SyntaxTreeOptionsProvider sourceOptions, SyntaxTree source, SyntaxTree candidate)
        {
            this.sourceOptions = sourceOptions ?? throw new ArgumentNullException(nameof(sourceOptions));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        }
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken)
        {
            if (tree is null)
            { throw new ArgumentNullException(nameof(tree)); }
            return sourceOptions.IsGenerated(ReferenceEquals(tree, candidate) ? source : tree, cancellationToken);
        }
        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            if (tree is null)
            { throw new ArgumentNullException(nameof(tree)); }
            if (diagnosticId is null)
            { throw new ArgumentNullException(nameof(diagnosticId)); }
            return sourceOptions.TryGetDiagnosticValue(ReferenceEquals(tree, candidate) ? source : tree, diagnosticId, cancellationToken, out severity);
        }
        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            if (diagnosticId is null)
            { throw new ArgumentNullException(nameof(diagnosticId)); }
            return sourceOptions.TryGetGlobalDiagnosticValue(diagnosticId, cancellationToken, out severity);
        }
    }
}
