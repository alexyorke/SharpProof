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

    public static ImmutableArray<ShapeRegistryEntry> RegistryEntries { get; } = ImmutableArray.Create(
        new ShapeRegistryEntry(
            "PureArithmetic",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Binary"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureArithmetic),
        new ShapeRegistryEntry(
            "PureStringConcat",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Binary"),
            ImmutableArray.Create("AddExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureStringConcat),
        new ShapeRegistryEntry(
            "PureListPattern",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.ListPattern)),
            ImmutableArray.Create("ListPattern"),
            ImmutableArray.Create("ListPattern"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureListPattern),
        new ShapeRegistryEntry(
            "PureCollectionExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.CollectionExpression)),
            ImmutableArray.Create("CollectionExpression"),
            ImmutableArray.Create("CollectionExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureCollectionExpression),
        new ShapeRegistryEntry(
            "PureInterpolatedString",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedString)),
            ImmutableArray.Create("InterpolatedString"),
            ImmutableArray.Create("InterpolatedStringExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureInterpolatedString),
        new ShapeRegistryEntry(
            "PureUtf8String",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Utf8String)),
            ImmutableArray.Create("Utf8String"),
            ImmutableArray.Create("Utf8StringLiteralExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureUtf8String),
        new ShapeRegistryEntry(
            "PureArrayCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.ArrayCreation)),
            ImmutableArray.Create("ArrayCreation"),
            ImmutableArray.Create("ArrayCreationExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureArrayCreation),
        new ShapeRegistryEntry(
            "PureNestedOwnershipChain",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("PropertyReference", "SimpleAssignment", "ObjectCreation"),
            ImmutableArray.Create("SimpleMemberAccessExpression", "SimpleAssignmentExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureNestedOwnershipChain),
        new ShapeRegistryEntry(
            "ImpureOwnershipEscapeChain",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.ObjectCreation)),
            ImmutableArray.Create("ObjectCreation", "PropertyReference", "Return"),
            ImmutableArray.Create("ObjectCreationExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            true,
            BuildImpureOwnershipEscapeChain),
        new ShapeRegistryEntry(
            "ImpureConsoleWrite",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Invocation)),
            ImmutableArray.Create("Invocation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            true,
            BuildImpureConsoleWrite),
        new ShapeRegistryEntry(
            "ImpureDynamicDispatch",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DynamicInvocation)),
            ImmutableArray.Create("DynamicInvocation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureDynamicDispatch),
        new ShapeRegistryEntry(
            "ImpureDelegateInvoke",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Invocation)),
            ImmutableArray.Create("Invocation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureDelegateInvoke),
        new ShapeRegistryEntry(
            "ImpureThrowExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowStatement"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureThrowExpression),
        new ShapeRegistryEntry(
            "ExceptionDirectThrowInvalidOperation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowStatement"),
            ImpureWithExceptionExpectation(),
            false,
            false,
            BuildExceptionDirectThrowInvalidOperation),
        new ShapeRegistryEntry(
            "ExceptionGuardedThrowArgumentNull",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowStatement"),
            ImpureWithExceptionExpectation(),
            false,
            false,
            BuildExceptionGuardedThrowArgumentNull),
        new ShapeRegistryEntry(
            "ExceptionThrowExpressionFormatException",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Throw"),
            ImmutableArray.Create("ThrowExpression"),
            ImpureWithExceptionExpectation(),
            false,
            false,
            BuildExceptionThrowExpressionFormatException),
        new ShapeRegistryEntry(
            "ExceptionCaughtInternalThrow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.Try),
                RoslynShapeManifest.OperationShapeId(OperationKind.CatchClause)),
            ImmutableArray.Create("Try", "CatchClause", "Throw"),
            ImmutableArray.Create("TryStatement", "CatchClause", "ThrowStatement"),
            ImpureWithoutExceptionExpectation(),
            false,
            false,
            BuildExceptionCaughtInternalThrow),
        new ShapeRegistryEntry(
            "ExceptionDeadBranchThrow",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("Conditional", "Throw"),
            ImmutableArray.Create("IfStatement", "ThrowStatement"),
            PureWithoutExceptionExpectation(),
            false,
            false,
            BuildExceptionDeadBranchThrow),
        new ShapeRegistryEntry(
            "ExceptionGuardedSafeDivideByZeroExcluded",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Conditional", "Binary"),
            ImmutableArray.Create("IfStatement", "DivideExpression"),
            PureWithoutExceptionExpectation(),
            false,
            false,
            BuildExceptionGuardedSafeDivideByZeroExcluded),
        new ShapeRegistryEntry(
            "ExceptionGuardedNullDereferenceExcluded",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("Conditional", "PropertyReference"),
            ImmutableArray.Create("IfStatement"),
            PureWithoutExceptionExpectation(),
            false,
            false,
            BuildExceptionGuardedNullDereferenceExcluded),
        new ShapeRegistryEntry(
            "ExceptionDefiniteDivideByZero",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Binary)),
            ImmutableArray.Create("Binary"),
            ImmutableArray.Create("DivideExpression"),
            ExceptionWithOptionalSp0002Expectation(),
            false,
            false,
            BuildExceptionDefiniteDivideByZero),
        new ShapeRegistryEntry(
            "ExceptionDefiniteNullReference",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("PropertyReference"),
            ImmutableArray.Create("SimpleMemberAccessExpression"),
            ExceptionWithOptionalSp0002Expectation(),
            false,
            false,
            BuildExceptionDefiniteNullReference),
        new ShapeRegistryEntry(
            "ExceptionUsingDisposeThrows",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.UsingDeclaration)),
            ImmutableArray.Create("UsingDeclaration"),
            ImmutableArray.Create("LocalDeclarationStatement"),
            ExceptionWithOptionalSp0002Expectation(),
            false,
            false,
            BuildExceptionUsingDisposeThrows),
        new ShapeRegistryEntry(
            "ExceptionInvokedLocalFunctionThrow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.LocalFunction),
                RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("LocalFunction", "Throw"),
            ImmutableArray.Create("LocalFunctionStatement", "ThrowStatement"),
            ExceptionWithOptionalSp0002Expectation().RequireExceptionEdgesOnAnySp0010(),
            false,
            false,
            BuildExceptionInvokedLocalFunctionThrow),
        new ShapeRegistryEntry(
            "ExceptionInvokedLambdaThrow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction),
                RoslynShapeManifest.OperationShapeId(OperationKind.Throw)),
            ImmutableArray.Create("AnonymousFunction", "Throw"),
            ImmutableArray.Create("ParenthesizedLambdaExpression", "ThrowExpression"),
            ExceptionWithOptionalSp0002Expectation().RequireExceptionEdgesOnAnySp0010(),
            false,
            false,
            BuildExceptionInvokedLambdaThrow),
        new ShapeRegistryEntry(
            "ImpureFieldWrite",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.SimpleAssignment),
                RoslynShapeManifest.OperationShapeId(OperationKind.FieldReference)),
            ImmutableArray.Create("SimpleAssignment", "FieldReference"),
            ImmutableArray.Create("SimpleAssignmentExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureFieldWrite),
        new ShapeRegistryEntry(
            "ImpureAmbientDateTime",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("PropertyReference"),
            ImmutableArray.Create("SimpleMemberAccessExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureAmbientDateTime),
        new ShapeRegistryEntry(
            "ImpureAwaitTaskDelay",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Await)),
            ImmutableArray.Create("Await"),
            ImmutableArray.Create("AwaitExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureAwaitTaskDelay),
        new ShapeRegistryEntry(
            "ImpureLockSection",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Lock)),
            ImmutableArray.Create("Lock"),
            ImmutableArray.Create("LockStatement"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureLockSection),
        new ShapeRegistryEntry(
            "ImpureUsingStandardOutput",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.UsingDeclaration)),
            ImmutableArray.Create("UsingDeclaration"),
            ImmutableArray.Create("LocalDeclarationStatement"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureUsingStandardOutput),
        new ShapeRegistryEntry(
            "ImpureTryCatch",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.Try),
                RoslynShapeManifest.OperationShapeId(OperationKind.CatchClause)),
            ImmutableArray.Create("Try", "CatchClause"),
            ImmutableArray.Create("TryStatement", "CatchClause"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureTryCatch),
        new ShapeRegistryEntry(
            "PureConditionalAccessCoalesce",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.ConditionalAccess),
                RoslynShapeManifest.OperationShapeId(OperationKind.Coalesce)),
            ImmutableArray.Create("ConditionalAccess", "Coalesce"),
            ImmutableArray.Create("ConditionalAccessExpression", "CoalesceExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureConditionalAccessCoalesce),
        new ShapeRegistryEntry(
            "PureIsTypeCheck",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.IsType)),
            ImmutableArray.Create("IsType"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureIsTypeCheck),
        new ShapeRegistryEntry(
            "PureNegatedPattern",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.NegatedPattern)),
            ImmutableArray.Create("NegatedPattern"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureNegatedPattern),
        new ShapeRegistryEntry(
            "PureSwitchStatement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Switch)),
            ImmutableArray.Create("Switch", "SwitchCase"),
            ImmutableArray.Create("SwitchStatement"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureSwitchStatement),
        new ShapeRegistryEntry(
            "ImpureUsingStatement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Using)),
            ImmutableArray.Create("Using"),
            ImmutableArray.Create("UsingStatement"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureUsingStatement),
        new ShapeRegistryEntry(
            "PureCompoundAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.CompoundAssignment)),
            ImmutableArray.Create("CompoundAssignment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureCompoundAssignment),
        new ShapeRegistryEntry(
            "PureCoalesceAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.CoalesceAssignment)),
            ImmutableArray.Create("CoalesceAssignment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureCoalesceAssignment),
        new ShapeRegistryEntry(
            "PureDeconstructionAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DeconstructionAssignment)),
            ImmutableArray.Create("DeconstructionAssignment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureDeconstructionAssignment),
        new ShapeRegistryEntry(
            "PureIncrement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Increment)),
            ImmutableArray.Create("Increment"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureIncrement),
        new ShapeRegistryEntry(
            "PureDecrement",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Decrement)),
            ImmutableArray.Create("Decrement"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureDecrement),
        new ShapeRegistryEntry(
            "ImpureDeclarationExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DeclarationExpression)),
            ImmutableArray.Create("DeclarationExpression"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            true,
            BuildImpureDeclarationExpression),
        new ShapeRegistryEntry(
            "PureDeclarationPattern",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DeclarationPattern)),
            ImmutableArray.Create("DeclarationPattern"),
            ImmutableArray.Create("DeclarationPattern"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureDeclarationPattern),
        new ShapeRegistryEntry(
            "ImpureTypeParameterObjectCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.TypeParameterObjectCreation)),
            ImmutableArray.Create("TypeParameterObjectCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            true,
            BuildImpureTypeParameterObjectCreation),
        new ShapeRegistryEntry(
            "ImpureEventAssignment",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.EventAssignment)),
            ImmutableArray.Create("EventAssignment", "EventReference"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureEventAssignment),
        new ShapeRegistryEntry(
            "PureAnonymousObjectCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousObjectCreation)),
            ImmutableArray.Create("AnonymousObjectCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureAnonymousObjectCreation),
        new ShapeRegistryEntry(
            "PureDefaultValue",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DefaultValue)),
            ImmutableArray.Create("DefaultValue"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureDefaultValue),
        new ShapeRegistryEntry(
            "PureSizeOf",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.SizeOf)),
            ImmutableArray.Create("SizeOf"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureSizeOf),
        new ShapeRegistryEntry(
            "PureTypeOf",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.TypeOf)),
            ImmutableArray.Create("TypeOf"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureTypeOf),
        new ShapeRegistryEntry(
            "PureNameOf",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.NameOf)),
            ImmutableArray.Create("NameOf"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureNameOf),
        new ShapeRegistryEntry(
            "ImpureDynamicIndexerAccess",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DynamicIndexerAccess)),
            ImmutableArray.Create("DynamicIndexerAccess"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureDynamicIndexerAccess),
        new ShapeRegistryEntry(
            "ImpureDynamicObjectCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DynamicObjectCreation)),
            ImmutableArray.Create("DynamicObjectCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureDynamicObjectCreation),
        new ShapeRegistryEntry(
            "PureTuple",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Tuple)),
            ImmutableArray.Create("Tuple"),
            ImmutableArray.Create("TupleExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            true,
            BuildPureTuple),
        new ShapeRegistryEntry(
            "ImpureInterfaceGetter",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.PropertyReference)),
            ImmutableArray.Create("PropertyReference"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureInterfaceGetter),
        new ShapeRegistryEntry(
            "PureRecursivePattern",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.RecursivePattern)),
            ImmutableArray.Create("RecursivePattern"),
            ImmutableArray.Create("RecursivePattern"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureRecursivePattern),
        new ShapeRegistryEntry(
            "PureSpreadCollectionExpression",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.CollectionExpression),
                RoslynShapeManifest.OperationShapeId(OperationKind.Spread)),
            ImmutableArray.Create("CollectionExpression", "Spread"),
            ImmutableArray.Create("CollectionExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureSpreadCollectionExpression),
        new ShapeRegistryEntry(
            "PureSwitchExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.SwitchExpression)),
            ImmutableArray.Create("SwitchExpression"),
            ImmutableArray.Create("SwitchExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureSwitchExpression),
        new ShapeRegistryEntry(
            "PureRangeSlice",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.Range)),
            ImmutableArray.Create("Range"),
            ImmutableArray.Create("RangeExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureRangeSlice),
        new ShapeRegistryEntry(
            "PureYieldReturn",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.YieldReturn)),
            ImmutableArray.Create("YieldReturn"),
            ImmutableArray.Create("YieldReturnStatement"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureYieldReturn),
        new ShapeRegistryEntry(
            "ImpureWithExpression",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.With)),
            ImmutableArray.Create("With"),
            ImmutableArray.Create("WithExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureWithExpression),
        new ShapeRegistryEntry(
            "PureAnonymousFunction",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction)),
            ImmutableArray.Create("AnonymousFunction"),
            ImmutableArray.Create("SimpleLambdaExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureAnonymousFunction),
        new ShapeRegistryEntry(
            "PureDelegateCreation",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.DelegateCreation)),
            ImmutableArray.Create("DelegateCreation"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureDelegateCreation),
        new ShapeRegistryEntry(
            "PureImplicitIndexerReference",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.ImplicitIndexerReference)),
            ImmutableArray.Create("ImplicitIndexerReference"),
            ImmutableArray.Create("ElementAccessExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureImplicitIndexerReference),
        new ShapeRegistryEntry(
            "PureInterpolatedStringHandler",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringHandlerCreation),
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringAddition),
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringAppendLiteral),
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringAppendFormatted),
                RoslynShapeManifest.OperationShapeId(OperationKind.InterpolatedStringHandlerArgumentPlaceholder)),
            ImmutableArray.Create("InterpolatedStringHandlerCreation", "InterpolatedStringAddition",
                "InterpolatedStringAppendLiteral", "InterpolatedStringAppendFormatted",
                "InterpolatedStringHandlerArgumentPlaceholder"),
            ImmutableArray.Create("InterpolatedStringExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureInterpolatedStringHandler),
        new ShapeRegistryEntry(
            "ImpureAddressOf",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.AddressOf)),
            ImmutableArray.Create("AddressOf"),
            ImmutableArray<string>.Empty,
            FuzzExpectation.DefinitelyImpure(),
            true,
            false,
            BuildImpureAddressOf),
        new ShapeRegistryEntry(
            "PureInlineArrayAccess",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.InlineArrayAccess)),
            ImmutableArray.Create("InlineArrayAccess"),
            ImmutableArray.Create("ElementAccessExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureInlineArrayAccess),
        new ShapeRegistryEntry(
            "ImpureFunctionPointer",
            ImmutableArray.Create(RoslynShapeManifest.OperationShapeId(OperationKind.FunctionPointerInvocation)),
            ImmutableArray.Create("FunctionPointerInvocation"),
            ImmutableArray.Create("FunctionPointerType"),
            FuzzExpectation.DefinitelyImpure(),
            true,
            false,
            BuildImpureFunctionPointer),
        new ShapeRegistryEntry(
            "PureNestedLambdaLocalFunction",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction),
                RoslynShapeManifest.OperationShapeId(OperationKind.LocalFunction)),
            ImmutableArray.Create("AnonymousFunction", "LocalFunction"),
            ImmutableArray.Create("SimpleLambdaExpression", "LocalFunctionStatement"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureNestedLambdaLocalFunction),
        new ShapeRegistryEntry(
            "PureTuplePatternSwitch",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.Tuple),
                RoslynShapeManifest.OperationShapeId(OperationKind.SwitchExpression)),
            ImmutableArray.Create("Tuple", "SwitchExpression"),
            ImmutableArray.Create("TupleExpression", "SwitchExpression"),
            FuzzExpectation.DefinitelyPure(),
            false,
            false,
            BuildPureTuplePatternSwitch),
        new ShapeRegistryEntry(
            "ImpureUsingAwaitDelegateFlow",
            ImmutableArray.Create(
                RoslynShapeManifest.OperationShapeId(OperationKind.UsingDeclaration),
                RoslynShapeManifest.OperationShapeId(OperationKind.Await),
                RoslynShapeManifest.OperationShapeId(OperationKind.AnonymousFunction)),
            ImmutableArray.Create("UsingDeclaration", "Await", "AnonymousFunction"),
            ImmutableArray.Create("LocalDeclarationStatement", "AwaitExpression", "ParenthesizedLambdaExpression"),
            FuzzExpectation.DefinitelyImpure(),
            false,
            false,
            BuildImpureUsingAwaitDelegateFlow));

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
