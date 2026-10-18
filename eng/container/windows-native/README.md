# Windows Z3 native build

The canonical Dockerfile builds Windows x64 and ARM64 DLLs from unmodified Z3
4.12.2 source with the pinned official LLVM-MinGW 20261006 compiler. The source
and compiler archives are independently verified before extraction. Both output
DLLs must match the enrolled byte lengths and SHA-256 digests in toolchain.json.
The final tooling image carries the DLLs, licenses and build receipts, without
the cross-compiler or its build dependencies.

The fixed source/build/compiler paths and SOURCE_DATE_EPOCH preserve repeatable
outputs. C++ compatibility flags explicitly select delayed template parsing and
supply the missing standard vector header; there are no source patches. The
static linker flag includes LLVM and MinGW runtimes, while the PE audit rejects
VC/MSVCP and other external runtime dependencies. Only reviewed Windows system
DLLs and OS API sets are admitted. Windows10 or later supplies the UCRT.

build.sh preserves configure/build logs, the linker map, compiler/tool versions,
static input hashes and import/export evidence. inspect-pe.py checks the real PE
machine and zero timestamp plus required Z3 C API exports. verify-output.py then
checks both output hashes and receipt provenance against the catalog.

licenses/ contains the exact LLVM component license texts from llvmorg-23.1.3
and LLVM-MinGW's ISC build-script license. The compiler archive's per-target
MinGW COPYRIGHT/COPYING texts are retained during the build. Complete runtime
notices are also included in the package's THIRD-PARTY-NOTICES.txt. The release
inventory records components statically incorporated in both DLLs.

Updating inputs requires a fresh build, a separate fresh rebuild with identical
DLL digests, reviewed import/export evidence and real native Windows x64/ARM64
consumer qualification. Do not update pins merely to accept a failed build.
