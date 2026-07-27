namespace SharpProof.Dataflow.Test;

[TestFixture]
public sealed class DependencyBoundaryTests {
    [Test]
    public void ProductionAssemblyHasNoRoslynRuntimeReference() =>
        Assert.That(
            typeof(IAbstractDomain<>).Assembly
                .GetReferencedAssemblies()
                .Select(static assembly => assembly.Name),
            Has.None.StartsWith("Microsoft.CodeAnalysis"));
}
