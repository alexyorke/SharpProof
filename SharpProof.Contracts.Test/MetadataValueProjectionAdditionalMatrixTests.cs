using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class MetadataValueProjectionAdditionalMatrixTests
{
    private static readonly (SpecialType Type, int Width, bool Signed)[] IntegerDomains =
    [
        (SpecialType.System_SByte, 8, true),
        (SpecialType.System_Byte, 8, false),
        (SpecialType.System_Int16, 16, true),
        (SpecialType.System_UInt16, 16, false),
        (SpecialType.System_Int32, 32, true),
        (SpecialType.System_UInt32, 32, false),
        (SpecialType.System_Int64, 64, true),
        (SpecialType.System_UInt64, 64, false),
        (SpecialType.System_Char, 16, false)
    ];

    private static readonly long[][] IntegerBoundarySamples =
    [
        [-128L, -1L, 0L, 1L, 127L],
        [0L, 1L, 2L, 255L],
        [-32768L, -1L, 0L, 1L, 32767L],
        [0L, 1L, 2L, 65535L],
        [int.MinValue, -1L, 0L, 1L, int.MaxValue],
        [0L, 1L, 2L, uint.MaxValue],
        [long.MinValue, -1L, 0L, 1L, long.MaxValue],
        [0L, 1L, 2L, long.MaxValue],
        [0L, 1L, 2L, char.MaxValue]
    ];

    private static readonly int[] LongBoundParameterOrdinals = [0, 1, 2, 3, 4];
    private static readonly int[] ReferenceParameterOrdinals = [0, 1, 2];
    private static readonly IrTypeKind[] ReferenceValueKinds =
        [IrTypeKind.String, IrTypeKind.Sequence, IrTypeKind.Reference];

    private const string MetadataLibrarySource = """
        using SharpProof.Attributes;

        public static class MetadataValueSource {
            public static void IntegerDomains(
                [Positive, InRange(-1L, 1L)] sbyte signed8,
                [Positive, InRange(-1L, 1L)] byte unsigned8,
                [Positive, InRange(-1L, 1L)] short signed16,
                [Positive, InRange(-1L, 1L)] ushort unsigned16,
                [Positive, InRange(-1L, 1L)] int signed32,
                [Positive, InRange(-1L, 1L)] uint unsigned32,
                [Positive, InRange(-1L, 1L)] long signed64,
                [Positive, InRange(-1L, 1L)] ulong unsigned64,
                [Positive, InRange(-1L, 1L)] char character) { }

            public static void LongBoundDomains(
                [InRange(long.MinValue, long.MaxValue)] sbyte signed8,
                [InRange(long.MinValue, long.MaxValue)] uint unsigned32,
                [InRange(long.MinValue, long.MaxValue)] ulong unsigned64,
                [InRange(long.MaxValue, long.MaxValue)] int aboveIntDomain,
                [InRange(long.MinValue, long.MinValue)] uint belowUIntDomain) { }

            public static void References(
                [NotNull] string text,
                [NotNull] int[] values,
                [NotNull] object instance) { }

            public static void Pair(
                [Positive] int validPrefix,
                [InRange(2L, 1L)] int invalidRange) { }

            public static void WrongIntegerGetter(
                [Positive, InRange(-1L, 1L)] int count,
                bool marker) { }

            public static void WrongReferenceGetter(
                [NotNull] string text,
                int marker) { }
        }
        """;

    [Test]
    public void AllNineIntegerDomainsMapFromActualMetadataAndMatchIndependentBoundaries()
    {
        var (compilation, target) = Import("IntegerDomains");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var selections = ContractSelectionInventory.ForCompilation(compilation);
        var sites = CreateSites(factory, context, target);
        var publicResult = new ContractBinder(compilation, factory).BindTotalMetadataRequires(context, CancellationToken.None);
        var projected = ClosedContractAttributeBinding.BindMetadataValueSites(factory, selections, sites, CancellationToken.None);

        Assert.That(target.Parameters.Select(static parameter => parameter.Type.SpecialType),
            Is.EqualTo(IntegerDomains.Select(static domain => domain.Type)));
        Assert.That(context.Parameters, Has.Length.EqualTo(IntegerDomains.Length));
        Assert.That(publicResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(projected.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(publicResult.Clauses, Has.Length.EqualTo(IntegerDomains.Length * 2));
        Assert.That(projected.Clauses, Has.Length.EqualTo(IntegerDomains.Length * 2));

        var interpreter = new IrInterpreter(factory);
        for (var parameterIndex = 0; parameterIndex < IntegerDomains.Length; parameterIndex++)
        {
            var domain = IntegerDomains[parameterIndex];
            var entry = context.Parameters[parameterIndex].Entry;
            var input = factory.Variable(entry);
            var info = factory.GetTypeInfo(input.Type);
            Assert.That(info.Kind, Is.EqualTo(IrTypeKind.Integer));
            Assert.That(info.Width, Is.EqualTo(domain.Width));
            Assert.That(info.Signed, Is.EqualTo(domain.Signed));
            Assert.That(projected.Clauses[parameterIndex * 2].ParameterOrdinal, Is.EqualTo(parameterIndex));
            Assert.That(projected.Clauses[parameterIndex * 2].Validation.Kind,
                Is.EqualTo(ClosedContractAttributeKind.Positive));
            Assert.That(projected.Clauses[parameterIndex * 2 + 1].ParameterOrdinal, Is.EqualTo(parameterIndex));
            Assert.That(projected.Clauses[parameterIndex * 2 + 1].Validation.Kind,
                Is.EqualTo(ClosedContractAttributeKind.InRange));

            foreach (var sample in IntegerBoundarySamples[parameterIndex])
            {
                var environment = new Dictionary<IrVarId, IrValue>
                {
                    [entry] = factory.CreateIntegerValue(input.Type, sample)
                };
                var expectedPositive = sample > 0;
                var expectedInRange = sample >= -1L && sample <= 1L;
                var positiveClause = projected.Clauses[parameterIndex * 2];
                var rangeClause = projected.Clauses[parameterIndex * 2 + 1];
                Assert.That(interpreter.Evaluate(positiveClause.Value, environment).Value!.Boolean,
                    Is.EqualTo(expectedPositive));
                Assert.That(interpreter.Evaluate(rangeClause.Value, environment).Value!.Boolean,
                    Is.EqualTo(expectedInRange));
                Assert.That(interpreter.Evaluate(positiveClause.SafeCondition, environment).Value!.Boolean,
                    Is.True);
                Assert.That(interpreter.Evaluate(rangeClause.SafeCondition, environment).Value!.Boolean,
                    Is.True);
                Assert.That(interpreter.Evaluate(publicResult.Clauses[parameterIndex * 2].Value, environment).Value!.Boolean,
                    Is.EqualTo(expectedPositive));
                Assert.That(interpreter.Evaluate(publicResult.Clauses[parameterIndex * 2 + 1].Value, environment).Value!.Boolean,
                    Is.EqualTo(expectedInRange));
            }
        }
    }

    [Test]
    public void OutOfDomainLongBoundsUseMathematicalRatherThanTruncatedComparisons()
    {
        var (compilation, target) = Import("LongBoundDomains");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var selections = ContractSelectionInventory.ForCompilation(compilation);
        var result = ClosedContractAttributeBinding.BindMetadataValueSites(
            factory, selections, CreateSites(factory, context, target), CancellationToken.None);

        Assert.That(result.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(result.Clauses, Has.Length.EqualTo(5));
        Assert.That(result.Clauses.Select(static clause => clause.ParameterOrdinal),
            Is.EqualTo(LongBoundParameterOrdinals));
        Assert.That(result.Clauses[0].Validation.Minimum, Is.EqualTo(long.MinValue));
        Assert.That(result.Clauses[0].Validation.Maximum, Is.EqualTo(long.MaxValue));
        Assert.That(result.Clauses[3].Validation.Minimum, Is.EqualTo(long.MaxValue));
        Assert.That(result.Clauses[4].Validation.Maximum, Is.EqualTo(long.MinValue));

        long[][] sampleValues =
        [
            [-128L, 0L, 127L],
            [0L, 1L, uint.MaxValue],
            [0L, 1L, long.MaxValue],
            [0L, int.MaxValue],
            [0L, uint.MaxValue]
        ];
        bool[][] expectedValues =
        [
            [true, true, true],
            [true, true, true],
            [true, true, true],
            [false, false],
            [false, false]
        ];
        var interpreter = new IrInterpreter(factory);
        for (var parameterIndex = 0; parameterIndex < result.Clauses.Length; parameterIndex++)
        {
            var input = factory.Variable(context.Parameters[parameterIndex].Entry);
            for (var sampleIndex = 0; sampleIndex < sampleValues[parameterIndex].Length; sampleIndex++)
            {
                var environment = new Dictionary<IrVarId, IrValue>
                {
                    [context.Parameters[parameterIndex].Entry] = factory.CreateIntegerValue(
                        input.Type, sampleValues[parameterIndex][sampleIndex])
                };
                Assert.That(interpreter.Evaluate(result.Clauses[parameterIndex].Value, environment).Value!.Boolean,
                    Is.EqualTo(expectedValues[parameterIndex][sampleIndex]));
            }
        }

        var ulongEntry = context.Parameters[2].Entry;
        var ulongType = factory.Variable(ulongEntry).Type;
        var maximumUlong = interpreter.Evaluate(
            factory.Integer(ulongType, ulong.MaxValue), new Dictionary<IrVarId, IrValue>()).Value!;
        var highUnsignedEnvironment = new Dictionary<IrVarId, IrValue>
        {
            [ulongEntry] = maximumUlong
        };
        Assert.That(interpreter.Evaluate(result.Clauses[2].Value, highUnsignedEnvironment).Value!.Boolean,
            Is.False);
    }

    [Test]
    public void NotNullProjectsAndEvaluatesOwnedStringArrayAndObjectInputs()
    {
        var (compilation, target) = Import("References");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var selections = ContractSelectionInventory.ForCompilation(compilation);
        var publicResult = new ContractBinder(compilation, factory).BindTotalMetadataRequires(context, CancellationToken.None);
        var projected = ClosedContractAttributeBinding.BindMetadataValueSites(
            factory, selections, CreateSites(factory, context, target), CancellationToken.None);
        Assert.That(publicResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(projected.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(projected.Clauses, Has.Length.EqualTo(3));
        Assert.That(projected.Clauses.Select(static clause => clause.ParameterOrdinal),
            Is.EqualTo(ReferenceParameterOrdinals));

        var interpreter = new IrInterpreter(factory);
        for (var parameterIndex = 0; parameterIndex < ReferenceValueKinds.Length; parameterIndex++)
        {
            var entry = context.Parameters[parameterIndex].Entry;
            var input = factory.Variable(entry);
            var info = factory.GetTypeInfo(input.Type);
            Assert.That(info.Kind, Is.EqualTo(ReferenceValueKinds[parameterIndex]));
            IrValue nonNullValue = parameterIndex switch
            {
                0 => factory.CreateStringValue("value"),
                1 => factory.CreateSequenceValue(input.Type,
                    [factory.CreateIntegerValue(info.ElementType!.Value, 1L)]),
                2 => factory.CreateReferenceValue(input.Type, new object()),
                _ => throw new ArgumentOutOfRangeException(nameof(parameterIndex))
            };
            var clause = projected.Clauses[parameterIndex];
            var nullEnvironment = new Dictionary<IrVarId, IrValue>
            {
                [entry] = factory.CreateNullValue(input.Type)
            };
            var nonNullEnvironment = new Dictionary<IrVarId, IrValue>
            {
                [entry] = nonNullValue
            };
            Assert.That(interpreter.Evaluate(clause.Value, nullEnvironment).Value!.Boolean, Is.False);
            Assert.That(interpreter.Evaluate(clause.Value, nonNullEnvironment).Value!.Boolean, Is.True);
            Assert.That(interpreter.Evaluate(clause.SafeCondition, nullEnvironment).Value!.Boolean, Is.True);
        }
    }

    [Test]
    public void InvalidDeclaredBoundsRejectTheWholeActualSiteBatch()
    {
        var (compilation, target) = Import("Pair");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var selections = ContractSelectionInventory.ForCompilation(compilation);
        var sites = CreateSites(factory, context, target);
        var validPrefix = ClosedContractAttributeBinding.BindMetadataValueSites(
            factory, selections, ImmutableArray.Create(sites[0]), CancellationToken.None);
        Assert.That(validPrefix.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(validPrefix.Clauses, Has.Length.EqualTo(1));

        var projected = ClosedContractAttributeBinding.BindMetadataValueSites(factory, selections, sites, CancellationToken.None);
        Assert.That(projected.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
        Assert.That(projected.Clauses, Is.Empty);
        var publicResult = new ContractBinder(compilation, factory).BindTotalMetadataRequires(context, CancellationToken.None);
        Assert.That(publicResult.Failure, Is.EqualTo(ContractBindingFailure.InvalidClosedAttribute));
        Assert.That(publicResult.Clauses, Is.Empty);
    }

    [Test]
    public void LegalWrongDomainValuesRejectIntegerAndNotNullAnnotations()
    {
        var (integerCompilation, integerTarget) = Import("WrongIntegerGetter");
        var integerFactory = new IrFactory(IrExecutionSemantics.Total);
        var integerContext = new TotalLoweringContext(integerFactory, integerTarget);
        var integerSelections = ContractSelectionInventory.ForCompilation(integerCompilation);
        var integerSites = CreateSites(integerFactory, integerContext, integerTarget);
        var integerPublic = new ContractBinder(integerCompilation, integerFactory)
            .BindTotalMetadataRequires(integerContext, CancellationToken.None);
        Assert.That(integerPublic.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(integerPublic.Clauses, Has.Length.EqualTo(2));
        var boolBackedSites = integerSites.SetItem(0, new ContractMetadataValueSite(
            integerTarget.Parameters[0],
            () => integerFactory.Variable(integerContext.Parameters[1].Entry)));
        var wrongInteger = ClosedContractAttributeBinding.BindMetadataValueSites(
            integerFactory, integerSelections, boolBackedSites, CancellationToken.None);
        Assert.That(integerFactory.GetTypeInfo(
            integerFactory.Variable(integerContext.Parameters[1].Entry).Type).Kind,
            Is.EqualTo(IrTypeKind.Boolean));
        Assert.That(wrongInteger.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(wrongInteger.Clauses, Is.Empty);

        var (referenceCompilation, referenceTarget) = Import("WrongReferenceGetter");
        var referenceFactory = new IrFactory(IrExecutionSemantics.Total);
        var referenceContext = new TotalLoweringContext(referenceFactory, referenceTarget);
        var referenceSelections = ContractSelectionInventory.ForCompilation(referenceCompilation);
        var referenceSites = CreateSites(referenceFactory, referenceContext, referenceTarget);
        var referencePublic = new ContractBinder(referenceCompilation, referenceFactory)
            .BindTotalMetadataRequires(referenceContext, CancellationToken.None);
        Assert.That(referencePublic.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(referencePublic.Clauses, Has.Length.EqualTo(1));
        var integerBackedSites = referenceSites.SetItem(0, new ContractMetadataValueSite(
            referenceTarget.Parameters[0],
            () => referenceFactory.Variable(referenceContext.Parameters[1].Entry)));
        var wrongReference = ClosedContractAttributeBinding.BindMetadataValueSites(
            referenceFactory, referenceSelections, integerBackedSites, CancellationToken.None);
        Assert.That(referenceFactory.GetTypeInfo(
            referenceFactory.Variable(referenceContext.Parameters[1].Entry).Type).Kind,
            Is.EqualTo(IrTypeKind.Integer));
        Assert.That(wrongReference.Failure, Is.EqualTo(ContractBindingFailure.UnsupportedExpression));
        Assert.That(wrongReference.Clauses, Is.Empty);
    }

    [Test]
    public void RequiredMetadataProjectionArgumentsRejectNull()
    {
        var (compilation, target) = Import("IntegerDomains");
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var selections = ContractSelectionInventory.ForCompilation(compilation);
        var sites = CreateSites(factory, context, target);

        Assert.Throws<ArgumentNullException>(new Action(() =>
        {
            _ = ClosedContractAttributeBinding.BindMetadataValueSites(null!, selections, sites, CancellationToken.None);
        }));
        Assert.Throws<ArgumentNullException>(new Action(() =>
        {
            _ = ClosedContractAttributeBinding.BindMetadataValueSites(factory, null!, sites, CancellationToken.None);
        }));
        Assert.Throws<ArgumentNullException>(new Action(() =>
        {
            _ = ClosedContractAttributeBinding.BindMetadataValueSites(factory, selections, null!, CancellationToken.None);
        }));
        Assert.Throws<ArgumentNullException>(new Action(() =>
        {
            _ = new ContractMetadataValueSite(null!,
                () => factory.Variable(context.Parameters[0].Entry));
        }));
        Assert.Throws<ArgumentNullException>(new Action(() =>
        {
            _ = new ContractMetadataValueSite(target.Parameters[0], null!);
        }));
    }

    private static ImmutableArray<ContractMetadataValueSite> CreateSites(
        IrFactory factory,
        TotalLoweringContext context,
        IMethodSymbol target)
    {
        return [.. target.Parameters.Select(parameter => new ContractMetadataValueSite(
            parameter, () => factory.Variable(context.Parameters[parameter.Ordinal].Entry)))];
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Target) Import(string methodName)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        var library = TestCompilation.Create("MetadataValueProjectionMatrixLibrary", MetadataLibrarySource);
        TestCompilation.AssertNoErrors(library);
        using var image = new MemoryStream();
        var emittedLibrary = library.Emit(image);
        Assert.That(emittedLibrary.Success, Is.True,
            string.Join(Environment.NewLine, emittedLibrary.Diagnostics));

        var compilation = TestCompilation.Create(
                "MetadataValueProjectionMatrixConsumer", "public static class Consumer {}")
            .AddReferences(MetadataReference.CreateFromImage(image.ToArray()));
        TestCompilation.AssertNoErrors(compilation);
        using var consumerImage = new MemoryStream();
        var emittedConsumer = compilation.Emit(consumerImage);
        Assert.That(emittedConsumer.Success, Is.True,
            string.Join(Environment.NewLine, emittedConsumer.Diagnostics));

        var target = compilation.GetTypeByMetadataName("MetadataValueSource")!
            .GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();
        Assert.That(target.DeclaringSyntaxReferences, Is.Empty);
        return (compilation, target);
    }
}
