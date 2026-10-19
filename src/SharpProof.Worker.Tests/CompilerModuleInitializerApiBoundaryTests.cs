using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerModuleInitializerApiBoundaryTests
{
    [Test]
    public void RealAccessorDiagnosticPreservesComponentAndPublicCaptureBoundaries()
    {
        const string validSource = "using SharpProof.Attributes; using System.Runtime.CompilerServices; public static class Subject { [ModuleInitializer] public static void Initialize() { } [DoesNotThrow] public static int Root() => 1; }";
        const string errorSource = "using SharpProof.Attributes; using System.Runtime.CompilerServices; public static class Subject { public static int Value { [ModuleInitializer] get => 1; } [DoesNotThrow] public static int Root() => 1; }";
        var valid = TestCompilation.Create("ModuleInitializerApiBoundary", ("valid.cs", validSource));
        using var image = new MemoryStream();
        Assert.That(valid.Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ModuleInitializerApiBoundary", isCollectible: true);
        try
        {
            Assert.That(runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Root")!.Invoke(null, null), Is.EqualTo(1));
        }
        finally { runtime.Unload(); }
        var validDiscovery = new ClaimManifestBuilder(valid).Build();
        var validTarget = validDiscovery.Targets.Values.Single();
        var validPreparation = new CompilerCallableLowerer(valid, new IrFactory()).Prepare(validTarget, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(validPreparation.FailureReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(validPreparation.Total, Is.Not.Null);
        Assert.That(validPreparation.Total!.ExceptionConstraints, Has.Length.EqualTo(1));
        var validArtifact = Capture(valid);
        Assert.That(validArtifact.CompilerDiagnostics, Is.Empty);
        Assert.That(validArtifact.Callables.Single().Total!.ExceptionConstraints, Has.Length.EqualTo(1));

        var tree = CSharpSyntaxTree.ParseText(errorSource, (CSharpParseOptions)valid.SyntaxTrees.Single().Options, "accessor-error.cs");
        var compilation = valid.RemoveAllSyntaxTrees().AddSyntaxTrees(tree);
        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == "CS8813" && diagnostic.Severity == DiagnosticSeverity.Error)), Is.True);
        using var invalidImage = new MemoryStream();
        Assert.That(compilation.Emit(invalidImage).Success, Is.False);
        var accessor = tree.GetRoot().DescendantNodes().OfType<AccessorDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(accessor)!;
        Assert.That(method.MethodKind, Is.EqualTo(MethodKind.PropertyGet));
        Assert.That(method.IsStatic, Is.True);
        Assert.That(method.GetAttributes().Single().AttributeClass!.ToDisplayString(), Is.EqualTo("System.Runtime.CompilerServices.ModuleInitializerAttribute"));
        Assert.That(method.DeclaringSyntaxReferences.Single().GetSyntax(), Is.SameAs(accessor));
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var target = discovery.Targets.Values.Single();
        Assert.That(target.Method.Name, Is.EqualTo("Root"));
        Assert.That(target.Method.MethodKind, Is.EqualTo(MethodKind.Ordinary));
        Assert.That(target.IsVerifierSupported, Is.True);
        Assert.That(target.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(target.SemanticModel.SyntaxTree, Is.SameAs(tree));
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(target, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(preparation.FailureReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.ExceptionConstraints, Is.Empty);

        var artifact = Capture(compilation);
        Assert.That(artifact.CompilerDiagnostics.Any(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Code == "compiler.CS8813")), Is.True);
        var claim = artifact.Manifest.Claims.Single();
        Assert.That(claim.EffectContractKind, Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
        var callable = artifact.Callables.Single();
        Assert.That(callable.CallableId, Is.EqualTo(claim.CallableId));
        Assert.That(callable.FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
        Assert.That(callable.Total, Is.Null);
        Assert.That(artifact.ReachableSource, Is.Null);
    }

    [TestCase("setter", MethodKind.PropertySet)]
    [TestCase("init", MethodKind.PropertySet)]
    [TestCase("event-add", MethodKind.EventAdd)]
    [TestCase("event-remove", MethodKind.EventRemove)]
    [TestCase("operator", MethodKind.UserDefinedOperator)]
    [TestCase("conversion", MethodKind.Conversion)]
    [TestCase("destructor", MethodKind.Destructor)]
    public void OtherRealNonordinaryMembersPreserveInitializerBoundaries(string shape, MethodKind expectedKind)
    {
        const string validSource = "using SharpProof.Attributes; using System.Runtime.CompilerServices; public class Subject { [ModuleInitializer] public static void Initialize() { } [DoesNotThrow] public static int Root() => 1; }";
        var member = shape switch
        {
            "setter" => "public static int Value { get => 1; [ModuleInitializer] set { } }",
            "init" => "public int Value { get => 1; [ModuleInitializer] init { } }",
            "event-add" => "public static event System.Action Value { [ModuleInitializer] add { } remove { } }",
            "event-remove" => "public static event System.Action Value { add { } [ModuleInitializer] remove { } }",
            "operator" => "[ModuleInitializer] public static Subject operator -(Subject value) => value;",
            "conversion" => "[ModuleInitializer] public static explicit operator int(Subject value) => 1;",
            "destructor" => "[ModuleInitializer] ~Subject() { }",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var errorSource = "using SharpProof.Attributes; using System.Runtime.CompilerServices; public class Subject { " +
            member + " [DoesNotThrow] public static int Root() => 1; }";
        var valid = TestCompilation.Create("ModuleInitializerNonordinary", ("valid.cs", validSource));
        using var image = new MemoryStream();
        Assert.That(valid.Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ModuleInitializerNonordinary", isCollectible: true);
        try
        {
            Assert.That(runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Root")!.Invoke(null, null), Is.EqualTo(1));
        }
        finally { runtime.Unload(); }
        Assert.That(Capture(valid).Callables.Single().Total!.ExceptionConstraints, Has.Length.EqualTo(1));
        var tree = CSharpSyntaxTree.ParseText(errorSource, (CSharpParseOptions)valid.SyntaxTrees.Single().Options, shape + ".cs");
        var compilation = valid.RemoveAllSyntaxTrees().AddSyntaxTrees(tree);
        Assert.That(compilation.GetDiagnostics().Any(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id == "CS8813" && diagnostic.Severity == DiagnosticSeverity.Error)), Is.True);
        using var invalidImage = new MemoryStream();
        Assert.That(compilation.Emit(invalidImage).Success, Is.False);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers().OfType<IMethodSymbol>()
            .Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.GetAttributes().Any(attribute => attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeClass!.ToDisplayString() ==
                "System.Runtime.CompilerServices.ModuleInitializerAttribute"))));
        Assert.That(method.MethodKind, Is.EqualTo(expectedKind));
        var syntax = method.DeclaringSyntaxReferences.Single().GetSyntax();
        Assert.That(syntax.SyntaxTree, Is.SameAs(tree));
        Assert.That(syntax, Is.Not.InstanceOf<MethodDeclarationSyntax>());
        Assert.That(syntax, Is.Not.InstanceOf<ConstructorDeclarationSyntax>());
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        Assert.That(target.Method.Name, Is.EqualTo("Root"));
        Assert.That(target.IsVerifierSupported, Is.True);
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(target, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.ExceptionConstraints, Is.Empty);
        var artifact = Capture(compilation);
        Assert.That(artifact.CompilerDiagnostics.Any(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Code == "compiler.CS8813")), Is.True);
        Assert.That(artifact.Callables.Single().FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
        Assert.That(artifact.Callables.Single().Total, Is.Null);
        Assert.That(artifact.ReachableSource, Is.Null);
    }

    [Test]
    public void RealFieldLikeEventAccessorsCannotSupplyAnInitializerSourceBody()
    {
        const string source = "using SharpProof.Attributes; using System.Runtime.CompilerServices; public static class Subject { [method:ModuleInitializer] public static event System.Action? Value; [DoesNotThrow] public static int Root() => 1; }";
        var valid = TestCompilation.Create("FieldLikeEventControl", "public static class Subject { public static int Root() => 1; }");
        var tree = CSharpSyntaxTree.ParseText(source, (CSharpParseOptions)valid.SyntaxTrees.Single().Options, "field-like-event.cs");
        var compilation = valid.RemoveAllSyntaxTrees().AddSyntaxTrees(tree);
        var eventSymbol = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Value").OfType<IEventSymbol>().Single();
        foreach (var method in ImmutableArray.Create(eventSymbol.AddMethod!, eventSymbol.RemoveMethod!))
        {
            Assert.That(method.IsImplicitlyDeclared, Is.True);
            Assert.That(method.IsStatic, Is.True);
            Assert.That(method.DeclaringSyntaxReferences, Is.Empty);
            Assert.That(method.GetAttributes().Single().AttributeClass!.ToDisplayString(), Is.EqualTo("System.Runtime.CompilerServices.ModuleInitializerAttribute"));
        }
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.EqualTo(errors.Length == 0));
        TestContext.Progress.WriteLine("FieldLikeEvent: EmitSuccess=" + emission.Success + "; errors=" + string.Join(",", errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id))));
        if (emission.Success)
        {
            image.Position = 0;
            var runtime = new System.Runtime.Loader.AssemblyLoadContext("FieldLikeEventBoundary", isCollectible: true);
            try
            {
                Assert.That(runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Root")!.Invoke(null, null), Is.EqualTo(1));
            }
            finally { runtime.Unload(); }
        }
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        Assert.That(target.Method.Name, Is.EqualTo("Root"));
        Assert.That(target.IsVerifierSupported, Is.True);
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(target, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.ExceptionConstraints, Is.Empty);
        var artifact = Capture(compilation);
        Assert.That(artifact.CompilerDiagnostics.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Code)),
            Is.EquivalentTo(errors.Select(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : ("compiler." + diagnostic.Id))));
        if (errors.Length == 0)
        {
            Assert.That(artifact.Callables.Single().Total, Is.Not.Null);
            Assert.That(artifact.Callables.Single().Total!.ExceptionConstraints, Is.Empty);
        }
        else
        {
            Assert.That(artifact.Callables.Single().FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
            Assert.That(artifact.Callables.Single().Total, Is.Null);
            Assert.That(artifact.ReachableSource, Is.Null);
        }
    }

    private static CompilerManifestArtifact Capture(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
    }
}
