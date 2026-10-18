"""Summarize runner telemetry samples as GitHub job-summary Markdown."""

import csv
import os
import re
import statistics
import sys

UNITS = {"B": 1 / 2**20, "KiB": 1 / 1024, "kB": 1 / 1024, "MiB": 1, "MB": 1, "GiB": 1024, "GB": 1024}


def number(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def mebibytes(text):
    match = re.match(r"([0-9.]+)([A-Za-z]+)", text)
    return float(match.group(1)) * UNITS.get(match.group(2), 0) if match else 0.0


def percentile(values, fraction):
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int(fraction * len(ordered)))]


def main(path, name):
    with open(path, newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    print(f"### Runner telemetry: {name}\n")
    if not rows:
        print("No samples were recorded (the job finished within one interval).\n")
        return
    cpu = [number(r["cpu_pct"]) or 0 for r in rows]
    mem = [number(r["mem_used_mb"]) or 0 for r in rows]
    total_mem = number(rows[-1]["mem_total_mb"]) or 1
    swap = [number(r["swap_used_mb"]) or 0 for r in rows]
    iowait = [number(r["iowait_pct"]) or 0 for r in rows]
    steal = [number(r["steal_pct"]) or 0 for r in rows]
    start, end = int(rows[0]["epoch"]), int(rows[-1]["epoch"])
    container_cpu, container_mem = {}, {}
    for row in rows:
        for entry in filter(None, (row.get("containers") or "").split(";")):
            label, _, usage = entry.partition("=")
            cpu_text, _, mem_text = usage.partition("|")
            label = re.sub(r"-run-[0-9a-f]+$", "", label)
            container_cpu[label] = max(container_cpu.get(label, 0), number(cpu_text.rstrip("%")) or 0)
            container_mem[label] = max(container_mem.get(label, 0), mebibytes(mem_text.split("/")[0]))

    def pressure(column):
        values = [v for v in (number(r[column]) for r in rows) if v is not None]
        return f"{statistics.fmean(values):.1f}% avg, {max(values):.1f}% max" if values else "n/a"

    print(f"{len(rows)} samples over {(end - start) / 60:.1f} min on {os.cpu_count()} vCPUs, "
          f"{total_mem / 1024:.1f} GiB RAM.\n")
    print("| Metric | Value |\n| --- | --- |")
    print(f"| CPU busy | {statistics.fmean(cpu):.0f}% avg, p50 {percentile(cpu, 0.5):.0f}%, "
          f"p90 {percentile(cpu, 0.9):.0f}%, max {max(cpu):.0f}% |")
    print(f"| Samples with CPU under 50% | {100 * sum(c < 50 for c in cpu) / len(cpu):.0f}% |")
    print(f"| Samples with CPU at least 90% | {100 * sum(c >= 90 for c in cpu) / len(cpu):.0f}% |")
    print(f"| I/O wait / steal | {statistics.fmean(iowait):.1f}% / {statistics.fmean(steal):.1f}% avg |")
    print(f"| Memory used | {statistics.fmean(mem) / 1024:.1f} GiB avg, {max(mem) / 1024:.1f} GiB peak "
          f"({100 * max(mem) / total_mem:.0f}% of RAM) |")
    print(f"| Swap used | {max(swap):.0f} MiB peak |")
    print(f"| Stall pressure (some, avg10) | CPU {pressure('cpu_psi')}; memory {pressure('mem_psi')}; "
          f"I/O {pressure('io_psi')} |")
    for label in sorted(container_mem, key=container_mem.get, reverse=True)[:5]:
        print(f"| Container `{label}` peak | CPU {container_cpu[label]:.0f}%, "
              f"memory {container_mem[label] / 1024:.1f} GiB |")

    print("\n<details><summary>Per-minute timeline</summary>\n")
    print("| Minute | CPU avg | CPU max | Memory peak |\n| ---: | :--- | ---: | ---: |")
    minutes = {}
    for row, c, m in zip(rows, cpu, mem):
        minutes.setdefault((int(row["epoch"]) - start) // 60, []).append((c, m))
    for minute, samples in sorted(minutes.items()):
        cpus = [c for c, _ in samples]
        bar = "#" * round(statistics.fmean(cpus) / 10)
        print(f"| {minute} | `{bar:<10}` {statistics.fmean(cpus):.0f}% | {max(cpus):.0f}% | "
              f"{max(m for _, m in samples) / 1024:.1f} GiB |")
    print("\n</details>\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
