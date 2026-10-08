using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class ExplicitLayoutFieldAliasAuditTests
{
    private const string SubjectTemplate = """
        #undef SHARPPROOF_CONTRACTS
        using System.Runtime.InteropServices;
        using SharpProof.Attributes;

        __LAYOUT_ATTRIBUTE__
        public sealed class Cell
        {
            __OBSERVED_OFFSET__public __READONLY__int Observed;
            __WRITTEN_OFFSET__public int Written;
        }

        public static class Subject
        {
            public static int Target(Cell cell)
            {
                Contract.Requires(cell != null);
                Contract.Requires(cell.Observed == 0);
                Contract.Ensures(Contract.Result<int>() == 0);
                cell.Written = __STORED_VALUE__;
                return cell.Observed;
            }
        }
        """ + "\n";

    [TestCase("overlap-writable")]
    [TestCase("ordinary-writable")]
    [TestCase("overlap-readonly")]
    [TestCase("ordinary-readonly")]
    [TestCase("partial-overlap")]
    [TestCase("ordinary-partial-control")]
    [TestCase("sequential-control")]
    public async Task FieldPostconditionMatchesEmittedStorage(string scenario)
    {
        var settings = Settings(scenario);
        var source = Source(settings);
        var sourceHash = Hash(Encoding.UTF8.GetBytes(source));
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "explicit-layout-field-alias-audit",
            scenario + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject-source.cs"), source);
        var compilation = TestCompilation.Create("ExplicitLayoutFieldAlias_" + scenario, ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        await Save(evidenceDirectory, "emission.json", new
        {
            SourceSha256 = sourceHash,
            Optimization = compilation.Options.OptimizationLevel.ToString(),
            emission.Success,
            Diagnostics = emission.Diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray()
        });
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        var imageHash = Hash(imageBytes);
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "emitted-subject.dll"), imageBytes);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("ExplicitLayoutFieldAlias_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        int actualReturn;
        try
        {
            var assembly = runtime.LoadFromStream(image);
            var cellType = assembly.GetType("Cell")!;
            var subjectType = assembly.GetType("Subject")!;
            var observed = cellType.GetField("Observed", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            var written = cellType.GetField("Written", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            var target = subjectType.GetMethod("Target", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
            var constructor = cellType.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single();
            var targetIl = target.GetMethodBody()!.GetILAsByteArray()!;
            var constructorIl = constructor.GetMethodBody()!.GetILAsByteArray()!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(OperatingSystem.IsLinux(), Is.True);
                Assert.That(RuntimeInformation.ProcessArchitecture, Is.EqualTo(Architecture.X64));
                Assert.That(BitConverter.IsLittleEndian, Is.True);
                Assert.That(targetIl, Has.Length.EqualTo(14));
                Assert.That(targetIl[0], Is.EqualTo(0x02));
                Assert.That(targetIl[1], Is.EqualTo(settings.StoredValue == -1 ? 0x15 : 0x17));
                Assert.That(targetIl[2], Is.EqualTo(0x7d));
                Assert.That(targetIl[7], Is.EqualTo(0x02));
                Assert.That(targetIl[8], Is.EqualTo(0x7b));
                Assert.That(targetIl[13], Is.EqualTo(0x2a));
                Assert.That(constructorIl, Has.Length.EqualTo(7));
                Assert.That(constructorIl[0], Is.EqualTo(0x02));
                Assert.That(constructorIl[1], Is.EqualTo(0x28));
                Assert.That(constructorIl[6], Is.EqualTo(0x2a));
                Assert.That(cellType.IsSealed, Is.True);
                Assert.That(cellType.TypeInitializer, Is.Null);
                Assert.That(cellType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                    BindingFlags.Static | BindingFlags.DeclaredOnly), Has.Length.EqualTo(2));
                Assert.That(target.GetParameters(), Has.Length.EqualTo(1));
                Assert.That(target.GetParameters().Single().ParameterType, Is.EqualTo(cellType));
                Assert.That(target.ReturnType, Is.EqualTo(typeof(int)));
                Assert.That(target.GetMethodImplementationFlags(), Is.EqualTo((MethodImplAttributes)0));
                Assert.That(constructor.GetParameters(), Is.Empty);
                Assert.That(observed.FieldType, Is.EqualTo(typeof(int)));
                Assert.That(written.FieldType, Is.EqualTo(typeof(int)));
                Assert.That(observed.IsInitOnly, Is.EqualTo(settings.ReadOnlyObserved));
                Assert.That(written.IsInitOnly, Is.False);
            }
            var storedField = target.Module.ResolveField(BitConverter.ToInt32(targetIl, 3))!;
            var returnedField = target.Module.ResolveField(BitConverter.ToInt32(targetIl, 9))!;
            var baseConstructor = constructor.Module.ResolveMethod(BitConverter.ToInt32(constructorIl, 2))!;
            using var pe = new PEReader(new MemoryStream(imageBytes, writable: false));
            var metadata = pe.GetMetadataReader();
            var cellHandle = MetadataTokens.TypeDefinitionHandle(cellType.MetadataToken & 0x00ffffff);
            var observedHandle = MetadataTokens.FieldDefinitionHandle(observed.MetadataToken & 0x00ffffff);
            var writtenHandle = MetadataTokens.FieldDefinitionHandle(written.MetadataToken & 0x00ffffff);
            var targetHandle = MetadataTokens.MethodDefinitionHandle(target.MetadataToken & 0x00ffffff);
            var cellDefinition = metadata.GetTypeDefinition(cellHandle);
            var observedDefinition = metadata.GetFieldDefinition(observedHandle);
            var writtenDefinition = metadata.GetFieldDefinition(writtenHandle);
            var targetDefinition = metadata.GetMethodDefinition(targetHandle);
            var observedOffset = observedDefinition.GetOffset();
            var writtenOffset = writtenDefinition.GetOffset();
            var observedSignature = Convert.ToHexStringLower(metadata.GetBlobBytes(observedDefinition.Signature));
            var writtenSignature = Convert.ToHexStringLower(metadata.GetBlobBytes(writtenDefinition.Signature));
            var layoutMask = cellDefinition.Attributes & TypeAttributes.LayoutMask;
            var expectedLayout = settings.Layout switch
            {
                "explicit" => TypeAttributes.ExplicitLayout,
                "sequential" => TypeAttributes.SequentialLayout,
                _ => TypeAttributes.AutoLayout
            };
            var mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(storedField.MetadataToken, Is.EqualTo(written.MetadataToken));
                Assert.That(returnedField.MetadataToken, Is.EqualTo(observed.MetadataToken));
                Assert.That(storedField.DeclaringType, Is.EqualTo(cellType));
                Assert.That(returnedField.DeclaringType, Is.EqualTo(cellType));
                Assert.That(storedField.Module.ModuleVersionId, Is.EqualTo(mvid));
                Assert.That(returnedField.Module.ModuleVersionId, Is.EqualTo(mvid));
                Assert.That(baseConstructor.DeclaringType, Is.EqualTo(typeof(object)));
                Assert.That(baseConstructor.Name, Is.EqualTo(".ctor"));
                Assert.That(assembly.ManifestModule.ModuleVersionId, Is.EqualTo(mvid));
                Assert.That(metadata.GetString(metadata.GetAssemblyDefinition().Name), Is.EqualTo(compilation.AssemblyName));
                Assert.That(metadata.GetString(cellDefinition.Name), Is.EqualTo("Cell"));
                Assert.That(cellDefinition.GetFields().Count, Is.EqualTo(2));
                Assert.That(observedDefinition.GetDeclaringType(), Is.EqualTo(cellHandle));
                Assert.That(writtenDefinition.GetDeclaringType(), Is.EqualTo(cellHandle));
                Assert.That(metadata.GetString(observedDefinition.Name), Is.EqualTo("Observed"));
                Assert.That(metadata.GetString(writtenDefinition.Name), Is.EqualTo("Written"));
                Assert.That(observedSignature, Is.EqualTo("0608"));
                Assert.That(writtenSignature, Is.EqualTo("0608"));
                Assert.That(observedDefinition.Attributes.HasFlag(FieldAttributes.InitOnly), Is.EqualTo(settings.ReadOnlyObserved));
                Assert.That(writtenDefinition.Attributes.HasFlag(FieldAttributes.InitOnly), Is.False);
                Assert.That(observedDefinition.Attributes.HasFlag(FieldAttributes.Static), Is.False);
                Assert.That(writtenDefinition.Attributes.HasFlag(FieldAttributes.Static), Is.False);
                Assert.That(layoutMask, Is.EqualTo(expectedLayout));
                Assert.That(cellType.Attributes & TypeAttributes.LayoutMask, Is.EqualTo(expectedLayout));
                Assert.That(observedOffset, Is.EqualTo(settings.Layout == "explicit" ? 0 : -1));
                Assert.That(writtenOffset, Is.EqualTo(settings.Layout == "explicit" ? settings.WrittenOffset : -1));
                Assert.That(metadata.GetString(targetDefinition.Name), Is.EqualTo("Target"));
                Assert.That(MetadataTokens.GetToken(targetDefinition.GetDeclaringType()), Is.EqualTo(subjectType.MetadataToken));
                Assert.That(pe.GetMethodBody(targetDefinition.RelativeVirtualAddress).GetILBytes(), Is.EqualTo(targetIl));
            }
            var receiver = Activator.CreateInstance(cellType)!;
            var initialObserved = (int)observed.GetValue(receiver)!;
            var initialWritten = (int)written.GetValue(receiver)!;
            var execute = target.CreateDelegate<Func<int>>(receiver);
            Assert.That(ReferenceEquals(execute.Target, receiver), Is.True);
            Assert.That(execute.Method.MetadataToken, Is.EqualTo(target.MetadataToken));
            Assert.That(execute.Method.Module.ModuleVersionId, Is.EqualTo(mvid));
            actualReturn = execute();
            var finalObserved = (int)observed.GetValue(receiver)!;
            var finalWritten = (int)written.GetValue(receiver)!;
            await Save(evidenceDirectory, "clr-layout-binding.json", new
            {
                Case = scenario,
                SourceSha256 = sourceHash,
                EmittedPeSha256 = imageHash,
                ModuleMvid = mvid,
                CellToken = cellType.MetadataToken,
                TargetToken = target.MetadataToken,
                ObservedToken = observed.MetadataToken,
                WrittenToken = written.MetadataToken,
                TypeAttributes = (int)cellDefinition.Attributes,
                LayoutMask = layoutMask.ToString(),
                ObservedOffset = observedOffset,
                WrittenOffset = writtenOffset,
                ObservedSignature = observedSignature,
                WrittenSignature = writtenSignature,
                ObservedWidthBytes = sizeof(int),
                WrittenWidthBytes = sizeof(int),
                ObservedInitOnly = observed.IsInitOnly,
                WrittenInitOnly = written.IsInitOnly,
                TargetIl = Convert.ToHexStringLower(targetIl),
                ConstructorIl = Convert.ToHexStringLower(constructorIl),
                StoreIlOffset = 2,
                ReadIlOffset = 8,
                InitialObserved = initialObserved,
                InitialWritten = initialWritten,
                InitialNonNull = receiver != null,
                InitialPreconditionsHold = receiver != null && initialObserved == 0,
                ActualReturn = actualReturn,
                FinalObserved = finalObserved,
                FinalWritten = finalWritten,
                PostconditionHoldsInClr = actualReturn == 0,
                ClosedTypedDelegate = ReferenceEquals(execute.Target, receiver),
                LittleEndian = BitConverter.IsLittleEndian,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString()
            });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(initialObserved, Is.EqualTo(0));
                Assert.That(initialWritten, Is.EqualTo(0));
                Assert.That(actualReturn, Is.EqualTo(settings.RuntimeValue));
                Assert.That(finalObserved, Is.EqualTo(settings.RuntimeValue));
                Assert.That(finalWritten, Is.EqualTo(settings.StoredValue));
                Assert.That(actualReturn == 0, Is.EqualTo(!settings.InvalidClaim));
            }
        }
        finally
        {
            runtime.Unload();
        }

        var tree = compilation.SyntaxTrees.Single();
        var syntaxRoot = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var targetSyntax = syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var targetSymbol = (IMethodSymbol)model.GetDeclaredSymbol(targetSyntax)!;
        var observedSyntax = syntaxRoot.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Identifier.ValueText == "Observed");
        var writtenSyntax = syntaxRoot.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Identifier.ValueText == "Written");
        var observedSymbol = (IFieldSymbol)model.GetDeclaredSymbol(observedSyntax)!;
        var writtenSymbol = (IFieldSymbol)model.GetDeclaredSymbol(writtenSyntax)!;
        var assignmentStatement = (IExpressionStatementOperation)model.GetOperation(targetSyntax.Body!.Statements[3])!;
        var assignment = (ISimpleAssignmentOperation)assignmentStatement.Operation;
        var writtenReference = (IFieldReferenceOperation)assignment.Target;
        var returnedSyntax = ((ReturnStatementSyntax)targetSyntax.Body.Statements[4]).Expression!;
        var returnedReference = (IFieldReferenceOperation)model.GetOperation(returnedSyntax)!;
        var requirements = targetSyntax.Body.Statements.Take(2).Select(statement =>
            (IInvocationOperation)((IExpressionStatementOperation)model.GetOperation(statement)!).Operation).ToArray();
        var observationRequirement = (IBinaryOperation)requirements[1].Arguments.Single().Value;
        var requiredField = (IFieldReferenceOperation)observationRequirement.LeftOperand;
        var discovery = new ClaimManifestBuilder(compilation).Build();
        Assert.That(discovery.Targets, Has.Count.EqualTo(1));
        var selected = discovery.Targets.Values.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.StartsWith("#undef SHARPPROOF_CONTRACTS\n", StringComparison.Ordinal), Is.True);
            Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
            Assert.That(targetSyntax.Body.Statements, Has.Count.EqualTo(5));
            Assert.That(targetSyntax.Body.Statements[0].ToString(), Is.EqualTo("Contract.Requires(cell != null);"));
            Assert.That(SymbolEqualityComparer.Default.Equals(selected.Method, targetSymbol), Is.True);
            Assert.That(selected.Declaration!.Span, Is.EqualTo(targetSyntax.Span));
            Assert.That(targetSymbol.Name, Is.EqualTo("Target"));
            Assert.That(targetSymbol.IsStatic, Is.True);
            Assert.That(targetSymbol.MethodKind, Is.EqualTo(MethodKind.Ordinary));
            Assert.That(targetSymbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(SymbolEqualityComparer.Default.Equals(targetSymbol.Parameters.Single().Type,
                observedSymbol.ContainingType), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(writtenReference.Field, writtenSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(returnedReference.Field, observedSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(requiredField.Field, observedSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(((IParameterReferenceOperation)writtenReference.Instance!).Parameter,
                targetSymbol.Parameters.Single()), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(((IParameterReferenceOperation)returnedReference.Instance!).Parameter,
                targetSymbol.Parameters.Single()), Is.True);
            Assert.That(assignment.Value.ConstantValue.Value, Is.EqualTo(settings.StoredValue));
            Assert.That(observationRequirement.OperatorKind, Is.EqualTo(BinaryOperatorKind.Equals));
            Assert.That(observationRequirement.RightOperand.ConstantValue.Value, Is.EqualTo(0));
            Assert.That(requirements.All(requirement => requirement.TargetMethod.Name == "Requires" &&
                requirement.TargetMethod.ContainingType.ToDisplayString() == "SharpProof.Attributes.Contract"), Is.True);
            Assert.That(observedSymbol.IsReadOnly, Is.EqualTo(settings.ReadOnlyObserved));
            Assert.That(writtenSymbol.IsReadOnly, Is.False);
            Assert.That(observedSyntax.Initializer, Is.Null);
            Assert.That(writtenSyntax.Initializer, Is.Null);
            Assert.That(observedSymbol.ContainingType.StaticConstructors, Is.Empty);
            Assert.That(syntaxRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>(), Is.Empty);
        }
        await Save(evidenceDirectory, "source-field-binding.json", new
        {
            SourceSha256 = sourceHash,
            Target = targetSymbol.ToDisplayString(),
            SelectedCallableId = selected.Entry.CallableId,
            TargetSpanStart = targetSyntax.SpanStart,
            TargetSpanLength = targetSyntax.Span.Length,
            Observed = observedSymbol.ToDisplayString(),
            Written = writtenSymbol.ToDisplayString(),
            ObservedReadOnly = observedSymbol.IsReadOnly,
            WrittenReadOnly = writtenSymbol.IsReadOnly,
            Requirements = requirements.Select(requirement => requirement.Syntax.ToString()).ToArray(),
            Assignment = assignment.Syntax.ToString(),
            Return = returnedReference.Syntax.ToString(),
            RuntimeContractCallsRemoved = true
        });
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var manifestBytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "compiler-manifest.json"), manifestBytes);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-request.json"),
            WorkerProtocolJson.SerializeRequest(project.Request) + "\n");
        Assert.That(Hash(manifestBytes), Is.EqualTo(project.Request.CompilerManifest.Sha256));
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-response.json"),
            WorkerProtocolJson.SerializeResponse(response) + "\n");
        var validation = WorkerProtocolJson.ValidateForRequest(response, WorkerProtocolJson.ComputeRequestHash(project.Request),
            Program.ComputeExpectedInputHash(project.Request, manifestBytes), artifact.Manifest, project.Request,
            Program.ExpectedVersions());
        var callable = artifact.Callables.Single();
        var manifestClaim = artifact.Manifest.Claims.Single();
        var claim = response.ClaimResults.Single();
        var coverage = response.CallableResults.Single();
        await Save(evidenceDirectory, "native-observations.json", new
        {
            Case = scenario,
            SourceSha256 = sourceHash,
            EmittedPeSha256 = imageHash,
            SameCompilationForClrAndNative = true,
            ActualClrReturn = actualReturn,
            ClrPostconditionHolds = actualReturn == 0,
            CapturedTotal = callable.Total != null,
            CapturedBodyAbstraction = callable.Total?.IsBodyAbstraction,
            CapturedClauseKinds = callable.Total?.Clauses.Select(clause => clause.Kind.ToString()).ToArray(),
            SelectedCallableId = selected.Entry.CallableId,
            ClaimId = claim.ClaimId,
            ClaimLocation = manifestClaim.Location,
            ClaimKind = manifestClaim.Kind.ToString(),
            CacheEnabled = project.Request.Cache.Enabled,
            Budgets = project.Request.Budgets,
            VerifyPolicy = project.Request.VerifyPolicy.ToString(),
            AssumptionPolicy = project.Request.AssumptionPolicy.ToString(),
            RunStatus = response.RunStatus.ToString(),
            FailureReason = response.FailureReason.ToString(),
            Coverage = coverage.Coverage.ToString(),
            Outcome = claim.Outcome.ToString(),
            Reason = claim.Reason.ToString(),
            Vacuity = claim.Vacuity.ToString(),
            ProtocolValid = validation.IsValid,
            ProtocolErrors = validation.Errors,
            response.Errors,
            FrozenExpectation = settings.InvalidClaim ? "The false CLR postcondition cannot be Proven; no exact Unknown reason is required."
                : "The distinct-storage control remains complete Proven with an exact captured Total."
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(validation.IsValid, Is.True, JsonSerializer.Serialize(validation.Errors));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(project.Request.Cache.Enabled, Is.False);
            Assert.That(project.Request.VerifyPolicy, Is.EqualTo(WorkerVerifyPolicy.Advisory));
            Assert.That(project.Request.AssumptionPolicy, Is.EqualTo(WorkerAssumptionPolicy.Allow));
            Assert.That(JsonSerializer.Serialize(project.Request.Budgets), Is.EqualTo(JsonSerializer.Serialize(new WorkerBudgets())));
            Assert.That(artifact.Callables, Has.Length.EqualTo(1));
            Assert.That(callable.CallableId, Is.EqualTo(selected.Entry.CallableId));
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(manifestClaim.CallableId, Is.EqualTo(selected.Entry.CallableId));
            Assert.That(manifestClaim.Kind, Is.EqualTo(WorkerClaimKind.Postcondition));
            Assert.That(manifestClaim.Evidence, Is.EqualTo(WorkerClaimEvidence.DirectClause));
            Assert.That(manifestClaim.Location.Start, Is.GreaterThanOrEqualTo(targetSyntax.SpanStart));
            Assert.That(manifestClaim.Location.Start + manifestClaim.Location.Length, Is.LessThanOrEqualTo(targetSyntax.Span.End));
            Assert.That(artifact.Manifest.Callables.Single().Assumptions, Has.Length.EqualTo(2));
            Assert.That(artifact.Manifest.Callables.Single().Assumptions.All(assumption =>
                assumption.Kind == WorkerAssumptionKind.Precondition), Is.True);
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            Assert.That(response.CallableResults, Has.Length.EqualTo(1));
            Assert.That(claim.ClaimId, Is.EqualTo(manifestClaim.ClaimId));
            Assert.That(coverage.CallableId, Is.EqualTo(selected.Entry.CallableId));
            Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            if (settings.InvalidClaim)
            {
                Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
                    "The exact emitted CLR violates Result<int>() == 0 after a write to overlapping storage.");
            }
            else
            {
                Assert.That(callable.Total, Is.Not.Null);
                Assert.That(callable.Total!.IsBodyAbstraction, Is.False);
                Assert.That(callable.Total.Clauses.Count(clause => clause.Kind == CompilerContractKind.Requires), Is.EqualTo(2));
                Assert.That(callable.Total.Clauses.Count(clause => clause.Kind == CompilerContractKind.Ensures), Is.EqualTo(1));
                Assert.That(coverage.Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
                Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
                Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.None));
            }
        }
    }

    private static CaseSettings Settings(string scenario)
    {
        return scenario switch
        {
            "overlap-writable" => new("explicit", false, 0, 1, 1, true),
            "ordinary-writable" => new("ordinary", false, 0, 1, 0, false),
            "overlap-readonly" => new("explicit", true, 0, 1, 1, true),
            "ordinary-readonly" => new("ordinary", true, 0, 1, 0, false),
            "partial-overlap" => new("explicit", false, 2, -1, -65536, true),
            "ordinary-partial-control" => new("ordinary", false, 0, -1, 0, false),
            "sequential-control" => new("sequential", false, 0, 1, 0, false),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    private static string Source(CaseSettings settings)
    {
        var layout = settings.Layout switch
        {
            "explicit" => "[StructLayout(LayoutKind.Explicit)]",
            "sequential" => "[StructLayout(LayoutKind.Sequential)]",
            _ => string.Empty
        };
        return SubjectTemplate.Replace("__LAYOUT_ATTRIBUTE__", layout, StringComparison.Ordinal)
            .Replace("__OBSERVED_OFFSET__", settings.Layout == "explicit" ? "[FieldOffset(0)] " : string.Empty, StringComparison.Ordinal)
            .Replace("__WRITTEN_OFFSET__", settings.Layout == "explicit"
                ? "[FieldOffset(" + settings.WrittenOffset.ToString(CultureInfo.InvariantCulture) + ")] " : string.Empty,
                StringComparison.Ordinal)
            .Replace("__READONLY__", settings.ReadOnlyObserved ? "readonly " : string.Empty, StringComparison.Ordinal)
            .Replace("__STORED_VALUE__", settings.StoredValue.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static string Hash(byte[] value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(value));
    }

    private static async Task Save(string directory, string fileName, object value)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, fileName), JsonSerializer.Serialize(value) + "\n");
    }

    private sealed record CaseSettings(string Layout, bool ReadOnlyObserved, int WrittenOffset, int StoredValue,
        int RuntimeValue, bool InvalidClaim);
}
