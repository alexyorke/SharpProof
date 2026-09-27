namespace SharpProof.CompilerArtifact;

// The compiler collector and the worker both consume the same capture image.
// Keep producer spellings and worker predicates together so a value cannot be
// valid merely because it is plausible JSON.
internal static class CompilerCaptureAuthority
{
    internal const string EmptyTextSha256 =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "A compiler capture path is required.",
                nameof(path));
        }

        return Path.GetFullPath(path);
    }

    internal static string CaptureVersion(Type type)
    {
        var value = type.Assembly.GetName().Version ??
            throw new InvalidOperationException(
                "The compiler version is unavailable.");
        return value.ToString();
    }

    internal static string CaptureMvid(Type type)
    {
        var value = type.Module.ModuleVersionId;
        if (value == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The compiler MVID is not canonical.");
        }

        return value.ToString("D");
    }

}
