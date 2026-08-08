using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Summaries.Test;

[TestFixture]
public sealed class IrRelationalSummaryTests
{
    [Test]
    public void ProvenanceRequiresPackIdentityOnlyForSpecificationPacks()
    {
        var digest = new string('a', 64);

        Assert.Throws<ArgumentException>((Action)(() =>
            _ = new IrSummaryProvenance(
                IrSummaryOrigin.SpecificationPack,
                digest)));
        Assert.Throws<ArgumentException>((Action)(() =>
            _ = new IrSummaryProvenance(
                IrSummaryOrigin.Source,
                digest,
                "source-name")));

        var pack = new IrSummaryProvenance(
            IrSummaryOrigin.SpecificationPack,
            digest,
            "dotnet.scalar@1");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pack.Origin, Is.EqualTo(IrSummaryOrigin.SpecificationPack));
            Assert.That(pack.EvidenceSha256, Is.EqualTo(digest));
            Assert.That(pack.EvidenceIdentity, Is.EqualTo("dotnet.scalar@1"));
        }
    }

    [Test]
    public void PublicSummaryGuardsRejectMalformedInputs()
    {
        var fixture = new SummaryFixture("Guards");
        var digest = new string('a', 64);

        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = new IrSummaryProvenance(
                (IrSummaryOrigin)int.MaxValue,
                digest)));
        foreach (var invalidDigest in new[]
                 {
                     "",
                     new string('a', 63),
                     new string('A', 64),
                     new string('g', 64)
                 })
        {
            Assert.Throws<ArgumentException>((Action)(() =>
                _ = new IrSummaryProvenance(
                    IrSummaryOrigin.Source,
                    invalidDigest)));
        }

        Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = new IrSummarySignature(
                fixture.Member,
                receiver: null,
                parameters: null!,
                fixture.Result,
                Provenance('b'))));
        Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = new IrSummarySignature(
                fixture.Member,
                receiver: null,
                [fixture.Parameter],
                fixture.Result,
                provenance: null!)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = new IrRelationalSummaryBuildLimits(maximumBlocks: 0)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = new IrRelationalSummaryBuildLimits(maximumInstructions: 0)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = new IrRelationalSummaryBuildLimits(
                maximumExpressionDepth: 0)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = new IrRelationalSummaryBuildLimits(
                maximumSymbolicOperations: 0)));

        var summary = BuildIdentitySummary(fixture);
        Assert.Throws<ArgumentNullException>((Action)(() =>
            IrRelationalSummaryInstantiator.Instantiate(
                null!,
                receiver: null,
                [],
                0)));
        Assert.Throws<ArgumentNullException>((Action)(() =>
            IrRelationalSummaryInstantiator.Instantiate(
                summary,
                receiver: null,
                arguments: null!,
                0)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            IrRelationalSummaryInstantiator.Instantiate(
                summary,
                receiver: null,
                [fixture.Factory.Integer(1)],
                -1)));
        Assert.Throws<ArgumentException>((Action)(() =>
            IrRelationalSummaryInstantiator.Instantiate(
                summary,
                receiver: fixture.Factory.Integer(1),
                [fixture.Factory.Integer(1)],
                0)));
        Assert.Throws<ArgumentException>((Action)(() =>
            IrRelationalSummaryInstantiator.Instantiate(
                summary,
                receiver: null,
                [],
                0)));
        Assert.Throws<ArgumentException>((Action)(() =>
            IrRelationalSummaryInstantiator.Instantiate(
                summary,
                receiver: null,
                [fixture.Factory.Boolean(true)],
                0)));
    }

    [Test]
    public void BuilderRejectsInvalidSignatureAndEnvironmentShapes()
    {
        var fixture = new SummaryFixture("InvalidShapes");
        var bodyParameter = fixture.Factory.CreateVariable(
            "body:value",
            fixture.Factory.IntegerType);
        var builder = new IrProgramBuilder(fixture.Factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(
            entry,
            fixture.Factory.CreateOperation("return"),
            fixture.Factory.Variable(bodyParameter));
        var program = builder.Build();
        var validEnvironment = new Dictionary<IrVarId, IrTerm>
        {
            [bodyParameter] = fixture.Factory.Variable(fixture.Parameter)
        };
        var boolean = fixture.Factory.CreateVariable(
            "wrong:boolean",
            fixture.Factory.BooleanType);
        var invalidSignatures = new[]
        {
            new IrSummarySignature(
                fixture.Member,
                receiver: fixture.Parameter,
                [fixture.Parameter],
                fixture.Result,
                Provenance('b')),
            new IrSummarySignature(
                fixture.Member,
                receiver: null,
                [],
                fixture.Result,
                Provenance('c')),
            new IrSummarySignature(
                fixture.Member,
                receiver: null,
                [boolean],
                fixture.Result,
                Provenance('d')),
            new IrSummarySignature(
                fixture.Member,
                receiver: null,
                [fixture.Parameter],
                boolean,
                Provenance('e')),
            new IrSummarySignature(
                fixture.Member,
                receiver: null,
                [fixture.Parameter],
                fixture.Parameter,
                Provenance('f'))
        };

        foreach (var signature in invalidSignatures)
        {
            var result = IrRelationalSummaryBuilder.Build(
                program,
                signature,
                validEnvironment);
            Assert.That(
                result.Reason,
                Is.EqualTo(IrSummaryAbstentionReason.InvalidSignature));
        }

        var unbound = fixture.Factory.CreateVariable(
            "unbound",
            fixture.Factory.IntegerType);
        var foreignFactory = new IrFactory();
        var invalidEnvironments = new IReadOnlyDictionary<IrVarId, IrTerm>[]
        {
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyParameter] = fixture.Factory.Boolean(true)
            },
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyParameter] = fixture.Factory.Variable(unbound)
            },
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyParameter] = foreignFactory.Integer(1)
            }
        };
        foreach (var environment in invalidEnvironments)
        {
            var result = IrRelationalSummaryBuilder.Build(
                program,
                fixture.Signature,
                environment);
            Assert.That(
                result.Reason,
                Is.EqualTo(IrSummaryAbstentionReason.InvalidSignature));
        }

        Assert.Throws<ArgumentNullException>((Action)(() =>
            IrRelationalSummaryBuilder.Build(
                null!,
                fixture.Signature,
                validEnvironment)));
        Assert.Throws<ArgumentNullException>((Action)(() =>
            IrRelationalSummaryBuilder.Build(
                program,
                null!,
                validEnvironment)));
        Assert.Throws<ArgumentNullException>((Action)(() =>
            IrRelationalSummaryBuilder.Build(
                program,
                fixture.Signature,
                null!)));
    }

    [Test]
    public void BuilderAbstainsAtDeclaredResourceBoundaries()
    {
        var fixture = new SummaryFixture("Boundaries");
        var bodyParameter = fixture.Factory.CreateVariable(
            "body:value",
            fixture.Factory.IntegerType);
        var environment = new Dictionary<IrVarId, IrTerm>
        {
            [bodyParameter] = fixture.Factory.Variable(fixture.Parameter)
        };

        var blockBuilder = new IrProgramBuilder(fixture.Factory);
        var blockEntry = blockBuilder.CreateBlock("entry");
        var whenTrue = blockBuilder.CreateBlock("true");
        var whenFalse = blockBuilder.CreateBlock("false");
        blockBuilder.Branch(
            blockEntry,
            fixture.Factory.CreateOperation("branch"),
            fixture.Factory.Boolean(true),
            whenTrue,
            whenFalse);
        blockBuilder.Return(
            whenTrue,
            fixture.Factory.CreateOperation("true-return"),
            fixture.Factory.Variable(bodyParameter));
        blockBuilder.Return(
            whenFalse,
            fixture.Factory.CreateOperation("false-return"),
            fixture.Factory.Variable(bodyParameter));
        Assert.That(
            IrRelationalSummaryBuilder.Build(
                blockBuilder.Build(),
                fixture.Signature,
                environment,
                limits: new IrRelationalSummaryBuildLimits(
                    maximumBlocks: 2)).Reason,
            Is.EqualTo(IrSummaryAbstentionReason.ResourceLimit));

        var instructionBuilder = new IrProgramBuilder(fixture.Factory);
        var instructionEntry = instructionBuilder.CreateBlock("entry");
        var temporary = fixture.Factory.CreateVariable(
            "temporary",
            fixture.Factory.IntegerType);
        instructionBuilder.Assign(
            instructionEntry,
            fixture.Factory.CreateOperation("assign"),
            temporary,
            fixture.Factory.Variable(bodyParameter));
        instructionBuilder.Return(
            instructionEntry,
            fixture.Factory.CreateOperation("return"),
            fixture.Factory.Variable(temporary));
        var instructionProgram = instructionBuilder.Build();
        Assert.That(
            IrRelationalSummaryBuilder.Build(
                instructionProgram,
                fixture.Signature,
                environment,
                limits: new IrRelationalSummaryBuildLimits(
                    maximumInstructions: 1)).Reason,
            Is.EqualTo(IrSummaryAbstentionReason.ResourceLimit));
        Assert.That(
            IrRelationalSummaryBuilder.Build(
                instructionProgram,
                fixture.Signature,
                environment,
                limits: new IrRelationalSummaryBuildLimits(
                    maximumSymbolicOperations: 1)).Reason,
            Is.EqualTo(IrSummaryAbstentionReason.ResourceLimit));

        var depthBuilder = new IrProgramBuilder(fixture.Factory);
        var depthEntry = depthBuilder.CreateBlock("entry");
        depthBuilder.Return(
            depthEntry,
            fixture.Factory.CreateOperation("return"),
            fixture.Factory.Binary(
                IrBinaryOperator.Add,
                fixture.Factory.Variable(bodyParameter),
                fixture.Factory.Integer(1)));
        Assert.That(
            IrRelationalSummaryBuilder.Build(
                depthBuilder.Build(),
                fixture.Signature,
                environment,
                limits: new IrRelationalSummaryBuildLimits(
                    maximumExpressionDepth: 1)).Reason,
            Is.EqualTo(IrSummaryAbstentionReason.ExpressionDepth));
    }

    [Test]
    public void InstanceSummaryInstantiationSubstitutesTheReceiver()
    {
        var factory = new IrFactory();
        var declaringType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "InstanceFunctions");
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            declaringType,
            "Read",
            factory.IntegerType,
            isStatic: false);
        var receiver = factory.CreateVariable("receiver", declaringType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(
            entry,
            factory.CreateOperation("return"),
            factory.Integer(1));
        var summary = IrRelationalSummaryBuilder.Build(
            builder.Build(),
            new IrSummarySignature(
                member,
                receiver,
                [],
                result,
                Provenance('9')),
            new Dictionary<IrVarId, IrTerm>()).Summary!;
        var actualReceiver = factory.CreateVariable(
            "actual-receiver",
            declaringType);

        var instantiated = IrRelationalSummaryInstantiator.Instantiate(
            summary,
            factory.Variable(actualReceiver),
            [],
            0);

        Assert.That(instantiated.FreshVariables, Has.Length.EqualTo(1));
    }

    [Test]
    public void StraightLineSummaryRelatesInputsToResult()
    {
        var fixture = new SummaryFixture("Increment");
        var bodyParameter = fixture.Factory.CreateVariable(
            "body:value",
            fixture.Factory.IntegerType);
        var builder = new IrProgramBuilder(fixture.Factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(
            entry,
            fixture.Factory.CreateOperation("return"),
            fixture.Factory.Binary(
                IrBinaryOperator.Add,
                fixture.Factory.Variable(bodyParameter),
                fixture.Factory.Integer(1)));

        var built = IrRelationalSummaryBuilder.Build(
            builder.Build(),
            fixture.Signature,
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyParameter] = fixture.Factory.Variable(fixture.Parameter)
            });

        Assert.That(built.IsSuccess, Is.True);
        Assert.That(built.Summary!.Dependencies, Is.Empty);
        Assert.That(
            Evaluate(
                fixture,
                built.Summary.NormalRelation,
                input: 4,
                result: 5),
            Is.True);
        Assert.That(
            Evaluate(
                fixture,
                built.Summary.NormalRelation,
                input: 4,
                result: 6),
            Is.False);
    }

    [Test]
    public void BranchSummaryJoinsAllNormalReturns()
    {
        var fixture = new SummaryFixture("Absolute");
        var bodyParameter = fixture.Factory.CreateVariable(
            "body:value",
            fixture.Factory.IntegerType);
        var builder = new IrProgramBuilder(fixture.Factory);
        var entry = builder.CreateBlock("entry");
        var nonnegative = builder.CreateBlock("nonnegative");
        var negative = builder.CreateBlock("negative");
        builder.Branch(
            entry,
            fixture.Factory.CreateOperation("test"),
            fixture.Factory.Binary(
                IrBinaryOperator.GreaterThanOrEqual,
                fixture.Factory.Variable(bodyParameter),
                fixture.Factory.Integer(0)),
            nonnegative,
            negative);
        builder.Return(
            nonnegative,
            fixture.Factory.CreateOperation("positive-return"),
            fixture.Factory.Variable(bodyParameter));
        builder.Return(
            negative,
            fixture.Factory.CreateOperation("negative-return"),
            fixture.Factory.Unary(
                IrUnaryOperator.Negate,
                fixture.Factory.Variable(bodyParameter)));

        var built = IrRelationalSummaryBuilder.Build(
            builder.Build(),
            fixture.Signature,
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyParameter] = fixture.Factory.Variable(fixture.Parameter)
            });

        Assert.That(built.IsSuccess, Is.True);
        Assert.That(
            Evaluate(fixture, built.Summary!.NormalRelation, -4, 4),
            Is.True);
        Assert.That(
            Evaluate(fixture, built.Summary.NormalRelation, 7, 7),
            Is.True);
        Assert.That(
            Evaluate(fixture, built.Summary.NormalRelation, -4, -4),
            Is.False);
    }

    [Test]
    public void CallCompositionUsesAReusableRelationAndFreshVariables()
    {
        var callee = new SummaryFixture("Double");
        var bodyParameter = callee.Factory.CreateVariable(
            "callee:value",
            callee.Factory.IntegerType);
        var calleeBuilder = new IrProgramBuilder(callee.Factory);
        var calleeEntry = calleeBuilder.CreateBlock("entry");
        calleeBuilder.Return(
            calleeEntry,
            callee.Factory.CreateOperation("return"),
            callee.Factory.Binary(
                IrBinaryOperator.Multiply,
                callee.Factory.Variable(bodyParameter),
                callee.Factory.Integer(2)));
        var calleeSummary = IrRelationalSummaryBuilder.Build(
            calleeBuilder.Build(),
            callee.Signature,
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyParameter] = callee.Factory.Variable(callee.Parameter)
            }).Summary!;

        var callerIdentity = callee.Factory.CreateIdentity();
        var callerMember = callee.Factory.GetOrCreateMember(
            callerIdentity,
            callee.DeclaringType,
            "AddOneAfterDouble",
            callee.Factory.IntegerType,
            isStatic: true,
            callee.Factory.IntegerType);
        var callerParameter = callee.Factory.CreateVariable(
            "caller:parameter",
            callee.Factory.IntegerType);
        var callerResult = callee.Factory.CreateVariable(
            "caller:result",
            callee.Factory.IntegerType);
        var callerBodyParameter = callee.Factory.CreateVariable(
            "caller:body-parameter",
            callee.Factory.IntegerType);
        var callResult = callee.Factory.CreateVariable(
            "caller:call-result",
            callee.Factory.IntegerType);
        var callerBuilder = new IrProgramBuilder(callee.Factory);
        var callerEntry = callerBuilder.CreateBlock("entry");
        var call = callerBuilder.Call(
            callerEntry,
            callee.Factory.CreateOperation("call"),
            callResult,
            callee.Member,
            receiver: null,
            callee.Factory.Variable(callerBodyParameter));
        callerBuilder.Return(
            callerEntry,
            callee.Factory.CreateOperation("return"),
            callee.Factory.Binary(
                IrBinaryOperator.Add,
                callee.Factory.Variable(callResult),
                callee.Factory.Integer(1)));
        var callerSignature = new IrSummarySignature(
            callerMember,
            receiver: null,
            [callerParameter],
            callerResult,
            Provenance('b'));

        var built = IrRelationalSummaryBuilder.Build(
            callerBuilder.Build(),
            callerSignature,
            new Dictionary<IrVarId, IrTerm>
            {
                [callerBodyParameter] =
                    callee.Factory.Variable(callerParameter)
            },
            new Dictionary<IrInstructionId, IrRelationalSummary>
            {
                [call.Id] = calleeSummary
            });

        Assert.That(built.IsSuccess, Is.True);
        Assert.That(
            built.Summary!.Dependencies,
            Is.EqualTo(new[] { callee.Member }));
        Assert.That(
            built.Summary.DependencyProvenance.Select(static item =>
                item.EvidenceSha256),
            Is.EqualTo(new[] { calleeSummary.Signature.Provenance.EvidenceSha256 }));
        Assert.That(built.Summary.ExistentialVariables.Length, Is.EqualTo(1));
        var internalResult = built.Summary.ExistentialVariables[0];
        var values = new Dictionary<IrVarId, IrValue>
        {
            [callerParameter] = callee.Factory.CreateIntegerValue(3),
            [callerResult] = callee.Factory.CreateIntegerValue(7),
            [internalResult] = callee.Factory.CreateIntegerValue(6)
        };
        var evaluation = new IrInterpreter(callee.Factory).Evaluate(
            built.Summary.NormalRelation,
            values);
        Assert.That(evaluation.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(evaluation.Value!.Boolean, Is.True);

        var first = IrRelationalSummaryInstantiator.Instantiate(
            calleeSummary,
            receiver: null,
            [callee.Factory.Integer(2)],
            1);
        var second = IrRelationalSummaryInstantiator.Instantiate(
            calleeSummary,
            receiver: null,
            [callee.Factory.Integer(2)],
            2);
        Assert.That(first.Result, Is.Not.EqualTo(second.Result));
    }

    [Test]
    public void CyclicControlFlowAbstainsWithTypedReason()
    {
        var fixture = new SummaryFixture("Loop");
        var bodyParameter = fixture.Factory.CreateVariable(
            "body:value",
            fixture.Factory.IntegerType);
        var builder = new IrProgramBuilder(fixture.Factory);
        var entry = builder.CreateBlock("entry");
        builder.Goto(
            entry,
            fixture.Factory.CreateOperation("loop"),
            entry);

        var built = IrRelationalSummaryBuilder.Build(
            builder.Build(),
            fixture.Signature,
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyParameter] = fixture.Factory.Variable(fixture.Parameter)
            });

        Assert.That(built.IsSuccess, Is.False);
        Assert.That(
            built.Reason,
            Is.EqualTo(IrSummaryAbstentionReason.CyclicControlFlow));
    }

    private static bool Evaluate(
        SummaryFixture fixture,
        IrTerm relation,
        long input,
        long result)
    {
        var evaluation = new IrInterpreter(fixture.Factory).Evaluate(
            relation,
            new Dictionary<IrVarId, IrValue>
            {
                [fixture.Parameter] =
                    fixture.Factory.CreateIntegerValue(input),
                [fixture.Result] =
                    fixture.Factory.CreateIntegerValue(result)
            });
        Assert.That(evaluation.Status, Is.EqualTo(IrEvaluationStatus.Value));
        return evaluation.Value!.Boolean;
    }

    private static IrRelationalSummary BuildIdentitySummary(
        SummaryFixture fixture)
    {
        var bodyParameter = fixture.Factory.CreateVariable(
            "identity:body-value",
            fixture.Factory.IntegerType);
        var builder = new IrProgramBuilder(fixture.Factory);
        var entry = builder.CreateBlock("identity:entry");
        builder.Return(
            entry,
            fixture.Factory.CreateOperation("identity:return"),
            fixture.Factory.Variable(bodyParameter));
        return IrRelationalSummaryBuilder.Build(
            builder.Build(),
            fixture.Signature,
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyParameter] = fixture.Factory.Variable(fixture.Parameter)
            }).Summary!;
    }

    private static IrSummaryProvenance Provenance(char digit)
    {
        return new IrSummaryProvenance(
            IrSummaryOrigin.Source,
            new string(digit, 64));
    }

    private sealed class SummaryFixture
    {
        internal SummaryFixture(string name)
        {
            Factory = new IrFactory();
            DeclaringType = Factory.GetOrCreateReferenceType(
                Factory.CreateIdentity(),
                "Functions");
            Member = CreateMember(name);
            Parameter = Factory.CreateVariable(
                "parameter:0",
                Factory.IntegerType);
            Result = Factory.CreateVariable(
                "result",
                Factory.IntegerType);
            Signature = new IrSummarySignature(
                Member,
                receiver: null,
                [Parameter],
                Result,
                Provenance('a'));
        }

        internal IrFactory Factory { get; }

        internal IrTypeId DeclaringType { get; }

        internal IrMemberId Member { get; }

        internal IrVarId Parameter { get; }

        internal IrVarId Result { get; }

        internal IrSummarySignature Signature { get; }

        internal IrMemberId CreateMember(string name)
        {
            return Factory.GetOrCreateMember(
                Factory.CreateIdentity(),
                DeclaringType,
                name,
                Factory.IntegerType,
                isStatic: true,
                Factory.IntegerType);
        }
    }
}
