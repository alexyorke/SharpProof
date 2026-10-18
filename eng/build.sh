#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"
if [[ "${SHARPPROOF_CONTAINER:-}" != 1 ]]; then
  docker compose build tooling
  exec pwsh -NoLogo -NoProfile -File eng/scripts/Invoke-SharpProof.ps1 arcade "$@"
fi
if [[ $# == 0 ]]; then
  set -- --restore --build
fi
arguments=()
while [[ $# -gt 0 ]]; do
  case "${1,,}" in
    --projects|-projects)
      arguments+=("$1")
      shift
      if [[ $# == 0 ]]; then
        echo '--projects requires a project or solution path.' >&2
        exit 2
      fi
      IFS=';' read -r -a project_paths <<< "$1"
      projects=''
      for project in "${project_paths[@]}"; do
        if [[ "$project" != /* ]]; then project="${repo_root}/${project}"; fi
        projects+="${projects:+;}${project}"
      done
      arguments+=("$projects")
      ;;
    *) arguments+=("$1") ;;
  esac
  shift
done
set -- "${arguments[@]}"
for argument in "$@"; do
  case "${argument,,}" in
    -h|--help|-help) exec bash "${repo_root}/eng/common/build.sh" "$@" ;;
  esac
done
# The repo-only analyzer must exist before any action that compiles source.
# Restore, clean and test-only actions retain Arcade's action semantics.
bootstrap_analyzer=false
for argument in "$@"; do
  case "${argument,,}" in
    --build|-build|-b|--rebuild|-rebuild|--pack|-pack|--sourcebuild|-sourcebuild|-sb|--productbuild|-productbuild|-pb)
      bootstrap_analyzer=true ;;
  esac
done
if ${bootstrap_analyzer}; then
  analyzer=src/DefensiveCodingAnalyzer/DefensiveCodingAnalyzer.csproj
  pwsh -NoLogo -NoProfile -File eng/scripts/Invoke-SharpProofDotnet.ps1 restore "${analyzer}" --locked-mode
  for configuration in Debug Release; do
    pwsh -NoLogo -NoProfile -File eng/scripts/Invoke-SharpProofDotnet.ps1 build "${analyzer}" --configuration "${configuration}" --no-restore /p:RunAnalyzersDuringBuild=false
  done
fi
export use_global_nuget_cache=true
export use_installed_dotnet_cli=true
exec bash "${repo_root}/eng/common/build.sh" "$@"
