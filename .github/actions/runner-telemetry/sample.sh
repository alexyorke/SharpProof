#!/usr/bin/env bash
# Temporary CI diagnostics: append one host sample every interval until killed.
set -u
out="$1"
interval="${2:-5}"

pressure() {
  # Prints the "some avg10" stall percentage, or nothing without PSI support.
  if [[ -r "/proc/pressure/$1" ]]; then
    awk '/^some/ { sub("avg10=", "", $2); print $2 }' "/proc/pressure/$1"
  fi
}

echo "epoch,cpu_pct,iowait_pct,steal_pct,mem_used_mb,mem_total_mb,swap_used_mb,load1,cpu_psi,mem_psi,io_psi,containers" > "${out}"
read -r _ u n s i w q sq st _ < /proc/stat
prev_total=$((u + n + s + i + w + q + sq + st))
prev_idle=$((i + w))
prev_wait=${w}
prev_steal=${st}
while :; do
  sleep "${interval}"
  read -r _ u n s i w q sq st _ < /proc/stat
  total=$((u + n + s + i + w + q + sq + st))
  dt=$((total - prev_total))
  if ((dt <= 0)); then continue; fi
  cpu=$((100 * (dt - (i + w - prev_idle)) / dt))
  iowait=$((100 * (w - prev_wait) / dt))
  steal=$((100 * (st - prev_steal) / dt))
  prev_total=${total}
  prev_idle=$((i + w))
  prev_wait=${w}
  prev_steal=${st}
  read -r mem_total mem_avail swap_total swap_free < <(awk '
    /^MemTotal:/ { t = $2 } /^MemAvailable:/ { a = $2 }
    /^SwapTotal:/ { st = $2 } /^SwapFree:/ { sf = $2 }
    END { print int(t / 1024), int(a / 1024), int(st / 1024), int(sf / 1024) }' /proc/meminfo)
  load1="$(cut -d' ' -f1 /proc/loadavg)"
  containers="$(timeout 4 docker stats --no-stream --format '{{.Name}}={{.CPUPerc}}|{{.MemUsage}}' 2> /dev/null |
    tr -d ' ' | tr '\n' ';' | tr ',' ' ')"
  printf '%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,"%s"\n' \
    "$(date +%s)" "${cpu}" "${iowait}" "${steal}" "$((mem_total - mem_avail))" "${mem_total}" \
    "$((swap_total - swap_free))" "${load1}" "$(pressure cpu)" "$(pressure memory)" "$(pressure io)" \
    "${containers}" >> "${out}"
done
