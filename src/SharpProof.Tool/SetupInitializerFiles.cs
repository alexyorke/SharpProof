using System.Security.AccessControl;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SharpProof.Tool;

internal sealed class SetupInitializerFiles
{
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<string, string?> _observed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _changes = new(StringComparer.OrdinalIgnoreCase);

    internal SetupInitializerFiles(CancellationToken cancellationToken)
    { _cancellationToken = cancellationToken; }

    internal string? Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        path = Path.GetFullPath(path);
        if (!_observed.TryGetValue(path, out var original))
        {
            original = ReadCurrent(path);
            _observed.Add(path, original);
        }
        return original;
    }

    internal void Add(string path, string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(content);
        path = Path.GetFullPath(path);
        var original = Read(path);
        if (original?.StartsWith('\uFEFF') == true && !content.StartsWith('\uFEFF'))
        { content = "\uFEFF" + content; }
        if (original?.Contains("\r\n", StringComparison.Ordinal) == true)
        { content = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal); }
        if (!_changes.TryAdd(path, content))
        { throw new InvalidOperationException("Initializer file plans collide: " + Path.GetFileName(path)); }
    }

    internal IReadOnlyList<SetupInitializationFile> Describe(bool applied)
    {
        return Array.AsReadOnly(_changes.Select(change =>
        {
            global::SharpProof.Internal.InputValidation.Require(change, nameof(change));
            var changed = !string.Equals(_observed[change.Key], change.Value, StringComparison.Ordinal);
            return new SetupInitializationFile(change.Key, changed, applied && changed);
        }).ToArray());
    }

    internal Task ApplyAsync(CancellationToken cancellationToken)
    {
        return ApplyAsync(static _ => Task.CompletedTask, cancellationToken);
    }

    internal async Task ApplyAsync(Func<CancellationToken, Task> stagedCallback, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stagedCallback);
        cancellationToken.ThrowIfCancellationRequested();
        VerifyObserved();
        var staged = new List<(string Temporary, string Target)>();
        try
        {
            foreach (var change in _changes)
            {
                if (string.Equals(_observed[change.Key], change.Value, StringComparison.Ordinal))
                { continue; }
                var directory = Path.GetDirectoryName(change.Key)!;
                Directory.CreateDirectory(directory);
                var temporary = Path.Combine(directory, ".sharpproof-init-" + Guid.NewGuid().ToString("N"));
                staged.Add((temporary, change.Key));
                var stream = CreateStage(temporary, change.Key);
                await using var streamLifetime = stream.ConfigureAwait(false);
                var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
                await using var writerLifetime = writer.ConfigureAwait(false);
                await writer.WriteAsync(change.Value.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            await stagedCallback(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            VerifyObserved();
            // Recheck after staging and at each commit; never overwrite a newly created target.
            foreach (var file in staged)
            {
                Verify(file.Target);
                if (_observed[file.Target] == null)
                {
                    File.Move(file.Temporary, file.Target, overwrite: false);
                    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    {
                        // New generated configuration is credential-free and readable by the container user.
                        File.SetUnixFileMode(file.Target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                    }
                }
                else
                {
                    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    { File.SetUnixFileMode(file.Temporary, File.GetUnixFileMode(file.Target)); }
                    File.Replace(file.Temporary, file.Target, destinationBackupFileName: null);
                }
            }
        }
        finally
        {
            foreach (var file in staged)
            {
                if (File.Exists(file.Temporary))
                { File.Delete(file.Temporary); }
            }
        }
    }

    private FileStream CreateStage(string temporary, string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(temporary);
        ArgumentException.ThrowIfNullOrEmpty(target);
        if (OperatingSystem.IsWindows() && _observed[target] != null)
        {
            // ReplaceFile preserves the DACL, but the staged file must already have the original owner.
            var security = new FileInfo(target).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            return new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096,
                FileOptions.Asynchronous, security);
        }
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
        return new FileStream(temporary, options);
    }

    private void VerifyObserved()
    {
        foreach (var path in _observed.Keys)
        { Verify(path); }
    }

    private void Verify(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!string.Equals(_observed[path], ReadCurrent(path), StringComparison.Ordinal))
        { throw new IOException("Setup configuration changed after inspection; preview again before applying."); }
    }
    private string? ReadCurrent(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _cancellationToken.ThrowIfCancellationRequested();
        if (new FileInfo(path).LinkTarget != null)
        { throw new InvalidOperationException("Initializer targets must not be linked files."); }
        if (Directory.Exists(path))
        { throw new InvalidOperationException("A directory collides with an initializer file: " + Path.GetFileName(path)); }
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory != null; directory = directory.Parent)
        {
            if (directory.Exists && directory.LinkTarget != null)
            { throw new InvalidOperationException("Initializer targets must not traverse linked directories."); }
            if (!directory.Exists && File.Exists(directory.FullName))
            { throw new InvalidOperationException("A file collides with an initializer directory."); }
        }
        return File.Exists(path) ? EvidenceSet.ReadBounded(path, 1024 * 1024, _cancellationToken) : null;
    }

    internal static XDocument Xml(string? content, string rootName)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootName);
        if (content == null)
        { return new XDocument(new XElement(rootName)); }
        try
        {
            using var text = new StringReader(content.TrimStart('\uFEFF'));
            using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            if (document.Root?.Name != rootName)
            { throw new InvalidDataException("Initializer configuration has an unsupported XML root."); }
            return document;
        }
        catch (XmlException)
        { throw new InvalidDataException("Initializer configuration must be well-formed XML without a DTD."); }
    }

    internal static void Append(XElement parent, XElement child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        var indentation = new string(' ', 2 * (parent.Ancestors().Count() + 1));
        if (parent.LastNode is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value))
        { whitespace.AddBeforeSelf(new XText("\n" + indentation), child); }
        else
        { parent.Add(new XText("\n" + indentation), child, new XText("\n" + indentation[..^2])); }
    }

    internal static string Serialize(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return (document.Declaration?.ToString() ?? string.Empty) + document.ToString(SaveOptions.DisableFormatting).TrimEnd('\r', '\n') + "\n";
    }

    internal static string? AncestorFile(string directory, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(name);
        for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
        {
            var path = Path.Combine(current.FullName, name);
            if (File.Exists(path) || Directory.Exists(path))
            { return path; }
        }
        return null;
    }
}
