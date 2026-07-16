namespace SharpProof.Tools.Fuzz;

public sealed class FuzzCaseGenerator
{
    private static readonly Lazy<ImmutableSortedDictionary<string, ImmutableArray<ShapeRegistryEntry>>>
        RegistryByPrimaryShape =
            new(() => RegistryEntries
                .SelectMany(registryEntry => registryEntry.PrimaryShapeIds.Select(shapeId =>
                    new KeyValuePair<string, ShapeRegistryEntry>(shapeId, registryEntry)))
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToImmutableSortedDictionary(
                    group => group.Key,
                    group => group.Select(pair => pair.Value)
                        .Distinct()
                        .OrderBy(registryEntry => registryEntry.Id, StringComparer.Ordinal)
                        .ToImmutableArray(),
                    StringComparer.Ordinal));

    private static readonly Lazy<ImmutableArray<string>> OrderedGeneratorBackedShapeIds =
        new(() => RegistryByPrimaryShape.Value.Keys.ToImmutableArray());

    private readonly int _seed;

    public FuzzCaseGenerator(int seed)
    {
        _seed = seed;
    }

    private static ShapeRegistryEntry Entry(
        string id,
        OperationKind[] primaryShapes,
        string[] operationKinds,
        string[] syntaxKinds,
        FuzzExpectation expectation,
        bool allowUnsafe,
        bool allowEffectPreservingWrappers,
        Func<int, Random, string, string> build)
    {
        return new ShapeRegistryEntry(
            id,
            primaryShapes.Select(RoslynShapeManifest.OperationShapeId).ToImmutableArray(),
            operationKinds.ToImmutableArray(),
            syntaxKinds.ToImmutableArray(),
            expectation,
            allowUnsafe,
            allowEffectPreservingWrappers,
            build);
    }

    private static ShapeRegistryEntry StaticEntry(
        string id,
        OperationKind[] primaryShapes,
        string[] operationKinds,
        string[] syntaxKinds,
        FuzzExpectation expectation,
        bool allowUnsafe,
        bool allowEffectPreservingWrappers,
        string member)
    {
        return Entry(
            id, primaryShapes, operationKinds, syntaxKinds, expectation, allowUnsafe,
            allowEffectPreservingWrappers, (_, _, className) => BuildClass(className, member));
    }

    private static ShapeRegistryEntry MethodEntry(
        string id,
        OperationKind[] primaryShapes,
        string[] operationKinds,
        string[] syntaxKinds,
        FuzzExpectation expectation,
        bool allowUnsafe,
        bool allowEffectPreservingWrappers,
        string returnType,
        string parameters,
        string body)
    {
        return StaticEntry(
            id, primaryShapes, operationKinds, syntaxKinds, expectation, allowUnsafe,
            allowEffectPreservingWrappers,
            $$"""
                  [EnforcePure]
                  public {{returnType}} TestMethod({{parameters}})
                  {
              {{Indent(body, 8)}}
                  }
              """);
    }

    private static ShapeRegistryEntry ExpressionEntry(
        string id,
        OperationKind[] primaryShapes,
        string[] operationKinds,
        string[] syntaxKinds,
        FuzzExpectation expectation,
        bool allowEffectPreservingWrappers,
        string parameters,
        string[] expressions)
    {
        return Entry(
            id, primaryShapes, operationKinds, syntaxKinds, expectation, false,
            allowEffectPreservingWrappers,
            (_, random, className) =>
            {
                var expression = expressions.Length == 1
                    ? expressions[0]
                    : expressions[random.Next(expressions.Length)];
                return BuildClass(className, BuildIntMethodFromExpression(expression, random, parameters));
            });
    }

