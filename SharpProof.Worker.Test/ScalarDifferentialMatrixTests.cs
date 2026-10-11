using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ScalarDifferentialMatrixTests
{
    // SP-CORR-0002: a Roslyn-unreachable right arm must not abstract the live body.
    [Test]
    public async Task ShortCircuitedRefCallDoesNotInvalidateProof()
    {
        using var project = DifferentialProject.Create("""
            using SharpProof.Attributes;
            public static class ScalarDifferentialSubject {
                private static bool Mutate(ref int value) { value = 1; return true; }
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == 0);
                    return false && Mutate(ref value) ? value : 0;
                }
            }
            """);
        var request = project.CreateRequest();
        using var runtime = project.EmitRuntimeAssembly();
        var method = RequireRuntimeMethod(RequireRuntimeSubject(runtime.Assembly), "Target");
        Assert.That(method.Invoke(null, [0]), Is.EqualTo(0));
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(project.FindCallable("Target").Total!.IsBodyAbstraction, Is.False);
        Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
            $"Reason={response.ClaimResults.Single().Reason}; coverage={response.CallableResults.Single().Coverage}; " +
            $"body abstraction={project.FindCallable("Target").Total?.IsBodyAbstraction}");
    }

    [TestCase("true || Mutate(ref value)", false)]
    [TestCase("true && Mutate(ref value)", true)]
    [TestCase("false || Mutate(ref value)", true)]
    [TestCase("false ? Mutate(ref value) : false", false)]
    [TestCase("true ? Mutate(ref value) : false", true)]
    public async Task BooleanArmsExecuteOnlyWhenSelected(string condition, bool callRuns)
    {
        using var project = DifferentialProject.Create($$"""
            using SharpProof.Attributes;
            public static class ScalarDifferentialSubject {
                private static bool Mutate(ref int value) { value = 1; return true; }
                public static int Target(int value) {
                    Contract.Requires(value == 0);
                    Contract.Ensures(Contract.Result<int>() == 0);
                    return ({{condition}}) ? value : 0;
                }
            }
            """);
        var request = project.CreateRequest();
        using var runtime = project.EmitRuntimeAssembly();
        var method = RequireRuntimeMethod(RequireRuntimeSubject(runtime.Assembly), "Target");
        Assert.That(method.Invoke(null, [0]), Is.EqualTo(callRuns ? 1 : 0));
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome,
            callRuns ? Is.Not.EqualTo(WorkerClaimOutcome.Proven) : Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase(null, null)]
    [TestCase(null, "")]
    [TestCase("", "")]
    [TestCase("left", null)]
    [TestCase(null, "right")]
    [TestCase("left", "right")]
    [TestCase("a\0b", "\ud83d\ude00")]
    public void StringConcatAgreesWithCompiledRuntime(string? left, string? right)
    {
        using var project = DifferentialProject.Create("""
            #nullable enable
            using SharpProof.Attributes;
            public static class ScalarDifferentialSubject {
                [return: NotNull]
                public static string Target(string? left, string? right) { return string.Concat(left, right); }
            }
            """);
        project.CreateRequest();
        var total = project.FindCallable("Target").Total;
        Assert.That(total, Is.Not.Null);
        Assert.That(total!.IsBodyAbstraction, Is.False);
        var factory = total.Program.Factory;
        var values = new[] { left, right };
        var execution = new IrProgramInterpreter(factory).Execute(total.Program,
            total.Parameters.Select((parameter, ordinal) => (parameter.Entry, Value: values[ordinal] is { } text
                    ? factory.CreateStringValue(text) : factory.CreateNullValue(factory.GetVariableInfo(parameter.Entry).Type)))
                .ToDictionary(item => item.Entry, item => item.Value));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        using var runtime = project.EmitRuntimeAssembly();
        var method = RequireRuntimeMethod(RequireRuntimeSubject(runtime.Assembly), "Target");
        Assert.That(execution.ReturnValue!.Kind, Is.EqualTo(IrValueKind.String));
        var runtimeValue = (string)method.Invoke(null, [left, right])!;
        Assert.That(execution.ReturnValue.String, Is.EqualTo(runtimeValue));
        Assert.That(ReferenceEquals(execution.ReturnValue.String, left), Is.EqualTo(ReferenceEquals(runtimeValue, left)));
        Assert.That(ReferenceEquals(execution.ReturnValue.String, right), Is.EqualTo(ReferenceEquals(runtimeValue, right)));
        if (runtimeValue.Length == 0)
        { Assert.That(runtimeValue, Is.SameAs(string.Empty)); }
    }

    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    [TestCase("ulong")]
    [TestCase("bool")]
    [TestCase("string")]
    [TestCase("object")]
    public void EmptyArrayFacetsAndIdentityAgreeWithCompiledRuntime(string elementType)
    {
        using var project = DifferentialProject.Create($$"""
            using System;
            using SharpProof.Attributes;
            public static class ScalarDifferentialSubject {
                [return: InRange(1, 1)]
                public static int Target(int unused) {
                    var first = Array.Empty<{{elementType}}>();
                    var second = Array.Empty<{{elementType}}>();
                    return first != null && first.Length == 0 && first == second ? 1 : 0;
                }
            }
            """);
        project.CreateRequest();
        var total = project.FindCallable("Target").Total;
        Assert.That(total, Is.Not.Null);
        Assert.That(total!.IsBodyAbstraction, Is.False);
        var interpreter = new IrProgramInterpreter(total.Program.Factory);
        var entries = total.Parameters.ToDictionary(parameter => parameter.Entry,
            parameter => total.Program.Factory.CreateIntegerValue(total.Program.Factory.GetVariableInfo(parameter.Entry).Type, 0L));
        Assert.That(interpreter.Execute(total.Program, entries).Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        var execution = interpreter.Execute(total.Program, entries, 10000, new IrProgramReplayOptions(_ => null));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        using var runtime = project.EmitRuntimeAssembly();
        var method = RequireRuntimeMethod(RequireRuntimeSubject(runtime.Assembly), "Target");
        Assert.That(execution.ReturnValue!.Integer, Is.EqualTo((int)method.Invoke(null, [0])!));
        Assert.That(execution.ReturnValue.Integer, Is.EqualTo(1));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void CalleeContractArgumentExecutionAgreesAcrossCompiledRuntimeAndNativeIr(bool emitted, bool caught)
    {
        var directive = emitted ? "#define SHARPPROOF_CONTRACTS" : "#undef SHARPPROOF_CONTRACTS";
        var body = caught ? "try { return Helper(value); } catch (DivideByZeroException) { return 7; }" : "return Helper(value);";
        using var project = DifferentialProject.Create($$"""
            {{directive}}
            using System;
            using SharpProof.Attributes;
            public static class ScalarDifferentialSubject {
                private static int Helper(int value) { Contract.Assume(1 / value > 0); return -1; }
                [return: InRange(7, 7)]
                public static int Target(int value) { {{body}} }
            }
            """);
        project.CreateRequest();
        var total = project.FindCallable("Target").Total;
        Assert.That(total, Is.Not.Null);
        Assert.That(total!.IsBodyAbstraction, Is.False);
        var factory = total.Program.Factory;
        var execution = new IrProgramInterpreter(factory).Execute(total.Program,
            total.Parameters.ToDictionary(parameter => parameter.Entry,
                parameter => factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type, 0L)));
        Assert.That(execution.ConsumedApproximation, Is.False);
        using var runtime = project.EmitRuntimeAssembly();
        var method = RequireRuntimeMethod(RequireRuntimeSubject(runtime.Assembly), "Target");
        object? value = null;
        Exception? exception = null;
        try
        { value = method.Invoke(null, [0]); }
        catch (TargetInvocationException failure) { exception = failure.InnerException; }
        if (emitted && !caught)
        {
            Assert.That(exception, Is.TypeOf<DivideByZeroException>());
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        }
        else
        {
            Assert.That(exception, Is.Null);
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue!.Integer, Is.EqualTo((int)value!));
        }
    }

    private static readonly ScalarCase[] SupportedCases = [
        new(
            "SByte",
            "sbyte",
            [sbyte.MinValue, (sbyte)-1, (sbyte)0, (sbyte)1, sbyte.MaxValue]),
        new(
            "Byte",
            "byte",
            [byte.MinValue, (byte)1, (byte)127, byte.MaxValue]),
        new(
            "Int16",
            "short",
            [short.MinValue, (short)-1, (short)0, (short)1, short.MaxValue]),
        new(
            "UInt16",
            "ushort",
            [ushort.MinValue, (ushort)1, (ushort)32767, ushort.MaxValue]),
        new(
            "Char",
            "char",
            [char.MinValue, (char)1, 'A', char.MaxValue]),
        new(
            "Int32",
            "int",
            [int.MinValue, -1, 0, 1, int.MaxValue]),
        new(
            "UInt32",
            "uint",
            [uint.MinValue, 1U, 2147483647U, uint.MaxValue]),
        new(
            "Int64",
            "long",
            [long.MinValue, -1L, 0L, 1L, long.MaxValue])
    ];

    private static readonly WideningCase[] WideningCases = [
        Widen("SByte", "sbyte", "Int16", "short"),
        Widen("SByte", "sbyte", "Int32", "int"),
        Widen("SByte", "sbyte", "Int64", "long"),
        Widen("Byte", "byte", "Int16", "short"),
        Widen("Byte", "byte", "UInt16", "ushort"),
        Widen("Byte", "byte", "Char", "char"),
        Widen("Byte", "byte", "Int32", "int"),
        Widen("Byte", "byte", "UInt32", "uint"),
        Widen("Byte", "byte", "Int64", "long"),
        Widen("Int16", "short", "Int32", "int"),
        Widen("Int16", "short", "Int64", "long"),
        Widen("UInt16", "ushort", "Char", "char"),
        Widen("UInt16", "ushort", "Int32", "int"),
        Widen("UInt16", "ushort", "UInt32", "uint"),
        Widen("UInt16", "ushort", "Int64", "long"),
        Widen("Char", "char", "UInt16", "ushort"),
        Widen("Char", "char", "Int32", "int"),
        Widen("Char", "char", "UInt32", "uint"),
        Widen("Char", "char", "Int64", "long"),
        Widen("Int32", "int", "Int64", "long"),
        Widen("UInt32", "uint", "Int64", "long")
    ];

    private static readonly ArithmeticCase[] ArithmeticCases = [
        Binary("AddNormal", "checked(left + right)", 2L, 3L, 5L),
        Binary(
            "AddBoundary",
            "checked(left + right)",
            long.MaxValue,
            0L,
            long.MaxValue),
        BinaryOverflow(
            "AddOverflow",
            "checked(left + right)",
            long.MaxValue,
            1L),
        Binary("SubtractNormal", "checked(left - right)", 7L, 3L, 4L),
        Binary(
            "SubtractBoundary",
            "checked(left - right)",
            long.MinValue,
            0L,
            long.MinValue),
        BinaryOverflow(
            "SubtractOverflow",
            "checked(left - right)",
            long.MinValue,
            1L),
        Binary("MultiplyNormal", "checked(left * right)", -7L, 3L, -21L),
        Binary(
            "MultiplyBoundary",
            "checked(left * right)",
            long.MinValue,
            1L,
            long.MinValue),
        BinaryOverflow(
            "MultiplyOverflow",
            "checked(left * right)",
            long.MinValue,
            -1L),
        Binary("DivideNormal", "checked(left / right)", -17L, 5L, -3L),
        Binary(
            "DivideBoundary",
            "checked(left / right)",
            long.MinValue,
            1L,
            long.MinValue),
        BinaryOverflow(
            "DivideOverflow",
            "checked(left / right)",
            long.MinValue,
            -1L),
        BinaryException(
            "DivideByZero",
            "checked(left / right)",
            1L,
            0L,
            typeof(DivideByZeroException),
            IrExceptionKind.DivideByZero),
        Binary("RemainderNormal", "checked(left % right)", -17L, 5L, -2L),
        Binary(
            "RemainderBoundary",
            "checked(left % right)",
            long.MaxValue,
            -1L,
            0L),
        BinaryException(
            "RemainderByZero",
            "checked(left % right)",
            1L,
            0L,
            typeof(DivideByZeroException),
            IrExceptionKind.DivideByZero),
        Unary("NegateNormal", "checked(-value)", 7L, -7L),
        Unary(
            "NegateBoundary",
            "checked(-value)",
            long.MaxValue,
            -long.MaxValue),
        new(
            "NegateOverflow",
            "checked(-value)",
            [long.MinValue],
            null,
            typeof(OverflowException),
            IrExceptionKind.Overflow)
    ];

    private static readonly string[] RequiredReferenceFileNames = [
        "System.Private.CoreLib.dll",
        "System.Linq.dll",
        "System.Runtime.dll",
        "netstandard.dll"
    ];

    private static readonly string[] UnaryParameterNames = ["value"];
    private static readonly string[] BinaryParameterNames = ["left", "right"];

    [Test]
    public async Task SupportedScalarMatrixAgreesAcrossRuntimeIrAndSmt()
    {
        using var project = DifferentialProject.Create(CreateSource());
        var request = project.CreateRequest();
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var supportedResults = response.ClaimResults;
        var proven = supportedResults.Where(static result =>
            result.Outcome == WorkerClaimOutcome.Proven).ToArray();
        var refuted = supportedResults.Where(static result =>
            result.Outcome == WorkerClaimOutcome.Refuted).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(supportedResults, Has.Length.EqualTo(64));
            Assert.That(
                proven,
                Has.Length.EqualTo(48));
            Assert.That(
                proven.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.None));
            Assert.That(
                refuted,
                Has.Length.EqualTo(16));
            Assert.That(
                refuted.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                supportedResults.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.None));
            Assert.That(
                response.CallableResults.Select(static result =>
                    result.Coverage),
                Is.All.EqualTo(WorkerCallableCoverage.Complete));
        }
        foreach (var item in SupportedCases)
        {
            var methodResults = supportedResults.Where(result =>
                CallableId(response, result).Contains(
                    "." + item.MethodName + "(",
                    StringComparison.Ordinal)).ToArray();
            Assert.That(
                methodResults.Where(result =>
                    ClaimOrdinal(response, result) is 0 or 1)
                    .Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Proven),
                item.MethodName);
            Assert.That(
                methodResults.Where(result =>
                    ClaimOrdinal(response, result) == 2)
                    .Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Refuted),
                item.MethodName);
            var counterexample = methodResults.Single(result =>
                ClaimOrdinal(response, result) == 2);
            Assert.That(
                counterexample.Model.Single(value =>
                    value.Variable == "parameter:0").Value,
                Is.EqualTo(Format(item.BoundaryValues[^1])),
                item.MethodName);
        }

        using var runtime = project.EmitRuntimeAssembly();
        var subject = RequireRuntimeSubject(runtime.Assembly);
        foreach (var item in SupportedCases)
        {
            var method = RequireRuntimeMethod(subject, item.MethodName);
            var target = project.FindCallable(item.MethodName);
            foreach (var input in item.BoundaryValues)
            {
                foreach (var chooseFirst in new[] { true, false })
                {
                    Assert.That(
                        method.Invoke(null, [input, chooseFirst]),
                        Is.EqualTo(input),
                        $"{item.MethodName}({Format(input)}, {chooseFirst})");
                    AssertIntegerReturn(
                        ExecuteIr(target, input, chooseFirst),
                        Convert.ToInt64(input, CultureInfo.InvariantCulture),
                        $"{item.MethodName}({Format(input)}, {chooseFirst})");
                }
            }

            var comparisonName = "Compare" + item.MethodName;
            var comparisonResults = supportedResults.Where(result =>
                CallableId(response, result).Contains(
                    "." + comparisonName + "(",
                    StringComparison.Ordinal)).ToArray();
            Assert.That(
                comparisonResults.Where(result =>
                    ClaimOrdinal(response, result) < 4)
                    .Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Proven),
                comparisonName);
            Assert.That(
                comparisonResults.Single(result =>
                    ClaimOrdinal(response, result) == 4).Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted),
                comparisonName);
            var comparison = RequireRuntimeMethod(subject, comparisonName);
            var comparisonTarget = project.FindCallable(comparisonName);
            foreach (var left in item.BoundaryValues)
            {
                foreach (var right in item.BoundaryValues)
                {
                    var expected = Convert.ToInt64(
                        left,
                        CultureInfo.InvariantCulture).CompareTo(
                            Convert.ToInt64(
                                right,
                                CultureInfo.InvariantCulture));
                    Assert.That(
                        comparison.Invoke(null, [left, right]),
                        Is.EqualTo(expected),
                        $"{comparisonName}({Format(left)}, {Format(right)})");
                    AssertIntegerReturn(
                        ExecuteIr(comparisonTarget, left, right),
                        expected,
                        $"{comparisonName}({Format(left)}, {Format(right)})");
                }
            }
        }
    }

    [Test]
    public async Task CatalogApprovedWideningsAgreeAcrossRuntimeIrAndSmt()
    {
        using var project = DifferentialProject.Create(
            CreateWideningSource());
        var request = project.CreateRequest();
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.ClaimResults,
                Has.Length.EqualTo(WideningCases.Length));
            Assert.That(
                response.ClaimResults.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.None));
            Assert.That(
                response.CallableResults.Select(static result =>
                    result.Coverage),
                Is.All.EqualTo(WorkerCallableCoverage.Complete));
        }

        using var runtime = project.EmitRuntimeAssembly();
        var subject = RequireRuntimeSubject(runtime.Assembly);
        foreach (var item in WideningCases)
        {
            var method = RequireRuntimeMethod(subject, item.MethodName);
            var target = project.FindCallable(item.MethodName);
            foreach (var input in item.BoundaryValues)
            {
                var expected = Convert.ToInt64(
                    input,
                    CultureInfo.InvariantCulture);
                var runtimeResult = method.Invoke(null, [input]);
                Assert.That(
                    Convert.ToInt64(
                        runtimeResult,
                        CultureInfo.InvariantCulture),
                    Is.EqualTo(expected),
                    $"{item.MethodName}({Format(input)})");
                AssertIntegerReturn(
                    ExecuteIr(target, input),
                    expected,
                    $"{item.MethodName}({Format(input)})");
            }
        }
    }

    [Test]
    public async Task CheckedLongArithmeticAgreesAcrossRuntimeIrAndSmt()
    {
        using var project = DifferentialProject.Create(
            CreateArithmeticSource());
        var request = project.CreateRequest();
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.ClaimResults,
                Has.Length.EqualTo(ArithmeticCases.Length));
            Assert.That(
                response.ClaimResults.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.None));
            Assert.That(
                response.CallableResults.Select(static result =>
                    result.Coverage),
                Is.All.EqualTo(WorkerCallableCoverage.Complete));
        }

        using var runtime = project.EmitRuntimeAssembly();
        var subject = RequireRuntimeSubject(runtime.Assembly);
        foreach (var item in ArithmeticCases)
        {
            var result = response.ClaimResults.Single(candidate =>
                CallableId(response, candidate).Contains(
                    "." + item.MethodName + "(",
                    StringComparison.Ordinal));
            var method = RequireRuntimeMethod(subject, item.MethodName);
            var target = project.FindCallable(item.MethodName);
            var execution = ExecuteIr(target, item.Inputs);
            if (item.ExpectedException == null)
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(
                        method.Invoke(null, item.Inputs),
                        Is.EqualTo(item.ExpectedResult),
                        item.MethodName);
                    AssertIntegerReturn(
                        execution,
                        item.ExpectedResult!.Value,
                        item.MethodName);
                    Assert.That(
                        result.Vacuity,
                        Is.EqualTo(WorkerVacuityKind.None),
                        item.MethodName);
                }
            }
            else
            {
                var thrown = Assert.Throws<TargetInvocationException>(
                    (Action)(() =>
                    {
                        method.Invoke(null, item.Inputs);
                    }),
                    item.MethodName);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(
                        thrown!.InnerException,
                        Is.TypeOf(item.ExpectedException),
                        item.MethodName);
                    Assert.That(
                        execution.Status,
                        Is.EqualTo(IrProgramExecutionStatus.Exception),
                        item.MethodName);
                    Assert.That(
                        execution.Exception?.Kind,
                        Is.EqualTo(item.ExpectedIrException),
                        item.MethodName);
                    Assert.That(
                        result.Vacuity,
                        Is.EqualTo(
                            WorkerVacuityKind.NoModeledNormalReturn),
                        item.MethodName);
                    Assert.That(
                        result.ProofCore,
                        Has.Some.StartsWith("normal-completion:"),
                        item.MethodName);
                }
            }
        }
    }

    [Test]
    public async Task WidthSensitiveConversionsAreProvenWithTypedSemantics()
    {
        using var project = DifferentialProject.Create(
            CreateUnsupportedConversionSource());
        var request = project.CreateRequest();
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        var conversions = response.ClaimResults
            .Where(result => CallableId(response, result).Contains(
                "Conversion(",
                StringComparison.Ordinal))
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(conversions, Has.Length.EqualTo(4));
            Assert.That(
                conversions.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                conversions.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.None));
        }
    }

    private static string CreateSource()
    {
        var methods = SupportedCases.Select(static item =>
            $$"""
                public static {{item.TypeName}} {{item.MethodName}}(
                    {{item.TypeName}} value,
                    bool chooseFirst) {
                    Contract.Ensures(
                        Contract.Result<{{item.TypeName}}>() ==
                        Contract.Old(value));
                    Contract.Ensures(
                        Contract.Result<{{item.TypeName}}>() >=
                            {{item.TypeName}}.MinValue &&
                        Contract.Result<{{item.TypeName}}>() <=
                            {{item.TypeName}}.MaxValue);
                    Contract.Ensures(
                        Contract.Result<{{item.TypeName}}>() !=
                            {{item.TypeName}}.MaxValue);
                    var snapshot = value;
                    if (chooseFirst) {
                        value = snapshot;
                        return value;
                    }
                    value = snapshot;
                    return value;
                }
            """);
        var comparisons = SupportedCases.Select(static item =>
            $$"""
                public static int Compare{{item.MethodName}}(
                    {{item.TypeName}} left,
                    {{item.TypeName}} right) {
                    Contract.Ensures(
                        (left < right && Contract.Result<int>() == -1) ||
                        (left == right && Contract.Result<int>() == 0) ||
                        (left > right && Contract.Result<int>() == 1));
                    Contract.Ensures(
                        (left <= right) ==
                            (Contract.Result<int>() <= 0));
                    Contract.Ensures(
                        (left >= right) ==
                            (Contract.Result<int>() >= 0));
                    Contract.Ensures(
                        (left != right) ==
                            (Contract.Result<int>() != 0));
                    Contract.Ensures(Contract.Result<int>() != 1);
                    if (left < right) {
                        return -1;
                    }
                    if (left == right) {
                        return 0;
                    }
                    return 1;
                }
            """);
        return
            """
            using SharpProof.Attributes;

            public static class ScalarDifferentialSubject {
            """ +
            Environment.NewLine +
            string.Join(Environment.NewLine, methods) +
            Environment.NewLine +
            string.Join(Environment.NewLine, comparisons) +
            Environment.NewLine +
            """
            }
            """;
    }

    private static string CreateWideningSource()
    {
        var methods = WideningCases.Select(static item =>
            $$"""
                public static {{item.TargetType}} {{item.MethodName}}(
                    {{item.SourceType}} value) {
                    Contract.Ensures(
                        Contract.Result<{{item.TargetType}}>() == value);
                    return ({{item.TargetType}})value;
                }
            """);
        return
            """
            using SharpProof.Attributes;

            public static class ScalarDifferentialSubject {
            """ +
            Environment.NewLine +
            string.Join(Environment.NewLine, methods) +
            Environment.NewLine +
            """
            }
            """;
    }

    private static string CreateArithmeticSource()
    {
        var methods = ArithmeticCases.Select(static item =>
        {
            var parameters = item.Inputs.Length == 1
                ? "long value"
                : "long left, long right";
            var names = item.Inputs.Length == 1
                ? UnaryParameterNames
                : BinaryParameterNames;
            var requires = names.Select((name, index) =>
                $"        Contract.Requires({name} == " +
                ToLongLiteral((long)item.Inputs[index]) + ");");
            var ensures = item.ExpectedException == null
                ? "        Contract.Ensures(Contract.Result<long>() == " +
                  ToLongLiteral(item.ExpectedResult!.Value) + ");"
                : "        Contract.Ensures(false);";
            return
                $$"""
                    public static long {{item.MethodName}}({{parameters}}) {
                    {{string.Join(Environment.NewLine, requires)}}
                    {{ensures}}
                        return {{item.Expression}};
                    }
                """;
        });
        return
            """
            using SharpProof.Attributes;

            public static class ScalarDifferentialSubject {
            """ +
            Environment.NewLine +
            string.Join(Environment.NewLine, methods) +
            Environment.NewLine +
            """
            }
            """;
    }

    private static string CreateUnsupportedConversionSource()
    {
        return
            """
            using SharpProof.Attributes;

            public static class ScalarDifferentialSubject {
                public static int UncheckedLongConversion(long value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return unchecked((int)value);
                }

                public static int CheckedLongConversion(long value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return checked((int)value);
                }

                public static byte UncheckedByteConversion(int value) {
                    Contract.Ensures(
                        Contract.Result<byte>() >= byte.MinValue);
                    return unchecked((byte)value);
                }

                public static int UncheckedUnsignedConversion(uint value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return unchecked((int)value);
                }
            }
            """;
    }

    private static int ClaimOrdinal(
        WorkerVerifyResponse response,
        WorkerClaimResult result)
    {
        return response.Manifest.Claims.Single(claim =>
            string.Equals(
                claim.ClaimId,
                result.ClaimId,
                StringComparison.Ordinal)).Ordinal;
    }

    private static string CallableId(
        WorkerVerifyResponse response,
        WorkerClaimResult result)
    {
        return response.Manifest.Claims.Single(claim =>
            string.Equals(
                claim.ClaimId,
                result.ClaimId,
                StringComparison.Ordinal)).CallableId;
    }

    private static string Format(object value)
    {
        return value is char character
            ? ((int)character).ToString(CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture) ??
              string.Empty;
    }

    private static Type RequireRuntimeSubject(Assembly assembly)
    {
        return assembly.GetType(
            "ScalarDifferentialSubject",
            throwOnError: true)!;
    }

    private static MethodInfo RequireRuntimeMethod(Type subject, string name)
    {
        return subject.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Static) ??
            throw new InvalidOperationException(
                $"Runtime method '{name}' is missing.");
    }

    private static IrProgramExecutionResult ExecuteIr(
        CompilerCallablePreparation target,
        params object[] inputs)
    {
        var total = target.Total ??
            throw new InvalidOperationException(
                $"Callable '{target.Entry.CallableId}' has no Total program.");
        var factory = total.Program.Factory;
        var initial = total.Parameters.Select((parameter, ordinal) => (parameter.Entry, Value: inputs[ordinal] switch
        {
            bool value => factory.CreateBooleanValue(value),
            { } value => factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type,
                Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            _ => throw new InvalidOperationException(
                "Null is not a supported scalar matrix input.")
        })).ToDictionary(static item => item.Entry, static item => item.Value);
        return new IrProgramInterpreter(factory).Execute(total.Program, initial);
    }

    private static void AssertIntegerReturn(
        IrProgramExecutionResult execution,
        long expected,
        string message)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                execution.Status,
                Is.EqualTo(IrProgramExecutionStatus.Returned),
                message);
            Assert.That(
                execution.ReturnValue?.Kind,
                Is.EqualTo(IrValueKind.Integer),
                message);
            Assert.That(
                execution.ReturnValue?.IntegerNumericValue,
                Is.EqualTo(new System.Numerics.BigInteger(expected)),
                message);
        }
    }

    private static WideningCase Widen(
        string sourceName,
        string sourceType,
        string targetName,
        string targetType)
    {
        return new(
            "Widen" + sourceName + "To" + targetName,
            sourceType,
            targetType,
            SupportedCases.Single(item =>
                item.MethodName == sourceName).BoundaryValues);
    }

    private static ArithmeticCase Binary(
        string methodName,
        string expression,
        long left,
        long right,
        long expected)
    {
        return new(methodName, expression, [left, right], expected, null, null);
    }

    private static ArithmeticCase BinaryOverflow(
        string methodName,
        string expression,
        long left,
        long right)
    {
        return BinaryException(
            methodName,
            expression,
            left,
            right,
            typeof(OverflowException),
            IrExceptionKind.Overflow);
    }

    private static ArithmeticCase BinaryException(
        string methodName,
        string expression,
        long left,
        long right,
        Type exception,
        IrExceptionKind irException)
    {
        return new(
            methodName,
            expression,
            [left, right],
            null,
            exception,
            irException);
    }

    private static ArithmeticCase Unary(
        string methodName,
        string expression,
        long value,
        long expected)
    {
        return new(methodName, expression, [value], expected, null, null);
    }

    private static string ToLongLiteral(long value)
    {
        return value switch
        {
            long.MinValue => "long.MinValue",
            long.MaxValue => "long.MaxValue",
            _ => value.ToString(CultureInfo.InvariantCulture) + "L"
        };
    }

    private sealed record ScalarCase(
        string MethodName,
        string TypeName,
        object[] BoundaryValues);

    private sealed record WideningCase(
        string MethodName,
        string SourceType,
        string TargetType,
        object[] BoundaryValues);

    private sealed record ArithmeticCase(
        string MethodName,
        string Expression,
        object[] Inputs,
        long? ExpectedResult,
        Type? ExpectedException,
        IrExceptionKind? ExpectedIrException);

    private sealed class DifferentialProject : IDisposable
    {
        private readonly TempDirectory _temporary;
        private readonly string _sourcePath;
        private CompilerCallablePreparation[] _callables = [];
        private CSharpCompilation? _compilation;

        private DifferentialProject(TempDirectory temporary)
        {
            _temporary = temporary;
            DirectoryPath = temporary.FullName;
            _sourcePath = Path.Combine(DirectoryPath, "Subject.cs");
        }

        internal string DirectoryPath
        {
            get;
        }

        internal static DifferentialProject Create(string source)
        {
            var temporary = TempDirectory.CreateOwned(
                "SharpProof.ScalarDifferential",
                string.Empty,
                "Refusing to remove an unexpected scalar-differential directory.");
            try
            {
                var sourcePath = Path.Combine(
                    temporary.FullName,
                    "Subject.cs");
                File.WriteAllText(
                    sourcePath,
                    source,
                    new System.Text.UTF8Encoding(false));
                return new DifferentialProject(temporary);
            }
            catch
            {
                temporary.Dispose();
                throw;
            }
        }

        internal WorkerVerifyRequest CreateRequest()
        {
            var compilation = CreateCompilation();
            var discovery = new ClaimManifestBuilder(compilation).Build();
            var artifact = CompilerManifestArtifactProducer.Create(
                compilation,
                DirectoryPath,
                "net8.0",
                WorkerFeatureSet.All,
                discovery,
                WorkerBudgets.DefaultMaximumExpressionDepth,
                CancellationToken.None);
            _callables = [
                .. CompilerManifestArtifactJson.DecodeCallables(artifact)
            ];
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                CompilerManifestArtifactJson.Serialize(artifact));
            var artifactPath = Path.Combine(
                DirectoryPath,
                "compiler-manifest.json");
            File.WriteAllBytes(artifactPath, bytes);
            return new WorkerVerifyRequest
            {
                CompilerManifest = new WorkerFileReference
                {
                    Path = artifactPath,
                    Sha256 = WorkerProtocolJson.ComputeSha256(bytes)
                },
                Cache = new WorkerCacheOptions
                {
                    Enabled = false,
                    Directory = Path.Combine(DirectoryPath, "cache")
                }
            };
        }

        internal CompilerCallablePreparation FindCallable(string methodName)
        {
            return _callables.Single(target =>
                target.Entry.CallableId.Contains(
                    "." + methodName + "(",
                    StringComparison.Ordinal));
        }

        internal RuntimeAssembly EmitRuntimeAssembly()
        {
            var compilation = CreateCompilation();
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.That(
                emit.Success,
                Is.True,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Select(static diagnostic =>
                        diagnostic.ToString())));
            image.Position = 0;
            var context = new AssemblyLoadContext(
                "SharpProof.ScalarDifferential." +
                Guid.NewGuid().ToString("N"),
                isCollectible: true);
            context.Resolving += ResolveContractAssembly;
            return new RuntimeAssembly(
                context,
                context.LoadFromStream(image));
        }

        public void Dispose()
        {
            _temporary.Dispose();
        }

        private CSharpCompilation CreateCompilation()
        {
            if (_compilation is not null)
            {
                return _compilation;
            }
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp12);
            var syntaxTree = CSharpSyntaxTree.ParseText(
                SourceText.From(
                    File.ReadAllText(_sourcePath),
                    System.Text.Encoding.UTF8,
                    SourceHashAlgorithm.Sha256),
                parseOptions,
                _sourcePath);
            var references = TestMetadataReferences.ForFileNames(
                RequiredReferenceFileNames,
                sort: true);
            return _compilation = CSharpCompilation.Create(
                "ScalarDifferential",
                [syntaxTree],
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    nullableContextOptions: NullableContextOptions.Enable,
                    deterministic: true,
                    concurrentBuild: false));
        }

        internal static Assembly? ResolveContractAssembly(
            AssemblyLoadContext context,
            AssemblyName name)
        {
            return string.Equals(
                name.Name,
                typeof(Contract).Assembly.GetName().Name,
                StringComparison.Ordinal)
                ? context.LoadFromAssemblyPath(typeof(Contract).Assembly.Location)
                : null;
        }
    }

    private sealed class RuntimeAssembly(
        AssemblyLoadContext context,
        Assembly assembly) : IDisposable
    {
        internal Assembly Assembly { get; } = assembly;

        public void Dispose()
        {
            context.Resolving -= DifferentialProject.ResolveContractAssembly;
            context.Unload();
        }
    }
}
