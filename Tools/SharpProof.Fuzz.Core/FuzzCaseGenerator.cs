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

    public static ImmutableArray<ShapeRegistryEntry> RegistryEntries { get; } = ImmutableArray.Create(
        Entry("PureArithmetic", [OperationKind.Binary], ["Binary"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureArithmetic),
        Entry("PureStringConcat", [OperationKind.Binary], ["Binary"], ["AddExpression"], FuzzExpectation.DefinitelyPure(), false, true, BuildPureStringConcat),
        Entry("PureListPattern", [OperationKind.ListPattern], ["ListPattern"], ["ListPattern"], FuzzExpectation.DefinitelyPure(), false, true, BuildPureListPattern),
        Entry("PureCollectionExpression", [OperationKind.CollectionExpression], ["CollectionExpression"], ["CollectionExpression"], FuzzExpectation.DefinitelyPure(), false, true, BuildPureCollectionExpression),
        Entry("PureInterpolatedString", [OperationKind.InterpolatedString], ["InterpolatedString"], ["InterpolatedStringExpression"], FuzzExpectation.DefinitelyPure(), false, true, BuildPureInterpolatedString),
        Entry("PureUtf8String", [OperationKind.Utf8String], ["Utf8String"], ["Utf8StringLiteralExpression"], FuzzExpectation.DefinitelyPure(), false, true, BuildPureUtf8String),
        Entry("PureArrayCreation", [OperationKind.ArrayCreation], ["ArrayCreation"], ["ArrayCreationExpression"], FuzzExpectation.DefinitelyPure(), false, true, BuildPureArrayCreation),
        Entry("PureNestedOwnershipChain", [OperationKind.PropertyReference], ["PropertyReference", "SimpleAssignment", "ObjectCreation"], ["SimpleMemberAccessExpression", "SimpleAssignmentExpression"], FuzzExpectation.DefinitelyPure(), false, true, BuildPureNestedOwnershipChain),
        Entry("ImpureOwnershipEscapeChain", [OperationKind.ObjectCreation], ["ObjectCreation", "PropertyReference", "Return"], ["ObjectCreationExpression"], FuzzExpectation.DefinitelyImpure(), false, true, BuildImpureOwnershipEscapeChain),
        Entry("ImpureConsoleWrite", [OperationKind.Invocation], ["Invocation"], [], FuzzExpectation.DefinitelyImpure(), false, true, BuildImpureConsoleWrite),
        Entry("ImpureDynamicDispatch", [OperationKind.DynamicInvocation], ["DynamicInvocation"], [], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureDynamicDispatch),
        Entry("ImpureDelegateInvoke", [OperationKind.Invocation], ["Invocation"], [], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureDelegateInvoke),
        Entry("ImpureThrowExpression", [OperationKind.Throw], ["Throw"], ["ThrowStatement"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureThrowExpression),
        Entry("ExceptionDirectThrowInvalidOperation", [OperationKind.Throw], ["Throw"], ["ThrowStatement"], ImpureWithExceptionExpectation(), false, false, BuildExceptionDirectThrowInvalidOperation),
        Entry("ExceptionGuardedThrowArgumentNull", [OperationKind.Throw], ["Throw"], ["ThrowStatement"], ImpureWithExceptionExpectation(), false, false, BuildExceptionGuardedThrowArgumentNull),
        Entry("ExceptionThrowExpressionFormatException", [OperationKind.Throw], ["Throw"], ["ThrowExpression"], ImpureWithExceptionExpectation(), false, false, BuildExceptionThrowExpressionFormatException),
        Entry("ExceptionCaughtInternalThrow", [OperationKind.Try, OperationKind.CatchClause], ["Try", "CatchClause", "Throw"], ["TryStatement", "CatchClause", "ThrowStatement"], ImpureWithoutExceptionExpectation(), false, false, BuildExceptionCaughtInternalThrow),
        Entry("ExceptionDeadBranchThrow", [OperationKind.Throw], ["Conditional", "Throw"], ["IfStatement", "ThrowStatement"], PureWithoutExceptionExpectation(), false, false, BuildExceptionDeadBranchThrow),
        Entry("ExceptionGuardedSafeDivideByZeroExcluded", [OperationKind.Binary], ["Conditional", "Binary"], ["IfStatement", "DivideExpression"], PureWithoutExceptionExpectation(), false, false, BuildExceptionGuardedSafeDivideByZeroExcluded),
        Entry("ExceptionGuardedNullDereferenceExcluded", [OperationKind.PropertyReference], ["Conditional", "PropertyReference"], ["IfStatement"], PureWithoutExceptionExpectation(), false, false, BuildExceptionGuardedNullDereferenceExcluded),
        Entry("ExceptionDefiniteDivideByZero", [OperationKind.Binary], ["Binary"], ["DivideExpression"], ExceptionWithOptionalSp0002Expectation(), false, false, BuildExceptionDefiniteDivideByZero),
        Entry("ExceptionDefiniteNullReference", [OperationKind.PropertyReference], ["PropertyReference"], ["SimpleMemberAccessExpression"], ExceptionWithOptionalSp0002Expectation(), false, false, BuildExceptionDefiniteNullReference),
        Entry("ExceptionUsingDisposeThrows", [OperationKind.UsingDeclaration], ["UsingDeclaration"], ["LocalDeclarationStatement"], ExceptionWithOptionalSp0002Expectation(), false, false, BuildExceptionUsingDisposeThrows),
        Entry("ExceptionInvokedLocalFunctionThrow", [OperationKind.LocalFunction, OperationKind.Throw], ["LocalFunction", "Throw"], ["LocalFunctionStatement", "ThrowStatement"], ExceptionWithOptionalSp0002Expectation().RequireExceptionEdgesOnAnySp0010(), false, false, BuildExceptionInvokedLocalFunctionThrow),
        Entry("ExceptionInvokedLambdaThrow", [OperationKind.AnonymousFunction, OperationKind.Throw], ["AnonymousFunction", "Throw"], ["ParenthesizedLambdaExpression", "ThrowExpression"], ExceptionWithOptionalSp0002Expectation().RequireExceptionEdgesOnAnySp0010(), false, false, BuildExceptionInvokedLambdaThrow),
        Entry("ImpureFieldWrite", [OperationKind.SimpleAssignment, OperationKind.FieldReference], ["SimpleAssignment", "FieldReference"], ["SimpleAssignmentExpression"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureFieldWrite),
        Entry("ImpureAmbientDateTime", [OperationKind.PropertyReference], ["PropertyReference"], ["SimpleMemberAccessExpression"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureAmbientDateTime),
        Entry("ImpureAwaitTaskDelay", [OperationKind.Await], ["Await"], ["AwaitExpression"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureAwaitTaskDelay),
        Entry("ImpureLockSection", [OperationKind.Lock], ["Lock"], ["LockStatement"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureLockSection),
        Entry("ImpureUsingStandardOutput", [OperationKind.UsingDeclaration], ["UsingDeclaration"], ["LocalDeclarationStatement"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureUsingStandardOutput),
        Entry("ImpureTryCatch", [OperationKind.Try, OperationKind.CatchClause], ["Try", "CatchClause"], ["TryStatement", "CatchClause"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureTryCatch),
        Entry("PureConditionalAccessCoalesce", [OperationKind.ConditionalAccess, OperationKind.Coalesce], ["ConditionalAccess", "Coalesce"], ["ConditionalAccessExpression", "CoalesceExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureConditionalAccessCoalesce),
        Entry("PureIsTypeCheck", [OperationKind.IsType], ["IsType"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureIsTypeCheck),
        Entry("PureNegatedPattern", [OperationKind.NegatedPattern], ["NegatedPattern"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureNegatedPattern),
        Entry("PureSwitchStatement", [OperationKind.Switch], ["Switch", "SwitchCase"], ["SwitchStatement"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureSwitchStatement),
        Entry("ImpureUsingStatement", [OperationKind.Using], ["Using"], ["UsingStatement"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureUsingStatement),
        Entry("PureCompoundAssignment", [OperationKind.CompoundAssignment], ["CompoundAssignment"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureCompoundAssignment),
        Entry("PureCoalesceAssignment", [OperationKind.CoalesceAssignment], ["CoalesceAssignment"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureCoalesceAssignment),
        Entry("PureDeconstructionAssignment", [OperationKind.DeconstructionAssignment], ["DeconstructionAssignment"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureDeconstructionAssignment),
        Entry("PureIncrement", [OperationKind.Increment], ["Increment"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureIncrement),
        Entry("PureDecrement", [OperationKind.Decrement], ["Decrement"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureDecrement),
        Entry("ImpureDeclarationExpression", [OperationKind.DeclarationExpression], ["DeclarationExpression"], [], FuzzExpectation.DefinitelyImpure(), false, true, BuildImpureDeclarationExpression),
        Entry("PureDeclarationPattern", [OperationKind.DeclarationPattern], ["DeclarationPattern"], ["DeclarationPattern"], FuzzExpectation.DefinitelyPure(), false, true, BuildPureDeclarationPattern),
        Entry("ImpureTypeParameterObjectCreation", [OperationKind.TypeParameterObjectCreation], ["TypeParameterObjectCreation"], [], FuzzExpectation.DefinitelyImpure(), false, true, BuildImpureTypeParameterObjectCreation),
        Entry("ImpureEventAssignment", [OperationKind.EventAssignment], ["EventAssignment", "EventReference"], [], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureEventAssignment),
        Entry("PureAnonymousObjectCreation", [OperationKind.AnonymousObjectCreation], ["AnonymousObjectCreation"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureAnonymousObjectCreation),
        Entry("PureDefaultValue", [OperationKind.DefaultValue], ["DefaultValue"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureDefaultValue),
        Entry("PureSizeOf", [OperationKind.SizeOf], ["SizeOf"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureSizeOf),
        Entry("PureTypeOf", [OperationKind.TypeOf], ["TypeOf"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureTypeOf),
        Entry("PureNameOf", [OperationKind.NameOf], ["NameOf"], [], FuzzExpectation.DefinitelyPure(), false, true, BuildPureNameOf),
        Entry("ImpureDynamicIndexerAccess", [OperationKind.DynamicIndexerAccess], ["DynamicIndexerAccess"], [], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureDynamicIndexerAccess),
        Entry("ImpureDynamicObjectCreation", [OperationKind.DynamicObjectCreation], ["DynamicObjectCreation"], [], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureDynamicObjectCreation),
        Entry("PureTuple", [OperationKind.Tuple], ["Tuple"], ["TupleExpression"], FuzzExpectation.DefinitelyPure(), false, true, BuildPureTuple),
        Entry("ImpureInterfaceGetter", [OperationKind.PropertyReference], ["PropertyReference"], [], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureInterfaceGetter),
        Entry("PureRecursivePattern", [OperationKind.RecursivePattern], ["RecursivePattern"], ["RecursivePattern"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureRecursivePattern),
        Entry("PureSpreadCollectionExpression", [OperationKind.CollectionExpression, OperationKind.Spread], ["CollectionExpression", "Spread"], ["CollectionExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureSpreadCollectionExpression),
        Entry("PureSwitchExpression", [OperationKind.SwitchExpression], ["SwitchExpression"], ["SwitchExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureSwitchExpression),
        Entry("PureRangeSlice", [OperationKind.Range], ["Range"], ["RangeExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureRangeSlice),
        Entry("PureYieldReturn", [OperationKind.YieldReturn], ["YieldReturn"], ["YieldReturnStatement"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureYieldReturn),
        Entry("ImpureWithExpression", [OperationKind.With], ["With"], ["WithExpression"], FuzzExpectation.DefinitelyImpure(), false, false, BuildImpureWithExpression),
        Entry("PureAnonymousFunction", [OperationKind.AnonymousFunction], ["AnonymousFunction"], ["SimpleLambdaExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureAnonymousFunction),
        Entry("PureDelegateCreation", [OperationKind.DelegateCreation], ["DelegateCreation"], [], FuzzExpectation.DefinitelyPure(), false, false, BuildPureDelegateCreation),
        Entry("PureImplicitIndexerReference", [OperationKind.ImplicitIndexerReference], ["ImplicitIndexerReference"], ["ElementAccessExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureImplicitIndexerReference),
        Entry("PureInterpolatedStringHandler", [OperationKind.InterpolatedStringHandlerCreation, OperationKind.InterpolatedStringAddition, OperationKind.InterpolatedStringAppendLiteral, OperationKind.InterpolatedStringAppendFormatted, OperationKind.InterpolatedStringHandlerArgumentPlaceholder], ["InterpolatedStringHandlerCreation", "InterpolatedStringAddition", "InterpolatedStringAppendLiteral", "InterpolatedStringAppendFormatted", "InterpolatedStringHandlerArgumentPlaceholder"], ["InterpolatedStringExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureInterpolatedStringHandler),
        Entry("ImpureAddressOf", [OperationKind.AddressOf], ["AddressOf"], [], FuzzExpectation.DefinitelyImpure(), true, false, BuildImpureAddressOf),
        Entry("PureInlineArrayAccess", [OperationKind.InlineArrayAccess], ["InlineArrayAccess"], ["ElementAccessExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureInlineArrayAccess),
        Entry("ImpureFunctionPointer", [OperationKind.FunctionPointerInvocation], ["FunctionPointerInvocation"], ["FunctionPointerType"], FuzzExpectation.DefinitelyImpure(), true, false, BuildImpureFunctionPointer),
        Entry("PureNestedLambdaLocalFunction", [OperationKind.AnonymousFunction, OperationKind.LocalFunction], ["AnonymousFunction", "LocalFunction"], ["SimpleLambdaExpression", "LocalFunctionStatement"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureNestedLambdaLocalFunction),
        Entry("PureTuplePatternSwitch", [OperationKind.Tuple, OperationKind.SwitchExpression], ["Tuple", "SwitchExpression"], ["TupleExpression", "SwitchExpression"], FuzzExpectation.DefinitelyPure(), false, false, BuildPureTuplePatternSwitch),
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
        var random = CreateRandom(HashCode.Combine(index, variant, registryEntry.Id));
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

    private static string BuildPureArithmetic(int index, Random random, string className)
    {
        var expression = random.Next(4) switch
        {
            0 => "x + 1",
            1 => "(x * 3) - 7",
            2 => "(x / 2) + 9",
            _ => "unchecked((x << 1) ^ 17)"
        };

        return BuildClass(
            className,
            BuildIntMethodFromExpression(expression, random));
    }

    private static string BuildPureStringConcat(int index, Random random, string className)
    {
        var expression = "(left + right).Length";

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

    private static string BuildPureInterpolatedString(int index, Random random, string className)
    {
        var expression = random.Next(2) == 0
            ? "$\"value={x}\".Length"
            : "$\"sum={x + 1}\".Length";

        return BuildClass(
            className,
            BuildIntMethodFromExpression(expression, random));
    }

    private static string BuildPureUtf8String(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    return "abc"u8.Length;
                }
            """);
    }

    private static string BuildPureArrayCreation(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    var values = new int[] { 1, x, 3 };
                    return values[1];
                }
            """);
    }

    private static string BuildPureNestedOwnershipChain(int index, Random random, string className)
    {
        return $$"""
                 using SharpProof.Attributes;

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
                     [EnforcePure]
                     public int TestMethod()
                     {
                         var outer = new {{className}}Outer { Value = new {{className}}Middle { Value = new {{className}}Box() } };
                         outer.Value.Value.Value = 1;
                         return outer.Value.Value.Value;
                     }
                 }
                 """;
    }

    private static string BuildImpureOwnershipEscapeChain(int index, Random random, string className)
    {
        return $$"""
                 using SharpProof.Attributes;

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
                     [EnforcePure]
                     public {{className}}Outer TestMethod()
                     {
                         return new {{className}}Outer { Value = new {{className}}Middle { Value = new {{className}}Box() } };
                     }
                 }
                 """;
    }

    private static string BuildPureListPattern(int index, Random random, string className)
    {
        var expression = random.Next(2) == 0
            ? "values is [1, .., 3] ? 1 : 0"
            : "values is [_, .. var rest] ? rest.Length : 0";

        return BuildClass(
            className,
            BuildIntMethodFromExpression(expression, random, "int[] values"));
    }

    private static string BuildPureCollectionExpression(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    int[] values = [1, x, 3];
                    return values.Length;
                }
            """);
    }

    private static string BuildImpureConsoleWrite(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public void TestMethod()
                {
                    Console.WriteLine("impure");
                }
            """);
    }

    private static string BuildImpureDynamicDispatch(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public string TestMethod(dynamic value)
                {
                    return value.ToString();
                }
            """);
    }

    private static string BuildImpureDelegateInvoke(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public void TestMethod(Action action)
                {
                    action();
                }
            """);
    }

    private static string BuildImpureThrowExpression(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    throw new InvalidOperationException("fuzz");
                }
            """);
    }

    private static string BuildExceptionDirectThrowInvalidOperation(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    throw new InvalidOperationException("fuzz");
                }
            """);
    }

    private static string BuildExceptionGuardedThrowArgumentNull(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text)
                {
                    if (text == null)
                    {
                        throw new ArgumentNullException(nameof(text));
                    }

                    return text.Length;
                }
            """);
    }

    private static string BuildExceptionThrowExpressionFormatException(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text)
                {
                    return string.IsNullOrWhiteSpace(text)
                        ? throw new FormatException("fuzz")
                        : text.Length;
                }
            """);
    }

    private static string BuildExceptionCaughtInternalThrow(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    try
                    {
                        throw new InvalidOperationException("fuzz");
                    }
                    catch (InvalidOperationException)
                    {
                        return 1;
                    }
                }
            """);
    }

    private static string BuildExceptionDeadBranchThrow(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    if (false)
                    {
                        throw new InvalidOperationException("fuzz");
                    }

                    return 1;
                }
            """);
    }

    private static string BuildExceptionGuardedSafeDivideByZeroExcluded(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int divisor)
                {
                    if (divisor != 0)
                    {
                        return 10 / divisor;
                    }

                    return 1;
                }
            """);
    }

    private static string BuildExceptionGuardedNullDereferenceExcluded(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text)
                {
                    if (text == null)
                    {
                        return 0;
                    }

                    return text.Length;
                }
            """);
    }

    private static string BuildExceptionDefiniteDivideByZero(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    var zero = 0;
                    return 10 / zero;
                }
            """);
    }

    private static string BuildExceptionDefiniteNullReference(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    string text = null;
                    return text.Length;
                }
            """);
    }

    private static string BuildExceptionUsingDisposeThrows(int index, Random random, string className)
    {
        return BuildClass(
            className,
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
            """);
    }

    private static string BuildExceptionInvokedLocalFunctionThrow(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    int Local()
                    {
                        throw new InvalidOperationException("fuzz");
                    }

                    return Local();
                }
            """);
    }

    private static string BuildExceptionInvokedLambdaThrow(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    Func<int> local = () => throw new FormatException("fuzz");
                    return local();
                }
            """);
    }

    private static string BuildImpureFieldWrite(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                private int _value;

                [EnforcePure]
                public void TestMethod(int value)
                {
                    _value = value;
                }
            """);
    }

    private static string BuildImpureAmbientDateTime(int index, Random random, string className)
    {
        return BuildClass(
            className,
            BuildIntMethodFromExpression("DateTime.Now.Day", random));
    }

    private static string BuildImpureAwaitTaskDelay(int index, Random random, string className)
    {
        return $$"""
                 using System;
                 using System.Threading.Tasks;
                 using SharpProof.Attributes;

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

    private static string BuildImpureLockSection(int index, Random random, string className)
    {
        return BuildClass(
            className,
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
            """);
    }

    private static string BuildImpureUsingStandardOutput(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    using var stream = Console.OpenStandardOutput();
                    return 1;
                }
            """);
    }

    private static string BuildImpureTryCatch(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
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
                }
            """);
    }

    private static string BuildPureConditionalAccessCoalesce(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text, string fallback)
                {
                    return text?.Trim().Length ?? fallback.Length;
                }
            """);
    }

    private static string BuildPureIsTypeCheck(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(object value)
                {
                    return value is string ? 1 : 0;
                }
            """);
    }

    private static string BuildPureNegatedPattern(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(object value)
                {
                    return value is not int ? 1 : 0;
                }
            """);
    }

    private static string BuildPureSwitchStatement(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int value)
                {
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
                }
            """);
    }

    private static string BuildImpureUsingStatement(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    using (var writer = new System.IO.StringWriter())
                    {
                        return 1;
                    }
                }
            """);
    }

    private static string BuildPureCompoundAssignment(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    var value = x;
                    value += 2;
                    return value;
                }
            """);
    }

    private static string BuildPureCoalesceAssignment(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public string TestMethod(string value)
                {
                    value ??= "fallback";
                    return value;
                }
            """);
    }

    private static string BuildPureDeconstructionAssignment(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x, int y)
                {
                    var left = x;
                    var right = y;
                    (left, right) = (right, left);
                    return left - right;
                }
            """);
    }

    private static string BuildPureIncrement(int index, Random random, string className)
    {
        return BuildPureUnaryMutation(className, "++");
    }

    private static string BuildPureDecrement(int index, Random random, string className)
    {
        return BuildPureUnaryMutation(className, "--");
    }

    private static string BuildPureUnaryMutation(string className, string operatorToken)
    {
        return BuildClass(
            className,
            $$"""
                [EnforcePure]
                public int TestMethod(int x)
                {
                    var value = x;
                    value{{operatorToken}};
                    return value;
                }
            """);
    }

    private static string BuildImpureDeclarationExpression(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text)
                {
                    return int.TryParse(text, out var value) ? value : 0;
                }
            """);
    }

    private static string BuildPureDeclarationPattern(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(object value)
                {
                    return value is int number ? number : 0;
                }
            """);
    }

    private static string BuildImpureTypeParameterObjectCreation(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public T TestMethod<T>() where T : new()
                {
                    return new T();
                }
            """);
    }

    private static string BuildImpureEventAssignment(int index, Random random, string className)
    {
        return $$"""
                 using System;
                 using SharpProof.Attributes;

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

    private static string BuildPureAnonymousObjectCreation(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    var item = new { Value = x, Next = x + 1 };
                    return item.Value + item.Next;
                }
            """);
    }

    private static string BuildPureDefaultValue(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    return default(int);
                }
            """);
    }

    private static string BuildPureSizeOf(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    return sizeof(int);
                }
            """);
    }

    private static string BuildPureTypeOf(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod()
                {
                    return typeof(int).Name.Length;
                }
            """);
    }

    private static string BuildPureNameOf(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int value)
                {
                    return nameof(value).Length;
                }
            """);
    }

    private static string BuildImpureDynamicIndexerAccess(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(dynamic values)
                {
                    return values[0];
                }
            """);
    }

    private static string BuildImpureDynamicObjectCreation(int index, Random random, string className)
    {
        return $$"""
                 using SharpProof.Attributes;

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

    private static string BuildPureTuple(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    var pair = (Left: x, Right: x + 1);
                    return pair.Left + pair.Right;
                }
            """);
    }

    private static string BuildImpureInterfaceGetter(int index, Random random, string className)
    {
        return $$"""
                 using SharpProof.Attributes;

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
                 using SharpProof.Attributes;

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

    private static string BuildPureSpreadCollectionExpression(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int[] values)
                {
                    int[] copy = [0, .. values, 9];
                    return copy.Length;
                }
            """);
    }

    private static string BuildPureSwitchExpression(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    return x switch
                    {
                        < 0 => -1,
                        0 => 0,
                        _ => 1
                    };
                }
            """);
    }

    private static string BuildPureRangeSlice(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(string text)
                {
                    return text[1..^1].Length;
                }
            """);
    }

    private static string BuildPureYieldReturn(int index, Random random, string className)
    {
        return $$"""
                 using System.Collections.Generic;
                 using SharpProof.Attributes;

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
                 using System;
                 using SharpProof.Attributes;

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

    private static string BuildPureAnonymousFunction(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
                    Func<int, int> project = static value => value + 1;
                    return project(x);
                }
            """);
    }

    private static string BuildPureDelegateCreation(int index, Random random, string className)
    {
        return BuildClass(
            className,
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
            """);
    }

    private static string BuildPureImplicitIndexerReference(int index, Random random, string className)
    {
        return $$"""
                 using System;
                 using SharpProof.Attributes;

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
                 using System;
                 using System.Runtime.CompilerServices;
                 using SharpProof.Attributes;

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
                 using SharpProof.Attributes;

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
                 using System.Runtime.CompilerServices;
                 using SharpProof.Attributes;

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
                 using SharpProof.Attributes;

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

    private static string BuildPureNestedLambdaLocalFunction(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x)
                {
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
                }
            """);
    }

    private static string BuildPureTuplePatternSwitch(int index, Random random, string className)
    {
        return BuildClass(
            className,
            """
                [EnforcePure]
                public int TestMethod(int x, int y)
                {
                    var pair = (x, y);
                    return pair switch
                    {
                        (> 0, > 0) => 1,
                        (0, _) => 0,
                        _ => -1
                    };
                }
            """);
    }

    private static string BuildImpureUsingAwaitDelegateFlow(int index, Random random, string className)
    {
        return $$"""
                 using System;
                 using System.Threading.Tasks;
                 using SharpProof.Attributes;

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
        return new Random(HashCode.Combine(_seed, index, 0x51ED270B));
    }

    private static FuzzExpectation ImpureWithExceptionExpectation()
    {
        return FuzzExpectation.Create(
            Sp0002ExpectationKind.MustEmit,
            Sp0010ExpectationKind.MustEmit);
    }

    private static FuzzExpectation ImpureWithoutExceptionExpectation()
    {
        return FuzzExpectation.Create(
            Sp0002ExpectationKind.MustEmit,
            Sp0010ExpectationKind.MustNotEmit);
    }

    private static FuzzExpectation ExceptionWithOptionalSp0002Expectation()
    {
        return FuzzExpectation.Create(
            Sp0002ExpectationKind.MayEmitConservatively,
            Sp0010ExpectationKind.MustEmit);
    }

    private static FuzzExpectation PureWithoutExceptionExpectation()
    {
        return FuzzExpectation.Create(
            Sp0002ExpectationKind.MustNotEmit,
            Sp0010ExpectationKind.MustNotEmit);
    }

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
                 using System;
                 using SharpProof.Attributes;

                 public class {{className}}
                 {
                 {{Indent(members, 4)}}
                 }
                 """;
    }

    private static string Indent(string text, int spaces)
    {
        var padding = new string(' ', spaces);
        return string.Join(
            Environment.NewLine,
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                .Select(line => line.Length == 0 ? line : padding + line));
    }
}
