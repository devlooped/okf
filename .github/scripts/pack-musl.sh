#!/bin/sh
# Pack one musl Native AOT nupkg. Run inside mcr.microsoft.com/dotnet/sdk:10.0-alpine3.23-aot
# (or any Alpine SDK image with a compiler). The host job invokes this via docker
# so GitHub's glibc runner can still run actions/checkout.
# Usage: pack-musl.sh <rid>
set -eu

rid=${1:?rid}

# Native AOT on Alpine needs clang and zlib headers. The -aot image already has
# them; apk keeps a plain Alpine SDK image working too.
apk add --no-cache clang build-base zlib-dev

# linux-musl-* is in the project's RuntimeIdentifiers, so the pointer package
# depends on this nupkg. Pack it before the pointer is published.
dotnet pack src/okf/okf.csproj \
  -c "${Configuration:-Release}" \
  -r "$rid" \
  -bl:"pack-${rid}.binlog"

count=0
for f in bin/okf."$rid".*.nupkg; do
  [ -f "$f" ] || continue
  case $f in
    *.symbols.nupkg) continue ;;
  esac
  count=$((count + 1))
done

if [ "$count" -ne 1 ]; then
  echo "Expected one RID nupkg for $rid, found $count." >&2
  ls -la bin >&2 || true
  exit 1
fi
