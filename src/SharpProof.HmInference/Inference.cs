using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

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
            var originalCreations = method.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().ToArray();
            var changedCreations = changedMethod.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().ToArray();
            for (var i = 0; i < originalCreations.Length; i++)
                if (model.GetSymbolInfo(originalCreations[i], cancellationToken).Symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) !=
                    changedModel.GetSymbolInfo(changedCreations[i], cancellationToken).Symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                    return new(symbol.Name, "Rejected", "Generalization changes a selected constructor.");
            var signature = $"{returnName} {method.Identifier.Text}<{string.Join(", ", variables.Values)}>({string.Join(", ", method.Parameters().Select((p, i) => parameterNames[i] + " " + p.Identifier.Text))})";
            return new(symbol.Name, "Suggestion", "Review as an API change; the candidate compiles in the current compilation.", signature);
        }

        private HmType Block(BlockSyntax block)
        {
            var flow = Scoped(() => Statements(block.Statements.ToArray()));
            if (flow.FallsThrough || flow.Breaks || flow.Continues)
                throw new InferenceException("Body has no structurally complete value return.");
            return flow.ReturnType ?? HmType.Variable();
        }

        private readonly record struct Flow(HmType? ReturnType, bool FallsThrough, bool Breaks = false, bool Continues = false);

        private Flow Scoped(Func<Flow> analyze)
        {
            var locals = environment.Keys.ToHashSet(SymbolEqualityComparer.Default);
            try { return analyze(); }
            finally
            {
                foreach (var local in environment.Keys.Where(k => !locals.Contains(k)).ToArray()) environment.Remove(local);
            }
        }

        private Flow Statements(StatementSyntax[] statements)
        {
            HmType? returns = null;
            var fallsThrough = true;
            var breaks = false;
            var continues = false;
            for (var index = 0; index < statements.Length; index++)
            {
                if (!fallsThrough) throw new InferenceException("Unreachable statements are unsupported.");
                var statement = statements[index];
                cancellationToken.ThrowIfCancellationRequested();
                var flow = new Flow(null, true);
                switch (statement)
                {
                    case LocalDeclarationStatementSyntax local when local.Modifiers.Count == 0 && local.UsingKeyword == default:
                        Declare(local.Declaration);
                        break;
                    case ExpressionStatementSyntax expression:
                        Expression(expression.Expression);
                        break;
                    case LocalFunctionStatementSyntax:
                        // C# already supplies an explicit (possibly generic) scheme.
                        // Captures and the retained local body are compiler checked
                        // after any change to the containing method signature.
                        break;
                    case ReturnStatementSyntax { Expression: not null } returned:
                        flow = new(Expression(returned.Expression), false);
                        break;
                    case IfStatementSyntax conditional:
                        HmType.Unify(Expression(conditional.Condition), new("bool"));
                        var yes = Branch(conditional.Statement);
                        var no = conditional.Else is not null ? Branch(conditional.Else.Statement)
                            : new Flow(null, true);
                        flow = new(MergeReturns(yes.ReturnType, no.ReturnType), yes.FallsThrough || no.FallsThrough,
                            yes.Breaks || no.Breaks, yes.Continues || no.Continues);
                        break;
                    case BlockSyntax nested: flow = Scoped(() => Statements(nested.Statements.ToArray())); break;
                    case WhileStatementSyntax loop:
                        HmType.Unify(Expression(loop.Condition), new("bool"));
                        flow = LoopFlow(Branch(loop.Statement));
                        break;
                    case DoStatementSyntax loop:
                        flow = LoopFlow(Branch(loop.Statement));
                        HmType.Unify(Expression(loop.Condition), new("bool"));
                        break;
                    case ForStatementSyntax loop:
                        flow = Scoped(() =>
                        {
                            if (loop.Declaration is not null) Declare(loop.Declaration);
                            foreach (var initializer in loop.Initializers) Expression(initializer);
                            if (loop.Condition is not null) HmType.Unify(Expression(loop.Condition), new("bool"));
                            var body = Branch(loop.Statement);
                            foreach (var increment in loop.Incrementors) Expression(increment);
                            return LoopFlow(body);
                        });
                        break;
                    case ForEachStatementSyntax loop when loop.AwaitKeyword == default:
                        flow = Scoped(() =>
                        {
                            var source = model.GetTypeInfo(loop.Expression, cancellationToken).Type as IArrayTypeSymbol
                                ?? throw new InferenceException("Only array foreach is supported.");
                            var element = HmType.Variable();
                            HmType.Unify(Expression(loop.Expression), new("[" + new string(',', source.Rank - 1) + "]", element));
                            var iteration = element;
                            if (!loop.Type.IsVar)
                            {
                                // Preserve explicit foreach conversions, e.g. int[] to long.
                                HmType.Unify(element, CSharpTypes.Rigid(source.ElementType));
                                iteration = CSharpTypes.Rigid(model.GetTypeInfo(loop.Type, cancellationToken).Type!);
                            }
                            environment.Add(model.GetDeclaredSymbol(loop, cancellationToken)!, iteration);
                            return LoopFlow(Branch(loop.Statement));
                        });
                        break;
                    case ForEachVariableStatementSyntax loop when loop.AwaitKeyword == default:
                        flow = Scoped(() =>
                        {
                            var source = model.GetTypeInfo(loop.Expression, cancellationToken).Type as IArrayTypeSymbol
                                ?? throw new InferenceException("Only array foreach deconstruction is supported.");
                            if (source.ElementType is not INamedTypeSymbol { IsTupleType: true })
                                throw new InferenceException("Only tuple array elements can be deconstructed.");
                            var element = HmType.Variable();
                            HmType.Unify(Expression(loop.Expression), new("[" + new string(',', source.Rank - 1) + "]", element));
                            BindDeconstruction(loop.Variable, element, source.ElementType);
                            return LoopFlow(Branch(loop.Statement));
                        });
                        break;
                    case BreakStatementSyntax: flow = new(null, false, Breaks: true); break;
                    case ContinueStatementSyntax: flow = new(null, false, Continues: true); break;
                    case SwitchStatementSyntax switched: flow = Switch(switched); break;
                    case CheckedStatementSyntax checkedStatement:
                        flow = Scoped(() => Statements(checkedStatement.Block.Statements.ToArray()));
                        break;
                    case ThrowStatementSyntax { Expression: not null } thrown:
                        Anchor(thrown.Expression);
                        flow = new(null, false);
                        break;
                    case EmptyStatementSyntax: break;
                    default: throw new InferenceException($"Unsupported statement: {statement.Kind()}.");
                }
                returns = MergeReturns(returns, flow.ReturnType);
                fallsThrough = flow.FallsThrough;
                breaks |= flow.Breaks;
                continues |= flow.Continues;
            }
            return new(returns, fallsThrough, breaks, continues);
        }

        private static Flow LoopFlow(Flow body) => new(body.ReturnType, true);

        private Flow Switch(SwitchStatementSyntax statement)
        {
            Anchor(statement.Expression);
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
                            case DefaultSwitchLabelSyntax: hasDefault = true; break;
                            case CaseSwitchLabelSyntax: break;
                            case CasePatternSwitchLabelSyntax { Pattern: ConstantPatternSyntax or DiscardPatternSyntax } pattern:
                                if (pattern.WhenClause is not null) HmType.Unify(Expression(pattern.WhenClause.Condition), new("bool"));
                                break;
                            default: throw new InferenceException("Only constant/discard switch labels are supported.");
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
            if (first is null) return second;
            if (second is not null) HmType.Unify(first, second);
            return first;
        }

        private void Declare(VariableDeclarationSyntax declaration)
        {
            foreach (var variable in declaration.Variables)
            {
                if (variable.Initializer is null) throw new InferenceException("Uninitialized local.");
                environment.Add(model.GetDeclaredSymbol(variable, cancellationToken)!, Expression(variable.Initializer.Value));
            }
        }

        private Flow Branch(StatementSyntax statement) => Scoped(() => statement is BlockSyntax block
            ? Statements(block.Statements.ToArray()) : Statements([statement]));

        private HmType Expression(ExpressionSyntax expression)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parentheses: return Expression(parentheses.Expression);
                case ThrowExpressionSyntax thrown:
                    Anchor(thrown.Expression);
                    return HmType.Variable();
                case AssignmentExpressionSyntax assignment:
                    if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        if (assignment.Left is TupleExpressionSyntax or DeclarationExpressionSyntax)
                        {
                            var original = model.GetTypeInfo(assignment.Right, cancellationToken).Type;
                            if (original is not INamedTypeSymbol { IsTupleType: true })
                                throw new InferenceException("Only tuple-based deconstruction is supported.");
                            var assignedTuple = Expression(assignment.Right);
                            BindDeconstruction(assignment.Left, assignedTuple, original);
                            return assignedTuple;
                        }
                        return AssignValue(assignment.Left, Expression(assignment.Right), model.GetTypeInfo(assignment.Right, cancellationToken).Type);
                    }
                    if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) ||
                        model.GetOperation(assignment, cancellationToken) is not ICompoundAssignmentOperation { OperatorMethod: null })
                        throw new InferenceException("User-defined and coalescing assignments are unsupported.");
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
                    if (model.GetOperation(invocation, cancellationToken) is INameOfOperation) return new("string");
                    return MethodCall(invocation);
                case BaseObjectCreationExpressionSyntax creation:
                    var constructed = model.GetOperation(creation, cancellationToken) as IObjectCreationOperation
                        ?? throw new InferenceException("Unsupported object construction.");
                    if (creation.Initializer is not null || constructed.Constructor is null || constructed.Constructor.Parameters.Any(p => p.RefKind != RefKind.None))
                        throw new InferenceException("Object initializers and ref constructors are unsupported.");
                    BindArguments(constructed.Arguments, constructed.Constructor.Parameters.Select(p => CSharpTypes.Rigid(p.Type)).ToArray());
                    return CSharpTypes.Rigid(constructed.Type!);
                case InterpolatedStringExpressionSyntax interpolated:
                    // Keep original hole types to preserve overloads and formatting;
                    // custom interpolation-handler conversions remain unsupported.
                    var converted = model.GetTypeInfo(interpolated, cancellationToken).ConvertedType;
                    if (converted?.SpecialType != SpecialType.System_String)
                        throw new InferenceException("Only string-targeted interpolation is supported.");
                    foreach (var hole in interpolated.Contents.OfType<InterpolationSyntax>())
                    {
                        Anchor(hole.Expression);
                        if (hole.AlignmentClause is not null) HmType.Unify(Expression(hole.AlignmentClause.Value), new("int"));
                    }
                    return new("string");
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

        private HmType AssignmentTarget(ExpressionSyntax target)
        {
            if (target is not IdentifierNameSyntax and not ElementAccessExpressionSyntax)
                throw new InferenceException("Only local, parameter and array-element assignment targets are supported.");
            return Expression(target);
        }

        private HmType AssignValue(ExpressionSyntax targetSyntax, HmType value, ITypeSymbol? sourceType)
        {
            var target = AssignmentTarget(targetSyntax);
            var targetType = model.GetTypeInfo(targetSyntax, cancellationToken).Type!;
            if (sourceType is not null && !SymbolEqualityComparer.Default.Equals(targetType, sourceType))
            {
                // Assignment conversions are not HM equality. Keep both original
                // endpoints so widening/boxing still happens at the assignment.
                HmType.Unify(target, CSharpTypes.Rigid(targetType));
                HmType.Unify(value, CSharpTypes.Rigid(sourceType));
            }
            else HmType.Unify(target, value);
            return target;
        }

        private HmType Increment(ExpressionSyntax expression, ExpressionSyntax operand)
        {
            if (model.GetOperation(expression, cancellationToken) is not IIncrementOrDecrementOperation { OperatorMethod: null })
                throw new InferenceException("User-defined increments are unsupported.");
            AssignmentTarget(operand);
            Anchor(operand);
            return CSharpTypes.Rigid(model.GetTypeInfo(expression, cancellationToken).Type!);
        }

        private void BindDeconstruction(ExpressionSyntax target, HmType value, ITypeSymbol original)
        {
            switch (target)
            {
                case DeclarationExpressionSyntax declaration:
                    if (!declaration.Type.IsVar) HmType.Unify(value, CSharpTypes.Rigid(model.GetTypeInfo(declaration.Type, cancellationToken).Type!));
                    BindDesignation(declaration.Designation, value, original);
                    break;
                case TupleExpressionSyntax tuple:
                    var source = original as INamedTypeSymbol;
                    if (source is not { IsTupleType: true } || source.TupleElements.Length != tuple.Arguments.Count)
                        throw new InferenceException("Custom nested Deconstruct calls are unsupported.");
                    var elements = tuple.Arguments.Select(_ => HmType.Variable()).ToArray();
                    HmType.Unify(value, new("Tuple", elements));
                    for (var i = 0; i < elements.Length; i++) BindDeconstruction(tuple.Arguments[i].Expression, elements[i], source.TupleElements[i].Type);
                    break;
                case IdentifierNameSyntax identifier:
                    if (model.GetOperation(identifier, cancellationToken) is not IDiscardOperation)
                        AssignValue(identifier, value, original);
                    break;
                default: throw new InferenceException("Mixed declarations and assignments in deconstruction are unsupported.");
            }
        }

        private void BindDesignation(VariableDesignationSyntax designation, HmType value, ITypeSymbol original)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (designation)
            {
                case SingleVariableDesignationSyntax variable:
                    environment.Add(model.GetDeclaredSymbol(variable, cancellationToken)!, value);
                    break;
                case DiscardDesignationSyntax: break;
                case ParenthesizedVariableDesignationSyntax tuple:
                    var source = original as INamedTypeSymbol;
                    if (source is not { IsTupleType: true } || source.TupleElements.Length != tuple.Variables.Count)
                        throw new InferenceException("Custom nested Deconstruct calls are unsupported.");
                    var elements = tuple.Variables.Select(_ => HmType.Variable()).ToArray();
                    HmType.Unify(value, new("Tuple", elements));
                    for (var i = 0; i < elements.Length; i++) BindDesignation(tuple.Variables[i], elements[i], source.TupleElements[i].Type);
                    break;
                default: throw new InferenceException("Unsupported deconstruction designation.");
            }
        }

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

        private HmType MethodCall(InvocationExpressionSyntax invocation)
        {
            var operation = model.GetOperation(invocation, cancellationToken) as IInvocationOperation
                ?? throw new InferenceException("Unresolved or dynamic invocation.");
            var called = operation.TargetMethod;
            if (called.Parameters.Any(p => p.RefKind != RefKind.None))
                throw new InferenceException("Ref calls are unsupported.");
            if (called.MethodKind == MethodKind.DelegateInvoke)
            {
                if (called.ReturnsVoid) throw new InferenceException("Void delegate calls are unsupported.");
                var inputs = called.Parameters.Select(_ => HmType.Variable()).ToArray();
                var output = HmType.Variable();
                HmType.Unify(Expression(invocation.Expression), HmType.Function(inputs, output));
                BindArguments(operation, inputs);
                return output;
            }
            if (SymbolEqualityComparer.Default.Equals(called, currentMethod))
            {
                BindArguments(operation, currentFunction!.Arguments[..^1]);
                return currentFunction.Arguments[^1];
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
            BindArguments(operation, definition.Parameters.Select(p => CSharpTypes.Rigid(p.Type, variables)).ToArray());
            return CSharpTypes.Rigid(definition.ReturnType, variables);
        }

        private void BindArguments(IInvocationOperation operation, HmType[] parameters)
            => BindArguments(operation.Arguments, parameters);

        private void BindArguments(IEnumerable<IArgumentOperation> arguments, HmType[] parameters)
        {
            foreach (var argument in arguments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parameter = argument.Parameter ?? throw new InferenceException("Unbound argument.");
                var expected = parameters[parameter.Ordinal];
                // Omitted defaults are independent of the caller's expressions.
                // In particular, default(T) must not pin a generic T to the type
                // from the original constructed call.
                if (argument.ArgumentKind == ArgumentKind.DefaultValue) continue;
                if (parameter.IsParams && argument.ArgumentKind is not ArgumentKind.Explicit and not ArgumentKind.ParamArray)
                    throw new InferenceException("Expanded non-array params collections are unsupported.");
                if (argument.ArgumentKind == ArgumentKind.ParamArray)
                {
                    if (argument.Value is not IArrayCreationOperation { Initializer: not null } array)
                        throw new InferenceException("Only array-based expanded params are supported.");
                    var element = HmType.Variable();
                    HmType.Unify(expected, new("[]", element));
                    foreach (var value in array.Initializer.ElementValues)
                        HmType.Unify(BoundExpression(value), element);
                }
                else HmType.Unify(BoundExpression(argument.Value), expected);
            }
        }

        private HmType BoundExpression(IOperation value) => value.Syntax is ExpressionSyntax expression
            ? Expression(expression) : throw new InferenceException("Unsupported synthesized argument.");
    }

    private static SeparatedSyntaxList<ParameterSyntax> Parameters(this MethodDeclarationSyntax method) => method.ParameterList.Parameters;
}
