using System.Collections.Immutable;
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
        var foreignVariable = foreignFactory.CreateVariable(
            "foreign",
            foreignFactory.IntegerType);
        var instanceMember = fixture.Factory.GetOrCreateMember(
            fixture.Factory.CreateIdentity(),
            fixture.DeclaringType,
            "InstanceInvalidShape",
            fixture.Factory.IntegerType,
            isStatic: false,
            fixture.Factory.IntegerType);
        var foreignSignatures = new[]
        {
            new IrSummarySignature(
                fixture.Member,
                receiver: null,
                [foreignVariable],
                fixture.Result,
                Provenance('a')),
            new IrSummarySignature(
                fixture.Member,
                receiver: null,
                [fixture.Parameter],
                foreignVariable,
                Provenance('b')),
            new IrSummarySignature(
                instanceMember,
                foreignVariable,
                [fixture.Parameter],
                fixture.Result,
                Provenance('c'))
        };
        foreach (var signature in foreignSignatures)
        {
            var result = IrRelationalSummaryBuilder.Build(
                program,
                signature,
                validEnvironment);
            Assert.That(
                result.Reason,
                Is.EqualTo(IrSummaryAbstentionReason.InvalidSignature));
        }

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
            },
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyParameter] = null!
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
    public void SymbolicBudgetRejectsBroadUniqueTermDag()
    {
        const int leafCount = 32;
        var factory = new IrFactory();
        var declaringType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(), "WideFunctions");
        var parameterTypes = Enumerable.Repeat(factory.IntegerType, leafCount).ToArray();
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(), declaringType, "Wide", factory.IntegerType,
            isStatic: true, parameterTypes);
        var parameters = Enumerable.Range(0, leafCount)
            .Select(index => factory.CreateVariable(
                "parameter:" + index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                factory.IntegerType))
            .ToImmutableArray();
        var resultVariable = factory.CreateVariable("result", factory.IntegerType);
        var signature = new IrSummarySignature(
            member, receiver: null, parameters, resultVariable, Provenance('b'));
        var environment = ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
        var leaves = new List<IrTerm>();
        for (var index = 0; index < leafCount; index++)
        {
            var bodyParameter = factory.CreateVariable(
                "body:" + index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                factory.IntegerType);
            environment.Add(bodyParameter, factory.Variable(parameters[index]));
            leaves.Add(factory.Variable(bodyParameter));
        }
        while (leaves.Count > 1)
        {
            var next = new List<IrTerm>();
            for (var index = 0; index < leaves.Count; index += 2)
            {
                next.Add(index + 1 == leaves.Count
                    ? leaves[index]
                    : factory.Binary(
                        IrBinaryOperator.Add, leaves[index], leaves[index + 1]));
            }
            leaves = next;
        }

        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(entry, factory.CreateOperation("return"), leaves[0]);
        var result = IrRelationalSummaryBuilder.Build(
            builder.Build(), signature, environment.ToImmutable(),
            limits: new IrRelationalSummaryBuildLimits(
                maximumSymbolicOperations: 8));

        Assert.That(result.Reason,
            Is.EqualTo(IrSummaryAbstentionReason.ResourceLimit));
    }

    [Test]
    public void CallCompositionPreservesExpressionDepthAbstention()
    {
        var fixture = new SummaryFixture("DepthDependency");
        var dependency = BuildIdentitySummary(fixture);
        var callerMember = fixture.CreateMember("DepthCaller");
        var bodyParameter = fixture.Factory.CreateVariable(
            "caller:body-value",
            fixture.Factory.IntegerType);
        var callResult = fixture.Factory.CreateVariable(
            "caller:call-result",
            fixture.Factory.IntegerType);
        var builder = new IrProgramBuilder(fixture.Factory);
        var entry = builder.CreateBlock("entry");
        var call = builder.Call(
            entry,
            fixture.Factory.CreateOperation("call"),
            callResult,
            fixture.Member,
            receiver: null,
            fixture.Factory.Variable(bodyParameter));
        builder.Return(
            entry,
            fixture.Factory.CreateOperation("return"),
            fixture.Factory.Variable(callResult));

        var built = IrRelationalSummaryBuilder.Build(
            builder.Build(),
            new IrSummarySignature(
                callerMember,
                receiver: null,
                [fixture.Parameter],
                fixture.Result,
                Provenance('d')),
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyParameter] =
                    fixture.Factory.Variable(fixture.Parameter)
            },
            new Dictionary<IrInstructionId, IrRelationalSummary>
            {
                [call.Id] = dependency
            },
            new IrRelationalSummaryBuildLimits(
                maximumExpressionDepth: 1));

        Assert.That(
            built.Reason,
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
    public void CallCompositionConjoinsDependencyNormalCompletion()
    {
        var fixture = new SummaryFixture("PartialDependency");
        var calleeBodyParameter = fixture.Factory.CreateVariable(
            "callee:body-value",
            fixture.Factory.IntegerType);
        var calleeBuilder = new IrProgramBuilder(fixture.Factory);
        var calleeEntry = calleeBuilder.CreateBlock("callee:entry");
        calleeBuilder.Return(
            calleeEntry,
            fixture.Factory.CreateOperation("callee:return"),
            fixture.Factory.Binary(
                IrBinaryOperator.Divide,
                fixture.Factory.Integer(10),
                fixture.Factory.Variable(calleeBodyParameter)));
        var calleeSummary = IrRelationalSummaryBuilder.Build(
            calleeBuilder.Build(),
            fixture.Signature,
            new Dictionary<IrVarId, IrTerm>
            {
                [calleeBodyParameter] =
                    fixture.Factory.Variable(fixture.Parameter)
            }).Summary!;

        var callerBodyParameter = fixture.Factory.CreateVariable(
            "caller:body-value",
            fixture.Factory.IntegerType);
        var callResult = fixture.Factory.CreateVariable(
            "caller:call-result",
            fixture.Factory.IntegerType);
        var callerBuilder = new IrProgramBuilder(fixture.Factory);
        var callerEntry = callerBuilder.CreateBlock("caller:entry");
        var call = callerBuilder.Call(
            callerEntry,
            fixture.Factory.CreateOperation("caller:call"),
            callResult,
            fixture.Member,
            receiver: null,
            fixture.Factory.Variable(callerBodyParameter));
        callerBuilder.Return(
            callerEntry,
            fixture.Factory.CreateOperation("caller:return"),
            fixture.Factory.Variable(callResult));

        var built = IrRelationalSummaryBuilder.Build(
            callerBuilder.Build(),
            new IrSummarySignature(
                fixture.CreateMember("Caller"),
                receiver: null,
                [fixture.Parameter],
                fixture.Result,
                Provenance('b')),
            new Dictionary<IrVarId, IrTerm>
            {
                [callerBodyParameter] =
                    fixture.Factory.Variable(fixture.Parameter)
            },
            new Dictionary<IrInstructionId, IrRelationalSummary>
            {
                [call.Id] = calleeSummary
            });

        Assert.That(built.IsSuccess, Is.True, built.Reason.ToString());
        var callValue = built.Summary!.ExistentialVariables.Single();
        var replacements = new Dictionary<IrVarId, IrTerm>
        {
            [fixture.Parameter] = fixture.Factory.Variable(fixture.Parameter),
            [fixture.Result] = fixture.Factory.Variable(callValue)
        };
        var expectedCompletion = IrSubstitution.Substitute(
            fixture.Factory,
            calleeSummary.NormalCompletion,
            replacements);
        var expectedRelation = IrSubstitution.Substitute(
            fixture.Factory,
            calleeSummary.NormalRelation,
            replacements);

        Assert.That(
            built.Summary.NormalCompletion,
            Is.EqualTo(fixture.Factory.Binary(
                IrBinaryOperator.AndAlso,
                expectedCompletion,
                expectedRelation)));
    }

    [Test]
    public void InstanceCallCompositionRequiresANonNullReceiver()
    {
        var factory = new IrFactory();
        var declaringType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "InstanceReceiver");
        var calleeMember = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            declaringType,
            "Read",
            factory.IntegerType,
            isStatic: false);
        var calleeReceiver = factory.CreateVariable(
            "callee:receiver",
            declaringType);
        var calleeResult = factory.CreateVariable(
            "callee:result",
            factory.IntegerType);
        var calleeBuilder = new IrProgramBuilder(factory);
        var calleeEntry = calleeBuilder.CreateBlock("callee:entry");
        calleeBuilder.Return(
            calleeEntry,
            factory.CreateOperation("callee:return"),
            factory.Integer(1));
        var calleeSummary = IrRelationalSummaryBuilder.Build(
            calleeBuilder.Build(),
            new IrSummarySignature(
                calleeMember,
                calleeReceiver,
                [],
                calleeResult,
                Provenance('c')),
            new Dictionary<IrVarId, IrTerm>()).Summary!;

        var callerMember = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            declaringType,
            "CallRead",
            factory.IntegerType,
            isStatic: true,
            declaringType);
        var callerReceiver = factory.CreateVariable(
            "caller:receiver",
            declaringType);
        var callerResult = factory.CreateVariable(
            "caller:result",
            factory.IntegerType);
        var bodyReceiver = factory.CreateVariable(
            "caller:body-receiver",
            declaringType);
        var callResult = factory.CreateVariable(
            "caller:call-result",
            factory.IntegerType);
        var callerBuilder = new IrProgramBuilder(factory);
        var callerEntry = callerBuilder.CreateBlock("caller:entry");
        var call = callerBuilder.Call(
            callerEntry,
            factory.CreateOperation("caller:call"),
            callResult,
            calleeMember,
            factory.Variable(bodyReceiver));
        callerBuilder.Return(
            callerEntry,
            factory.CreateOperation("caller:return"),
            factory.Variable(callResult));
        var built = IrRelationalSummaryBuilder.Build(
            callerBuilder.Build(),
            new IrSummarySignature(
                callerMember,
                receiver: null,
                [callerReceiver],
                callerResult,
                Provenance('d')),
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyReceiver] = factory.Variable(callerReceiver)
            },
            new Dictionary<IrInstructionId, IrRelationalSummary>
            {
                [call.Id] = calleeSummary
            });

        Assert.That(built.IsSuccess, Is.True, built.Reason.ToString());
        var summary = built.Summary!;
        var internalResult = summary.ExistentialVariables.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.Effects, Is.EqualTo(IrSummaryEffect.MayThrow));
            Assert.That(
                EvaluateCompletion(factory.CreateNullValue(declaringType)),
                Is.False);
            Assert.That(
                EvaluateCompletion(
                    factory.CreateReferenceValue(declaringType, new object())),
                Is.True);
        }

        bool EvaluateCompletion(IrValue receiver)
        {
            var evaluation = new IrInterpreter(factory).Evaluate(
                summary.NormalCompletion,
                new Dictionary<IrVarId, IrValue>
                {
                    [callerReceiver] = receiver,
                    [internalResult] = factory.CreateIntegerValue(1)
                });
            Assert.That(
                evaluation.Status,
                Is.EqualTo(IrEvaluationStatus.Value));
            return evaluation.Value!.Boolean;
        }
    }

    [Test]
    public void DependencyProvenanceIdentityComponentsAreDeduplicatedStructurally()
    {
        var fixture = new SummaryFixture("Caller");
        var digest = new string('f', 64);
        var first = CreateCallee(
            "First",
            new IrSummaryProvenance(
                IrSummaryOrigin.SpecificationPack,
                digest,
                evidenceIdentity: "C",
                evidenceCallIdentity: "A|B"));
        var second = CreateCallee(
            "Second",
            new IrSummaryProvenance(
                IrSummaryOrigin.SpecificationPack,
                digest,
                evidenceIdentity: "B|C",
                evidenceCallIdentity: "A"));
        var callerParameter = fixture.Factory.CreateVariable(
            "caller:parameter",
            fixture.Factory.IntegerType);
        var callerResult = fixture.Factory.CreateVariable(
            "caller:result",
            fixture.Factory.IntegerType);
        var bodyParameter = fixture.Factory.CreateVariable(
            "caller:body-parameter",
            fixture.Factory.IntegerType);
        var firstResult = fixture.Factory.CreateVariable(
            "caller:first-result",
            fixture.Factory.IntegerType);
        var secondResult = fixture.Factory.CreateVariable(
            "caller:second-result",
            fixture.Factory.IntegerType);
        var builder = new IrProgramBuilder(fixture.Factory);
        var entry = builder.CreateBlock("entry");
        var firstCall = builder.Call(
            entry,
            fixture.Factory.CreateOperation("first-call"),
            firstResult,
            first.Member,
            receiver: null,
            fixture.Factory.Variable(bodyParameter));
        var secondCall = builder.Call(
            entry,
            fixture.Factory.CreateOperation("second-call"),
            secondResult,
            second.Member,
            receiver: null,
            fixture.Factory.Variable(firstResult));
        builder.Return(
            entry,
            fixture.Factory.CreateOperation("return"),
            fixture.Factory.Variable(secondResult));
        var signature = new IrSummarySignature(
            fixture.Member,
            receiver: null,
            [callerParameter],
            callerResult,
            Provenance('b'));

        var built = IrRelationalSummaryBuilder.Build(
            builder.Build(),
            signature,
            new Dictionary<IrVarId, IrTerm>
            {
                [bodyParameter] = fixture.Factory.Variable(callerParameter)
            },
            new Dictionary<IrInstructionId, IrRelationalSummary>
            {
                [firstCall.Id] = first.Summary,
                [secondCall.Id] = second.Summary
            });

        Assert.That(built.IsSuccess, Is.True, built.Reason.ToString());
        Assert.That(
            built.Summary!.DependencyProvenance.Count(static provenance =>
                provenance.Origin == IrSummaryOrigin.SpecificationPack),
            Is.EqualTo(2));

        (IrMemberId Member, IrRelationalSummary Summary) CreateCallee(
            string name,
            IrSummaryProvenance provenance)
        {
            var member = fixture.CreateMember(name);
            var parameter = fixture.Factory.CreateVariable(
                name + ":parameter",
                fixture.Factory.IntegerType);
            var result = fixture.Factory.CreateVariable(
                name + ":result",
                fixture.Factory.IntegerType);
            var calleeBodyParameter = fixture.Factory.CreateVariable(
                name + ":body-parameter",
                fixture.Factory.IntegerType);
            var calleeBuilder = new IrProgramBuilder(fixture.Factory);
            var calleeEntry = calleeBuilder.CreateBlock(name + ":entry");
            calleeBuilder.Return(
                calleeEntry,
                fixture.Factory.CreateOperation(name + ":return"),
                fixture.Factory.Variable(calleeBodyParameter));
            var calleeSignature = new IrSummarySignature(
                member,
                receiver: null,
                [parameter],
                result,
                provenance);
            var summary = IrRelationalSummaryBuilder.Build(
                calleeBuilder.Build(),
                calleeSignature,
                new Dictionary<IrVarId, IrTerm>
                {
                    [calleeBodyParameter] = fixture.Factory.Variable(parameter)
                });
            Assert.That(summary.IsSuccess, Is.True, summary.Reason.ToString());
            return (member, summary.Summary!);
        }
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