    public static ImmutableArray<ShapeRegistryEntry> RegistryEntries { get; } = ImmutableArray.Create(
        ExpressionEntry("PureArithmetic", [OperationKind.Binary], ["Binary"], [], FuzzExpectation.DefinitelyPure(), true, "int x",
            ["x + 1", "(x * 3) - 7", "(x / 2) + 9", "unchecked((x << 1) ^ 17)"]),
        Entry("PureStringConcat", [OperationKind.Binary], ["Binary"], ["AddExpression"], FuzzExpectation.DefinitelyPure(), false, true, BuildPureStringConcat),
        ExpressionEntry("PureListPattern", [OperationKind.ListPattern], ["ListPattern"], ["ListPattern"], FuzzExpectation.DefinitelyPure(), true,
            "int[] values", ["values is [1, .., 3] ? 1 : 0", "values is [_, .. var rest] ? rest.Length : 0"]),
        MethodEntry("PureCollectionExpression", [OperationKind.CollectionExpression], ["CollectionExpression"], ["CollectionExpression"], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "int x", "int[] values = [1, x, 3];\nreturn values.Length;"),
        ExpressionEntry("PureInterpolatedString", [OperationKind.InterpolatedString], ["InterpolatedString"], ["InterpolatedStringExpression"], FuzzExpectation.DefinitelyPure(), true,
            "int x", ["$\"value={x}\".Length", "$\"sum={x + 1}\".Length"]),
        MethodEntry("PureUtf8String", [OperationKind.Utf8String], ["Utf8String"], ["Utf8StringLiteralExpression"], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "", "return \"abc\"u8.Length;"),
        MethodEntry("PureArrayCreation", [OperationKind.ArrayCreation], ["ArrayCreation"], ["ArrayCreationExpression"], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "int x", "var values = new int[] { 1, x, 3 };\nreturn values[1];"),
        Entry("PureNestedOwnershipChain", [OperationKind.PropertyReference], ["PropertyReference", "SimpleAssignment", "ObjectCreation"], ["SimpleMemberAccessExpression", "SimpleAssignmentExpression"], FuzzExpectation.DefinitelyPure(), false, true, BuildPureNestedOwnershipChain),
        Entry("ImpureOwnershipEscapeChain", [OperationKind.ObjectCreation], ["ObjectCreation", "PropertyReference", "Return"], ["ObjectCreationExpression"], FuzzExpectation.DefinitelyImpure(), false, true, BuildImpureOwnershipEscapeChain),
        MethodEntry("ImpureConsoleWrite", [OperationKind.Invocation], ["Invocation"], [], FuzzExpectation.DefinitelyImpure(), false, true,
            "void", "", "Console.WriteLine(\"impure\");"),
        MethodEntry("ImpureDynamicDispatch", [OperationKind.DynamicInvocation], ["DynamicInvocation"], [], FuzzExpectation.DefinitelyImpure(), false, false,
            "string", "dynamic value", "return value.ToString();"),
        MethodEntry("ImpureDelegateInvoke", [OperationKind.Invocation], ["Invocation"], [], FuzzExpectation.DefinitelyImpure(), false, false,
            "void", "Action action", "action();"),
        MethodEntry("ImpureThrowExpression", [OperationKind.Throw], ["Throw"], ["ThrowStatement"], FuzzExpectation.DefinitelyImpure(), false, false,
            "int", "", "throw new InvalidOperationException(\"fuzz\");"),
        MethodEntry("ExceptionDirectThrowInvalidOperation", [OperationKind.Throw], ["Throw"], ["ThrowStatement"], FuzzExpectation.Create(Sp0002ExpectationKind.MustEmit, Sp0010ExpectationKind.MustEmit), false, false,
            "int", "", "throw new InvalidOperationException(\"fuzz\");"),
        MethodEntry("ExceptionGuardedThrowArgumentNull", [OperationKind.Throw], ["Throw"], ["ThrowStatement"], FuzzExpectation.Create(Sp0002ExpectationKind.MustEmit, Sp0010ExpectationKind.MustEmit), false, false,
            "int", "string text", """
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return text.Length;
            """),
        MethodEntry("ExceptionThrowExpressionFormatException", [OperationKind.Throw], ["Throw"], ["ThrowExpression"], FuzzExpectation.Create(Sp0002ExpectationKind.MustEmit, Sp0010ExpectationKind.MustEmit), false, false,
            "int", "string text", """
            return string.IsNullOrWhiteSpace(text)
                ? throw new FormatException("fuzz")
                : text.Length;
            """),
        MethodEntry("ExceptionCaughtInternalThrow", [OperationKind.Try, OperationKind.CatchClause], ["Try", "CatchClause", "Throw"], ["TryStatement", "CatchClause", "ThrowStatement"], FuzzExpectation.Create(Sp0002ExpectationKind.MustEmit, Sp0010ExpectationKind.MustNotEmit), false, false,
            "int", "", """
            try
            {
                throw new InvalidOperationException("fuzz");
            }
            catch (InvalidOperationException)
            {
                return 1;
            }
            """),
        MethodEntry("ExceptionDeadBranchThrow", [OperationKind.Throw], ["Conditional", "Throw"], ["IfStatement", "ThrowStatement"], FuzzExpectation.Create(Sp0002ExpectationKind.MustNotEmit, Sp0010ExpectationKind.MustNotEmit), false, false,
            "int", "", """
            if (false)
            {
                throw new InvalidOperationException("fuzz");
            }

            return 1;
            """),
        MethodEntry("ExceptionGuardedSafeDivideByZeroExcluded", [OperationKind.Binary], ["Conditional", "Binary"], ["IfStatement", "DivideExpression"], FuzzExpectation.Create(Sp0002ExpectationKind.MustNotEmit, Sp0010ExpectationKind.MustNotEmit), false, false,
            "int", "int divisor", """
            if (divisor != 0)
            {
                return 10 / divisor;
            }

            return 1;
            """),
        MethodEntry("ExceptionGuardedNullDereferenceExcluded", [OperationKind.PropertyReference], ["Conditional", "PropertyReference"], ["IfStatement"], FuzzExpectation.Create(Sp0002ExpectationKind.MustNotEmit, Sp0010ExpectationKind.MustNotEmit), false, false,
            "int", "string text", """
            if (text == null)
            {
                return 0;
            }

            return text.Length;
            """),
        MethodEntry("ExceptionDefiniteDivideByZero", [OperationKind.Binary], ["Binary"], ["DivideExpression"], FuzzExpectation.Create(Sp0002ExpectationKind.MayEmitConservatively, Sp0010ExpectationKind.MustEmit), false, false,
            "int", "", "var zero = 0;\nreturn 10 / zero;"),
        MethodEntry("ExceptionDefiniteNullReference", [OperationKind.PropertyReference], ["PropertyReference"], ["SimpleMemberAccessExpression"], FuzzExpectation.Create(Sp0002ExpectationKind.MayEmitConservatively, Sp0010ExpectationKind.MustEmit), false, false,
            "int", "", "string text = null;\nreturn text.Length;"),
        StaticEntry("ExceptionUsingDisposeThrows", [OperationKind.UsingDeclaration], ["UsingDeclaration"], ["LocalDeclarationStatement"], FuzzExpectation.Create(Sp0002ExpectationKind.MayEmitConservatively, Sp0010ExpectationKind.MustEmit), false, false,
            """
                private sealed class ThrowingDisposable : IDisposable
                {
                    public void Dispose()
                    {
                        throw new ObjectDisposedException("fuzz");
                    }
                }

                [EnforcePure]
                public int TestMethod()
                {
                    using var disposable = new ThrowingDisposable();
                    return 1;
                }
            """),
        MethodEntry("ExceptionInvokedLocalFunctionThrow", [OperationKind.LocalFunction, OperationKind.Throw], ["LocalFunction", "Throw"], ["LocalFunctionStatement", "ThrowStatement"], FuzzExpectation.Create(Sp0002ExpectationKind.MayEmitConservatively, Sp0010ExpectationKind.MustEmit).RequireExceptionEdgesOnAnySp0010(), false, false,
            "int", "", """
            int Local()
            {
                throw new InvalidOperationException("fuzz");
            }

            return Local();
            """),
        MethodEntry("ExceptionInvokedLambdaThrow", [OperationKind.AnonymousFunction, OperationKind.Throw], ["AnonymousFunction", "Throw"], ["ParenthesizedLambdaExpression", "ThrowExpression"], FuzzExpectation.Create(Sp0002ExpectationKind.MayEmitConservatively, Sp0010ExpectationKind.MustEmit).RequireExceptionEdgesOnAnySp0010(), false, false,
            "int", "", "Func<int> local = () => throw new FormatException(\"fuzz\");\nreturn local();"),
        StaticEntry("ImpureFieldWrite", [OperationKind.SimpleAssignment, OperationKind.FieldReference], ["SimpleAssignment", "FieldReference"], ["SimpleAssignmentExpression"], FuzzExpectation.DefinitelyImpure(), false, false,
            """
                private int _value;

                [EnforcePure]
                public void TestMethod(int value)
                {
                    _value = value;
                }
            """),
        ExpressionEntry("ImpureAmbientDateTime", [OperationKind.PropertyReference], ["PropertyReference"], ["SimpleMemberAccessExpression"], FuzzExpectation.DefinitelyImpure(), false,
            "int x", ["DateTime.Now.Day"]),
        Entry("ImpureAwaitTaskDelay", [OperationKind.Await], ["Await"], ["AwaitExpression"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureAwaitTaskDelay),
        StaticEntry("ImpureLockSection", [OperationKind.Lock], ["Lock"], ["LockStatement"], FuzzExpectation.DefinitelyImpure(), false, false,
            """
                private readonly object _gate = new object();

                [EnforcePure]
                public int TestMethod()
                {
                    lock (_gate)
                    {
                        return 1;
                    }
                }
            """),
        MethodEntry("ImpureUsingStandardOutput", [OperationKind.UsingDeclaration], ["UsingDeclaration"], ["LocalDeclarationStatement"], FuzzExpectation.DefinitelyImpure(), false, false,
            "int", "", "using var stream = Console.OpenStandardOutput();\nreturn 1;"),
        MethodEntry("ImpureTryCatch", [OperationKind.Try, OperationKind.CatchClause], ["Try", "CatchClause"], ["TryStatement", "CatchClause"], FuzzExpectation.DefinitelyImpure(), false, false,
            "int", "int x", """
            try
            {
                if (x < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(x));
                }

                return x + 1;
            }
            catch (ArgumentOutOfRangeException)
            {
                return 0;
            }
            """),
        MethodEntry("PureConditionalAccessCoalesce", [OperationKind.ConditionalAccess, OperationKind.Coalesce], ["ConditionalAccess", "Coalesce"], ["ConditionalAccessExpression", "CoalesceExpression"], FuzzExpectation.DefinitelyPure(), false, false,
            "int", "string text, string fallback", "return text?.Trim().Length ?? fallback.Length;"),
        MethodEntry("PureIsTypeCheck", [OperationKind.IsType], ["IsType"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "object value", "return value is string ? 1 : 0;"),
        MethodEntry("PureNegatedPattern", [OperationKind.NegatedPattern], ["NegatedPattern"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "object value", "return value is not int ? 1 : 0;"),
        MethodEntry("PureSwitchStatement", [OperationKind.Switch], ["Switch", "SwitchCase"], ["SwitchStatement"], FuzzExpectation.DefinitelyPure(), false, false,
            "int", "int value", """
            switch (value)
            {
                case 0:
                    return 0;
                case 1:
                case 2:
                    return 1;
                default:
                    return value;
            }
            """),
        MethodEntry("ImpureUsingStatement", [OperationKind.Using], ["Using"], ["UsingStatement"], FuzzExpectation.DefinitelyImpure(), false, false,
            "int", "", """
            using (var writer = new System.IO.StringWriter())
            {
                return 1;
            }
            """),
        MethodEntry("PureCompoundAssignment", [OperationKind.CompoundAssignment], ["CompoundAssignment"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "int x", "var value = x;\nvalue += 2;\nreturn value;"),
        MethodEntry("PureCoalesceAssignment", [OperationKind.CoalesceAssignment], ["CoalesceAssignment"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "string", "string value", "value ??= \"fallback\";\nreturn value;"),
        MethodEntry("PureDeconstructionAssignment", [OperationKind.DeconstructionAssignment], ["DeconstructionAssignment"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "int x, int y", "var left = x;\nvar right = y;\n(left, right) = (right, left);\nreturn left - right;"),
        MethodEntry("PureIncrement", [OperationKind.Increment], ["Increment"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "int x", "var value = x;\nvalue++;\nreturn value;"),
        MethodEntry("PureDecrement", [OperationKind.Decrement], ["Decrement"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "int x", "var value = x;\nvalue--;\nreturn value;"),
        MethodEntry("ImpureDeclarationExpression", [OperationKind.DeclarationExpression], ["DeclarationExpression"], [], FuzzExpectation.DefinitelyImpure(), false, true,
            "int", "string text", "return int.TryParse(text, out var value) ? value : 0;"),
        MethodEntry("PureDeclarationPattern", [OperationKind.DeclarationPattern], ["DeclarationPattern"], ["DeclarationPattern"], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "object value", "return value is int number ? number : 0;"),
        StaticEntry("ImpureTypeParameterObjectCreation", [OperationKind.TypeParameterObjectCreation], ["TypeParameterObjectCreation"], [], FuzzExpectation.DefinitelyImpure(), false, true,
            """
                [EnforcePure]
                public T TestMethod<T>() where T : new()
                {
                    return new T();
                }
            """),
        Entry("ImpureEventAssignment", [OperationKind.EventAssignment], ["EventAssignment", "EventReference"], [], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureEventAssignment),
        MethodEntry("PureAnonymousObjectCreation", [OperationKind.AnonymousObjectCreation], ["AnonymousObjectCreation"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "int x", "var item = new { Value = x, Next = x + 1 };\nreturn item.Value + item.Next;"),
        MethodEntry("PureDefaultValue", [OperationKind.DefaultValue], ["DefaultValue"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "", "return default(int);"),
        MethodEntry("PureSizeOf", [OperationKind.SizeOf], ["SizeOf"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "", "return sizeof(int);"),
        MethodEntry("PureTypeOf", [OperationKind.TypeOf], ["TypeOf"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "", "return typeof(int).Name.Length;"),
        MethodEntry("PureNameOf", [OperationKind.NameOf], ["NameOf"], [], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "int value", "return nameof(value).Length;"),
        MethodEntry("ImpureDynamicIndexerAccess", [OperationKind.DynamicIndexerAccess], ["DynamicIndexerAccess"], [], FuzzExpectation.DefinitelyImpure(), false, false,
            "int", "dynamic values", "return values[0];"),
        Entry("ImpureDynamicObjectCreation", [OperationKind.DynamicObjectCreation], ["DynamicObjectCreation"], [], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureDynamicObjectCreation),
        MethodEntry("PureTuple", [OperationKind.Tuple], ["Tuple"], ["TupleExpression"], FuzzExpectation.DefinitelyPure(), false, true,
            "int", "int x", "var pair = (Left: x, Right: x + 1);\nreturn pair.Left + pair.Right;"),
        Entry("ImpureInterfaceGetter", [OperationKind.PropertyReference], ["PropertyReference"], [], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureInterfaceGetter),
        Entry("PureRecursivePattern", [OperationKind.RecursivePattern], ["RecursivePattern"], ["RecursivePattern"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureRecursivePattern),
        MethodEntry("PureSpreadCollectionExpression", [OperationKind.CollectionExpression, OperationKind.Spread], ["CollectionExpression", "Spread"], ["CollectionExpression"], FuzzExpectation.DefinitelyPure(), false, false,
            "int", "int[] values", "int[] copy = [0, .. values, 9];\nreturn copy.Length;"),
        MethodEntry("PureSwitchExpression", [OperationKind.SwitchExpression], ["SwitchExpression"], ["SwitchExpression"], FuzzExpectation.DefinitelyPure(), false, false,
            "int", "int x", """
            return x switch
            {
                < 0 => -1,
                0 => 0,
                _ => 1
            };
            """),
        MethodEntry("PureRangeSlice", [OperationKind.Range], ["Range"], ["RangeExpression"], FuzzExpectation.DefinitelyPure(), false, false,
            "int", "string text", "return text[1..^1].Length;"),
        Entry("PureYieldReturn", [OperationKind.YieldReturn], ["YieldReturn"], ["YieldReturnStatement"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureYieldReturn),
        Entry("ImpureWithExpression", [OperationKind.With], ["With"], ["WithExpression"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureWithExpression),
        MethodEntry("PureAnonymousFunction", [OperationKind.AnonymousFunction], ["AnonymousFunction"], ["SimpleLambdaExpression"], FuzzExpectation.DefinitelyPure(), false, false,
            "int", "int x", "Func<int, int> project = static value => value + 1;\nreturn project(x);"),
        StaticEntry("PureDelegateCreation", [OperationKind.DelegateCreation], ["DelegateCreation"], [], FuzzExpectation.DefinitelyPure(), false, false,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    Func<int, int> project = Project;
                    return project(x);
                }

                private static int Project(int value)
                {
                    return value + 1;
                }
            """),
        Entry("PureImplicitIndexerReference", [OperationKind.ImplicitIndexerReference], ["ImplicitIndexerReference"], ["ElementAccessExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureImplicitIndexerReference),
        Entry("PureInterpolatedStringHandler", [OperationKind.InterpolatedStringHandlerCreation, OperationKind.InterpolatedStringAddition, OperationKind.InterpolatedStringAppendLiteral, OperationKind.InterpolatedStringAppendFormatted, OperationKind.InterpolatedStringHandlerArgumentPlaceholder], ["InterpolatedStringHandlerCreation", "InterpolatedStringAddition", "InterpolatedStringAppendLiteral", "InterpolatedStringAppendFormatted", "InterpolatedStringHandlerArgumentPlaceholder"], ["InterpolatedStringExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureInterpolatedStringHandler),
        Entry("ImpureAddressOf", [OperationKind.AddressOf], ["AddressOf"], [], FuzzExpectation.DefinitelyImpure(), true, false, BuildImpureAddressOf),
        Entry("PureInlineArrayAccess", [OperationKind.InlineArrayAccess], ["InlineArrayAccess"], ["ElementAccessExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureInlineArrayAccess),
        Entry("ImpureFunctionPointer", [OperationKind.FunctionPointerInvocation], ["FunctionPointerInvocation"], ["FunctionPointerType"], FuzzExpectation.DefinitelyImpure(), true, false, BuildImpureFunctionPointer),
        MethodEntry("PureNestedLambdaLocalFunction", [OperationKind.AnonymousFunction, OperationKind.LocalFunction], ["AnonymousFunction", "LocalFunction"], ["SimpleLambdaExpression", "LocalFunctionStatement"], FuzzExpectation.DefinitelyPure(), false, false,
            "int", "int x", """
            int Outer(int seed)
            {
                Func<int, int> addSeed = value =>
                {
                    Func<int, int> inner = local => local + seed;
                    return inner(value);
                };

                return addSeed(x);
            }

            return Outer(1);
            """),
        MethodEntry("PureTuplePatternSwitch", [OperationKind.Tuple, OperationKind.SwitchExpression], ["Tuple", "SwitchExpression"], ["TupleExpression", "SwitchExpression"], FuzzExpectation.DefinitelyPure(), false, false,
            "int", "int x, int y", """
            var pair = (x, y);
            return pair switch
            {
                (> 0, > 0) => 1,
                (0, _) => 0,
                _ => -1
            };
            """),
        Entry("ImpureUsingAwaitDelegateFlow", [OperationKind.UsingDeclaration, OperationKind.Await, OperationKind.AnonymousFunction], ["UsingDeclaration", "Await", "AnonymousFunction"], ["LocalDeclarationStatement", "AwaitExpression", "ParenthesizedLambdaExpression"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureUsingAwaitDelegateFlow));

    public FuzzCase Next(int index)
    {
        var shapeIds = OrderedGeneratorBackedShapeIds.Value;
        var shapeId = shapeIds[index % shapeIds.Length];
        var variant = index / shapeIds.Length;
        return GenerateForShapeCore(shapeId, variant, index);
    }

    private FuzzCase GenerateForShapeCore(string shapeId, int variant, int index)
    {
        if (!RegistryByPrimaryShape.Value.TryGetValue(shapeId, out var entries))
            throw new ArgumentException($"Unknown generator-backed shape '{shapeId}'.", nameof(shapeId));

        var entry = entries[variant % entries.Length];
        var entryVariant = variant / entries.Length;
        return GenerateForRegistryEntry(entry, index, entryVariant);
    }

    public FuzzCase GenerateForRegistryEntry(ShapeRegistryEntry registryEntry, int index, int variant = 0)
    {
        var random = CreateRandom(StableHash(index, variant, registryEntry.Id));
        var className = $"FuzzCase{index}_{registryEntry.Id}_V{variant}";
        var source = registryEntry.Build(index, random, className);
        return new FuzzCase(
            $"{index:000000}-{registryEntry.Id}",
            registryEntry.Id,
            source,
            registryEntry.AllowUnsafe ||
            source.Contains("unsafe", StringComparison.Ordinal) ||
            source.Contains("delegate*", StringComparison.Ordinal),
            registryEntry.Expectation,
            registryEntry.PrimaryShapeIds,
            registryEntry.ExpectedOperationKinds,
            registryEntry.ExpectedSyntaxKinds);
    }

    private static string BuildPureStringConcat(int index, Random random, string className)
    {
        const string expression = "(left + right).Length";
        return BuildClass(
            className,
            $$"""
                  [EnforcePure]
                  public int TestMethod(string left, string right)
                  {
              {{Indent(BuildReturnBody(expression, random), 8)}}
                  }
              """);
    }

    private static string BuildPureNestedOwnershipChain(int index, Random random, string className)
    {
        return BuildOwnershipChainClass(
            className,
            $$"""
                [EnforcePure]
                public int TestMethod()
                {
                    var outer = new {{className}}Outer { Value = new {{className}}Middle { Value = new {{className}}Box() } };
                    outer.Value.Value.Value = 1;
                    return outer.Value.Value.Value;
                }
            """);
    }

    private static string BuildImpureOwnershipEscapeChain(int index, Random random, string className)
    {
        return BuildOwnershipChainClass(
            className,
            $$"""
                [EnforcePure]
                public {{className}}Outer TestMethod()
                {
                    return new {{className}}Outer { Value = new {{className}}Middle { Value = new {{className}}Box() } };
                }
            """);
    }

    private static string BuildOwnershipChainClass(string className, string testMethod)
    {
        return $$"""
                 {{BuildUsings()}}

                 public sealed class {{className}}Box
                 {
                     public int Value;
                 }

                 public sealed class {{className}}Middle
                 {
                     public {{className}}Box Value { get; init; }
                 }

                 public sealed class {{className}}Outer
                 {
                     public {{className}}Middle Value { get; init; }
                 }

                 public class {{className}}
                 {
                 {{testMethod}}
                 }
                 """;
    }



















    private static string BuildImpureAwaitTaskDelay(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings("System", "System.Threading.Tasks")}}

                 public class {{className}}
                 {
                     [EnforcePure]
                     public async Task<int> TestMethod()
                     {
                         await Task.Delay(1);
                         return 1;
                     }
                 }
                 """;
    }















    private static string BuildImpureEventAssignment(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings("System")}}

                 public sealed class {{className}}Source
                 {
                     public event EventHandler Changed
                     {
                         add { }
                         remove { }
                     }
                 }

                 public class {{className}}
                 {
                     [EnforcePure]
                     public void TestMethod({{className}}Source source)
                     {
                         source.Changed += Handle;
                     }

                     private static void Handle(object sender, EventArgs args) { }
                 }
                 """;
    }







    private static string BuildImpureDynamicObjectCreation(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings()}}

                 public sealed class {{className}}Widget
                 {
                     public {{className}}Widget(int value)
                     {
                     }
                 }

                 public class {{className}}
                 {
                     [EnforcePure]
                     public int TestMethod(dynamic value)
                     {
                         var widget = new {{className}}Widget(value);
                         return 1;
                     }
                 }
                 """;
    }


    private static string BuildImpureInterfaceGetter(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings()}}

                 public interface I{{className}}Value
                 {
                     int Value { get; }
                 }

                 public class {{className}}
                 {
                     [EnforcePure]
                     public int TestMethod(I{{className}}Value value)
                     {
                         return value.Value;
                     }
                 }
                 """;
    }

    private static string BuildPureRecursivePattern(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings()}}

                 public sealed class {{className}}Node
                 {
                     public {{className}}Node? Next { get; set; }
                     public int Value { get; set; }
                 }

                 public class {{className}}
                 {
                     [EnforcePure]
                     public int TestMethod({{className}}Node node)
                     {
                         return node is { Next: { Value: > 0 } } ? 1 : 0;
                     }
                 }
                 """;
    }




    private static string BuildPureYieldReturn(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings("System.Collections.Generic")}}

                 public class {{className}}
                 {
                     [EnforcePure]
                     public IEnumerable<int> TestMethod(int x)
                     {
                         yield return x + 1;
                         yield break;
                     }
                 }
                 """;
    }

    private static string BuildImpureWithExpression(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings("System")}}

                 public record {{className}}Data(int Value, int Other);

                 public class {{className}}
                 {
                     [EnforcePure]
                     public int TestMethod({{className}}Data data, int x)
                     {
                         var updated = data with { Value = x };
                         return updated.Value;
                     }
                 }
                 """;
    }



    private static string BuildPureImplicitIndexerReference(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings("System")}}

                 public sealed class {{className}}Bag
                 {
                     public int Length => 3;
                     public int this[int index] => index + 10;
                 }

                 public class {{className}}
                 {
                     [EnforcePure]
                     public int TestMethod({{className}}Bag bag)
                     {
                         return bag[^1];
                     }
                 }
                 """;
    }

    private static string BuildPureInterpolatedStringHandler(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings("System", "System.Runtime.CompilerServices")}}

                 [InterpolatedStringHandler]
                 public ref struct {{className}}Handler
                 {
                     public {{className}}Handler(int literalLength, int formattedCount, int value) { }
                     public void AppendLiteral(string value) { }
                     public void AppendFormatted<T>(T value) { }
                 }

                 public class {{className}}
                 {
                     [EnforcePure]
                     public void TestMethod(int value)
                     {
                         Log(value, $"left={value}" + $"right={value}");
                     }

                     private void Log(int value, [InterpolatedStringHandlerArgument("value")] {{className}}Handler handler) { }
                 }
                 """;
    }

    private static string BuildImpureAddressOf(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings()}}

                 public unsafe class {{className}}
                 {
                     [EnforcePure]
                     public int TestMethod()
                     {
                         int value = 1;
                         int* pointer = &value;
                         return *pointer;
                     }
                 }
                 """;
    }

    private static string BuildPureInlineArrayAccess(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings("System.Runtime.CompilerServices")}}

                 [InlineArray(4)]
                 public struct {{className}}Buffer
                 {
                     private int _element0;
                 }

                 public class {{className}}
                 {
                     [EnforcePure]
                     public int TestMethod()
                     {
                         {{className}}Buffer buffer = default;
                         return buffer[0];
                     }
                 }
                 """;
    }

    private static string BuildImpureFunctionPointer(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings()}}

                 public unsafe class {{className}}
                 {
                     [EnforcePure]
                     public int TestMethod(delegate*<int, int> pointer)
                     {
                         return pointer(1);
                     }
                 }
                 """;
    }



    private static string BuildImpureUsingAwaitDelegateFlow(int index, Random random, string className)
    {
        return $$"""
                 {{BuildUsings("System", "System.Threading.Tasks")}}

                 public class {{className}}
                 {
                     [EnforcePure]
                     public async Task<int> TestMethod()
                     {
                         using var stream = Console.OpenStandardOutput();
                         Func<Task<int>> factory = async () =>
                         {
                             await Task.Delay(1);
                             return stream.CanWrite ? 1 : 0;
                         };

                         return await factory();
                     }
                 }
                 """;
    }

    private Random CreateRandom(int index)
    {
        return new Random(StableHash(_seed, index, 0x51ED270B));
    }

    private static int StableHash(int first, int second, int third) =>
        Mix(Mix(Mix(unchecked((int)2166136261), first), second), third);

    private static int StableHash(int first, int second, string third)
    {
        var hash = Mix(Mix(unchecked((int)2166136261), first), second);
        foreach (var character in third)
            hash = Mix(hash, character);

        return hash;
    }

    private static int Mix(int hash, int value) => unchecked((hash ^ value) * 16777619);

    private static string BuildIntMethodFromExpression(string expression, Random random, string parameterList = "int x")
    {
        return $$"""
                             [EnforcePure]
                             public int TestMethod({{parameterList}})
                             {
                 {{Indent(BuildReturnBody(expression, random), 8)}}
                             }
                 """;
    }

    private static string BuildReturnBody(string expression, Random random)
    {
        return random.Next(5) switch
        {
            0 => $"return {expression};",
            1 => $"var value = {expression};\nreturn value;",
            2 => $"if (true)\n{{\n    return {expression};\n}}\nreturn 0;",
            3 => $"return true ? {expression} : 0;",
            _ => $"int Local() => {expression};\nreturn Local();"
        };
    }

    private static string BuildClass(string className, string members)
    {
        return $$"""
                 {{BuildUsings("System")}}

                 public class {{className}}
                 {
                 {{Indent(members, 4)}}
                 }
                 """;
    }

    private static string BuildUsings(params string[] namespaces) =>
        string.Join("\n", namespaces
            .Append("SharpProof.Attributes")
            .Select(static value => $"using {value};"));

    private static string Indent(string text, int spaces)
    {
        var padding = new string(' ', spaces);
        return string.Join(
            Environment.NewLine,
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                .Select(line => line.Length == 0 ? line : padding + line));
    }
}
