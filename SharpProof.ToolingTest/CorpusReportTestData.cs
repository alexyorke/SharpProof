using SharpProof.Tools.CorpusReport;

namespace SharpProof.Test;

internal static class CorpusReportTestData
{
    internal static CorpusReportSummary CreateFromSarifJson(string inputName, string sarifJson)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SharpProof.CorpusReport.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(inputName));
        try
        {
            File.WriteAllText(path, sarifJson);
            return SarifCorpusReport.CreateFromSarifFiles(new[] { new SarifCorpusInput(inputName, path) });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
