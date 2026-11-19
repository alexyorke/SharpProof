using System.Text;

namespace SharpProof.Gates.Corpus;

// Read the pinned source and notices from Git objects. The extra sources are
// only present in the in-memory census document used to produce the reports.
internal static class CensusSourceLoader
{
    internal static async Task<OpenSourceCorpusDocument> LoadAsync(
        string repositoryRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        var document = OpenSourceCorpusCatalog.Load(repositoryRoot);
        var sources = document.Sources.ToBuilder();
        var files = document.Files.ToBuilder();
        var configuration = OpenSourceCorpusImporter.LoadSources(repositoryRoot);
        var temporaryRoot = Directory.CreateTempSubdirectory("sharpproof-census-");
        try
        {
            foreach (var source in configuration.Sources.Where(static source => source.CensusOnly))
            {
                var gitRoot = Directory.CreateDirectory(Path.Combine(temporaryRoot.FullName, source.Id)).FullName;
                _ = await OpenSourceCorpusImporter.ReadGitAsync(gitRoot, ["init", "--bare", "--quiet"], cancellationToken)
                    .ConfigureAwait(false);
                _ = await OpenSourceCorpusImporter.ReadGitAsync(
                        gitRoot, ["fetch", "--depth=1", "--no-tags", "--", source.Repository, source.Commit], cancellationToken)
                    .ConfigureAwait(false);
                var fetched = await OpenSourceCorpusImporter.ReadGitAsync(gitRoot, ["rev-parse", "FETCH_HEAD"], cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(fetched, source.Commit, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Source {source.Id} did not fetch its pinned commit.");
                }

                foreach (var reviewed in source.Notices.Prepend(source.License))
                {
                    var blob = await OpenSourceCorpusImporter.ReadGitBlobAsync(gitRoot, source.Commit, reviewed.Path, cancellationToken)
                        .ConfigureAwait(false);
                    _ = OpenSourceCorpusImporter.ValidateReviewedUpstreamFile(source.Id, reviewed, Encoding.UTF8.GetString(blob.AsSpan()));
                }

                var sourceFiles = await OpenSourceCorpusImporter.ReadSourceFilesAsync(gitRoot, source, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var root in source.Roots)
                {
                    var prefix = root.TrimEnd('/') + "/";
                    if (!sourceFiles.Any(file => file.Path.StartsWith(prefix, StringComparison.Ordinal)))
                    {
                        throw new InvalidDataException($"Source {source.Id} has no C# files under {root}.");
                    }
                }

                sources.Add(new OpenSourceCorpusSource(
                    source.Id, source.Repository, source.Commit, source.LicenseSpdx, source.License.Path, source.License.Sha256)
                {
                    NoticeFiles = [.. source.Notices.Select(static notice => new OpenSourceCorpusNoticeFile(notice.Path, notice.Sha256))],
                    Compilation = source.Compilation
                });
                files.AddRange(sourceFiles);
            }
        }
        finally
        {
            temporaryRoot.Delete(recursive: true);
        }

        return document with { Sources = sources.ToImmutable(), Files = files.ToImmutable() };
    }
}
