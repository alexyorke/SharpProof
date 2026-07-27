# Security policy

## Reporting a vulnerability

Do not open a public issue for a suspected vulnerability.

Report it privately through
[GitHub Security Advisories](https://github.com/alexyorke/SharpProof/security/advisories/new).
Include the affected version or commit, a minimal reproduction, the expected
impact, and any known workaround. Avoid including secrets or unrelated
personal data.

The maintainers will acknowledge the report, investigate it, and coordinate
disclosure and remediation with the reporter. Timing depends on severity,
reproducibility, and release risk; this policy does not promise a fixed
response or release deadline.

## Supported versions

Security fixes are made on the current development line and the most recent
published preview, release candidate, or stable release. Older previews are
not supported unless a repository advisory says otherwise.

## Scope

Reports about analyzer isolation, verifier containment, native Z3 loading,
cache or artifact integrity, malformed worker protocol handling, and
soundness failures that can turn an incomplete analysis into a false
`Proven` result are in scope.

Unsupported language constructs that already produce a visible typed
`Unknown` or incomplete-analysis result are normally correctness reports,
not security vulnerabilities.
