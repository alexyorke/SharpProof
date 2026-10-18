[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SharpProof.ContainerExecution.psm1') -Force
Assert-SharpProofContainer 'NuGet repository signature probes require the canonical Linux container.'
$probeRoot = Join-Path ([IO.Path]::GetTempPath()) ('nuget-repository-signatures-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($probeRoot)
try {
    $probePath = Join-Path $probeRoot 'probe.sh'
    # The public fixture's NuGet content hash is pinned. Its repository signing
    # envelope may legitimately change; its outer archive digest is not pinned.
    $probeScript = @'
#!/usr/bin/env bash
set -eEuo pipefail
phase=setup
failure_line=unknown
trap 'failure_line=$LINENO' ERR
report_failure() {
  status=$1
  line=$2
  if [[ "$status" == 0 ]]; then return; fi
  printf 'NuGet repository probe failed: phase=%s line=%s exit=%s\n' "$phase" "${failure_line:-$line}" "$status" >&2
  for log in trust.log valid.log wrong-pin.log tampered.log; do
    if [[ -f "$log" ]]; then
      printf '\n--- public diagnostic tail: %s ---\n' "$log" >&2
      tail -c 8192 "$log" >&2
    fi
  done
  if [[ -f trust.config ]]; then
    printf '\n--- public repository certificate configuration ---\n' >&2
    tail -c 4096 trust.config >&2
  fi
}
trap 'report_failure "$?" "$LINENO"' EXIT
cd -- "${1:?Expected isolated probe directory}"
export DOTNET_CLI_UI_LANGUAGE=en-US DOTNET_NUGET_SIGNATURE_VERIFICATION=true
expected='YKRwjVfrG7GYOovlGyQoMvr1/IJdn+7QzNXJxyMh0YfFF5yvDmTYaJOVYWsckreNjGsGSEtrMTpnzxTUq/tZQw=='
phase=download
curl --fail --silent --show-error --location --proto '=https' --proto-redir '=https' \
  --connect-timeout 10 --max-time 30 --retry 2 --retry-all-errors --max-filesize 33554432 \
  https://api.nuget.org/v3-flatcontainer/dapper/2.1.35/dapper.2.1.35.nupkg -o original.nupkg
original=$(sha512sum original.nupkg)
printf '%s' '<configuration><packageSources><clear/></packageSources><config><add key="signatureValidationMode" value="require"/></config><trustedSigners><clear/></trustedSigners></configuration>' > trust.config
run_nuget() (
  log=$1
  shift
  status=0
  timeout --kill-after=5 30 dotnet nuget "$@" 2>&1 < /dev/null | head -c 65537 > "$log" || status=$?
  [[ $(stat -c %s "$log") -le 65536 ]] || return 1
  return "$status"
)
phase=official-repository-trust
run_nuget trust.log trust source NuGetOfficial --source-url https://api.nuget.org/v3/index.json --configfile trust.config
[[ $(stat -c %s trust.config) -le 65536 ]]
phase=certificate-pin-parsing
mapfile -t certificates < <(grep '<certificate ' trust.config)
[[ ${#certificates[@]} -gt 0 && ${#certificates[@]} -le 16 ]]
pins=()
for certificate in "${certificates[@]}"; do
  fingerprint=$(printf '%s\n' "$certificate" | sed -nE 's/^ *<certificate fingerprint="([a-fA-F0-9]{64})" hashAlgorithm="SHA256" allowUntrustedRoot="false" *\/> *$/\1/p')
  [[ "$fingerprint" =~ ^[a-fA-F0-9]{64}$ ]]
  pins+=(--certificate-fingerprint "$fingerprint")
done
phase=valid-signature-verification
run_nuget valid.log verify original.nupkg --all --configfile trust.config "${pins[@]}" --verbosity normal
phase=valid-content-and-repository-policy
[[ $(stat -c %s valid.log) -le 65536 ]]
[[ $(grep -c '^Signature type:' valid.log) == 1 ]]
grep -Fxq 'Signature type: Repository' valid.log
grep -Fxq 'Service index: https://api.nuget.org/v3/index.json' valid.log
mapfile -t hashes < <(sed -nE 's/^Content hash: ([A-Za-z0-9+/]{86}==)\r?$/\1/p' valid.log)
[[ ${#hashes[@]} == 1 && "${hashes[0]}" == "$expected" ]]
phase=wrong-certificate-pin
wrong_pin=$(printf '0%.0s' {1..64})
if run_nuget wrong-pin.log verify original.nupkg --all --configfile trust.config --certificate-fingerprint "$wrong_pin" --verbosity normal; then
  echo 'SDK accepted a repository signature with the wrong allowed fingerprint.' >&2
  exit 1
fi
[[ $(stat -c %s wrong-pin.log) -le 65536 ]]
grep -Fq 'NU3034:' wrong-pin.log
# A printed matching content hash must never turn failed authentication into success.
grep -Fxq "Content hash: $expected" wrong-pin.log
phase=retained-signature-content-tamper
cp original.nupkg tampered.nupkg
pwsh -NoLogo -NoProfile -Command '$archive=[IO.Compression.ZipFile]::Open("tampered.nupkg",[IO.Compression.ZipArchiveMode]::Update); try { $entry=$archive.GetEntry("Dapper.nuspec"); $stream=$entry.Open(); try { $stream.Position=$stream.Length; $stream.WriteByte(32) } finally { $stream.Dispose() } } finally { $archive.Dispose() }' < /dev/null
if run_nuget tampered.log verify tampered.nupkg --all --configfile trust.config "${pins[@]}" --verbosity normal; then
  echo 'SDK accepted modified package content under its retained repository signature.' >&2
  exit 1
fi
[[ $(stat -c %s tampered.log) -le 65536 ]]
grep -Fq 'NU3008:' tampered.log
[[ $(sha512sum original.nupkg) == "$original" ]]
printf 'Real SDK NuGet repository-primary probes passed: Dapper 2.1.35, pinned content hash %s; valid/wrong-pin/tampered controls.\n' "$expected"
'@
    [IO.File]::WriteAllText($probePath, $probeScript.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))
    # Bound the complete network/verification sequence, not only individual operations.
    & timeout --kill-after=5 110 bash $probePath $probeRoot
    if ($LASTEXITCODE -ne 0) { throw 'Real SDK NuGet repository signature regression failed.' }
}
finally {
    if ($probeRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::Ordinal) -and
        [IO.Path]::GetFileName($probeRoot).StartsWith('nuget-repository-signatures-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $probeRoot -Recurse -Force
    }
}
