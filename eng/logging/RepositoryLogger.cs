using System;
using System.IO;

namespace SharpProof.Logging;

// Preserve the command-line tools' stdout/stderr protocol while keeping
// terminal writes behind one logging boundary.
public sealed class RepositoryLogger
{
    private readonly TextWriter output;
    private readonly TextWriter error;

    public RepositoryLogger(TextWriter output, TextWriter error)
    {
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        this.error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public void Info(string message)
    {
        output.WriteLine(message);
    }

    public void Error(string message)
    {
        error.WriteLine(message);
    }

    public void Error(Exception exception)
    {
        error.WriteLine(exception);
    }
}
