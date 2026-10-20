# Solution performance baseline

The benchmark measures complete CLI analysis of a generated solution, including
MSBuild solution loading, fresh source-reference metadata, inference, candidate
validation and diagnostic pipe capture. Restore and native compilation are timed
separately. Each sample starts a new CLI process; no sample is described as a
cold-cache measurement.

The default workload has 12 projects, 21 shared/chain project references, 120 C#
files and 468 source methods. Nine method families cover identity, nullable
references, Task, ValueTask, arrays, generic lists, delegates, iterators and calls
through source-project dependencies. Four instances of each family per project
must produce exactly 432 signatures. The 36 existing-generic/concrete helper
methods must produce no additional suggestions. Project counts and the complete,
location-independent signature multiset are checked on every sample.

This synthetic workload establishes a repeatable reference for mixed-feature
solution analysis. It does not establish production-repository latency, editor
responsiveness, generator or multi-target performance, or a performance SLA.
Those profiles remain work to do. Timing is observational: there is no absolute
CI time threshold, and changes need comparable repeated runs and equal quality
before a performance improvement can be claimed.

## Recorded observation

`Performance/baseline-linux-amd64.json` records the engine at commit
`c80397ac19bc6ec975137fbabb53a6593ebad4f9` in the canonical Linux amd64 tooling
container, using SDK 10.0.402. The three CLI samples took 22.896, 24.462 and
22.513 seconds; the median was 22.896 seconds. All three analyzed 12 projects and
produced exactly 432 expected signatures. The JSON retains unrounded samples,
setup cost, fixture/quality/CLI hashes and the source commit. Its environment
identifier hashes the execution-host name and tooling-image ID rather than
publishing the host name. Docker resource settings and competing host activity
were not independently fingerprinted; retain the same allocations and host
conditions for comparisons.

## Reproduce and compare

Run from the repository root through the canonical tooling wrapper:

```powershell
./eng/scripts/Invoke-SharpProof.ps1 dev src/SharpProof.HmInference/benchmark-solution.sh -EnvironmentId local-profile-a
```

This builds the CLI in a private container workspace and exports JSON and logs
under `artifacts/hm-inference/solution-baseline*`. Give the environment a stable
identifier that changes when the execution host, tooling image or Docker resource
configuration changes. The committed observation is a reference, not a portable
latency target for different machines.

For a subsequent build on the same environment, preserve the first report, then
use a new label and its absolute container-visible comparison path:

```powershell
./eng/scripts/Invoke-SharpProof.ps1 dev src/SharpProof.HmInference/benchmark-solution.sh -EnvironmentId local-profile-a -Label solution-candidate -Compare /workspace/SharpProof/artifacts/hm-inference/solution-baseline.json
```

Comparisons require equal declared environment, SDK, platform, architecture,
fixture hash and expected diagnostic hash. Incomparable reports fail before the
fixture build. Every measured run still verifies actual suggestions and project
coverage. A ratio greater than one means the baseline median divided by the new
median exceeds one; assess sample spread and host conditions before interpreting
it as an improvement.

`-Projects`, `-Replicas` and `-Samples` select the workload. Validate a small sample
before increasing these values. The ordinary verification gate runs a two-project
fixture, checks a repeated comparable run, rejects an altered fixture identity
before setup and rejects an intentionally wrong signature set with an unchanged
suggestion count. Failed-quality samples never become JSON performance reports.
