namespace SharpProof.Attributes;

/// <summary>Identifies a declared deployment profile for conditional task resource proofs.</summary>
public enum ResourceTaskDeploymentProfile
{
    /// <summary>No deployment profile; this value cannot declare a premise.</summary>
    Unspecified = 0,
    /// <summary>Stock CoreCLR 8.0.32 on Linux x64 using ordinary JIT execution at every tier.</summary>
    /// <remarks>
    /// Declares stock unmodified helper bindings, the unchanged emitted module, the default scheduler,
    /// default or suppressed execution context, invariant culture, stock embedded resources, and a closed
    /// non-dynamic, non-collectible loader. Excludes ReadyToRun, NativeAOT, external callbacks and instrumentation.
    /// Task and holder lifecycles are closed, without external mutation, external re-registration or external resurrection.
    /// Internal stock task, holder and loader retries remain included in the declared runtime budgets.
    /// The compiler records this deployment assertion; it does not inspect the deployed runtime.
    /// </remarks>
    CoreClr8032LinuxX64OrdinaryJitStockClosed = 1
}

/// <summary>Declares the deployment identity required by conditional task runtime bounds.</summary>
/// <remarks>
/// Applies only to the annotated root method. All required runtime quantities must also be declared.
/// The deployment manifest digest identifies the caller's evidence; it does not prove compatibility.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ResourceTaskDeploymentAttribute : Attribute
{
    /// <summary>Creates an explicit deployment premise.</summary>
    /// <param name="profile">The supported closed deployment profile.</param>
    /// <param name="deploymentManifestSha256">The 64 hexadecimal digits of the deployment manifest SHA-256.</param>
    /// <param name="evidence">A nonempty printable reference or explanation supporting the declaration.</param>
    /// <exception cref="ArgumentOutOfRangeException">The deployment profile is unsupported.</exception>
    /// <exception cref="ArgumentException">The digest or evidence is malformed.</exception>
    public ResourceTaskDeploymentAttribute(ResourceTaskDeploymentProfile profile, string deploymentManifestSha256, string evidence)
    {
        if (profile != ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed)
        { throw new ArgumentOutOfRangeException(nameof(profile)); }
        if (deploymentManifestSha256 is null)
        { throw new ArgumentNullException(nameof(deploymentManifestSha256)); }
        if (deploymentManifestSha256.Length != 64 || deploymentManifestSha256.Any(static character =>
            character is not (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')))
        { throw new ArgumentException("A deployment manifest SHA-256 is required.", nameof(deploymentManifestSha256)); }
        if (string.IsNullOrWhiteSpace(evidence) || evidence.Length > 1024 || evidence.Any(static character => char.IsControl(character)))
        { throw new ArgumentException("Printable deployment evidence is required.", nameof(evidence)); }
        Profile = profile;
        DeploymentManifestSha256 = new string(deploymentManifestSha256.Select(static character =>
            character is >= 'A' and <= 'F' ? (char)(character + ('a' - 'A')) : character).ToArray());
        Evidence = evidence;
    }

    /// <summary>Gets the declared deployment profile.</summary>
    /// <value>The closed deployment profile.</value>
    public ResourceTaskDeploymentProfile Profile { get; }
    /// <summary>Gets the canonical lowercase deployment manifest digest.</summary>
    /// <value>The deployment manifest SHA-256.</value>
    public string DeploymentManifestSha256 { get; }
    /// <summary>Gets the caller's deployment evidence reference.</summary>
    /// <value>The printable deployment evidence.</value>
    public string Evidence { get; }
}
