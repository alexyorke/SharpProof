using System.Text.Json;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerTests {
    private static readonly string[] InvalidBudgetErrorCodes = [
        "protocol.unsupported",
        "budgets.rlimit",
        "budgets.method_rlimit",
        "budgets.parallelism",
        "budgets.expression_depth",
        "budgets.worker_processes",
        "budgets.wall_order",
        "cache.maximum_bytes"
    ];

    private static readonly string[] MissingCompilationErrorCodes = [
        "compilation.target_framework",
        "compilation.language_version",
        "compilation.nullable",
        "compilation.optimization",
        "compilation.checked_overflow",
        "compilation.allow_unsafe",
        "compilation.deterministic",
        "compilation.output_kind",
        "compilation.platform"
    ];

    private static readonly string[] RequiredReferenceFileNames = [
        "System.Private.CoreLib.dll",
        "System.Linq.dll",
        "System.Runtime.dll",
        "netstandard.dll"
    ];

    [Test]
    public void ProtocolValidationClosesVersionAndBudgetBounds() {
        var request = new WorkerVerifyRequest {
            ProtocolVersion = "unsupported",
            ProjectDirectory = "x",
            SourceFiles = ["a.cs"],
            ReferenceAssemblies = ["a.dll"],
            Budgets = new WorkerBudgets {
                QueryRlimit = 0,
                MethodRlimit = 0,
                MaxParallelism = 5,
                MaximumExpressionDepth = 300,
                MaxWorkerProcesses = 5,
                MethodWallTimeMilliseconds = 20,
                ProjectWallTimeMilliseconds = 10
            },
            Cache = new WorkerCacheOptions {
                MaximumBytes = WorkerCacheOptions.DefaultMaximumBytes + 1
            }
        };
        var validation = WorkerProtocolJson.Validate(request);
        Assert.That(validation.IsValid, Is.False);
        Assert.That(
            validation.Errors.Select(static error => error.Code),
            Is.SupersetOf(InvalidBudgetErrorCodes));
        Assert.Throws<JsonException>((Action)(() =>
            WorkerProtocolJson.DeserializeRequest(
                """{"protocolVersion":"1","unknown":true}""")));

        request.ProtocolVersion = WorkerProtocolVersions.Current;
        request.Compilation = CreateCompilationOptions();
        request.Budgets.QueryRlimit = 2;
        request.Budgets.MethodRlimit = 1;
        validation = WorkerProtocolJson.Validate(request);
        Assert.That(
            validation.Errors.Select(static error => error.Code),
            Does.Contain("budgets.rlimit_order"));
    }

    [Test]
    public void ProtocolDefaultsFailClosedWithoutCompilationIdentity() {
        var request = new WorkerVerifyRequest {
            ProjectDirectory = "x",
            SourceFiles = ["a.cs"],
            ReferenceAssemblies = ["a.dll"]
        };

        var validation = WorkerProtocolJson.Validate(request);

        Assert.That(validation.IsValid, Is.False);
        Assert.That(
            validation.Errors.Select(static error => error.Code),
            Is.SupersetOf(MissingCompilationErrorCodes));
    }

    [Test]
    public async Task WorkerCompilationUsesTheRequestedSemanticOptions() {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Compilation.LanguageVersion = "13.0";
        request.Compilation.NullableContext =
            WorkerNullableContext.Warnings;
        request.Compilation.Optimization =
            WorkerOptimizationLevel.Debug;
        request.Compilation.CheckOverflow = true;
        request.Compilation.AllowUnsafe = true;
        request.Compilation.Deterministic = false;
        request.Compilation.OutputKind =
            WorkerOutputKind.ConsoleApplication;
        request.Compilation.Platform = WorkerPlatform.X64;
        var snapshot = await WorkerInputSnapshot.LoadAsync(
            request,
            CancellationToken.None);

        var compilation = WorkerCompilation.Create(request, snapshot);
        var parse = (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)
            compilation.SyntaxTrees.Single().Options;
        var options = compilation.Options;

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                parse.LanguageVersion,
                Is.EqualTo(
                    Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp13));
            Assert.That(
                options.NullableContextOptions,
                Is.EqualTo(
                    Microsoft.CodeAnalysis.NullableContextOptions.Warnings));
            Assert.That(
                options.OptimizationLevel,
                Is.EqualTo(Microsoft.CodeAnalysis.OptimizationLevel.Debug));
            Assert.That(options.CheckOverflow, Is.True);
            Assert.That(options.AllowUnsafe, Is.True);
            Assert.That(options.Deterministic, Is.False);
            Assert.That(
                options.OutputKind,
                Is.EqualTo(
                    Microsoft.CodeAnalysis.OutputKind.ConsoleApplication));
            Assert.That(
                options.Platform,
                Is.EqualTo(Microsoft.CodeAnalysis.Platform.X64));
        }
    }

    [Test]
    public async Task EverySemanticCompilationOptionInvalidatesTheCache() {
        using var project = TestProject.Create(TautologySource);
        var requests = new List<WorkerVerifyRequest>();
        Add();
        Add(static options => options.TargetFramework = "net8.0-windows");
        Add(static options => options.LanguageVersion = "11.0");
        Add(static options =>
            options.NullableContext = WorkerNullableContext.Warnings);
        Add(static options =>
            options.Optimization = WorkerOptimizationLevel.Debug);
        Add(static options => options.CheckOverflow = true);
        Add(static options => options.AllowUnsafe = true);
        Add(static options => options.Deterministic = false);
        Add(static options =>
            options.OutputKind = WorkerOutputKind.NetModule);
        Add(static options => options.Platform = WorkerPlatform.X64);
        Add(
            mutateRequest: static request =>
                request.DefineConstants =
                    [Contract.ConditionalSymbol, "EXTRA"]);
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);
        var hashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var request in requests) {
            var response = await worker.VerifyAsync(request);
            Assert.That(response.Errors, Is.Empty);
            Assert.That(hashes.Add(response.InputHash), Is.True);
        }

        Assert.That(backend.CallCount, Is.EqualTo(requests.Count));
        Assert.That(
            Directory.GetFiles(project.CacheDirectory, "*.json"),
            Has.Length.EqualTo(requests.Count));

        void Add(
            Action<WorkerCompilationOptions>? mutate = null,
            Action<WorkerVerifyRequest>? mutateRequest = null) {
            var request = project.CreateRequest(cacheEnabled: true);
            mutate?.Invoke(request.Compilation);
            mutateRequest?.Invoke(request);
            requests.Add(request);
        }
    }

    [Test]
    public async Task ToolAndApiSpecIdentitiesInvalidateTheInputHash() {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        var baselineIdentity = WorkerCacheIdentity.Current;
        var changedTool = new WorkerCacheIdentity(
            baselineIdentity.ToolIdentity,
            baselineIdentity.ToolVersion + ".changed",
            baselineIdentity.ApiSpecIdentity,
            baselineIdentity.ApiSpecVersion);
        var changedSpecs = new WorkerCacheIdentity(
            baselineIdentity.ToolIdentity,
            baselineIdentity.ToolVersion,
            baselineIdentity.ApiSpecIdentity,
            baselineIdentity.ApiSpecVersion + ".changed");

        var baseline = await WorkerInputSnapshot.LoadAsync(
            request,
            baselineIdentity,
            CancellationToken.None);
        var tool = await WorkerInputSnapshot.LoadAsync(
            request,
            changedTool,
            CancellationToken.None);
        var specs = await WorkerInputSnapshot.LoadAsync(
            request,
            changedSpecs,
            CancellationToken.None);

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                baselineIdentity.ToolIdentity,
                Is.EqualTo(WorkerCacheIdentity.CurrentToolIdentity));
            Assert.That(baselineIdentity.ToolVersion, Is.Not.Empty);
            Assert.That(
                baselineIdentity.ApiSpecIdentity,
                Is.EqualTo(
                    SharpProof.Specs.ApiSpecTable.DefaultTableIdentity));
            Assert.That(
                baselineIdentity.ApiSpecVersion,
                Is.EqualTo(
                    SharpProof.Specs.ApiSpecTable.DefaultTableVersion));
            Assert.That(tool.InputHash, Is.Not.EqualTo(baseline.InputHash));
            Assert.That(specs.InputHash, Is.Not.EqualTo(baseline.InputHash));
        }
    }

    [Test]
    public async Task RealSourceAndReferenceInputsProduceDeterministicProofs() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long ZBroken(long value) {
                    Contract.Ensures(Contract.Result<long>() > value);
                    return value;
                }
                public static long AIdentity(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var firstWorker = SharpProofWorker.Create(request.Budgets);
        using var secondWorker = SharpProofWorker.Create(request.Budgets);
        var first = await firstWorker.VerifyAsync(request);
        var second = await secondWorker.VerifyAsync(request);

        Assert.That(first.Errors, Is.Empty);
        Assert.That(first.Records.Length, Is.EqualTo(2));
        Assert.That(
            first.Records.Select(static record => record.Status),
            Is.EquivalentTo(new[] {
                WorkerVerificationStatus.Proven,
                WorkerVerificationStatus.Refuted
            }));
        Assert.That(
            first.Records.Select(static record => record.CallableId),
            Is.Ordered);
        Assert.That(
            WorkerProtocolJson.SerializeResponse(second),
            Is.EqualTo(WorkerProtocolJson.SerializeResponse(first)));
    }

    [Test]
    public async Task PartialMethodDiscoveryUsesOnlyTheImplementation() {
        using var project = TestProject.Create(
            (
                "Definition.cs",
                """
                public static partial class Subject {
                    public static partial long Identity(long value);
                }
                """),
            (
                "Implementation.cs",
                """
                using SharpProof.Attributes;
                public static partial class Subject {
                    public static partial long Identity(long value) {
                        Contract.Ensures(
                            Contract.Result<long>() == value);
                        return value;
                    }
                }
                """));
        var request = project.CreateRequest(cacheEnabled: false);
        var snapshot = await WorkerInputSnapshot.LoadAsync(
            request,
            CancellationToken.None);
        var compilation = WorkerCompilation.Create(request, snapshot);
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        var verifier = new CallableVerifier(
            compilation,
            backend,
            request.Budgets.MaximumExpressionDepth);

        var target = verifier.Discover().Single(candidate =>
            candidate.Method.Name == "Identity");
        using (Assert.EnterMultipleScope()) {
            Assert.That(target.Method.PartialDefinitionPart, Is.Not.Null);
            Assert.That(target.Method.PartialImplementationPart, Is.Null);
            Assert.That(
                Path.GetFileName(target.Declaration.SyntaxTree.FilePath),
                Is.EqualTo("Implementation.cs"));
        }

        using var worker = new SharpProofWorker(backend);
        var response = await worker.VerifyAsync(request);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.Records, Has.Length.EqualTo(1));
        Assert.That(
            response.Records[0].Status,
            Is.EqualTo(WorkerVerificationStatus.Proven));
        Assert.That(backend.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GeneratedContractVerdictsMatchConcreteRuntime() {
        var cases = CreateRuntimeContractCases(seed: 23063, count: 24);
        using var project = TestProject.Create(
            CreateRuntimeContractSource(cases));
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.Records, Has.Length.EqualTo(cases.Length));

        var runtimeRequest = project.CreateRequest(cacheEnabled: false);
        runtimeRequest.DefineConstants = [];
        var snapshot = await WorkerInputSnapshot.LoadAsync(
            runtimeRequest,
            CancellationToken.None);
        var runtimeCompilation = WorkerCompilation.Create(
            runtimeRequest,
            snapshot);
        using var image = new MemoryStream();
        var emit = runtimeCompilation.Emit(image);
        Assert.That(
            emit.Success,
            Is.True,
            string.Join(
                Environment.NewLine,
                emit.Diagnostics.Select(static diagnostic =>
                    diagnostic.ToString())));

        var loadContext = new System.Runtime.Loader.AssemblyLoadContext(
            "SharpProof.Worker.Test.RuntimeContractOracle",
            isCollectible: true);
        loadContext.Resolving += ResolveRuntimeContractAssembly;
        try {
            image.Position = 0;
            var assembly = loadContext.LoadFromStream(image);
            var fixture = assembly.GetType(
                    "RuntimeContractOracle",
                    throwOnError: true)!;
            foreach (var item in cases) {
                var record = response.Records.Single(candidate =>
                    candidate.CallableId.Contains(
                        "." + item.MethodName + "(",
                        StringComparison.Ordinal));
                Assert.That(
                    record.Status,
                    Is.EqualTo(item.ExpectedStatus),
                    item.MethodName);

                var method = fixture.GetMethod(
                        item.MethodName,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static) ??
                    throw new InvalidOperationException(
                        $"Runtime method '{item.MethodName}' is missing.");
                var runtimeWitnesses = 0;
                foreach (var input in item.Inputs) {
                    if (!item.Requires(input)) continue;
                    var result = (long)method.Invoke(null, [input])!;
                    var holds = item.Ensures(input, result);
                    if (!holds) runtimeWitnesses++;
                    if (item.ExpectedStatus ==
                        WorkerVerificationStatus.Proven)
                        Assert.That(
                            holds,
                            Is.True,
                            $"{item.MethodName}({input})");
                }
                if (item.ExpectedStatus ==
                    WorkerVerificationStatus.Refuted)
                    Assert.That(
                        runtimeWitnesses,
                        Is.GreaterThan(0),
                        item.MethodName);
            }
        }
        finally {
            loadContext.Resolving -= ResolveRuntimeContractAssembly;
            loadContext.Unload();
        }
    }

    [Test]
    public async Task NarrowIntegralSourceDomainsAreHygienicAndExact() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static sbyte SByteIdentity(sbyte value) {
                    Contract.Ensures(
                        Contract.Result<sbyte>() >= sbyte.MinValue &&
                        Contract.Result<sbyte>() <= sbyte.MaxValue);
                    return value;
                }
                public static byte ByteIdentity(byte value) {
                    Contract.Ensures(
                        Contract.Result<byte>() >= byte.MinValue &&
                        Contract.Result<byte>() <= byte.MaxValue);
                    return value;
                }
                public static short Int16Identity(short value) {
                    Contract.Ensures(
                        Contract.Result<short>() >= short.MinValue &&
                        Contract.Result<short>() <= short.MaxValue);
                    return value;
                }
                public static ushort UInt16Identity(ushort value) {
                    Contract.Ensures(
                        Contract.Result<ushort>() >= ushort.MinValue &&
                        Contract.Result<ushort>() <= ushort.MaxValue);
                    return value;
                }
                public static char CharIdentity(char value) {
                    Contract.Ensures(
                        Contract.Result<char>() >= char.MinValue &&
                        Contract.Result<char>() <= char.MaxValue);
                    return value;
                }
                public static int Id(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return value;
                }
                public static uint UInt32Identity(uint value) {
                    Contract.Ensures(
                        Contract.Result<uint>() >= uint.MinValue &&
                        Contract.Result<uint>() <= uint.MaxValue);
                    return value;
                }
                public static long Int64Identity(long value) {
                    Contract.Ensures(
                        Contract.Result<long>() >= long.MinValue &&
                        Contract.Result<long>() <= long.MaxValue);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.Records, Has.Length.EqualTo(8));
        Assert.That(
            response.Records.Select(static record => record.Status),
            Is.All.EqualTo(WorkerVerificationStatus.Proven));
        var intIdentity = response.Records.Single(record =>
            record.CallableId.Contains(
                ".Id(",
                StringComparison.Ordinal));
        Assert.That(
            intIdentity.ProofCore,
            Is.EqualTo(["domain:parameter:0"]));
        Assert.That(
            response.Records
                .Where(record => !record.CallableId.Contains(
                    ".Int64Identity(",
                    StringComparison.Ordinal))
                .SelectMany(static record => record.ProofCore),
            Is.All.EqualTo("domain:parameter:0"));
    }

    [Test]
    public async Task SourceDomainAssumptionsUseLoweredEvidence() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Id(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        var backend = new CapturingBackend(
            BackendCheckResult.Unsatisfiable([0]));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

        var query = backend.Query;
        Assert.That(query.Assumptions, Has.Length.EqualTo(2));
        Assert.That(
            query.Assumptions[0].Justification,
            Is.TypeOf<LoweredJustification>());
        Assert.That(
            query.Assumptions[1].Justification,
            Is.TypeOf<LoweredJustification>());
        Assert.That(
            response.Records.Single().ProofCore,
            Is.EqualTo(["domain:parameter:0"]));
    }

    [Test]
    public async Task DirectMathAbsReturnIsProvenFromItsApiSpec() {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Absolute(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= 0);
                    return Math.Abs(value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.Records.Single();
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                record.Status,
                Is.EqualTo(WorkerVerificationStatus.Proven));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerVerificationReason.None));
            Assert.That(
                record.ProofCore,
                Is.EqualTo(["spec:bcl.math.abs.int32"]));
            Assert.That(record.Model, Is.Empty);
        }
    }

    [Test]
    public async Task SpecResultFacetsProveConcatAndArrayEmptyContracts() {
        using var project = TestProject.Create(
            """
            #nullable enable
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static string Concat(string? left, string? right) {
                    Contract.Ensures(
                        Contract.Result<string>() != null);
                    return string.Concat(left, right);
                }

                public static int[] Empty() {
                    Contract.Ensures(
                        Contract.Result<int[]>() != null);
                    Contract.Ensures(
                        Contract.Result<int[]>().Length == 0);
                    var result = Array.Empty<int>();
                    return result;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(
            response.Records,
            Has.Length.EqualTo(3),
            string.Join(
                Environment.NewLine,
                response.Records.Select(record =>
                    record.CallableId + " / " +
                    record.ContractOrdinal + " / " +
                    record.Status + " / " +
                    record.Reason)));
        Assert.That(
            response.Records.Select(static record => record.Status),
            Is.All.EqualTo(WorkerVerificationStatus.Proven));
        var concat = response.Records.Single(record =>
            record.CallableId.Contains(
                ".Concat(",
                StringComparison.Ordinal));
        var empty = response.Records
            .Where(record => record.CallableId.Contains(
                ".Empty",
                StringComparison.Ordinal))
            .ToArray();
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                concat.ProofCore,
                Is.EqualTo(["spec:bcl.string.concat.string-string"]));
            Assert.That(empty, Has.Length.EqualTo(2));
            foreach (var record in empty)
                Assert.That(
                    record.ProofCore,
                    Is.EqualTo(["spec:bcl.array.empty"]));
        }
    }

    [Test]
    public async Task EnumerableCardinalityIsNotTreatedAsArrayCardinality() {
        using var project = TestProject.Create(
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            using SharpProof.Attributes;
            public static class Subject {
                public static IEnumerable<int> Empty() {
                    Contract.Ensures(
                        Contract.Result<IEnumerable<int>>() != null);
                    return Enumerable.Empty<int>();
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(
            response.Errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                response.Errors.Select(error =>
                    error.Code + ": " + error.Message)));
        var record = response.Records.Single();
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                record.Status,
                Is.EqualTo(WorkerVerificationStatus.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerVerificationReason.UnsupportedBody));
            Assert.That(record.ProofCore, Is.Empty);
        }
    }

    [Test]
    public async Task ArraySummaryDoesNotAuthorizeALaterImpureCallHavoc() {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                private static int s_ambient;
                private static void TouchAmbient() => s_ambient++;

                public static int[] Unsafe() {
                    Contract.Ensures(
                        Contract.Result<int[]>() != null);
                    var result = Array.Empty<int>();
                    TouchAmbient();
                    return result;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.Records.Single();
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                record.Status,
                Is.EqualTo(WorkerVerificationStatus.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerVerificationReason.UnsupportedBody));
            Assert.That(record.ProofCore, Is.Empty);
        }
    }

    [Test]
    public async Task AcyclicCfgLocalsBranchesAndMultipleReturnsAreProven() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static bool ThroughLocals(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() == value);
                    var local = value;
                    local = !!local;
                    return local;
                }

                public static bool Choose(
                    bool chooseLeft,
                    bool left,
                    bool right) {
                    Contract.Ensures(
                        Contract.Result<bool>() ==
                        (chooseLeft ? left : right));
                    if (chooseLeft) {
                        return left;
                    }
                    return right;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.Records, Has.Length.EqualTo(2));
        Assert.That(
            response.Records.Select(static record => record.Status),
            Is.All.EqualTo(WorkerVerificationStatus.Proven));
        Assert.That(
            response.Records.Select(static record => record.Reason),
            Is.All.EqualTo(WorkerVerificationReason.None));
    }

    [Test]
    public async Task OldUsesEntryStateBeforeParameterMutation() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Flip(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() !=
                        Contract.Old(value));
                    value = !value;
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.Records.Single();
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                record.Status,
                Is.EqualTo(WorkerVerificationStatus.Proven));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerVerificationReason.None));
        }
    }

    [Test]
    public async Task LoopsAndUnspecifiedCallsAbstainSafely() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                private static bool Read(bool value) => value;

                public static bool Loop(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() == false);
                    while (value) {
                        value = false;
                    }
                    return value;
                }

                public static bool Call(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() == value);
                    return Read(value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.Records, Has.Length.EqualTo(2));
        Assert.That(
            response.Records.Select(static record => record.Status),
            Is.All.EqualTo(WorkerVerificationStatus.Unknown));
        Assert.That(
            response.Records.Select(static record => record.Reason),
            Is.All.EqualTo(WorkerVerificationReason.UnsupportedBody));
    }

    [Test]
    public async Task NestedSameShapeCallsRemainBoundToCompilerIdentity() {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Nested(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= 0);
                    return Math.Abs(Math.Sign(value));
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.Records.Single();
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                record.Status,
                Is.EqualTo(WorkerVerificationStatus.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerVerificationReason.UnsupportedBody));
            Assert.That(record.ProofCore, Is.Empty);
        }
    }

    [Test]
    public async Task SpecModeledCallCannotEmitAnUnreplayedCounterexample() {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Absolute(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= value);
                    return Math.Abs(value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.Records.Single();
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                record.Status,
                Is.EqualTo(WorkerVerificationStatus.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(
                    WorkerVerificationReason.CounterexampleReplayFailed));
            Assert.That(record.ProofCore, Is.Empty);
            Assert.That(record.Model, Is.Empty);
        }
    }

    [Test]
    public async Task WorkerProductPathInstantiatesApiSpecPostconditions() {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Absolute(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= 0);
                    return Math.Abs(value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        var backend = new CapturingBackend(
            BackendCheckResult.Unsatisfiable([0]));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

        var query = backend.Query;
        var specAssumption = query.Assumptions.Single(assumption =>
            assumption.Justification is SpecJustification);
        var predicate = specAssumption.Predicate as IrBinaryTerm;
        using (Assert.EnterMultipleScope()) {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(
                response.Records.Single().ProofCore,
                Is.EqualTo(["spec:bcl.math.abs.int32"]));
            Assert.That(predicate, Is.Not.Null);
            Assert.That(
                predicate!.Operator,
                Is.EqualTo(IrBinaryOperator.GreaterThanOrEqual));
            Assert.That(predicate.Left, Is.TypeOf<IrVariableTerm>());
            Assert.That(
                predicate.Right,
                Is.TypeOf<IrIntegerTerm>()
                    .And.Property(nameof(IrIntegerTerm.Value)).EqualTo(0));
        }
    }

    [Test]
    public async Task NarrowIntegralCounterexampleStaysInsideSourceDomain() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static byte NotAlwaysBelowMaximum(byte value) {
                    Contract.Ensures(
                        Contract.Result<byte>() < byte.MaxValue);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.Records.Single();
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                record.Status,
                Is.EqualTo(WorkerVerificationStatus.Refuted));
            Assert.That(
                record.Model.Single(value =>
                    value.Variable == "parameter:0").Value,
                Is.EqualTo(byte.MaxValue.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    [Test]
    public async Task WidthSensitiveArithmeticAndConversionsAbstain() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static int UncheckedContract(int value) {
                    Contract.Ensures(
                        unchecked(Contract.Result<int>() + 1) >
                        Contract.Result<int>());
                    return value;
                }
                public static int CheckedContract(int value) {
                    Contract.Ensures(
                        checked(Contract.Result<int>() + 1) >
                        Contract.Result<int>());
                    return value;
                }
                public static int UncheckedBody(long value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return unchecked((int)value);
                }
                public static int CheckedBody(long value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return checked((int)value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.Records, Has.Length.EqualTo(4));
        Assert.That(
            response.Records.Select(static record => record.Status),
            Is.All.EqualTo(WorkerVerificationStatus.Unknown));
        Assert.That(
            response.Records
                .Where(record => record.CallableId.Contains(
                    "Contract(",
                    StringComparison.Ordinal))
                .Select(static record => record.Reason),
            Is.All.EqualTo(
                WorkerVerificationReason.UnsupportedExpression));
        Assert.That(
            response.Records
                .Where(record => record.CallableId.Contains(
                    "Body(",
                    StringComparison.Ordinal))
                .Select(static record => record.Reason),
            Is.All.EqualTo(WorkerVerificationReason.UnsupportedBody));
    }

    [Test]
    public async Task BodyNormalCompletionConstrainsPartialCorrectness() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long DivideOverflow(long value) {
                    Contract.Requires(value == long.MinValue);
                    Contract.Ensures(false);
                    return value / -1L;
                }

                public static long DivideByZero(long value) {
                    Contract.Ensures(false);
                    return value / 0L;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.Records, Has.Length.EqualTo(2));
        Assert.That(
            response.Records.Select(static record => record.Status),
            Is.All.EqualTo(WorkerVerificationStatus.Proven),
            string.Join(
                Environment.NewLine,
                response.Records.Select(record =>
                    record.CallableId + ": " +
                    record.Status + " / " +
                    record.Reason)));
        Assert.That(
            response.Records.SelectMany(static record => record.ProofCore),
            Does.Contain("body:normal-completion"));
    }

    [Test]
    public async Task UndefinedPostconditionCannotProduceAProof() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Zero(long divisor) {
                    Contract.Ensures(
                        Contract.Result<long>() / divisor == 0L);
                    return 0L;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.Records.Single();
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                record.Status,
                Is.EqualTo(WorkerVerificationStatus.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(
                    WorkerVerificationReason.CounterexampleReplayFailed));
        }
    }

    [Test]
    public async Task MismatchedResultTypeAbstains() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Id(int value) {
                    Contract.Ensures(
                        checked(Contract.Result<long>() + 1L) >
                        Contract.Result<long>());
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.Records.Single();
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                record.Status,
                Is.EqualTo(WorkerVerificationStatus.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerVerificationReason.UnsupportedContract));
        }
    }

    [Test]
    public async Task NullableStringProofsAbstainWithoutNullTagEncoding() {
        using var project = TestProject.Create(
            """
            #nullable enable
            using SharpProof.Attributes;
            public static class Subject {
                public static string? ResultIntrinsic(string? value) {
                    Contract.Ensures(
                        Contract.Result<string?>() + "" ==
                        Contract.Result<string?>());
                    return value;
                }
                public static string? DirectParameter(string? value) {
                    Contract.Ensures(value + "" == value);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.Records, Has.Length.EqualTo(2));
        Assert.That(
            response.Records.Select(static record => record.Status),
            Is.All.EqualTo(WorkerVerificationStatus.Unknown));
        Assert.That(
            response.Records.Select(static record => record.Reason),
            Is.All.EqualTo(
                WorkerVerificationReason.UnsupportedExpression));
    }

    [Test]
    public async Task UnsupportedBodyAndDeepEnsuresAbstain() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                private static long Read(long value) => value;
                public static long Unsupported(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return Read(value);
                }
                public static long Deep(long value) {
                    Contract.Ensures(
                        value > 0 && value > 1 && value > 2 && value > 3);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaximumExpressionDepth = 3;
        using var worker = new SharpProofWorker(new CountingBackend(
            BackendCheckResult.Unsatisfiable([])));
        var response = await worker.VerifyAsync(request);

        Assert.That(
            response.Records.Select(static record => record.Reason),
            Is.EquivalentTo(new[] {
                WorkerVerificationReason.DeepEnsures,
                WorkerVerificationReason.UnsupportedBody
            }));
        Assert.That(
            response.Records.All(static record =>
                record.Status == WorkerVerificationStatus.Unknown),
            Is.True);
    }

    [Test]
    public async Task TrailingAssumeCannotBecomeAnEntryAssumption() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Invalid() {
                    Contract.Ensures(Contract.Result<long>() > 0);
                    return -1;
                    Contract.Assume(false);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.Records.Single();
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                record.Status,
                Is.EqualTo(WorkerVerificationStatus.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerVerificationReason.UnsupportedContract));
        }
    }

    [Test]
    public async Task CacheOnOffOutputsMatchAndTerminalOutcomesAreReused() {
        using var project = TestProject.Create(TautologySource);
        var enabled = project.CreateRequest(cacheEnabled: true);
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var firstWorker = new SharpProofWorker(backend);
        var first = await firstWorker.VerifyAsync(enabled);
        var second = await firstWorker.VerifyAsync(enabled);
        Assert.That(backend.CallCount, Is.EqualTo(1));
        Assert.That(
            WorkerProtocolJson.SerializeResponse(second),
            Is.EqualTo(WorkerProtocolJson.SerializeResponse(first)));

        var disabled = project.CreateRequest(cacheEnabled: false);
        var disabledBackend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var disabledWorker = new SharpProofWorker(disabledBackend);
        var withoutCache = await disabledWorker.VerifyAsync(disabled);
        Assert.That(
            WorkerProtocolJson.SerializeResponse(withoutCache),
            Is.EqualTo(WorkerProtocolJson.SerializeResponse(first)));
    }

    [Test]
    public async Task UnknownOutcomesNeverEnterTheCache() {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new CountingBackend(
            BackendCheckResult.Unknown(
                BackendFailureReason.ResourceLimit));
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        var second = await worker.VerifyAsync(request);

        Assert.That(backend.CallCount, Is.EqualTo(2));
        Assert.That(
            first.Records.Single().Status,
            Is.EqualTo(WorkerVerificationStatus.Unknown));
        Assert.That(
            WorkerProtocolJson.SerializeResponse(second),
            Is.EqualTo(WorkerProtocolJson.SerializeResponse(first)));
        Assert.That(
            Directory.Exists(project.CacheDirectory)
                ? Directory.GetFiles(project.CacheDirectory, "*.json")
                : [],
            Is.Empty);
    }

    [Test]
    public void CacheableResponseRequiresValidatedTerminalRecords() {
        var response = new WorkerVerifyResponse {
            ProtocolVersion = WorkerProtocolVersions.Current,
            InputHash = new string('a', 64),
            Records = [
                new WorkerVerificationRecord {
                    CallableId = "Subject.M()",
                    SourcePath = "input.cs",
                    Status = WorkerVerificationStatus.Proven,
                    Reason = WorkerVerificationReason.None
                }
            ],
            Errors = []
        };

        Assert.That(
            CacheableWorkerResponse.TryCreate(
                response,
                response.InputHash,
                out var proven),
            Is.True);
        Assert.That(proven, Is.Not.Null);

        response.Records[0].Status = WorkerVerificationStatus.Refuted;
        Assert.That(
            CacheableWorkerResponse.TryCreate(
                response,
                response.InputHash,
                out var refuted),
            Is.True);
        Assert.That(refuted, Is.Not.Null);

        response.Records[0].Status = WorkerVerificationStatus.Unknown;
        Assert.That(
            CacheableWorkerResponse.TryCreate(
                response,
                response.InputHash,
                out _),
            Is.False);

        response.Records[0].Status = WorkerVerificationStatus.Proven;
        response.Records[0].Reason =
            WorkerVerificationReason.InfrastructureFailure;
        Assert.That(
            CacheableWorkerResponse.TryCreate(
                response,
                response.InputHash,
                out _),
            Is.False);

        response.Records[0].Reason = WorkerVerificationReason.None;
        response.Errors = [
            new WorkerProtocolError {
                Code = "worker.error",
                Message = "Not cacheable."
            }
        ];
        Assert.That(
            CacheableWorkerResponse.TryCreate(
                response,
                response.InputHash,
                out _),
            Is.False);
        Assert.That(
            CacheableWorkerResponse.TryCreate(
                response,
                new string('b', 64),
                out _),
            Is.False);
        Assert.That(
            CacheableWorkerResponse.TryCreate(
                response,
                "not-a-sha-256-hash",
                out _),
            Is.False);
    }

    [Test]
    public async Task CorruptCacheFailsClosedAndRecomputes() {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        var cacheFile = Directory.GetFiles(
            project.CacheDirectory,
            "*.json").Single();
        await File.WriteAllTextAsync(cacheFile, "{corrupt");
        var second = await worker.VerifyAsync(request);

        Assert.That(backend.CallCount, Is.EqualTo(2));
        Assert.That(
            WorkerProtocolJson.SerializeResponse(second),
            Is.EqualTo(WorkerProtocolJson.SerializeResponse(first)));
    }

    [Test]
    public async Task CacheEvictionHonorsTheConfiguredByteBound() {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        request.Cache.MaximumBytes = 1;
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);
        await worker.VerifyAsync(request);
        await worker.VerifyAsync(request);

        Assert.That(backend.CallCount, Is.EqualTo(2));
        Assert.That(
            Directory.GetFiles(project.CacheDirectory, "*.json"),
            Is.Empty);
    }

    [Test]
    public async Task ReplayValidatedRefutationIsCacheable() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Broken(long value) {
                    Contract.Ensures(Contract.Result<long>() > value);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);

        Assert.That(
            response.Records.Single().Status,
            Is.EqualTo(WorkerVerificationStatus.Refuted));
        Assert.That(
            Directory.GetFiles(project.CacheDirectory, "*.json"),
            Has.Length.EqualTo(1));
    }

    [Test]
    public async Task TinyRlimitProducesResourceAbstention() {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Bounded(long value) {
                    Contract.Requires(value > 0);
                    Contract.Ensures(Contract.Result<long>() > 0);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.QueryRlimit = 1;
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);

        Assert.That(
            response.Records.Single().Status,
            Is.EqualTo(WorkerVerificationStatus.Unknown));
        Assert.That(
            response.Records.Single().Reason,
            Is.EqualTo(WorkerVerificationReason.ResourceLimit));
    }

    [Test]
    public async Task MethodRlimitIsCumulativeAcrossCallableQueries() {
        using var project = TestProject.Create(
            MultipleEnsuresSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.QueryRlimit = 6;
        request.Budgets.MethodRlimit = 12;
        var backend = new ResourceCountingBackend(
            resourceCost: 6,
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(
            backend,
            () => backend.ConsumedResourceCount);
        var response = await worker.VerifyAsync(request);
        WorkerVerificationStatus[] expectedStatuses = [
            WorkerVerificationStatus.Proven,
            WorkerVerificationStatus.Proven,
            WorkerVerificationStatus.Unknown
        ];

        Assert.That(response.Errors, Is.Empty);
        Assert.That(backend.CallCount, Is.EqualTo(2));
        Assert.That(
            response.Records.Select(static record => record.Status),
            Is.EqualTo(expectedStatuses));
        Assert.That(
            response.Records[2].Reason,
            Is.EqualTo(WorkerVerificationReason.ResourceLimit));
    }

    [Test]
    public async Task BuiltInBackendChargesTheMethodRlimit() {
        using var project = TestProject.Create(MultipleEnsuresSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MethodRlimit = request.Budgets.QueryRlimit;
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(
            response.Records[0].Status,
            Is.EqualTo(WorkerVerificationStatus.Proven));
        Assert.That(
            response.Records.Skip(1).Select(static record => record.Reason),
            Is.All.EqualTo(WorkerVerificationReason.ResourceLimit));
    }

    [Test]
    public async Task UnmeteredBackendReservesThePerQueryRlimit() {
        using var project = TestProject.Create(MultipleEnsuresSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.QueryRlimit = 6;
        request.Budgets.MethodRlimit = 12;
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);
        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(backend.CallCount, Is.EqualTo(2));
        Assert.That(
            response.Records[2].Reason,
            Is.EqualTo(WorkerVerificationReason.ResourceLimit));
    }

    [Test]
    public async Task MethodRlimitParticipatesInCacheIdentity() {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);

        request.Budgets.MethodRlimit--;
        var second = await worker.VerifyAsync(request);

        Assert.That(backend.CallCount, Is.EqualTo(2));
        Assert.That(second.InputHash, Is.Not.EqualTo(first.InputHash));
        Assert.That(
            Directory.GetFiles(project.CacheDirectory, "*.json"),
            Has.Length.EqualTo(2));
    }

    [Test]
    public void CallerCancellationPropagatesAndIsNotCached() {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        request.Budgets.MethodWallTimeMilliseconds = 5_000;
        using var worker = new SharpProofWorker(new DelayingBackend());
        using var cancellation = new CancellationTokenSource(50);

        Assert.ThrowsAsync<OperationCanceledException>(
            (Func<Task>)(async () =>
                await worker.VerifyAsync(request, cancellation.Token)));
        Assert.That(
            Directory.Exists(project.CacheDirectory)
                ? Directory.GetFiles(project.CacheDirectory, "*.json")
                : [],
            Is.Empty);
    }

    [Test]
    public async Task MethodAndProjectWallBoundariesBecomeUnknown() {
        using var methodProject = TestProject.Create(TautologySource);
        var methodRequest = methodProject.CreateRequest(cacheEnabled: false);
        methodRequest.Budgets.MethodWallTimeMilliseconds = 30;
        methodRequest.Budgets.ProjectWallTimeMilliseconds = 1_000;
        using (var worker = new SharpProofWorker(new DelayingBackend())) {
            var response = await worker.VerifyAsync(methodRequest);
            Assert.That(
                response.Records.Single().Reason,
                Is.EqualTo(WorkerVerificationReason.MethodTimeout));
        }

        using var projectProject = TestProject.Create(
            TautologySource.Replace(
                "Proof(long value)",
                "Proof(long value)",
                StringComparison.Ordinal) +
            """
            public static class Second {
                public static long Proof(long value) {
                    SharpProof.Attributes.Contract.Ensures(
                        SharpProof.Attributes.Contract.Result<long>() == value);
                    return value;
                }
            }
            """);
        var projectRequest = projectProject.CreateRequest(cacheEnabled: false);
        projectRequest.Budgets.MethodWallTimeMilliseconds = 40;
        projectRequest.Budgets.ProjectWallTimeMilliseconds = 40;
        projectRequest.Budgets.MaxParallelism = 1;
        using var projectWorker = new SharpProofWorker(new DelayingBackend());
        var projectResponse = await projectWorker.VerifyAsync(projectRequest);
        Assert.That(
            projectResponse.Records,
            Has.Some.Property(nameof(WorkerVerificationRecord.Reason))
                .EqualTo(WorkerVerificationReason.ProjectTimeout));
    }

    [Test]
    public void DefaultsExposeRlimitAndLauncherJobBudgets() {
        var budgets = new WorkerBudgets();
        Assert.That(
            budgets.QueryRlimit,
            Is.EqualTo(WorkerBudgets.DefaultQueryRlimit));
        Assert.That(
            budgets.MethodRlimit,
            Is.EqualTo(WorkerBudgets.DefaultMethodRlimit));
        Assert.That(budgets.MaxParallelism, Is.EqualTo(4));
        Assert.That(budgets.MaxWorkerProcesses, Is.EqualTo(4));
        Assert.That(
            budgets.ProcessMemoryLimitBytes,
            Is.EqualTo(2L * 1024 * 1024 * 1024));
        Assert.That(
            new SharpProof.Smt.IrSmtBackendOptions(17).QueryRlimit,
            Is.EqualTo(17));
    }

    [Test]
    public void AcceptanceContractMatchesWorkerDefaults() {
        var contractPath = Path.Combine(
            FindRepositoryRoot(),
            "eng",
            "acceptance",
            "contract.json");
        using var document = JsonDocument.Parse(
            File.ReadAllText(contractPath));
        var root = document.RootElement;
        var worker = root.GetProperty("worker");
        var cache = root.GetProperty("cache");

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                worker.GetProperty("protocolVersion").GetInt32(),
                Is.EqualTo(int.Parse(
                    WorkerProtocolVersions.Current,
                    System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(
                worker.GetProperty("maximumParallelism").GetInt32(),
                Is.EqualTo(WorkerBudgets.MaximumParallelism));
            Assert.That(
                worker.GetProperty("maximumMemoryMiB").GetInt64() *
                1024 * 1024,
                Is.EqualTo(WorkerBudgets.DefaultProcessMemoryLimitBytes));
            Assert.That(
                worker.GetProperty("queryRlimit").GetUInt32(),
                Is.EqualTo(WorkerBudgets.DefaultQueryRlimit));
            Assert.That(
                worker.GetProperty("methodRlimit").GetUInt32(),
                Is.EqualTo(WorkerBudgets.DefaultMethodRlimit));
            Assert.That(
                worker.GetProperty("maximumMethodWallSeconds").GetInt32() *
                1_000,
                Is.EqualTo(
                    WorkerBudgets.DefaultMethodWallTimeMilliseconds));
            Assert.That(
                worker.GetProperty("maximumProjectWallSeconds").GetInt32() *
                1_000,
                Is.EqualTo(
                    WorkerBudgets.DefaultProjectWallTimeMilliseconds));
            Assert.That(
                worker.GetProperty("forcedTerminationMilliseconds")
                    .GetInt32(),
                Is.EqualTo(
                    WorkerLauncherDefaults.TerminationGraceMilliseconds));
            Assert.That(
                cache.GetProperty("schemaVersion").GetInt32(),
                Is.EqualTo(WorkerCacheVersions.Current));
            Assert.That(
                cache.GetProperty("maximumMiB").GetInt64() * 1024 * 1024,
                Is.EqualTo(WorkerCacheOptions.DefaultMaximumBytes));
        }
    }

    private static RuntimeContractCase[] CreateRuntimeContractCases(
        int seed,
        int count) {
        var random = new Random(seed);
        var result = new RuntimeContractCase[count];
        for (var index = 0; index < count; index++) {
            var boundary = random.Next(-50, 51);
            var inputs = new[] {
                -100L,
                -1L,
                0L,
                1L,
                100L,
                boundary - 1L,
                boundary,
                boundary + 1L,
                random.Next(-100, 101),
                random.Next(-100, 101)
            };
            var name = "M" + index.ToString(
                "D2",
                System.Globalization.CultureInfo.InvariantCulture);
            var boundaryLiteral = boundary.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + "L";
            result[index] = (index % 8) switch {
                0 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() == value",
                    static _ => true,
                    static (value, actual) => actual == value,
                    WorkerVerificationStatus.Proven,
                    inputs),
                1 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() <= value",
                    static _ => true,
                    static (value, actual) => actual <= value,
                    WorkerVerificationStatus.Proven,
                    inputs),
                2 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() >= value",
                    static _ => true,
                    static (value, actual) => actual >= value,
                    WorkerVerificationStatus.Proven,
                    inputs),
                3 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() > value",
                    static _ => true,
                    static (value, actual) => actual > value,
                    WorkerVerificationStatus.Refuted,
                    inputs),
                4 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() < value",
                    static _ => true,
                    static (value, actual) => actual < value,
                    WorkerVerificationStatus.Refuted,
                    inputs),
                5 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() != value",
                    static _ => true,
                    static (value, actual) => actual != value,
                    WorkerVerificationStatus.Refuted,
                    inputs),
                6 => new RuntimeContractCase(
                    name,
                    "value > " + boundaryLiteral,
                    "Contract.Result<long>() > " + boundaryLiteral,
                    value => value > boundary,
                    (_, actual) => actual > boundary,
                    WorkerVerificationStatus.Proven,
                    inputs),
                _ => new RuntimeContractCase(
                    name,
                    "value < " + boundaryLiteral,
                    "Contract.Result<long>() < " + boundaryLiteral,
                    value => value < boundary,
                    (_, actual) => actual < boundary,
                    WorkerVerificationStatus.Proven,
                    inputs)
            };
        }
        return result;
    }

    private static string CreateRuntimeContractSource(
        IEnumerable<RuntimeContractCase> cases) {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("using SharpProof.Attributes;");
        builder.AppendLine("public static class RuntimeContractOracle {");
        foreach (var item in cases) {
            builder.Append("    public static long ")
                .Append(item.MethodName)
                .AppendLine("(long value) {");
            if (item.RequiresSource != null)
                builder.Append("        Contract.Requires(")
                    .Append(item.RequiresSource)
                    .AppendLine(");");
            builder.Append("        Contract.Ensures(")
                .Append(item.EnsuresSource)
                .AppendLine(");");
            builder.AppendLine("        return value;");
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static System.Reflection.Assembly?
        ResolveRuntimeContractAssembly(
            System.Runtime.Loader.AssemblyLoadContext context,
            System.Reflection.AssemblyName requestedName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate =>
                System.Reflection.AssemblyName.ReferenceMatchesDefinition(
                    candidate.GetName(),
                    requestedName));

    private sealed record RuntimeContractCase(
        string MethodName,
        string? RequiresSource,
        string EnsuresSource,
        Func<long, bool> Requires,
        Func<long, long, bool> Ensures,
        WorkerVerificationStatus ExpectedStatus,
        long[] Inputs);

    private const string TautologySource =
        """
        using SharpProof.Attributes;
        public static class Subject {
            public static long Proof(long value) {
                Contract.Ensures(Contract.Result<long>() == value);
                return value;
            }
        }
        """;

    private const string MultipleEnsuresSource =
        """
        using SharpProof.Attributes;
        public static class Subject {
            public static long Identity(long value) {
                Contract.Ensures(Contract.Result<long>() == value);
                Contract.Ensures(Contract.Result<long>() <= value);
                Contract.Ensures(Contract.Result<long>() >= value);
                return value;
            }
        }
        """;

    private sealed class CountingBackend(BackendCheckResult result)
        : ISmtBackend {
        private readonly BackendCheckResult _result = result;
        private int _callCount;

        internal int CallCount => Volatile.Read(ref _callCount);

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(_result);
        }
    }

    private sealed class CapturingBackend(BackendCheckResult result)
        : ISmtBackend {
        private readonly BackendCheckResult _result = result;
        private VerificationQuery? _query;

        internal VerificationQuery Query =>
            _query ?? throw new InvalidOperationException(
                "The backend has not received a query.");

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            _query = query;
            return Task.FromResult(_result);
        }
    }

    private sealed class DelayingBackend : ISmtBackend {
        public async Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken) {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return BackendCheckResult.Unknown(
                BackendFailureReason.InfrastructureFailure);
        }
    }

    private sealed class ResourceCountingBackend(
        long resourceCost,
        BackendCheckResult result) : ISmtBackend {
        private readonly long _resourceCost = resourceCost;
        private readonly BackendCheckResult _result = result;
        private int _callCount;
        private long _consumedResourceCount;

        internal int CallCount => Volatile.Read(ref _callCount);
        internal long ConsumedResourceCount =>
            Interlocked.Read(ref _consumedResourceCount);

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            Interlocked.Add(ref _consumedResourceCount, _resourceCost);
            return Task.FromResult(_result);
        }
    }

    private static string FindRepositoryRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null) {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "SharpProof.Release.props")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Repository root was not found.");
    }

    private sealed class TestProject : IDisposable {
        private TestProject(string directory, string[] sourcePaths) {
            DirectoryPath = directory;
            SourcePaths = sourcePaths;
            CacheDirectory = Path.Combine(directory, "cache");
        }

        internal string DirectoryPath { get; }
        internal string[] SourcePaths { get; }
        internal string CacheDirectory { get; }

        internal static TestProject Create(string source) =>
            Create(("Subject.cs", source));

        internal static TestProject Create(
            params (string FileName, string Source)[] sources) {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.Worker.Test",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sourcePaths = sources.Select(source => {
                var sourcePath = Path.Combine(directory, source.FileName);
                File.WriteAllText(
                    sourcePath,
                    source.Source,
                    new System.Text.UTF8Encoding(false));
                return sourcePath;
            }).ToArray();
            return new TestProject(directory, sourcePaths);
        }

        internal WorkerVerifyRequest CreateRequest(bool cacheEnabled) =>
            new() {
                ProjectDirectory = DirectoryPath,
                AssemblyName = "WorkerTest",
                SourceFiles = SourcePaths,
                ReferenceAssemblies = GetReferences(),
                DefineConstants = [Contract.ConditionalSymbol],
                Compilation = CreateCompilationOptions(),
                Cache = new WorkerCacheOptions {
                    Enabled = cacheEnabled,
                    Directory = CacheDirectory
                }
            };

        public void Dispose() {
            var resolved = Path.GetFullPath(DirectoryPath);
            var expectedRoot = Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "SharpProof.Worker.Test"));
            if (!resolved.StartsWith(
                    expectedRoot + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected test directory.");
            if (Directory.Exists(resolved))
                Directory.Delete(resolved, recursive: true);
        }

        private static string[] GetReferences() {
            var trusted = ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator);
            var names = new HashSet<string>(
                RequiredReferenceFileNames,
                StringComparer.OrdinalIgnoreCase);
            return [.. trusted
                .Where(path => names.Contains(Path.GetFileName(path)))
                .Append(typeof(Contract).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path, StringComparer.Ordinal)];
        }
    }

    private static WorkerCompilationOptions CreateCompilationOptions() =>
        new() {
            TargetFramework = "net8.0",
            LanguageVersion = "12.0",
            NullableContext = WorkerNullableContext.Enabled,
            Optimization = WorkerOptimizationLevel.Release,
            CheckOverflow = false,
            AllowUnsafe = false,
            Deterministic = true,
            OutputKind = WorkerOutputKind.DynamicallyLinkedLibrary,
            Platform = WorkerPlatform.AnyCpu
        };
}
