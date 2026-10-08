using System.Reflection;
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
public sealed class SynchronizedCompoundGetterPurityAuditTests
{
    private const string SubjectTemplate = """
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;

        public sealed class Cell
        {
            public int Value
            {
                [MethodImpl(__METHOD_IMPL_OPTIONS__)]
                get;
                set;
            }
        }

        public static class Subject
        {
            [EnforcePure]
            public static int Target(int value)
            {
                Contract.Requires(value == 0);
                var cell = new Cell();
                cell.Value += 1 / value;
                return value;
            }
        }
        """ + "\n";

    [TestCase(false)]
    [TestCase(true)]
    public async Task CompoundGetterSynchronizationPrecedesDivisionFault(bool synchronized)
    {
        var options = synchronized ? "MethodImplOptions.NoInlining | MethodImplOptions.Synchronized"
            : "MethodImplOptions.NoInlining";
        var source = SubjectTemplate.Replace("__METHOD_IMPL_OPTIONS__", options, StringComparison.Ordinal);
        var sourceHash = Hash(Encoding.UTF8.GetBytes(source));
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var caseName = synchronized ? "synchronized" : "no-inlining-control";
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "synchronized-compound-getter-purity-audit",
            caseName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject-source.cs"), source);
        var compilation = TestCompilation.Create("SynchronizedCompoundGetterPurity_" + caseName, ("Subject.cs", source));
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
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "emitted-subject.dll"), imageBytes);
        image.Position = 0;
        var loadContext = new AssemblyLoadContext("SynchronizedCompoundGetterPurity_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        MethodImplAttributes getterFlags;
        try
        {
            var assembly = loadContext.LoadFromStream(image);
            var subject = assembly.GetType("Subject")!;
            var cell = assembly.GetType("Cell")!;
            var target = subject.GetMethod("Target", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
            var property = cell.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            var getter = property.GetMethod!;
            var setter = property.SetMethod!;
            var field = cell.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Single();
            var constructor = cell.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single();
            var targetIl = target.GetMethodBody()!.GetILAsByteArray()!;
            var getterIl = getter.GetMethodBody()!.GetILAsByteArray()!;
            var setterIl = setter.GetMethodBody()!.GetILAsByteArray()!;
            var constructorIl = constructor.GetMethodBody()!.GetILAsByteArray()!;
            var instructions = ReadTargetIl(targetIl);
            var virtualCalls = instructions.Where(instruction => instruction.Opcode == 0x6f).ToArray();
            Assert.That(virtualCalls, Has.Length.EqualTo(2));
            var getterCall = virtualCalls[0];
            var setterCall = virtualCalls[1];
            var divisionInstruction = instructions.Single(instruction => instruction.Opcode == 0x5b);
            var creationInstruction = instructions.Single(instruction => instruction.Opcode == 0x73);
            var requirementInstruction = instructions.Single(instruction => instruction.Opcode == 0x28);
            var calledGetter = (MethodInfo)target.Module.ResolveMethod(getterCall.Token!.Value)!;
            var calledSetter = (MethodInfo)target.Module.ResolveMethod(setterCall.Token!.Value)!;
            var calledConstructor = (ConstructorInfo)target.Module.ResolveMethod(creationInstruction.Token!.Value)!;
            var calledRequirement = (MethodInfo)target.Module.ResolveMethod(requirementInstruction.Token!.Value)!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(getterIl, Has.Length.EqualTo(7));
                Assert.That(getterIl[0], Is.EqualTo(0x02));
                Assert.That(getterIl[1], Is.EqualTo(0x7b));
                Assert.That(getterIl[6], Is.EqualTo(0x2a));
                Assert.That(setterIl, Has.Length.EqualTo(8));
                Assert.That(setterIl[0], Is.EqualTo(0x02));
                Assert.That(setterIl[1], Is.EqualTo(0x03));
                Assert.That(setterIl[2], Is.EqualTo(0x7d));
                Assert.That(setterIl[7], Is.EqualTo(0x2a));
                Assert.That(constructorIl, Has.Length.EqualTo(7));
                Assert.That(constructorIl[0], Is.EqualTo(0x02));
                Assert.That(constructorIl[1], Is.EqualTo(0x28));
                Assert.That(constructorIl[6], Is.EqualTo(0x2a));
            }
            var getFieldToken = BitConverter.ToInt32(getterIl, 2);
            var setFieldToken = BitConverter.ToInt32(setterIl, 3);
            var returnedField = getter.Module.ResolveField(getFieldToken)!;
            var storedField = setter.Module.ResolveField(setFieldToken)!;
            var baseConstructorToken = BitConverter.ToInt32(constructorIl, 2);
            var baseConstructor = (ConstructorInfo)constructor.Module.ResolveMethod(baseConstructorToken)!;
            getterFlags = getter.GetMethodImplementationFlags();
            var execute = target.CreateDelegate<Func<int, int>>();
            var exception = Assert.Throws<DivideByZeroException>(new Action(() => execute(0)));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(calledGetter.DeclaringType, Is.EqualTo(cell));
                Assert.That(calledGetter.MetadataToken, Is.EqualTo(getter.MetadataToken));
                Assert.That(calledGetter.Name, Is.EqualTo("get_Value"));
                Assert.That(calledSetter.DeclaringType, Is.EqualTo(cell));
                Assert.That(calledSetter.MetadataToken, Is.EqualTo(setter.MetadataToken));
                Assert.That(calledSetter.Name, Is.EqualTo("set_Value"));
                Assert.That(calledConstructor.DeclaringType, Is.EqualTo(cell));
                Assert.That(calledConstructor.MetadataToken, Is.EqualTo(constructor.MetadataToken));
                Assert.That(calledRequirement.DeclaringType!.FullName, Is.EqualTo("SharpProof.Attributes.Contract"));
                Assert.That(calledRequirement.Name, Is.EqualTo("Requires"));
                Assert.That(requirementInstruction.Offset, Is.LessThan(creationInstruction.Offset));
                Assert.That(creationInstruction.Offset, Is.LessThan(getterCall.Offset));
                Assert.That(getterCall.Offset, Is.LessThan(divisionInstruction.Offset));
                Assert.That(divisionInstruction.Offset, Is.LessThan(setterCall.Offset));
                Assert.That(returnedField.MetadataToken, Is.EqualTo(field.MetadataToken));
                Assert.That(returnedField.DeclaringType, Is.EqualTo(cell));
                Assert.That(storedField.MetadataToken, Is.EqualTo(field.MetadataToken));
                Assert.That(storedField.DeclaringType, Is.EqualTo(cell));
                Assert.That(baseConstructor.DeclaringType, Is.EqualTo(typeof(object)));
                Assert.That(constructor.GetParameters(), Is.Empty);
                Assert.That(constructor.GetMethodImplementationFlags(), Is.EqualTo((MethodImplAttributes)0));
                Assert.That(target.GetParameters(), Has.Length.EqualTo(1));
                Assert.That(target.GetParameters().Single().ParameterType, Is.EqualTo(typeof(int)));
                Assert.That(target.ReturnType, Is.EqualTo(typeof(int)));
                Assert.That(target.GetMethodImplementationFlags(), Is.EqualTo((MethodImplAttributes)0));
                Assert.That(getter.GetParameters(), Is.Empty);
                Assert.That(getter.ReturnType, Is.EqualTo(typeof(int)));
                Assert.That(getter.IsStatic, Is.False);
                Assert.That(getterFlags, Is.EqualTo((MethodImplAttributes)(synchronized ? 40 : 8)));
                Assert.That(setter.GetMethodImplementationFlags(), Is.EqualTo((MethodImplAttributes)0));
                Assert.That(field.Name, Is.EqualTo("<Value>k__BackingField"));
                Assert.That(field.IsInitOnly, Is.False);
                Assert.That(field.IsStatic, Is.False);
                Assert.That(field.FieldType, Is.EqualTo(typeof(int)));
                Assert.That(cell.TypeInitializer, Is.Null);
                Assert.That(exception, Is.TypeOf<DivideByZeroException>());
                Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
            }
            await Save(evidenceDirectory, "getter-divisionInstruction-setter-binding.json", new
            {
                SourceSha256 = sourceHash,
                EmittedPeSha256 = Hash(imageBytes),
                TargetMetadataToken = target.MetadataToken,
                TargetIlHex = Convert.ToHexStringLower(targetIl),
                Instructions = instructions,
                RequiresOffset = requirementInstruction.Offset,
                ConstructorOffset = creationInstruction.Offset,
                ConstructorToken = creationInstruction.Token,
                GetterOffset = getterCall.Offset,
                GetterToken = getterCall.Token,
                DivisionOffset = divisionInstruction.Offset,
                SetterOffset = setterCall.Offset,
                SetterToken = setterCall.Token,
                GetterMetadataToken = getter.MetadataToken,
                SetterMetadataToken = setter.MetadataToken,
                GetterIlHex = Convert.ToHexStringLower(getterIl),
                SetterIlHex = Convert.ToHexStringLower(setterIl),
                BackingFieldMetadataToken = field.MetadataToken,
                GetterFieldToken = getFieldToken,
                SetterStoreFieldToken = setFieldToken,
                ConstructorMetadataToken = constructor.MetadataToken,
                ConstructorIlHex = Convert.ToHexStringLower(constructorIl),
                BaseConstructorCallToken = baseConstructorToken,
                GetterImplementationFlags = (int)getterFlags,
                SetterImplementationFlags = (int)setter.GetMethodImplementationFlags(),
                ModuleVersionId = cell.Module.ModuleVersionId.ToString("D"),
                GetterBeforeDivisionBeforeSetter = getterCall.Offset < divisionInstruction.Offset && divisionInstruction.Offset < setterCall.Offset
            });
            await Save(evidenceDirectory, "runtime-observations.json", new
            {
                SourceSha256 = sourceHash,
                EmittedPeSha256 = Hash(imageBytes),
                Case = caseName,
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Optimization = compilation.Options.OptimizationLevel.ToString(),
                TypedInvocation = "Func<int, int> bound directly to emitted static Target; Target creates its own Cell receiver",
                InputValue = 0,
                PreconditionSatisfied = true,
                ExceptionType = exception!.GetType().FullName,
                ExactDivideByZeroException = exception.GetType() == typeof(DivideByZeroException),
                RawGetterImplementationFlags = (int)getterFlags,
                GetterImplementationFlags = getterFlags.ToString(),
                RawSetterImplementationFlags = (int)setter.GetMethodImplementationFlags(),
                ExpectedGetterSynchronized = synchronized,
                CellFieldCount = cell.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Length,
                BackingFieldName = field.Name,
                BackingFieldReadonly = field.IsInitOnly,
                BackingFieldStatic = field.IsStatic,
                HasTypeInitializer = cell.TypeInitializer != null,
                SynchronizationBasis = "Reflected instance getter Synchronized flag, emitted getter-before-divisionInstruction order and CLR runtime source semantics; no contention/timing observation.",
                ClrSource = "https://github.com/dotnet/runtime/blob/v9.0.0/src/coreclr/jit/flowgraph.cpp#L1572-L1588",
                RuntimeLimit = "Runtime identity is recorded. Reference CLR source is v9.0.0; no JIT machine-code disassembly is performed."
            });
        }
        finally
        {
            loadContext.Unload();
        }

        var tree = compilation.SyntaxTrees.Single();
        var syntaxRoot = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var targetSyntax = syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var targetSymbol = (IMethodSymbol)model.GetDeclaredSymbol(targetSyntax)!;
        var propertySyntax = syntaxRoot.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var propertySymbol = (IPropertySymbol)model.GetDeclaredSymbol(propertySyntax)!;
        var getterSymbol = propertySymbol.GetMethod!;
        var setterSymbol = propertySymbol.SetMethod!;
        var requirementSyntax = (InvocationExpressionSyntax)((ExpressionStatementSyntax)targetSyntax.Body!.Statements[0]).Expression;
        var requirement = (IInvocationOperation)model.GetOperation(requirementSyntax)!;
        var predicate = (IBinaryOperation)requirement.Arguments.Single().Value;
        var variableSyntax = syntaxRoot.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var localSymbol = (ILocalSymbol)model.GetDeclaredSymbol(variableSyntax)!;
        var creation = (IObjectCreationOperation)model.GetOperation(variableSyntax.Initializer!.Value)!;
        var assignment = (ICompoundAssignmentOperation)((IExpressionStatementOperation)model.GetOperation(targetSyntax.Body.Statements[2])!).Operation;
        var propertyReference = (IPropertyReferenceOperation)assignment.Target;
        var receiver = (ILocalReferenceOperation)propertyReference.Instance!;
        var division = (IBinaryOperation)assignment.Value;
        var divisor = (IParameterReferenceOperation)division.RightOperand;
        var cellSymbol = propertySymbol.ContainingType;
        var discovery = new ClaimManifestBuilder(compilation).Build();
        Assert.That(discovery.Targets, Has.Count.EqualTo(1));
        var selectedTarget = discovery.Targets.Values.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(targetSymbol.MethodKind, Is.EqualTo(MethodKind.Ordinary));
            Assert.That(targetSymbol.Name, Is.EqualTo("Target"));
            Assert.That(targetSymbol.IsStatic, Is.True);
            Assert.That(getterSymbol.MethodKind, Is.EqualTo(MethodKind.PropertyGet));
            Assert.That((int)getterSymbol.MethodImplementationFlags, Is.EqualTo((int)getterFlags));
            Assert.That((int)setterSymbol.MethodImplementationFlags, Is.Zero);
            Assert.That(SymbolEqualityComparer.Default.Equals(selectedTarget.Method, targetSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(selectedTarget.Method, getterSymbol), Is.False);
            Assert.That(SymbolEqualityComparer.Default.Equals(propertyReference.Property.GetMethod, getterSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(propertyReference.Property.SetMethod, setterSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(receiver.Local, localSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(creation.Type, cellSymbol), Is.True);
            Assert.That(creation.Arguments, Is.Empty);
            Assert.That(creation.Initializer, Is.Null);
            Assert.That(creation.Constructor!.IsImplicitlyDeclared, Is.True);
            Assert.That(assignment.OperatorKind, Is.EqualTo(BinaryOperatorKind.Add));
            Assert.That(assignment.OperatorMethod, Is.Null);
            Assert.That(division.OperatorKind, Is.EqualTo(BinaryOperatorKind.Divide));
            Assert.That(division.LeftOperand.ConstantValue.Value, Is.EqualTo(1));
            Assert.That(SymbolEqualityComparer.Default.Equals(divisor.Parameter, targetSymbol.Parameters.Single()), Is.True);
            Assert.That(requirement.TargetMethod.Name, Is.EqualTo("Requires"));
            Assert.That(requirement.TargetMethod.ContainingType.ToDisplayString(), Is.EqualTo("SharpProof.Attributes.Contract"));
            Assert.That(predicate.OperatorKind, Is.EqualTo(BinaryOperatorKind.Equals));
            Assert.That(SymbolEqualityComparer.Default.Equals(((IParameterReferenceOperation)predicate.LeftOperand).Parameter, targetSymbol.Parameters.Single()), Is.True);
            Assert.That(predicate.RightOperand.ConstantValue.Value, Is.EqualTo(0));
            Assert.That(cellSymbol.IsSealed, Is.True);
            Assert.That(cellSymbol.StaticConstructors, Is.Empty);
            Assert.That(syntaxRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>(), Is.Empty);
            Assert.That(syntaxRoot.DescendantNodes().OfType<FieldDeclarationSyntax>(), Is.Empty);
            Assert.That(propertySyntax.Initializer, Is.Null);
            Assert.That(propertySyntax.AccessorList!.Accessors.All(accessor => accessor.Body == null && accessor.ExpressionBody == null), Is.True);
            Assert.That(getterSymbol.GetAttributes(), Has.Length.EqualTo(1));
            Assert.That(getterSymbol.GetAttributes().Single().AttributeClass!.ToDisplayString(), Is.EqualTo("System.Runtime.CompilerServices.MethodImplAttribute"));
            Assert.That(setterSymbol.GetAttributes(), Is.Empty);
            Assert.That(selectedTarget.Declaration!.Span, Is.EqualTo(targetSyntax.Span));
            Assert.That(selectedTarget.EffectClaims, Has.Length.EqualTo(1));
            Assert.That(selectedTarget.EffectClaims.Single().HasValidConstraint, Is.True);
        }
        await Save(evidenceDirectory, "source-call-binding.json", new
        {
            SourceSha256 = sourceHash,
            SelectedCallableId = selectedTarget.Entry.CallableId,
            TargetSourceMethod = targetSymbol.ToDisplayString(),
            GetterSourceMethod = getterSymbol.ToDisplayString(),
            SetterSourceMethod = setterSymbol.ToDisplayString(),
            SelectedSourceIsTarget = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, targetSymbol),
            SelectedSourceIsGetter = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, getterSymbol),
            GetterSourceImplementationFlags = (int)getterSymbol.MethodImplementationFlags,
            SetterSourceImplementationFlags = (int)setterSymbol.MethodImplementationFlags,
            TargetSpanStart = targetSyntax.SpanStart,
            TargetSpanLength = targetSyntax.Span.Length,
            Requirement = requirementSyntax.ToString(),
            RequirementParameterIsTargetInput = SymbolEqualityComparer.Default.Equals(((IParameterReferenceOperation)predicate.LeftOperand).Parameter, targetSymbol.Parameters.Single()),
            RequirementZero = predicate.RightOperand.ConstantValue.Value,
            AssignmentKind = assignment.Kind.ToString(),
            Operator = assignment.OperatorKind.ToString(),
            ReceiverOperationKind = receiver.Kind.ToString(),
            ReceiverFreshInitializerKind = creation.Kind.ToString(),
            ReceiverConstructorImplicit = creation.Constructor!.IsImplicitlyDeclared,
            DivisorIsTargetInput = SymbolEqualityComparer.Default.Equals(divisor.Parameter, targetSymbol.Parameters.Single()),
            SourceInitializersAbsent = creation.Initializer == null && propertySyntax.Initializer == null,
            StaticConstructorsAbsent = cellSymbol.StaticConstructors.Length == 0
        });
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var manifestBytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "compiler-manifest.json"), manifestBytes);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-request.json"), WorkerProtocolJson.SerializeRequest(project.Request) + "\n");
        Assert.That(Hash(manifestBytes), Is.EqualTo(project.Request.CompilerManifest.Sha256));
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-response.json"), WorkerProtocolJson.SerializeResponse(response) + "\n");
        var validation = WorkerProtocolJson.ValidateForRequest(response, WorkerProtocolJson.ComputeRequestHash(project.Request),
            Program.ComputeExpectedInputHash(project.Request, manifestBytes), artifact.Manifest, project.Request, Program.ExpectedVersions());
        await Save(evidenceDirectory, "native-observations.json", new
        {
            SourceSha256 = sourceHash,
            EmittedPeSha256 = Hash(imageBytes),
            Case = caseName,
            SameCompilationForClrAndNative = true,
            CapturedTotal = artifact.Callables.Single().Total != null,
            CapturedBodyAbstraction = artifact.Callables.Single().Total?.IsBodyAbstraction,
            SelectedCallableId = selectedTarget.Entry.CallableId,
            SelectedSourceMethod = targetSymbol.ToDisplayString(),
            CalledGetterSourceMethod = getterSymbol.ToDisplayString(),
            SelectedSourceIsTarget = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, targetSymbol),
            SelectedSourceIsGetter = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, getterSymbol),
            ReflectedGetterImplementationFlags = getterFlags.ToString(),
            ReflectedGetterSynchronized = (getterFlags & MethodImplAttributes.Synchronized) != 0,
            CacheEnabled = project.Request.Cache.Enabled,
            Budgets = project.Request.Budgets,
            RunStatus = response.RunStatus.ToString(),
            FailureReason = response.FailureReason.ToString(),
            ProtocolValid = validation.IsValid,
            ProtocolErrors = validation.Errors,
            response.Errors,
            ManifestClaims = artifact.Manifest.Claims.Select(claim => new { claim.ClaimId, Kind = claim.Kind.ToString(), ContractKind = claim.EffectContractKind.ToString() }).ToArray(),
            Claims = response.ClaimResults.Select(claim => new
            {
                claim.ClaimId,
                Outcome = claim.Outcome.ToString(),
                Reason = claim.Reason.ToString(),
                EffectCertainty = claim.EffectCertainty.ToString(),
                Vacuity = claim.Vacuity.ToString()
            }).ToArray(),
            FrozenExpectation = synchronized ? "The synchronized getter executes before the divide-by-zero fault, so the compound caller cannot be Proven EnforcePure."
                : "The NoInlining-only getter compound caller remains complete Proven EnforcePure under its value==0 precondition."
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(validation.IsValid, Is.True, JsonSerializer.Serialize(validation.Errors));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(project.Request.Cache.Enabled, Is.False);
            Assert.That(JsonSerializer.Serialize(project.Request.Budgets), Is.EqualTo(JsonSerializer.Serialize(new WorkerBudgets())));
            Assert.That(artifact.Callables, Has.Length.EqualTo(1));
            Assert.That(artifact.Callables.Single().CallableId, Is.EqualTo(selectedTarget.Entry.CallableId));
            Assert.That(artifact.Manifest.Callables.Single().Assumptions, Has.Length.EqualTo(1));
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(artifact.Manifest.Claims.Single().CallableId, Is.EqualTo(selectedTarget.Entry.CallableId));
            Assert.That(artifact.Manifest.Claims.Single().Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(artifact.Manifest.Claims.Single().EffectContractKind, Is.EqualTo(WorkerEffectContractKind.EnforcePure));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            Assert.That(response.CallableResults, Has.Length.EqualTo(1));
            var claim = response.ClaimResults.Single();
            Assert.That(claim.ClaimId, Is.EqualTo(artifact.Manifest.Claims.Single().ClaimId));
            Assert.That(response.CallableResults.Single().CallableId, Is.EqualTo(selectedTarget.Entry.CallableId));
            Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            if (synchronized)
            {
                Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
                    "The synchronized getter executes before the RHS division fault even though the setter store is unreachable under the nonvacuous precondition.");
            }
            else
            {
                Assert.That(artifact.Callables.Single().Total, Is.Not.Null);
                Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
                Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
                Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.None));
                Assert.That(claim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            }
        }
    }

    private static DecodedInstruction[] ReadTargetIl(byte[] il)
    {
        var result = new List<DecodedInstruction>();
        var offset = 0;
        while (offset < il.Length)
        {
            var start = offset;
            var opcode = (int)il[offset++];
            int? token = null;
            if (opcode == 0xfe)
            {
                opcode = (opcode << 8) | il[offset++];
                if (opcode != 0xfe01)
                {
                    throw new InvalidOperationException("Unexpected multi-byte Target opcode.");
                }
            }
            else if (opcode is 0x28 or 0x6f or 0x73)
            {
                token = BitConverter.ToInt32(il, offset);
                offset += 4;
            }
            else if (opcode is not (0x00 or 0x02 or 0x03 or 0x06 or 0x07 or 0x0a or 0x0b or 0x16 or 0x17 or 0x25 or 0x58 or 0x5b or 0x2a))
            {
                throw new InvalidOperationException("Unexpected Target opcode: " + opcode.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            result.Add(new(start, opcode, token));
        }
        return result.ToArray();
    }

    private sealed record DecodedInstruction(int Offset, int Opcode, int? Token);

    private static string Hash(byte[] value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(value));
    }

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value) + "\n");
    }
}
