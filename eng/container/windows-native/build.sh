#!/usr/bin/env bash
set -euo pipefail
trap 'printf "Cross-build failed at line %s: %s (status %s)\n" "$LINENO" "$BASH_COMMAND" "$?" >&2' ERR

readonly root=/opt/sharpproof-windows-cross
readonly compiler="$root/compiler"
readonly source="$root/src/z3"
readonly compiler_archive=llvm-mingw-20261006-ucrt-ubuntu-22.04-x86_64.tar.xz
readonly compiler_sha=5f9c6ed95b2d4bdb2869a488c5fd5857fbdabcf288a0aa3eb1da43f6a08d8ab4
readonly source_archive=z3-4.12.2.tar.gz
readonly source_sha=9f58f3710bd2094085951a75791550f547903d75fe7e2fcb373c5f03fc761b8f
readonly source_commit=e417f7d78509b2d0c9ebc911fee7632e6ef546b6

test "$(uname -m)" = x86_64
test -d /archives
test -d /output
test ! -e "$compiler"
test ! -e "$source"
printf '%s  %s\n' "$compiler_sha" "/archives/$compiler_archive" | sha256sum --check --strict
printf '%s  %s\n' "$source_sha" "/archives/$source_archive" | sha256sum --check --strict
mkdir -p "$compiler" "$source" /output/licenses
tar -xJf "/archives/$compiler_archive" -C "$compiler" --strip-components=1
tar -xzf "/archives/$source_archive" -C "$source" --strip-components=1
for tool in llvm-readobj llvm-ar llvm-ranlib; do
  if [[ ! -x "$compiler/bin/$tool" ]]; then
    echo "Required tool missing from verified compiler archive: $compiler/bin/$tool" >&2
    exit 1
  fi
done
cp "$compiler/LICENSE.TXT" /output/licenses/LLVM-LICENSE.TXT
cp "$source/LICENSE.txt" /output/licenses/Z3-LICENSE.txt
cp "$root/licenses/"* /output/licenses/
(
  cd /output/licenses
  sha256sum --check --strict << 'LICENSE_HASHES'
1a8f1058753f1ba890de984e48f0242a3a5c29a6a8f2ed9fd813f36985387e8d  compiler-rt-LICENSE.TXT
539dd7aed86e8a4f12cbdd0e6c50c189c7d74847e4fecc64ce2c6ee3a01da38b  libcxx-LICENSE.TXT
e2b35be49f7284a45b7baca8fc7b3ab7440e7902392b2528a457816b5bb2a15c  libcxxabi-LICENSE.TXT
b5efebcaca80879234098e52d1725e6d9eb8fb96a19fce625d39184b705f7b6d  libunwind-LICENSE.TXT
8e5db0129069005be2a07be3297e198888fd72fad62a13a611504660c6b7e3c7  llvm-mingw-LICENSE.txt
LICENSE_HASHES
)
export SOURCE_DATE_EPOCH=1683907200
export TZ=UTC
export LC_ALL=C

