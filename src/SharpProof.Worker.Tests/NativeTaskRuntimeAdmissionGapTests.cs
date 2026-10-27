using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeTaskRuntimeAdmissionGapTests
{
    private const string DeploymentHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestCase("foreign-id")]
    [TestCase("instance")]
    [TestCase("generic-method")]
    [TestCase("generic-type")]
    public void RealDeclarationsCannotBindToAnUnsupportedOrForeignRoot(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var declaration = mutation == "instance" ? "public void Target() {}" :
            mutation == "generic-method" ? "public static void Target<T>() {}" : "public static void Target() {}";
        var compilation = Compile("using SharpProof.Attributes; public class " + (mutation == "generic-type" ? "C<T>" : "C") +
            " {" + Attributes(2) + declaration + "}");
        var root = Root(compilation, mutation == "generic-type" ? "C`1" : "C", "Target");
        var id = SemanticClaimIdentity.CreateCallableId(root) + (mutation == "foreign-id" ? ":foreign" : "");
        AssertRejected(CompilerTaskRuntimePremiseCollector.Parse(root, id, TestContext.CurrentContext.CancellationToken),
            "TaskRuntimePremisesInvalid");
    }

    [Test]
    public void DuplicateRealDeploymentAttributesAreRejectedEvenWhenRoslynReportsTheirDuplicateUsage()
    {
        var source = "using SharpProof.Attributes; public static class C {" + Attributes(2) +
            "[ResourceTaskDeployment(ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed,\"" +
            DeploymentHash + "\",\"second declaration\")] public static void Target() {} }";
        var compilation = CreateUnchecked("DuplicateRuntimeDeployment", source, includeAttributes: true);
        Assert.That(compilation.GetDiagnostics().Any(static diagnostic => diagnostic is null
            ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Id == "CS0579"), Is.True);
        var root = Root(compilation, "C", "Target");
        AssertRejected(CompilerTaskRuntimePremiseCollector.Parse(root, SemanticClaimIdentity.CreateCallableId(root),
            TestContext.CurrentContext.CancellationToken), "TaskRuntimePremisesDuplicate");
    }

    [Test]
    public void NonconstantEvidenceCannotEnterTheImmutableDeclarationSet()
    {
        var source = "using SharpProof.Attributes; public static class C {" + Attributes(2)
            .Replace("\"admission deployment\"", "System.DateTime.Now.ToString()", StringComparison.Ordinal) +
            "public static void Target() {} }";
        var compilation = CreateUnchecked("NonconstantRuntimeEvidence", source, includeAttributes: true);
        Assert.That(compilation.GetDiagnostics().Any(static diagnostic => diagnostic is null
            ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Id == "CS0182"), Is.True);
        var root = Root(compilation, "C", "Target");
        AssertRejected(CompilerTaskRuntimePremiseCollector.Parse(root, SemanticClaimIdentity.CreateCallableId(root),
            TestContext.CurrentContext.CancellationToken), "TaskRuntimePremisesInvalid");
    }

    [Test]
    public void AnUnresolvedAttributeCannotCreateRuntimeDeclarationsDuringCompilerErrorRecovery()
    {
        var compilation = CreateUnchecked("UnresolvedRuntimeAttribute",
            "public static class C { [RuntimeDeclarationDoesNotExist] public static void Target() {} }", includeAttributes: true);
        Assert.That(compilation.GetDiagnostics().Any(static diagnostic => diagnostic is null
            ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Id == "CS0246"), Is.True);
        var root = Root(compilation, "C", "Target");
        AssertRejected(CompilerTaskRuntimePremiseCollector.Parse(root, SemanticClaimIdentity.CreateCallableId(root),
            TestContext.CurrentContext.CancellationToken), "TaskRuntimePremisesNotDeclared");
    }

    [TestCase("version", "TaskRuntimePremisesAttributeIdentityInvalid")]
    [TestCase("missing-type", "TaskRuntimePremisesAttributeIdentityInvalid")]
    [TestCase("short-constructor", "TaskRuntimePremisesAttributeIdentityInvalid")]
    [TestCase("optional-constructor", "TaskRuntimePremisesAttributeIdentityInvalid")]
    [TestCase("named-argument", "TaskRuntimePremisesInvalid")]
    public void RealMetadataLookalikesMustMeetTheWholeAttributeIdentityAndConstructorContract(string mutation, string reason)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(reason);
        var version = typeof(CompilerTaskRuntimePremiseCollector).Assembly.GetName().Version!;
        var identityVersion = mutation == "version" ? new Version(version.Major + 1, version.Minor, version.Build, version.Revision) : version;
        var parameters = mutation == "short-constructor" ? "ResourceTaskDeploymentProfile profile,string hash" :
            "ResourceTaskDeploymentProfile profile,string hash,string evidence" + (mutation == "optional-constructor" ? "=\"default\"" : "");
        var source = "using System; [assembly:System.Reflection.AssemblyVersion(\"" + identityVersion + "\")]" +
            "namespace SharpProof.Attributes { public enum ResourceTaskDeploymentProfile { Closed=1 }" +
            "public enum ResourceTaskRuntimeQuantity { First=1 }" +
            "public sealed class ResourceTaskDeploymentAttribute:Attribute {public ResourceTaskDeploymentAttribute(" + parameters +
            "){} public string Note {get;set;} }" + (mutation == "missing-type" ? "" :
            "public sealed class ResourceTaskRuntimeUpperBoundAttribute:Attribute {public ResourceTaskRuntimeUpperBoundAttribute(ResourceTaskRuntimeQuantity quantity,long upper,string evidence){}}") + "}";
        var metadata = CreateUnchecked("SharpProof.Attributes", source, includeAttributes: false);
        TestCompilation.AssertNoErrors(metadata);
        using var image = new MemoryStream();
        var emission = metadata.Emit(image, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(emission.Success, Is.True, string.Join(Environment.NewLine, emission.Diagnostics));
        var arguments = "ResourceTaskDeploymentProfile.Closed,\"" + DeploymentHash + "\"" +
            (mutation == "short-constructor" ? "" : ",\"metadata evidence\"") +
            (mutation == "named-argument" ? ",Note=\"not an immutable constructor value\"" : "");
        var metadataReference = MetadataReference.CreateFromImage(image.ToArray());
        var compilation = CreateUnchecked("RuntimeMetadataConsumer", "using SharpProof.Attributes; public static class C {" +
            "[ResourceTaskDeployment(" + arguments + ")] public static void Target() {} }", includeAttributes: false)
            .AddReferences(metadataReference);
        TestCompilation.AssertNoErrors(compilation);
        var root = Root(compilation, "C", "Target");
        var imported = compilation.GetAssemblyOrModuleSymbol(metadataReference) as IAssemblySymbol;
        Assert.That(imported, Is.Not.Null);
        var attribute = root.GetAttributes().Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(attribute.AttributeClass!.ContainingAssembly, imported), Is.True,
            "The consumer must bind the emitted lookalike rather than the installed attribute assembly.");
        Assert.That(attribute.AttributeConstructor!.Parameters.Length, Is.EqualTo(mutation == "short-constructor" ? 2 : 3));
        if (mutation == "optional-constructor")
        { Assert.That(attribute.AttributeConstructor.Parameters[2].IsOptional, Is.True); }
        if (mutation == "named-argument")
        { Assert.That(attribute.NamedArguments, Has.Length.EqualTo(1)); }
        AssertRejected(CompilerTaskRuntimePremiseCollector.Parse(root, SemanticClaimIdentity.CreateCallableId(root),
            TestContext.CurrentContext.CancellationToken), reason);
    }

    [TestCase("module")]
    [TestCase("compiler")]
    [TestCase("framework")]
    public void CaptureRejectsMalformedOwnershipAfterParsingRealCompleteDeclarations(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var compilation = Compile("using SharpProof.Attributes; public static class C {" + Attributes(2) + "public static void Target() {} }");
        var root = Root(compilation, "C", "Target");
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image, cancellationToken: TestContext.CurrentContext.CancellationToken).Success, Is.True);
        var module = Convert.ToHexStringLower(SHA256.HashData(image.ToArray()));
        var compiler = typeof(CSharpCompilation).Assembly.FullName!;
        var framework = compilation.GetSpecialType(SpecialType.System_Int32).ContainingAssembly.Identity.ToString();
        var id = SemanticClaimIdentity.CreateCallableId(root);
        var control = CompilerTaskRuntimePremiseCollector.Capture(root, id, module, compiler, framework, TestContext.CurrentContext.CancellationToken);
        Assert.That(control.Inventory, Is.Not.Null, control.UnknownReason);
        var rejected = CompilerTaskRuntimePremiseCollector.Capture(root, id, mutation == "module" ? "invalid" : module,
            mutation == "compiler" ? "" : compiler, mutation == "framework" ? "invalid\nframework" : framework,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(rejected.Inventory, Is.Null);
        Assert.That(rejected.UnknownReason, Is.EqualTo("TaskRuntimePremisesOwnershipInvalid"));
    }

    [Test]
    public void UninitializedManifestAssumptionsCannotBeTreatedAsAnEmptyVerifiedSet()
    {
        var compilation = Compile("using SharpProof.Attributes; public static class C {" + Attributes(2) + "public static void Target() {} }");
        var root = Root(compilation, "C", "Target");
        Assert.That(CompilerTaskRuntimePremiseCollector.VerifiedManifestAssumptionIds(root, SemanticClaimIdentity.CreateCallableId(root),
            default, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [TestCase(WorkerResourceTaskRuntimeQuantity.Unspecified)]
    [TestCase((WorkerResourceTaskRuntimeQuantity)(-1))]
    [TestCase((WorkerResourceTaskRuntimeQuantity)12)]
    public void UnknownQuantitiesCannotAcquireUnitsOrScopes(WorkerResourceTaskRuntimeQuantity quantity)
    {
        Action unit = () => { _ = ResourceTaskRuntimePremiseIdentity.Unit(quantity); };
        Action scope = () => { _ = ResourceTaskRuntimePremiseIdentity.Scope(quantity); };
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(unit)!.ParamName, Is.EqualTo("quantity"));
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(scope)!.ParamName, Is.EqualTo("quantity"));
    }

    [Test]
    public void MissingProtocolLabelsHaveNoEvidenceOrRootIdentity()
    {
        Assert.That(ResourceTaskRuntimePremiseIdentity.IsEvidence(null), Is.False);
        Assert.That(ResourceTaskRuntimePremiseIdentity.IsRootCallableId(null), Is.False);
        Assert.That(ResourceTaskRuntimePremiseIdentity.IsEvidence("owned evidence"), Is.True);
        Assert.That(ResourceTaskRuntimePremiseIdentity.IsRootCallableId("M:C.Target"), Is.True);
    }

    [Test]
    public void AValueDeclaredOnAnotherRealRootCannotSupplyAnOwnedQuantityDependency()
    {
        var compilation = Compile("using SharpProof.Attributes; public static class C {" + Attributes(2) + "public static void Target() {}" +
            Attributes(3) + "public static void Other() {} }");
        var root = Root(compilation, "C", "Target");
        var other = Root(compilation, "C", "Other");
        var owned = CompilerTaskRuntimePremiseCollector.Parse(root, SemanticClaimIdentity.CreateCallableId(root), TestContext.CurrentContext.CancellationToken).Declarations!;
        var foreign = CompilerTaskRuntimePremiseCollector.Parse(other, SemanticClaimIdentity.CreateCallableId(other), TestContext.CurrentContext.CancellationToken).Declarations!;
        Assert.That(ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(owned), Is.Not.EqualTo(ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(foreign)));
        Assert.That(ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(owned, owned.Quantities[0]), Is.Not.Empty);
        Action substitute = () => { _ = ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(owned, foreign.Quantities[0]); };
        Assert.That(Assert.Throws<ArgumentException>(substitute)!.ParamName, Is.EqualTo("declaration"));
    }

    [TestCase("declarations")]
    [TestCase("deployment")]
    [TestCase("quantity")]
    public async Task IncompleteRealDeclarationsCannotProduceIdentityOrPartialNativeEvidence(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var target = Prepare(1000000);
        var projection = target.ForkJoinProjection!;
        var inventory = projection.RuntimePremises!.Inventory!;
        var incomplete = inventory.Declarations with { Quantities = inventory.Declarations.Quantities.RemoveAt(10) };
        Action identity = () =>
        {
            _ = operation switch
            {
                "declarations" => ResourceTaskRuntimePremiseIdentity.DeclarationSha256(incomplete),
                "deployment" => ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(incomplete),
                "quantity" => ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(incomplete, incomplete.Quantities[0]),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        };
        Assert.That(Assert.Throws<InvalidDataException>(identity)!.Message, Is.EqualTo("TaskRuntimePremisesIncomplete"));
        var changed = target with
        {
            ForkJoinProjection = projection with { RuntimePremises = new(inventory with { Declarations = incomplete }, null) }
        };
        AssertBindingUnknown(NativeResourceTaskRuntimeBinding.Bind(changed, TestContext.CurrentContext.CancellationToken), "TaskRuntimePremisesIncomplete");
        AssertAtomicUnknown(await Verify(changed), "TaskRuntimePremisesIncomplete");
    }

    [TestCase("projection", "TaskRuntimePremisesNotDeclared")]
    [TestCase("entry", "TaskRuntimePremisesNotDeclared")]
    [TestCase("capture", "TaskRuntimePremisesNotDeclared")]
    [TestCase("references", "TaskRuntimeReferencePremiseNotBound")]
    [TestCase("platform", "TaskRuntimeReferencePremiseNotBound")]
    public void RuntimeBindingIndependentlyRequiresItsEntryAndReferencePremises(string mutation, string reason)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(reason);
        var target = Prepare(1000000);
        Assert.That(NativeResourceTaskRuntimeBinding.Bind(target, TestContext.CurrentContext.CancellationToken).Evidence, Is.Not.Null);
        target = mutation switch
        {
            "projection" => target with { ForkJoinProjection = null },
            "entry" => target with { TotalEntry = null },
            "capture" => target with { ForkJoinProjection = target.ForkJoinProjection! with { RuntimePremises = null } },
            "references" => target with { Compilation = target.Compilation with { ResourceMetadataReferences = [] } },
            "platform" => target with { Compilation = target.Compilation with { ResourceTargetPlatform = "AnyCpu" } },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        AssertBindingUnknown(NativeResourceTaskRuntimeBinding.Bind(target, TestContext.CurrentContext.CancellationToken), reason);
    }

    [Test]
    public async Task CompletePremisesCannotUpgradeAnActualComparisonUnknownDuringProjection()
    {
        var target = Prepare(0);
        var result = await Verify(target);
        AssertAtomicUnknown(result, "TaskRuntimeBoundComparisonUnproved");
        var binding = NativeResourceTaskRuntimeBinding.Bind(target, TestContext.CurrentContext.CancellationToken);
        Assert.That(binding.Evidence, Is.Not.Null, binding.UnknownReason);
        Action project = () => { _ = NativeResourceTaskRuntimeBinding.Project(target, result, binding); };
        Assert.That(Assert.Throws<ArgumentException>(project)!.ParamName, Is.EqualTo("binding"));
        AssertAtomicUnknown(result, "TaskRuntimeBoundComparisonUnproved");
    }

    private static CSharpCompilation Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("RuntimeAdmission", source);
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }

    private static CSharpCompilation CreateUnchecked(string assemblyName, string source, bool includeAttributes)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        ArgumentNullException.ThrowIfNull(source);
        return CSharpCompilation.Create(assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12),
                cancellationToken: TestContext.CurrentContext.CancellationToken)],
            includeAttributes ? TestMetadataReferences.WithSharpProof : TestMetadataReferences.WithoutSharpProof,
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static IMethodSymbol Root(CSharpCompilation compilation, string typeName, string methodName)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(methodName);
        return compilation.GetTypeByMetadataName(typeName)!.GetMembers(methodName).OfType<IMethodSymbol>().Single();
    }

    private static string Attributes(long upperBound)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(upperBound);
        var source = new StringBuilder("[ResourceTaskDeployment(ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed,\"");
        source.Append(DeploymentHash).Append("\",\"admission deployment\")]");
        foreach (var quantity in Enum.GetValues<ResourceTaskRuntimeQuantity>())
        {
            if (quantity == ResourceTaskRuntimeQuantity.Unspecified)
            { continue; }
            source.Append("[ResourceTaskRuntimeUpperBound(ResourceTaskRuntimeQuantity.").Append(quantity)
                .Append(',').Append(upperBound.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append("L,\"admission quantity\")]");
        }
        return source.ToString();
    }

    private static CompilerCallablePreparation Prepare(long bound)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bound);
        var source = "using SharpProof.Attributes; using System.Threading.Tasks; public static class C {" + Attributes(2) +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes," + bound.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "L)] public static Task Target(int n)=>Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(n)));" +
            "static void Child(int n){var values=new int[3];}}";
        var initial = Compile(source);
        const string references = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.32/ref/net8.0";
        var compilation = initial.WithReferences(Directory.GetFiles(references, "*.dll")
            .Append(typeof(ResourceUpperBoundAttribute).Assembly.Location)
            .Select(static path => path is null ? throw new ArgumentNullException(nameof(path)) : MetadataReference.CreateFromFile(path)))
            .WithOptions(initial.Options.WithPlatform(Platform.X64).WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var manifest = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All, cancellation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net8.0", WorkerFeatureSet.All,
            manifest, WorkerBudgets.DefaultMaximumExpressionDepth, cancellation);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellation);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellation);
        return preparations.Single();
    }

    private static async Task<WorkerClaimResult> Verify(CompilerCallablePreparation target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(),
            TestContext.CurrentContext.CancellationToken)).Single();
    }

    private static void AssertRejected(CompilerTaskRuntimePremiseDeclarationCapture capture, string reason)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(capture.Declarations, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo(reason));
    }

    private static void AssertBindingUnknown(NativeTaskRuntimePremiseBindingCheck check, string reason)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(check.Inputs, Is.Null);
        Assert.That(check.Evidence, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo(reason));
    }

    private static void AssertAtomicUnknown(WorkerClaimResult result, string reason)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.RuntimePremises, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
        Assert.That(result.ResourceBound.Expected, Is.Null);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.ProofCore, Is.Empty);
        Assert.That(result.Model, Is.Empty);
        Assert.That(result.EffectWitness, Is.Null);
        Assert.That(result.Assumptions.Any(static assumption => assumption is null
            ? throw new ArgumentNullException(nameof(assumption)) : assumption.Used), Is.False);
    }
}
