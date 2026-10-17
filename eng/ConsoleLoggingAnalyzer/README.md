# Repository console logging rule

`SPLOG001` is an error in repository builds. It rejects direct calls to
`System.Console.WriteLine`, and to `WriteLine` or `WriteLineAsync` on
`System.Console.Out` or `System.Console.Error`. The rule resolves symbols,
so similarly named methods and injected `TextWriter` instances are allowed.

Command-line tools use `SharpProof.Logging.RepositoryLogger` to preserve
their existing stdout/stderr protocol. The logger accepts writers so tests can
capture output and entry points can use the current console streams.

C# snippets stored as strings and golden fixtures are inputs to compiler and
effect-analysis tests. They are not repository call sites compiled by MSBuild;
some intentionally exercise the semantic effect of `Console.WriteLine`.
Generated code follows the repository's usual Roslyn analyzer exemption.
