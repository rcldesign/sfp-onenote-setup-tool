#!/usr/bin/env bash
# Publishes the release folder and zip (M6.6): sfp2onenote.exe (the command line) and sfp2onenote-app.exe (the window)
# as self-contained single-file win-x64 executables in ONE folder, with the templates folder and LICENSE.md beside
# them, then checks the folder and zips it as sfp2onenote-<version>-win-x64.zip next to it. The app starts the
# sfp2onenote.exe beside itself for the daily scheduled task, so the two always ship together.
#
# Runs wherever the .NET 8 SDK runs; on Linux and macOS the WPF app is cross-published (EnableWindowsTargeting in
# Directory.Build.props). Trimming stays off: WPF is not trim-safe.
#
# Usage: build/publish.sh [--out DIR] [--version VERSION]
#   --out DIR          the release folder. Default: artifacts/sfp2onenote-win-x64 under the repository root, emptied
#                      on every run. Any other folder must be empty or missing (the script never deletes it).
#   --version VERSION  stamped into both executables and the zip name, for example 1.2.0; a leading "v" is dropped so
#                      a tag name works. Default: the Version property of Directory.Build.props, read through MSBuild.
set -euo pipefail

die() {
  echo "publish.sh: $*" >&2
  exit 1
}

bytes() {
  wc -c < "$1" | tr -d ' '
}

megabytes() {
  awk -v b="$(bytes "$1")" 'BEGIN { printf "%.1f MB", b / 1048576 }'
}

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cli_project="$root/src/Sfp.Cli/Sfp.Cli.csproj"
app_project="$root/src/Sfp.App/Sfp.App.csproj"
default_out="$root/artifacts/sfp2onenote-win-x64"
version_pattern='^[0-9]+(\.[0-9]+){1,3}(-[0-9A-Za-z][0-9A-Za-z.-]*)?$'
out=""
version=""

while [ $# -gt 0 ]; do
  case "$1" in
    --out)
      [ $# -ge 2 ] || die "--out needs a folder"
      out="$2"
      shift 2
      ;;
    --version)
      [ $# -ge 2 ] || die "--version needs a value, for example 1.2.0"
      version="$2"
      shift 2
      ;;
    -h|--help)
      awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "${BASH_SOURCE[0]}"
      exit 0
      ;;
    *)
      die "unknown argument '$1' (try --help)"
      ;;
  esac
done

command -v dotnet > /dev/null 2>&1 || die "the .NET 8 SDK (dotnet) is not on PATH"

if [ -z "$version" ]; then
  # The last non-empty line: the SDK's first-run welcome text, if any, comes before the value.
  version="$(dotnet msbuild "$cli_project" -getProperty:Version | tr -d '\r' | awk 'NF { last = $0 } END { print last }')"
  [ -n "$version" ] || die "could not read the Version property through MSBuild"
fi
version="${version#v}"
[[ "$version" =~ $version_pattern ]] || die "version '$version' is not a version number such as 1.2.0 or 1.2.0-rc.1"

if [ -z "$out" ]; then
  out="$default_out"
  rm -rf "$out"
fi
mkdir -p "$out"
out="$(cd "$out" && pwd)"
if [ "$out" = "$root" ] || [ -n "$(ls -A "$out")" ]; then
  die "the release folder $out is not empty; empty it or choose another --out"
fi
zip="$(dirname "$out")/sfp2onenote-$version-win-x64.zip"

echo "Publishing sfp2onenote $version for win-x64 into $out"
publish=(-c Release -r win-x64 --self-contained
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
  "-p:Version=$version" -o "$out")
dotnet publish "$cli_project" "${publish[@]}"
dotnet publish "$app_project" "${publish[@]}"
cp "$root/LICENSE.md" "$out/LICENSE.md"

# Check the folder: both executables are single-file bundles (a bare app host is about 150 KB, a self-contained
# bundle about 100 MB) and templates/ is a byte-identical copy of the shipped templates folder.
problems=()
for exe in sfp2onenote.exe sfp2onenote-app.exe; do
  if [ ! -f "$out/$exe" ]; then
    problems+=("$exe is missing")
  elif [ "$(bytes "$out/$exe")" -lt 10485760 ]; then
    problems+=("$exe is $(megabytes "$out/$exe"), not a self-contained single-file bundle")
  fi
done
for required in templates/sfp-quarterly.json templates/holidays-us.json; do
  [ -f "$out/$required" ] || problems+=("$required is missing")
done
fonts=0
for font in "$out"/templates/fonts/*.otf; do
  if [ -f "$font" ]; then
    fonts=$((fonts + 1))
  fi
done
[ "$fonts" -gt 0 ] || problems+=("templates/fonts/*.otf is missing")
while IFS= read -r -d '' source; do
  relative="${source#"$root"/}"
  if [ ! -f "$out/$relative" ]; then
    problems+=("$relative is missing")
  elif ! cmp -s "$source" "$out/$relative"; then
    problems+=("$relative differs from the repository copy")
  fi
done < <(find "$root/templates" -type f -print0)
if [ ${#problems[@]} -gt 0 ]; then
  printf 'publish.sh: %s\n' "${problems[@]}" | awk '!seen[$0]++' >&2
  die "the release folder $out is incomplete"
fi
for entry in "$out"/* "$out"/.[!.]*; do
  [ -e "$entry" ] || continue
  case "$(basename "$entry")" in
    sfp2onenote.exe | sfp2onenote-app.exe | templates | LICENSE.md) ;;
    *) echo "publish.sh: warning: unexpected entry in the release folder: $(basename "$entry")" >&2 ;;
  esac
done

rm -f "$zip"
if command -v zip > /dev/null 2>&1; then
  (cd "$out" && zip -q -r -X "$zip" .)
elif command -v python3 > /dev/null 2>&1; then
  python3 -I - "$out" "$zip" << 'PY'
import os
import sys
import zipfile

folder, target = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
    for parent, dirs, files in os.walk(folder):
        dirs.sort()
        for name in sorted(files):
            path = os.path.join(parent, name)
            archive.write(path, os.path.relpath(path, folder).replace(os.sep, "/"))
PY
else
  die "neither zip nor python3 is available to write $zip"
fi

echo
echo "Release folder: $out"
echo "  sfp2onenote.exe      $(megabytes "$out/sfp2onenote.exe")"
echo "  sfp2onenote-app.exe  $(megabytes "$out/sfp2onenote-app.exe")"
echo "  templates/           $(find "$out/templates" -type f | wc -l | tr -d ' ') files"
echo "Zip: $zip ($(megabytes "$zip"))"
