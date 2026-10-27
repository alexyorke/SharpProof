using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class RuntimePremiseCompilerCollectorCoverageTests
{
    private const string ModuleHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Deployment = "[ResourceTaskDeployment((ResourceTaskDeploymentProfile)1,\"" + ModuleHash + "\",\"deployment evidence\")]";

    [Test]
    public void DefaultManifestAssumptionsCannotAcquireRuntimeDeclarationOwnership()
    {
        var (_, root) = CreateRoot(CompleteDeclarations());
        var callableId = SemanticClaimIdentity.CreateCallableId(root);
        Assert.That(CompilerTaskRuntimePremiseCollector.Parse(root, callableId,
            TestContext.CurrentContext.CancellationToken).Declarations, Is.Not.Null);

        var result = CompilerTaskRuntimePremiseCollector.VerifiedManifestAssumptionIds(root, callableId, default,
            TestContext.CurrentContext.CancellationToken);

        Assert.That(result, Is.Null);
    }

    [TestCase("complete")]
    [TestCase("missing")]
    [TestCase("duplicate")]
    public void RuntimeManifestRequiresOneDeploymentAndEveryDistinctQuantityIdentity(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var (_, root) = CreateRoot(CompleteDeclarations());
        var callableId = SemanticClaimIdentity.CreateCallableId(root);
        var parsed = CompilerTaskRuntimePremiseCollector.Parse(root, callableId, TestContext.CurrentContext.CancellationToken);
        Assert.That(parsed.Declarations, Is.Not.Null, parsed.UnknownReason);
        var declarations = parsed.Declarations!;
        var assumptions = ImmutableArray.CreateBuilder<WorkerAssumptionEvidence>();
        assumptions.Add(new()
        {
            Id = ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(declarations),
            Kind = WorkerAssumptionKind.UserAssume
        });
        foreach (var quantity in declarations.Quantities)
        {
            assumptions.Add(new()
            {
                Id = ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(declarations, quantity),
                Kind = WorkerAssumptionKind.UserAssume
            });
        }
        Assert.That(assumptions.Select(static assumption => assumption is null
            ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id).Distinct().Count(), Is.EqualTo(12));
        if (mutation == "missing")
        { assumptions.RemoveAt(assumptions.Count - 1); }
        else if (mutation == "duplicate")
        { assumptions.Add(assumptions[0]); }

        var result = CompilerTaskRuntimePremiseCollector.VerifiedManifestAssumptionIds(root, callableId,
            assumptions.ToImmutable(), TestContext.CurrentContext.CancellationToken);

        if (mutation == "complete")
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(12));
            Assert.That(result, Is.EquivalentTo(assumptions.Select(static assumption => assumption is null
                ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id)));
        }
        else
        { Assert.That(result, Is.Null); }
    }

    [TestCase("identity")]
    [TestCase("instance")]
    [TestCase("generic")]
    public void DeclarationsCannotMoveToAForeignOrUnsupportedRoot(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var (_, root) = CreateRoot(Deployment, mutation switch
        {
            "instance" => "public void Root() {}",
            "generic" => "public static void Root<T>() {}",
            _ => "public static void Root() {}"
        });
        var callableId = mutation == "identity" ? "M:Foreign.Root" : SemanticClaimIdentity.CreateCallableId(root);

        var parsed = CompilerTaskRuntimePremiseCollector.Parse(root, callableId, TestContext.CurrentContext.CancellationToken);

        Assert.That(parsed.Declarations, Is.Null);
        Assert.That(parsed.UnknownReason, Is.EqualTo("TaskRuntimePremisesInvalid"));
    }

    [TestCase("version", "TaskRuntimePremisesAttributeIdentityInvalid")]
    [TestCase("missing-type", "TaskRuntimePremisesAttributeIdentityInvalid")]
    [TestCase("optional-constructor", "TaskRuntimePremisesAttributeIdentityInvalid")]
    [TestCase("named-argument", "TaskRuntimePremisesInvalid")]
    public void ActualReferencedAttributeMetadataMustMatchTheClosedDeclarationContract(string mutation, string expected)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(expected);
        var reference = CreateAttributeReference(mutation);
        var attribute = mutation == "named-argument" ? Deployment.Replace(
            "\"deployment evidence\")", "\"deployment evidence\", Note = \"unreviewed\")", StringComparison.Ordinal) : Deployment;
        var (_, root) = CreateRoot(attribute, references: TestMetadataReferences.WithoutSharpProof.Add(reference));

        var result = CompilerTaskRuntimePremiseCollector.Parse(root, SemanticClaimIdentity.CreateCallableId(root),
            TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Declarations, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo(expected));
    }

    [Test]
    public void DuplicateActualDeploymentAttributesCannotPublishTheFirstDeclaration()
    {
        var (compilation, root) = CreateRoot(Deployment + Deployment, allowErrors: true);
        Assert.That(compilation.GetDiagnostics().Select(static diagnostic => diagnostic is null
            ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Id), Does.Contain("CS0579"));

        var parsed = CompilerTaskRuntimePremiseCollector.Parse(root, SemanticClaimIdentity.CreateCallableId(root),
            TestContext.CurrentContext.CancellationToken);

        Assert.That(parsed.Declarations, Is.Null);
        Assert.That(parsed.UnknownReason, Is.EqualTo("TaskRuntimePremisesDuplicate"));
    }

    [TestCase("module")]
    [TestCase("compiler")]
    [TestCase("framework")]
    public void ValidSourceDeclarationsCannotSupplyInvalidEmittedOwnership(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var (_, root) = CreateRoot(CompleteDeclarations());

        var result = CompilerTaskRuntimePremiseCollector.Capture(root, SemanticClaimIdentity.CreateCallableId(root),
            mutation == "module" ? "invalid" : ModuleHash,
            mutation == "compiler" ? string.Empty : "Microsoft.CodeAnalysis.CSharp/4.14.0",
            mutation == "framework" ? string.Empty : "System.Private.CoreLib", TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Inventory, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskRuntimePremisesOwnershipInvalid"));
    }

    [Test]
    public void MissingAttributeTypeHasNoMetadataIdentity()
    {
        Assert.That(Inspect("MetadataName", [null]), Is.Null);
    }

    [Test]
    public void ActualGlobalNamespaceHasAnEmptyMetadataPrefix()
    {
        var (compilation, _) = CreateRoot(string.Empty);

        Assert.That(Inspect("NamespaceName", compilation.GlobalNamespace), Is.EqualTo(string.Empty));
    }

    private static string CompleteDeclarations()
    {
        return Deployment + string.Concat(ResourceTaskRuntimePremiseIdentity.RequiredQuantities.Select(static quantity =>
            "[ResourceTaskRuntimeUpperBound((ResourceTaskRuntimeQuantity)" + ((int)quantity).ToString(CultureInfo.InvariantCulture) +
            ",0L,\"quantity evidence\")]"));
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Root) CreateRoot(string attributes,
        string declaration = "public static void Root() {}", ImmutableArray<MetadataReference> references = default, bool allowErrors = false)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(declaration);
        references = references.IsDefault ? TestMetadataReferences.WithSharpProof : references;
        var compilation = CSharpCompilation.Create(nameof(RuntimePremiseCompilerCollectorCoverageTests),
            [CSharpSyntaxTree.ParseText("using SharpProof.Attributes;public class Subject {" + attributes + declaration + "}")],
            references, TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        if (!allowErrors)
        { TestCompilation.AssertNoErrors(compilation); }
        return (compilation, compilation.GetTypeByMetadataName("Subject")!.GetMembers("Root").OfType<IMethodSymbol>().Single());
    }

    private static PortableExecutableReference CreateAttributeReference(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var version = typeof(CompilerTaskRuntimePremiseCollector).Assembly.GetName().Version!;
        if (mutation == "version")
        { version = new Version(version.Major + 1, version.Minor, version.Build, version.Revision); }
        const string quantityAttribute = """
            public sealed class ResourceTaskRuntimeUpperBoundAttribute : System.Attribute {
                public ResourceTaskRuntimeUpperBoundAttribute(ResourceTaskRuntimeQuantity quantity, long upperBound, string evidence) {}
            }
            """;
        var source = """
            [assembly: System.Reflection.AssemblyVersion("__VERSION__")]
            namespace SharpProof.Attributes {
                public enum ResourceTaskDeploymentProfile { CoreClr8032LinuxX64OrdinaryJitStockClosed = 1 }
                public enum ResourceTaskRuntimeQuantity { RawTraceArrayAttemptsPerFaultEpisode = 1 }
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class ResourceTaskDeploymentAttribute : System.Attribute {
                    public ResourceTaskDeploymentAttribute(ResourceTaskDeploymentProfile profile, string digest, string evidence__OPTIONAL__) {}
                    public string Note { get; set; }
                }
                __QUANTITY_ATTRIBUTE__
            }
            """.Replace("__VERSION__", version.ToString(), StringComparison.Ordinal)
            .Replace("__OPTIONAL__", mutation == "optional-constructor" ? " = \"default\"" : string.Empty, StringComparison.Ordinal)
            .Replace("__QUANTITY_ATTRIBUTE__", mutation == "missing-type" ? string.Empty : quantityAttribute, StringComparison.Ordinal);
        var compilation = CSharpCompilation.Create("SharpProof.Attributes", [CSharpSyntaxTree.ParseText(source)],
            TestMetadataReferences.WithoutSharpProof, TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        TestCompilation.AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage([.. stream.ToArray()]);
    }

    private static object? Inspect(string name, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);
        var method = typeof(CompilerTaskRuntimePremiseCollector).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, name);
        return method!.Invoke(null, arguments);
    }
}
