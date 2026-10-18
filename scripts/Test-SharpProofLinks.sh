#!/bin/sh
set -eu

report_path="${1:-/reports/report.md}"
lychee --config lychee.toml --root-dir /input --no-progress \
  --format markdown --output "$report_path" .
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
