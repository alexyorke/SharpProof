# Adopt SharpProof in an existing repository

Start with one small SDK-style library on a [qualified host](preview-support.md). Keep the existing target framework and warning policy. Match all SharpProof package versions; the Attributes API is an application reference, while analyzer and verifier references use `PrivateAssets="all"`.

## Introduce portable feedback

Use the two-reference [portable setup](getting-started.md#first-use-in-three-steps), advisory profile, and verification disabled. Record the diagnostics produced by a clean build, including their locations and reasons. Review existing runtime checks: contracts do not replace input validation at runtime.

Apply configuration per compilation. A shared `Directory.Build.props` can opt a set of projects in, but do not accidentally analyze generated test assets or every project in a large solution before measuring feedback. [Configuration](configuration.md) explains supported global settings and severity overrides.

Fix actual invalid calls and malformed declarations first. Use the [diagnostic examples](diagnostic-examples.md) to distinguish conservative incomplete analysis from a replayed violation. If adopting gradually, track existing diagnostic debt with owners and review dates in your team's normal tracker. Suppression changes reporting; it cannot establish a proof or baseline Unknown into success.

## Select and review proof obligations

Add the matching Verifier package to the pilot project. Under the advisory profile explicitly set `SharpProofVerify=true`, retaining an explicit advisory verifier policy during discovery. Review selected claims, incomplete callable coverage, assumptions, trusted boundaries, vacuity, and reasons in every completed run. A Refuted result remains an error even under advisory policy; infrastructure failures remain failures.

Start with small pure functions and simple postconditions. Add only preconditions that represent the application's real domain. Understand an Unknown before raising resource limits; unsupported semantics cannot be fixed with time. See [Unknown reasons](unknown-reasons.md) and [verification evidence](verification-results.md).

## Establish the CI gate

Once the selected pilot's obligations are understood, set `SharpProofProfile=strict`, `SharpProofVerifyPolicy=require-proven`, and `SharpProofAssumptionPolicy=error` as in the [CI configuration](configuration.md#a-ci-consumer-example). Keep local and CI selection settings aligned. Save request/result/manifest together with source revision, package versions, SDK version, and optional SARIF. Evidence may contain source and paths; apply access control and retention appropriate to your repository.

Give each target framework and concurrent build an appropriate output set. Do not reuse stale reports as current evidence. Verify one deliberately incorrect contract fails the gate in a temporary branch, then restore the correct contract. Repeat that negative control when changing integration or policy.

Measure the same project's clean, no-op, and edited builds before and after adoption, including outcomes and selected coverage. Expand one project at a time. Strict policy should expose newly incomplete obligations rather than hiding them behind diagnostic baselines.

## Consumer CI recipe

Pin your compiler SDK, obtain the matching packages from your configured feed, restore with the repository's normal lock-file policy, then build the opted-in project in Release on a qualified host. Treat build failure as failure; archive evidence through your CI's restricted artifact mechanism. The SharpProof source repository's Compose tasks qualify SharpProof itself; an external application should use its own consumer build rather than assuming a source-repository sample task builds that application.

For upgrades, preserve a known-good source revision and evidence, then follow [the upgrade checklist](upgrading.md). For a failure you cannot explain, use [support](support.md).
