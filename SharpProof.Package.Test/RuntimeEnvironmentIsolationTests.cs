using NUnit.Framework;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1050:Declare types in namespaces",
    Justification = "The .NET startup-hook protocol requires this global type name.")]
public static class StartupHook
{
    public static void Initialize()
    {
        var temporaryDirectory = Environment.GetEnvironmentVariable("TMPDIR");
        var arguments = Environment.GetCommandLineArgs();
        if (string.IsNullOrWhiteSpace(temporaryDirectory) ||
            arguments.Length == 0 ||
            !string.Equals(
                Path.GetFileName(arguments[0]),
                Path.GetFileName(temporaryDirectory) + ".dll",
                StringComparison.Ordinal))
        {
            return;
        }
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "startup-hook-loaded"),
            "loaded");
    }
}

namespace SharpProof.Package.Test
{
    using SharpProof.Host;

    [TestFixture]
    [NonParallelizable]
    public sealed class RuntimeEnvironmentIsolationTests
    {
        [TestCase("dotnet", false, true)]
        [TestCase("dotnet.exe", false, false)]
        [TestCase("dotnet.exe", true, true)]
        [TestCase("dotnet", true, false)]
        [TestCase("other.exe", true, false)]
        public void DirectMuxerNameMatchesTheHostPlatform(
            string fileName, bool isWindows, bool expected)
        {
            System.ArgumentNullException.ThrowIfNull(fileName);
            Assert.That(DotNetMuxer.IsFileName(fileName, isWindows), Is.EqualTo(expected));
        }

        [Test]
        public void MuxerPathComparisonFollowsTheHostPlatform()
        {
            Assert.That("C:\\DOTNET\\dotnet.exe".Equals(
                "c:\\dotnet\\dotnet.exe", DotNetMuxer.PathComparison(true)), Is.True);
            Assert.That("/opt/DotNet/dotnet".Equals(
                "/opt/dotnet/dotnet", DotNetMuxer.PathComparison(false)), Is.False);
        }

        [TestCase("SharpProof.Worker")]
        public async Task ProductRuntimeExecutablesDoNotLoadStartupHooks(
            string project)
        {
            System.ArgumentNullException.ThrowIfNull(project);
            TestRepository.RequireCanonicalContainer();

            using var temporary = new TempDirectory("sharp-hook-");
            var hookDirectory = Path.Combine(temporary.FullName, project);
            Directory.CreateDirectory(hookDirectory);
            var markerPath = Path.Combine(
                hookDirectory,
                "startup-hook-loaded");
            var assemblyPath = ProductBuildOutputs.RuntimeAssemblyPath(project);
            var dotnetHostPath = Environment.ProcessPath ??
                throw new InvalidOperationException(
                    "The current dotnet host path is unavailable.");
            var startInfo = ProcessRunner.CreateStartInfo(
                hookDirectory,
                dotnetHostPath,
                [assemblyPath]);
            startInfo.Environment["DOTNET_STARTUP_HOOKS"] =
                typeof(global::StartupHook).Assembly.Location;
            startInfo.Environment["TMPDIR"] = hookDirectory;

            var result = await ProcessRunner.RunCapturedAsync(
                startInfo,
                TestContext.CurrentContext.CancellationToken);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(
                    File.Exists(markerPath),
                    Is.False,
                    "Startup hooks must be disabled before the product entry point runs.");
            }
        }

