# Unified analysis errors

`SharpProofAnalysisResult` distinguishes Succeeded, Unknown, Failed, and Canceled.
Failures carry a `SharpProofError` with a stable code, category, message,
retryability, recommended exit code, and details. Unknown evidence is returned in
the ordinary result instead of being converted into an error.

The CLI returns 0 for accepted results, 2 for usage or input errors, 3 for analysis
failures, 4 when `--fail-on-unknown` is enabled and matches, and 5 when
`--fail-on-disproven` is enabled and matches. JSON output serializes the same
`SharpProofAnalysisResult` returned by the .NET API.
