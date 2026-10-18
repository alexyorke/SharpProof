"""Validate the pinned cross-build's real PE headers and LLVM import/export evidence."""
import hashlib
import json
import os
import pathlib
import re
import struct
import sys

output = pathlib.Path(sys.argv[1])
rid, expected_machine, source_sha, source_commit, compiler_sha = sys.argv[2:]
binary = (output / "libz3.dll").read_bytes()
if binary[:2] != b"MZ":
    raise SystemExit("Not a Windows PE image")
pe = struct.unpack_from("<I", binary, 0x3C)[0]
if binary[pe:pe + 4] != b"PE\0\0":
    raise SystemExit("Invalid PE signature")
machine = struct.unpack_from("<H", binary, pe + 4)[0]
timestamp = struct.unpack_from("<I", binary, pe + 8)[0]
if machine != int(expected_machine, 0) or timestamp != 0:
    raise SystemExit(f"Unexpected PE machine/timestamp: {machine:#x}/{timestamp}")

imports, exports = [], []
section = None
for line in (output / "llvm-readobj.txt").read_text().splitlines():
    if line.strip() in ("Import {", "Export {"):
        section = line.strip().split()[0]
    elif line.strip() == "}":
        section = None
    elif section and (match := re.match(r"\s*Name:\s*(\S+)", line)):
        (imports if section == "Import" else exports).append(match.group(1))

system_dlls = {
    "kernel32.dll", "kernelbase.dll", "advapi32.dll", "user32.dll",
    "shell32.dll", "ole32.dll", "ws2_32.dll", "bcrypt.dll", "ntdll.dll",
    "ucrtbase.dll", "msvcrt.dll",
}
for name in imports:
    lowered = name.lower()
    if re.search(r"vcruntime|msvcp|libc\+\+|libunwind|winpthread|libgcc|libstdc\+\+", lowered):
        raise SystemExit(f"External C++ runtime dependency remains: {name}")
    if lowered not in system_dlls and not re.fullmatch(r"(?:api|ext)-ms-win-[a-z0-9-]+\.dll", lowered):
        raise SystemExit(f"Unreviewed non-system native dependency: {name}")
required = {"Z3_get_version", "Z3_mk_context_rc", "Z3_mk_solver", "Z3_solver_check", "Z3_solver_get_model", "Z3_del_context"}
if not imports or not required.issubset(exports) or len([e for e in exports if e.startswith("Z3_")]) < 100:
    raise SystemExit("Missing real imports or required Z3 C API exports")

receipt = {
    "schemaVersion": 1,
    "rid": rid,
    "z3Version": "4.12.2.0",
    "sourceUrl": "https://github.com/Z3Prover/z3/archive/z3-4.12.2.tar.gz",
    "sourceCommit": source_commit,
    "sourceArchiveSha256": source_sha,
    "sourceModified": False,
    "templateCompatibility": {
        "flag": "-fdelayed-template-parsing",
        "reason": "Documented MSVC-style deferred parsing for Z3 4.12.2 dependent template bodies; no source patches",
        "documentation": "https://clang.llvm.org/docs/MSVCCompatibility.html",
    },
    "headerCompatibility": {
        "flag": "-include vector",
        "reason": "Z3 nla_monotone_lemmas.h declares std::vector without its standard header; explicitly supply that header without source patches",
        "libcxxHeaderPolicy": "https://libcxx.llvm.org/DesignDocs/HeaderRemovalPolicy.html",
    },
    "compilerUrl": "https://github.com/mstorsjo/llvm-mingw/releases/download/20261006/llvm-mingw-20261006-ucrt-ubuntu-22.04-x86_64.tar.xz",
    "compilerArchiveSha256": compiler_sha,
    "compilerVersion": (output / "compiler-version.txt").read_text().strip(),
    "sourceDateEpoch": int(os.environ["SOURCE_DATE_EPOCH"]),
    "linkedRuntimeProvenance": {
        "llvmVersion": "23.1.3",
        "llvmSourceTag": "llvmorg-23.1.3",
        "components": ["libc++", "libc++abi", "libunwind", "compiler-rt builtins", "MinGW-w64 runtime"],
        "llvmLicense": "Apache-2.0 WITH LLVM-exception; component licenses include historical notices",
        "mingwVersion": "15.0.0",
        "mingwSourceCommit": "a3d93999ef0521681d45e445c39964a7af0f593f",
        "mingwLicense": "See verbatim per-RID COPYING* from the verified compiler archive",
        "inputArchiveHashes": (output / "static-library-inputs.sha256").read_text().splitlines(),
        "linkMapSha256": hashlib.sha256((output / "link.map").read_bytes()).hexdigest(),
        "licenseFiles": {
            str(path.relative_to(output.parent)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in sorted((output.parent / "licenses").rglob("*")) if path.is_file()
        },
    },
    "bootstrapPackages": (output / "bootstrap-packages.txt").read_text().splitlines(),
    "cmakeArguments": (output / "cmake-arguments.nul").read_bytes().decode().rstrip("\0").split("\0"),
    "libraryBytes": len(binary),
    "librarySha256": hashlib.sha256(binary).hexdigest(),
    "peMachine": f"0x{machine:04x}",
    "peTimestamp": timestamp,
    "imports": sorted(set(imports)),
    "z3Exports": sorted(e for e in set(exports) if e.startswith("Z3_")),
    "qualification": "Cross-compiled, pinned and statically inspected; native execution is a separate CI gate",
}
(output / "payload-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
print(json.dumps({key: receipt[key] for key in ("rid", "libraryBytes", "librarySha256", "peMachine", "imports")}))