        [Test]
        [Platform("Linux")]
        public void WorkerProcessReceivesOnlyTheRuntimeAllowlist()
        {
            using var temporary = new TempDirectory("sharp-env-");
            var inheritedEnvironment = new Dictionary<string, string?>
            {
                ["PATH"] = "/usr/bin:/bin",
                ["HOME"] = temporary.FullName,
                ["TMPDIR"] = temporary.FullName,
                ["SHARPPROOF_CONTAINER"] = "1",
                ["SHARPPROOF_CONTAINER_CONTRACT"] = "/etc/sharpproof/container-contract.json",
                ["SHARPPROOF_NATIVE_ROOT"] = "/opt/sharpproof/native",
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                ["DOTNET_STARTUP_HOOKS"] = "/tmp/injected-hook.dll",
                ["DOTNET_ADDITIONAL_DEPS"] = "/tmp/injected.deps.json",
                ["DOTNET_SHARED_STORE"] = "/tmp/injected-store",
                ["DOTNET_ROLL_FORWARD"] = "Major",
                ["DOTNET_HOST_PATH"] = "/tmp/untrusted/dotnet",
                ["LD_PRELOAD"] = "/tmp/injected.so",
                ["LD_LIBRARY_PATH"] = "/tmp/injected-libraries",
                ["LD_AUDIT"] = "/tmp/injected-audit.so",
                ["SHARPPROOF_UNTRUSTED"] = "must-not-pass"
            };
            const string script =
                "test \"$PATH\" = /usr/bin:/bin && " +
                "test \"$HOME\" = \"$TMPDIR\" && " +
                "test \"$SHARPPROOF_CONTAINER\" = 1 && " +
                "test \"$SHARPPROOF_CONTAINER_CONTRACT\" = /etc/sharpproof/container-contract.json && " +
                "test \"$SHARPPROOF_NATIVE_ROOT\" = /opt/sharpproof/native && " +
                "test \"$DOTNET_CLI_TELEMETRY_OPTOUT\" = 1 && " +
                "test -z \"${DOTNET_STARTUP_HOOKS+x}\" && " +
                "test -z \"${DOTNET_ADDITIONAL_DEPS+x}\" && " +
                "test -z \"${DOTNET_SHARED_STORE+x}\" && " +
                "test -z \"${DOTNET_ROLL_FORWARD+x}\" && " +
                "test -z \"${DOTNET_HOST_PATH+x}\" && " +
                "test -z \"${LD_PRELOAD+x}\" && " +
                "test -z \"${LD_LIBRARY_PATH+x}\" && " +
                "test -z \"${LD_AUDIT+x}\" && " +
                "test -z \"${SHARPPROOF_UNTRUSTED+x}\"";

            var startInfo = new System.Diagnostics.ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                WorkingDirectory = temporary.FullName
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(script);
            TrustedChildEnvironment.Apply(
                startInfo,
                validatedDotNetHostPath: null,
                name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (inheritedEnvironment.GetValueOrDefault(name)));
            using var process = System.Diagnostics.Process.Start(startInfo)!;
            Assert.That(process.WaitForExit(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(process.ExitCode, Is.Zero);
        }

        [Test]
        public void DotNetChildRootComesFromTheValidatedMuxer()
        {
            var inheritedEnvironment = new Dictionary<string, string?>
            {
                ["PATH"] = "/safe/bin",
                ["DOTNET_ROOT"] = "/untrusted/dotnet",
                ["DOTNET_STARTUP_HOOKS"] = "/tmp/injected-hook.dll"
            };
            var startInfo = new System.Diagnostics.ProcessStartInfo();
            TrustedChildEnvironment.Apply(
                startInfo,
                "/trusted/dotnet/dotnet",
                name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (inheritedEnvironment.TryGetValue(name, out var value)
                    ? value
                    : null));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(startInfo.Environment["PATH"], Is.EqualTo("/safe/bin"));
                Assert.That(startInfo.Environment["DOTNET_ROOT"], Is.EqualTo("/trusted/dotnet"));
                Assert.That(startInfo.Environment.ContainsKey("DOTNET_STARTUP_HOOKS"), Is.False);
                Assert.That(startInfo.Environment.Count, Is.EqualTo(2));
            }
        }

        [Test]
        public void ChildRuntimePreservesWindowsSystemAndTemporaryDirectories()
        {
            var inherited = new Dictionary<string, string?>
            {
                ["SystemRoot"] = "C:\\Windows",
                ["TEMP"] = "C:\\Temp",
                ["TMP"] = "C:\\Temp",
                ["USERPROFILE"] = "C:\\Users\\Proof",
                ["DOTNET_STARTUP_HOOKS"] = "C:\\untrusted.dll"
            };
            var startInfo = new System.Diagnostics.ProcessStartInfo();
            TrustedChildEnvironment.Apply(startInfo, null, inherited.GetValueOrDefault);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(startInfo.Environment["SystemRoot"], Is.EqualTo("C:\\Windows"));
                Assert.That(startInfo.Environment["TEMP"], Is.EqualTo("C:\\Temp"));
                Assert.That(startInfo.Environment["TMP"], Is.EqualTo("C:\\Temp"));
                Assert.That(startInfo.Environment["USERPROFILE"], Is.EqualTo("C:\\Users\\Proof"));
                Assert.That(startInfo.Environment.ContainsKey("DOTNET_STARTUP_HOOKS"), Is.False);
            }
        }

        [TestCase("dotnet")]
        [TestCase("/untrusted/other")]
        public void InvalidDotNetMuxerPathIsRejectedWithoutChangingChildEnvironment(string path)
        {
            System.ArgumentNullException.ThrowIfNull(path);
            var startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.Environment["MARKER"] = "unchanged";

            Assert.Throws<InvalidOperationException>((Action)(() =>
                TrustedChildEnvironment.Apply(startInfo, path, _ => null)));
            Assert.That(startInfo.Environment["MARKER"], Is.EqualTo("unchanged"));
        }

        [Test]
        public void UnsafeRuntimeOverridesAreIdentifiedBeforeLaunch()
        {
            var inheritedEnvironment = new Dictionary<string, string?>
            {
                ["DOTNET_ROOT"] = "/untrusted/dotnet"
            };

            Assert.That(
                TrustedChildEnvironment.FindUnsafeRuntimeVariable(
                    name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (inheritedEnvironment.TryGetValue(name, out var value)
                        ? value
                        : null)),
                Is.EqualTo("DOTNET_ROOT"));
        }
    }
}
