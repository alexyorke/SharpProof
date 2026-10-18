#!/bin/sh
set -eu

report_path="${1:-/reports/report.md}"
repository_root=$(pwd)
# lychee 0.24.2 does not retry HTTP 503 responses via max_retries.
# Retry the full audit only when every reported error is transient HTTP.
# Require matching summary/detail counts so unrecognized failures fail closed.
attempt=1
while :; do
  rm -f "$report_path"
  if lychee --config lychee.toml --root-dir "$repository_root" --no-progress \
    --format markdown --output "$report_path" .; then
    break
  else
    audit_exit=$?
  fi
  if [ "$audit_exit" -ne 2 ] || [ ! -s "$report_path" ] || [ "$attempt" -ge 3 ]; then
    exit "$audit_exit"
  fi
  if ! awk '
    /Errors[[:space:]]*\|/ { split($0, fields, "|"); errors = fields[3] + 0 }
    /\* \[/ { details++ }
    /^\* \[(408|429|5[0-9][0-9])\] / { transient++ }
    /(Timeouts|Unknown|Unsupported)[[:space:]]*\|/ {
      split($0, fields, "|"); other += fields[3] + 0
    }
    END { exit !(errors > 0 && errors == details && errors == transient && other == 0) }
  ' "$report_path"; then
    exit "$audit_exit"
  fi
  cp "$report_path" "$report_path.attempt-$attempt.md"
  delay=$((attempt * 5))
  echo "Transient HTTP link failures; retrying the audit in $delay seconds (attempt $((attempt + 1))/3)." >&2
  sleep "$delay"
  attempt=$((attempt + 1))
done
test -s "$report_path"
if grep -Eq 'Total[[:space:]]*\|[[:space:]]*0[[:space:]]*\|' "$report_path"; then
  echo 'No links were checked; refusing an empty successful audit.' >&2
  exit 1
fi

# A successful audit must also prove missing files and fragments are rejected.
fixture_root=$(mktemp -d)
printf '# Target\n' > "$fixture_root/target.md"
printf '<h1 id="target">Target</h1>\n' > "$fixture_root/target.html"
printf '[valid](target.md#target)\n' > "$fixture_root/valid.md"
lychee --config lychee.toml --offline --root-dir "$fixture_root" "$fixture_root/valid.md"
for target in absent.md target.md#absent target.html#absent; do
  printf '[broken](%s)\n' "$target" > "$fixture_root/broken.md"
  if lychee --config lychee.toml --offline --root-dir "$fixture_root" "$fixture_root/broken.md"; then
    echo "Broken link unexpectedly passed: $target" >&2
    exit 1
  fi
done
