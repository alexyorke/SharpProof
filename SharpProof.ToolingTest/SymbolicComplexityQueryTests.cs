using NUnit.Framework;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class SymbolicComplexityQueryTests
    {
        [Test]
        public async Task SymbolicCli_Complexity_RejectsInvalidCombinations()
        {
            const string source = """
public sealed class TestClass
{
    public int TestMethod()
    {
        return 42;
    }
}
""";
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicComplexityInvalid-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                    "--file",
                    sourcePath,
                    "--complexity",
                    "--all-lines");

                Assert.That(result.ExitCode, Is.EqualTo(64));
                Assert.That(result.StandardError, Does.Contain("--complexity supports --line, --line with --column, or --position only."));
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }
    }
}