for rid in ${TARGET_RIDS:-win-x64 win-arm64}; do
  case "$rid" in
    win-x64)
      triple=x86_64-w64-mingw32
      processor=x86_64
      machine=0x8664
      ;;
    win-arm64)
      triple=aarch64-w64-mingw32
      processor=aarch64
      machine=0xaa64
      ;;
    *)
      echo "Unsupported experiment RID: $rid" >&2
      exit 2
      ;;
  esac
  build="$root/build/$rid"
  output="/output/$rid"
  test ! -e "$build"
  test ! -e "$output"
  mkdir -p "$build" "$output" "/output/licenses/$rid"
  cp "$compiler/$triple/share/mingw32/COPYING"* "/output/licenses/$rid/"
  "$compiler/bin/$triple-clang++" --version > "$output/compiler-version.txt"
  # Confirm the pinned Z3 architecture detector can see ARM's GNU predefined
  # macros without a source patch. This is a preprocessor-only probe.
  "$compiler/bin/$triple-clang++" -dM -E -x c++ /dev/null > "$output/target-macros.txt"
  if [[ "$rid" = win-arm64 ]]; then
    grep -q '^#define __ARM_ARCH_ISA_A64 1$' "$output/target-macros.txt"
  fi
  # Z3 4.12.2 contains unused dependent template bodies with invalid member
  # names. Use Clang's documented MSVC-style deferred parsing, preserving the
  # verified source rather than patching solver code. Instantiated bodies
  # remain checked by the compiler.
  # nla_monotone_lemmas.h also relies on transitive std::vector visibility.
  # Supply its actual standard header explicitly without changing solver code.
  common_flags="-fdelayed-template-parsing -include vector -ffile-prefix-map=$source=/usr/src/z3 -ffile-prefix-map=$build=/usr/src/z3/build -fdebug-prefix-map=$source=/usr/src/z3 -fdebug-prefix-map=$build=/usr/src/z3/build"
  cmake_args=(
    -S "$source" -B "$build" -G Ninja
    -DCMAKE_BUILD_TYPE=Release
    -DCMAKE_SYSTEM_NAME=Windows
    "-DCMAKE_SYSTEM_PROCESSOR=$processor"
    "-DCMAKE_C_COMPILER=$compiler/bin/$triple-clang"
    "-DCMAKE_CXX_COMPILER=$compiler/bin/$triple-clang++"
    "-DCMAKE_RC_COMPILER=$compiler/bin/$triple-windres"
    "-DCMAKE_AR=$compiler/bin/llvm-ar"
    "-DCMAKE_RANLIB=$compiler/bin/llvm-ranlib"
    -DCMAKE_TRY_COMPILE_TARGET_TYPE=STATIC_LIBRARY
    "-DCMAKE_FIND_ROOT_PATH=$compiler/$triple"
    -DCMAKE_FIND_ROOT_PATH_MODE_PROGRAM=NEVER
    -DCMAKE_FIND_ROOT_PATH_MODE_LIBRARY=ONLY
    -DCMAKE_FIND_ROOT_PATH_MODE_INCLUDE=ONLY
    -DCMAKE_FIND_ROOT_PATH_MODE_PACKAGE=ONLY
    "-DCMAKE_CXX_FLAGS=$common_flags"
    '-DCMAKE_EXE_LINKER_FLAGS=-static -Wl,--no-insert-timestamp'
    "-DCMAKE_SHARED_LINKER_FLAGS=-static -Wl,--no-insert-timestamp -Wl,-Map,$build/libz3.map"
    -DPYTHON_EXECUTABLE=/usr/bin/python3
    -DZ3_BUILD_LIBZ3_SHARED=ON
    -DZ3_BUILD_EXECUTABLE=OFF
    -DZ3_BUILD_TEST_EXECUTABLES=OFF
    -DZ3_ENABLE_EXAMPLE_TARGETS=OFF
    -DZ3_BUILD_DOTNET_BINDINGS=OFF
    -DZ3_BUILD_PYTHON_BINDINGS=OFF
    -DZ3_BUILD_JAVA_BINDINGS=OFF
    -DZ3_USE_LIB_GMP=OFF
    -DZ3_LINK_TIME_OPTIMIZATION=OFF
    -DZ3_INCLUDE_GIT_HASH=OFF
    -DZ3_INCLUDE_GIT_DESCRIBE=OFF
    -DWARNINGS_AS_ERRORS=OFF
  )
  printf '%s\0' "${cmake_args[@]}" > "$output/cmake-arguments.nul"
  cmake --version > "$output/cmake-version.txt"
  ninja --version > "$output/ninja-version.txt"
  python3 --version > "$output/python-version.txt"
  dpkg-query -W cmake ninja-build python3 xz-utils curl > "$output/bootstrap-packages.txt"
  cmake "${cmake_args[@]}" > "$output/configure.log" 2>&1
  cmake --build "$build" --target libz3 --parallel "${BUILD_PARALLELISM:-4}" > "$output/build.log" 2>&1
  # Z3 4.12.2's top-level CMakeLists sets CMAKE_RUNTIME_OUTPUT_DIRECTORY
  # to PROJECT_BINARY_DIR, so the Windows DLL is in the build root.
  if [[ ! -f "$build/libz3.dll" ]]; then
    echo "Expected Z3 DLL missing: $build/libz3.dll" >&2
    find "$build" -maxdepth 5 -type f -iname '*z3*.dll' -print | head -n 20 >&2
    exit 1
  fi
  cp "$build/libz3.dll" "$output/libz3.dll"
  if [[ ! -f "$build/libz3.map" ]]; then
    echo "Required linker map missing: $build/libz3.map" >&2
    exit 1
  fi
  cp "$build/libz3.map" "$output/link.map"
  sha256sum "$compiler/$triple/lib/"*.a "$compiler/lib/clang/23/lib/windows/libclang_rt.builtins-$processor.a" > "$output/static-library-inputs.sha256"
  "$compiler/bin/llvm-readobj" --file-headers --coff-imports --coff-exports \
    "$output/libz3.dll" > "$output/llvm-readobj.txt"
  python3 "$root/inspect-pe.py" "$output" "$rid" "$machine" \
    "$source_sha" "$source_commit" "$compiler_sha"
  echo "Validated $rid payload and receipt: $output"
done
