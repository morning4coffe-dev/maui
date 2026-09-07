#!/usr/bin/env bash
set -euo pipefail

print_output_directory=false
if [[ "${1:-}" == --print-output-directory ]]; then
    print_output_directory=true
    shift
fi

if [[ $# -ne 3 || "$(uname -s)" != Darwin ]]; then
    echo "Usage (macOS): build-icu-catalyst.sh [--print-output-directory] CACHE_DIRECTORY arm64|x86_64 MINIMUM_IOS_VERSION" >&2
    exit 1
fi

architecture="$2"
minimum_version="$3"
case "$architecture" in
    arm64|x86_64) ;;
    *) echo "Unsupported Catalyst architecture: $architecture" >&2; exit 1 ;;
esac
if [[ ! "$minimum_version" =~ ^[0-9]+\.[0-9]+(\.[0-9]+)?$ ]]; then
    echo "Invalid minimum Catalyst version: $minimum_version" >&2
    exit 1
fi

mkdir -p "$1"
cache_root="$(cd "$1" && pwd -P)"
sdk="$(xcrun --sdk macosx --show-sdk-path)"
export SDKROOT="$sdk"
compiler="$(xcrun --find clang)"
cxx_compiler="$(xcrun --find clang++)"
source_hash="a47d6d9c327d037a05ea43d1d1a06b2fd757cc02a94f7c1a238f35cfc3dfd4ab78d0612790f3a3cca0292c77412a9c2c15c8f24b718f79a857e007e66f07e7cd"
jobs="${MAUI_ICU_BUILD_JOBS:-4}"
if [[ ! "$jobs" =~ ^[1-9][0-9]*$ ]]; then
    echo "MAUI_ICU_BUILD_JOBS must be a positive integer." >&2
    exit 1
fi

build_key="$(
    {
        shasum -a 256 "$0" | cut -d ' ' -f 1
        "$compiler" --version
        xcrun --sdk macosx --show-sdk-build-version
        printf '%s\n' "$architecture" "$minimum_version" "$source_hash"
    } | shasum -a 256 | cut -d ' ' -f 1
)"
output="$cache_root/$build_key"
work="$cache_root/.build-$build_key"
lock="$cache_root/$build_key.lock"

if [[ "$print_output_directory" == true ]]; then
    printf '%s\n' "$output"
    exit 0
fi

cache_ready() {
    if [[ ! -e "$output" ]]; then
        return 1
    fi
    if [[ ! -d "$output" || -L "$output" ||
          ! -f "$output/build-key" || "$(cat "$output/build-key")" != "$build_key" ||
          ! -s "$output/libicuuc.a" || ! -s "$output/libicudata.a" || ! -s "$output/ICU-LICENSE.txt" ]]; then
        echo "Incomplete ICU cache entry: $output. Move it aside before rebuilding." >&2
        exit 1
    fi
    return 0
}

if cache_ready; then
    exit 0
fi

waited=0
until mkdir "$lock" 2>/dev/null; do
    if cache_ready; then
        exit 0
    fi
    if (( waited >= 900 )); then
        echo "Timed out waiting for the ICU build at $lock. Check whether its owner is still running." >&2
        exit 1
    fi
    sleep 1
    waited=$((waited + 1))
done
trap 'rmdir "$lock"' EXIT
if cache_ready; then
    exit 0
fi

mkdir -p "$work"
archive="$work/icu4c-77_1-src.tgz"
if [[ ! -f "$archive" ]]; then
    curl --fail --location --retry 3 \
        https://github.com/unicode-org/icu/releases/download/release-77-1/icu4c-77_1-src.tgz \
        --output "$archive.download"
    printf '%s  %s\n' "$source_hash" "$archive.download" | shasum -a 512 -c -
    mv "$archive.download" "$archive"
else
    printf '%s  %s\n' "$source_hash" "$archive" | shasum -a 512 -c -
fi

source_root="$work/source-77.1"
if [[ ! -f "$source_root/extracted" ]]; then
    mkdir -p "$source_root"
    tar -xzf "$archive" -C "$source_root" --strip-components=1
    touch "$source_root/extracted"
fi

build_root="$work/build"
host_root="$build_root/host"
target_root="$build_root/catalyst"
mkdir -p "$host_root" "$target_root"
configure="$source_root/source/configure"

# ICU's data compiler must run on macOS while the target archives are built for macabi.
if ! (
    cd "$host_root"
    if [[ ! -f Makefile ]]; then
        CC="$compiler" CXX="$cxx_compiler" \
        CFLAGS="-O2" CXXFLAGS="-O2 -std=c++17" LDFLAGS="" "$configure" \
            --enable-static --disable-shared --with-data-packaging=static \
            --disable-tests --disable-samples --disable-extras || exit "$?"
    fi
    make -s -j "$jobs"
) > "$build_root/host.log" 2>&1; then
    tail -80 "$build_root/host.log" >&2
    echo "ICU host build failed; see $build_root/host.log" >&2
    exit 1
fi

target="$architecture-apple-ios$minimum_version-macabi"
if ! (
    cd "$target_root"
    if [[ ! -f Makefile ]]; then
        CC="$compiler" CXX="$cxx_compiler" \
        CFLAGS="-O2 -target $target" \
        CXXFLAGS="-O2 -target $target -stdlib=libc++ -std=c++17" \
        LDFLAGS="-target $target -stdlib=libc++" \
        "$configure" --host="$architecture-apple-darwin" --with-cross-build="$host_root" \
            --enable-static --disable-shared --with-data-packaging=static \
            --disable-tests --disable-samples --disable-tools --disable-extras --disable-dyload || exit "$?"
    fi
    make -s -j "$jobs" || exit "$?"
    make -s -j "$jobs" -C data
) > "$build_root/catalyst.log" 2>&1; then
    tail -80 "$build_root/catalyst.log" >&2
    echo "ICU Catalyst build failed; see $build_root/catalyst.log" >&2
    exit 1
fi

publish="$work/publish"
mkdir -p "$publish"
cp "$target_root/lib/libicuuc.a" "$target_root/lib/libicudata.a" "$publish/"
cp "$source_root/LICENSE" "$publish/ICU-LICENSE.txt"
printf '%s\n' "$build_key" > "$publish/build-key"
# Publish the whole entry atomically; linkers only consume immutable keyed paths.
mv "$publish" "$output"
echo "Built ICU 77.1 for $target in $output"
