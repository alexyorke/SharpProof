using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

public sealed record MethodResult(string Method, string Status, string Message, string? Signature = null);

public static class Inference
{
    public static MethodResult Analyze(MethodDeclarationSyntax method, SemanticModel model,
        CancellationToken cancellationToken = default)
    {
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

    private sealed class Session(SemanticModel model, CancellationToken cancellationToken)
    {
        private readonly Dictionary<ISymbol, HmType> environment = new(SymbolEqualityComparer.Default);
        private IMethodSymbol? currentMethod;
        private HmType? currentFunction;

        public MethodResult Analyze(MethodDeclarationSyntax method)
        {
            var symbol = model.GetDeclaredSymbol(method, cancellationToken)
                ?? throw new InferenceException("Method has no symbol.");
            if (!symbol.IsStatic || symbol.IsGenericMethod || symbol.ReturnsVoid ||
                symbol.IsExtern || symbol.IsAsync || method.AttributeLists.Count != 0 ||
                symbol.ContainingType.TypeParameters.Length != 0 ||
                method.Modifiers.Any(SyntaxKind.PartialKeyword) ||
                method.DescendantNodes().OfType<NullableTypeSyntax>().Any())
                throw new InferenceException("Only non-generic, static, synchronous value-returning methods without attributes or nullable annotations are supported.");
            if (method.Parameters().Any(p => p.Modifiers.Count != 0 || p.Default is not null || p.AttributeLists.Count != 0))
                throw new InferenceException("Parameter modifiers, attributes and defaults are unsupported.");
            var parameters = symbol.Parameters.Select(p => CSharpTypes.Erase(p.Type)).ToArray();
            currentMethod = symbol;
            var recursiveReturn = HmType.Variable();
            currentFunction = HmType.Function(parameters, recursiveReturn);
            for (var i = 0; i < parameters.Length; i++) environment.Add(symbol.Parameters[i], parameters[i]);
            HmType result;
            if (method.ExpressionBody is not null) result = Expression(method.ExpressionBody.Expression);
            else if (method.Body is not null) result = Block(method.Body);
            else throw new InferenceException("No method body.");
            HmType.Unify(recursiveReturn, result);

            var inferable = parameters.SelectMany(p => p.FreeVariables()).ToHashSet();
            PreserveResultOnly(result, CSharpTypes.Rigid(symbol.ReturnType), inferable);

            var variables = new Dictionary<HmType, string>();
            var parameterNames = parameters.Select((t, i) => CSharpTypes.Render(t, symbol.Parameters[i].Type, variables)).ToArray();
            var returnName = CSharpTypes.Render(result, symbol.ReturnType, variables);
            if (variables.Count == 0)
                return new(symbol.Name, "Concrete", "Body requires concrete types; no generic suggestion.");
            // A type variable only present in the result cannot be inferred by C# callers.
            var inputVariables = new Dictionary<HmType, string>();
            foreach (var parameter in parameters) parameter.Format(inputVariables);
            if (variables.Keys.Any(v => !inputVariables.ContainsKey(v)))
                throw new InferenceException("A result-only type variable cannot be inferred at a C# call site.");

            var candidate = method.WithReturnType(SyntaxFactory.ParseTypeName(returnName))
                .WithTypeParameterList(SyntaxFactory.TypeParameterList(SyntaxFactory.SeparatedList(
                    variables.Values.Select(SyntaxFactory.TypeParameter))))
                .WithParameterList(method.ParameterList.WithParameters(SyntaxFactory.SeparatedList(
                    method.Parameters().Select((p, i) => p.WithType(SyntaxFactory.ParseTypeName(parameterNames[i]))))));
            // Recheck the original compilation with just this signature changed. This catches
            // explicit local types, conversions, name collisions and broken call sites.
            var tree = method.SyntaxTree;
            var changed = tree.WithRootAndOptions(tree.GetRoot(cancellationToken).ReplaceNode(method, candidate), tree.Options);
            var changedCompilation = model.Compilation.ReplaceSyntaxTree(tree, changed);
            if (changedCompilation.GetDiagnostics(cancellationToken)
                .Any(d => d.Severity == DiagnosticSeverity.Error))
                return new(symbol.Name, "Rejected", "Inferred signature fails C# compilation or breaks a call site.");
            var originalCalls = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            var changedMethod = changed.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(m => m.SpanStart == method.SpanStart);
            var changedCalls = changedMethod.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
            var changedModel = changedCompilation.GetSemanticModel(changed);
            for (var i = 0; i < originalCalls.Length; i++)
            {
                var before = model.GetSymbolInfo(originalCalls[i], cancellationToken).Symbol as IMethodSymbol;
                var after = changedModel.GetSymbolInfo(changedCalls[i], cancellationToken).Symbol as IMethodSymbol;
                if (SymbolEqualityComparer.Default.Equals(before, symbol) && after?.DeclaringSyntaxReferences.Any(r =>
                    r.SyntaxTree == changed && r.Span.Start == changedMethod.SpanStart) == true) continue;
                if (before?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) !=
                    after?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                    return new(symbol.Name, "Rejected", "Generalization changes a selected method overload.");
            }
            var signature = $"{returnName} {symbol.Name}<{string.Join(", ", variables.Values)}>({string.Join(", ", method.Parameters().Select((p, i) => parameterNames[i] + " " + p.Identifier.Text))})";
            return new(symbol.Name, "Suggestion", "Review as an API change; the candidate compiles in the current compilation.", signature);
        }

        private HmType Block(BlockSyntax block)
        {
            var locals = environment.Keys.ToHashSet(SymbolEqualityComparer.Default);
            try { return Statements(block.Statements.ToArray()); }
            finally
            {
                foreach (var local in environment.Keys.Where(k => !locals.Contains(k)).ToArray()) environment.Remove(local);
            }
        }

        private HmType Statements(StatementSyntax[] statements)
        {
            for (var index = 0; index < statements.Length; index++)
            {
                var statement = statements[index];
                cancellationToken.ThrowIfCancellationRequested();
                switch (statement)
                {
                    case LocalDeclarationStatementSyntax local when local.Modifiers.Count == 0:
                        foreach (var variable in local.Declaration.Variables)
                        {
                            if (variable.Initializer is null) throw new InferenceException("Uninitialized local.");
                            environment.Add(model.GetDeclaredSymbol(variable, cancellationToken)!, Expression(variable.Initializer.Value));
                        }
                        break;
                    case LocalFunctionStatementSyntax:
                        // C# already supplies an explicit (possibly generic) scheme.
                        // Captures and the retained local body are compiler checked
                        // after any change to the containing method signature.
                        break;
                    case ReturnStatementSyntax { Expression: not null } returned:
                        if (index != statements.Length - 1) throw new InferenceException("Statements after return are unsupported.");
                        return Expression(returned.Expression);
                    case IfStatementSyntax conditional:
                        HmType.Unify(Expression(conditional.Condition), new("bool"));
                        var yes = Branch(conditional.Statement);
                        var no = conditional.Else is not null ? Branch(conditional.Else.Statement)
                            : Statements(statements[(index + 1)..]);
                        if (conditional.Else is not null && index != statements.Length - 1)
                            throw new InferenceException("Statements after exhaustive branching are unsupported.");
                        HmType.Unify(yes, no);
                        return yes;
                    case BlockSyntax nested when index == statements.Length - 1: return Block(nested);
                    default: throw new InferenceException($"Unsupported statement: {statement.Kind()}.");
                }
            }
            throw new InferenceException("Missing return expression.");
        }

        private HmType Branch(StatementSyntax statement) => statement is BlockSyntax block ? Block(block) : Statements([statement]);

        private HmType Expression(ExpressionSyntax expression)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parentheses: return Expression(parentheses.Expression);
                case IdentifierNameSyntax identifier:
                    var symbol = model.GetSymbolInfo(identifier, cancellationToken).Symbol;
                    if (symbol is not null && environment.TryGetValue(symbol, out var type)) return type;
                    throw new InferenceException("Only local and parameter references are supported.");
                case LiteralExpressionSyntax literal when !literal.IsKind(SyntaxKind.NullLiteralExpression) && !literal.IsKind(SyntaxKind.DefaultLiteralExpression):
                    return CSharpTypes.Rigid(model.GetTypeInfo(literal, cancellationToken).Type!);
                case ConditionalExpressionSyntax conditional:
                    HmType.Unify(Expression(conditional.Condition), new("bool"));
                    var yes = Expression(conditional.WhenTrue);
                    HmType.Unify(yes, Expression(conditional.WhenFalse));
                    return yes;
                case TupleExpressionSyntax tuple:
                    return new("Tuple", tuple.Arguments.Select(a => Expression(a.Expression)).ToArray());
                case ImplicitArrayCreationExpressionSyntax implicitArray:
                    var inferredElement = HmType.Variable();
                    foreach (var item in implicitArray.Initializer.Expressions) HmType.Unify(inferredElement, Expression(item));
                    return new("[" + new string(',', implicitArray.Commas.Count) + "]", inferredElement);
                case ArrayCreationExpressionSyntax explicitArray:
                    var createdArray = (IArrayTypeSymbol)model.GetTypeInfo(explicitArray, cancellationToken).Type!;
                    foreach (var rank in explicitArray.Type.RankSpecifiers)
                    foreach (var size in rank.Sizes.Where(size => size is not OmittedArraySizeExpressionSyntax))
                        HmType.Unify(Expression(size), new("int"));
                    if (explicitArray.Initializer is not null) AnchorInitializer(explicitArray.Initializer, createdArray.ElementType);
                    return CSharpTypes.Rigid(createdArray);
                case LambdaExpressionSyntax lambda:
                    var lambdaParameters = lambda switch
                    {
                        SimpleLambdaExpressionSyntax simple => new[] { simple.Parameter },
                        ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.ToArray(),
                        _ => throw new InferenceException("Unsupported lambda.")
                    };
                    if (lambda.AsyncKeyword != default || lambdaParameters.Any(p => p.Modifiers.Count != 0 || p.AttributeLists.Count != 0) || lambda.AttributeLists.Count != 0)
                        throw new InferenceException("Only synchronous lambdas without modifiers or attributes are supported.");
                    var inputs = lambdaParameters.Select(p => p.Type is null ? HmType.Variable()
                        : CSharpTypes.Rigid(model.GetTypeInfo(p.Type, cancellationToken).Type!)).ToArray();
                    for (var i = 0; i < inputs.Length; i++) environment.Add(model.GetDeclaredSymbol(lambdaParameters[i], cancellationToken)!, inputs[i]);
                    var output = lambda.Body is ExpressionSyntax body ? Expression(body) : Block((BlockSyntax)lambda.Body);
                    foreach (var parameter in lambdaParameters) environment.Remove(model.GetDeclaredSymbol(parameter, cancellationToken)!);
                    return HmType.Function(inputs, output);
                case InvocationExpressionSyntax invocation:
                    if (invocation.ArgumentList.Arguments.Any(a => a.NameColon is not null || a.RefKindKeyword != default))
                        throw new InferenceException("Named and ref arguments are unsupported.");
                    var called = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol
                        ?? throw new InferenceException("Unresolved or dynamic invocation.");
                    if (called.MethodKind != MethodKind.DelegateInvoke) return MethodCall(invocation, called);
                    var function = Expression(invocation.Expression);
                    var arguments = invocation.ArgumentList.Arguments.Select(a => Expression(a.Expression)).ToArray();
                    var applied = HmType.Variable();
                    HmType.Unify(function, HmType.Function(arguments, applied));
                    return applied;
                case BinaryExpressionSyntax binary: return Binary(binary);
                case PrefixUnaryExpressionSyntax unary when unary.Kind() is SyntaxKind.UnaryMinusExpression or SyntaxKind.UnaryPlusExpression or SyntaxKind.LogicalNotExpression or SyntaxKind.BitwiseNotExpression:
                    if (model.GetSymbolInfo(unary, cancellationToken).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator })
                        throw new InferenceException("User-defined operators are unsupported.");
                    Anchor(unary.Operand);
                    return CSharpTypes.Rigid(model.GetTypeInfo(unary, cancellationToken).Type!);
                case ElementAccessExpressionSyntax element:
                    var array = model.GetTypeInfo(element.Expression, cancellationToken).Type as IArrayTypeSymbol
                        ?? throw new InferenceException("Only array indexing is supported.");
                    var elementType = HmType.Variable();
                    HmType.Unify(Expression(element.Expression), new("[" + new string(',', array.Rank - 1) + "]", elementType));
                    if (element.ArgumentList.Arguments.Count != array.Rank) throw new InferenceException("Array index count mismatch.");
                    foreach (var index in element.ArgumentList.Arguments) HmType.Unify(Expression(index.Expression), new("int"));
                    return elementType;
                case MemberAccessExpressionSyntax member: return Member(member);
                case CastExpressionSyntax cast:
                    Anchor(cast.Expression);
                    return CSharpTypes.Rigid(model.GetTypeInfo(cast.Type, cancellationToken).Type!);
                case DefaultExpressionSyntax @default: return CSharpTypes.Rigid(model.GetTypeInfo(@default.Type, cancellationToken).Type!);
                case CheckedExpressionSyntax checkedExpression: return Expression(checkedExpression.Expression);
                case SwitchExpressionSyntax switched:
                    Anchor(switched.GoverningExpression);
                    var switchedResult = HmType.Variable();
                    foreach (var arm in switched.Arms)
                    {
                        if (arm.Pattern is not ConstantPatternSyntax and not DiscardPatternSyntax)
                            throw new InferenceException("Only constant/discard switch patterns are supported.");
                        if (arm.WhenClause is not null) HmType.Unify(Expression(arm.WhenClause.Condition), new("bool"));
                        HmType.Unify(switchedResult, Expression(arm.Expression));
                    }
                    return switchedResult;
                default: throw new InferenceException($"Unsupported expression: {expression.Kind()}.");
            }
        }

        private void Anchor(ExpressionSyntax expression) => HmType.Unify(Expression(expression),
            CSharpTypes.Rigid(model.GetTypeInfo(expression, cancellationToken).Type
                ?? throw new InferenceException("Untyped expression.")));

        private void AnchorInitializer(InitializerExpressionSyntax initializer, ITypeSymbol element)
        {
            foreach (var item in initializer.Expressions)
                if (item is InitializerExpressionSyntax nested) AnchorInitializer(nested, element);
                else HmType.Unify(Expression(item), CSharpTypes.Rigid(element));
        }

        private static void PreserveResultOnly(HmType inferred, HmType declared, HashSet<HmType> inferable)
        {
            inferred = inferred.Root;
            declared = declared.Root;
            if (inferred.IsVariable && !inferable.Contains(inferred)) { HmType.Unify(inferred, declared); return; }
            if (inferred.Name == declared.Name && inferred.Arguments.Length == declared.Arguments.Length)
                for (var i = 0; i < inferred.Arguments.Length; i++) PreserveResultOnly(inferred.Arguments[i], declared.Arguments[i], inferable);
        }

        private HmType Binary(BinaryExpressionSyntax binary)
        {
            if (binary.Kind() is SyntaxKind.CoalesceExpression or SyntaxKind.AsExpression or SyntaxKind.IsExpression ||
                model.GetSymbolInfo(binary, cancellationToken).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator })
                throw new InferenceException("User-defined, null-coalescing and type-test operators are unsupported.");
            // Retain original operand types so numeric promotion, boxing and string
            // concatenation keep their C# meaning. Equality is not unconstrained HM equality.
            Anchor(binary.Left);
            Anchor(binary.Right);
            return CSharpTypes.Rigid(model.GetTypeInfo(binary, cancellationToken).Type!);
        }

        private HmType Member(MemberAccessExpressionSyntax member)
        {
            var receiverType = model.GetTypeInfo(member.Expression, cancellationToken).Type;
            if (receiverType is INamedTypeSymbol { IsTupleType: true } tuple &&
                model.GetSymbolInfo(member, cancellationToken).Symbol is IFieldSymbol tupleField)
            {
                var index = tuple.TupleElements.IndexOf(tupleField);
                if (index < 0) index = tuple.TupleElements.IndexOf(tupleField.CorrespondingTupleField!);
                if (index < 0)
                    index = tuple.TupleElements.Select((field, i) => (field, i)).First(p => p.field.CorrespondingTupleField?.Name == tupleField.Name).i;
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
                throw new InferenceException("Unsupported member access.");
            if (selected is IPropertySymbol { IsStatic: false } or IFieldSymbol { IsStatic: false }) Anchor(member.Expression);
            return CSharpTypes.Rigid(model.GetTypeInfo(member, cancellationToken).Type!);
        }

        private HmType MethodCall(InvocationExpressionSyntax invocation, IMethodSymbol called)
        {
            if (called.ReducedFrom is not null || called.Parameters.Any(p => p.RefKind != RefKind.None || p.IsParams || p.IsOptional) ||
                called.Parameters.Length != invocation.ArgumentList.Arguments.Count || called.ReturnsVoid)
                throw new InferenceException("Extension, ref, params, optional and void calls are unsupported.");
            if (SymbolEqualityComparer.Default.Equals(called, currentMethod))
            {
                var recursiveOutput = HmType.Variable();
                HmType.Unify(currentFunction!, HmType.Function(invocation.ArgumentList.Arguments.Select(a => Expression(a.Expression)), recursiveOutput));
                return recursiveOutput;
            }
            if (!called.IsStatic && called.MethodKind != MethodKind.LocalFunction)
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax member)
                    throw new InferenceException("Implicit instance calls are unsupported.");
                Anchor(member.Expression);
            }
            // Imported generic schemes are instantiated per call. Constraints are
            // checked by Roslyn after rewriting; no C# constraint language is invented.
            var definition = called.IsGenericMethod ? called.ConstructedFrom : called;
            var variables = new Dictionary<ITypeParameterSymbol, HmType>(SymbolEqualityComparer.Default);
            foreach (var parameter in definition.TypeParameters) variables.Add(parameter, HmType.Variable());
            for (var i = 0; i < definition.Parameters.Length; i++)
                HmType.Unify(Expression(invocation.ArgumentList.Arguments[i].Expression), CSharpTypes.Rigid(definition.Parameters[i].Type, variables));
            return CSharpTypes.Rigid(definition.ReturnType, variables);
        }
    }

    private static SeparatedSyntaxList<ParameterSyntax> Parameters(this MethodDeclarationSyntax method) => method.ParameterList.Parameters;
}
