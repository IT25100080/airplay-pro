#!/usr/bin/env bash
# Builds the UxPlay AirPlay engine on Windows and packages it, with every DLL and
# GStreamer plugin it needs, into a self-contained folder the tray app can run.
#
# Run from an MSYS2 "UCRT64" shell:
#   ./scripts/build-uxplay.sh [output-dir]
#
# Env vars:
#   UXPLAY_REF         UxPlay tag, branch or full commit hash to build. Default is a
#                      development snapshot (2026-10-21) with YouTube/HLS crash fixes
#                      that the last release, v1.73.7, doesn't have.
#   BONJOUR_SDK_HOME   Apple's Bonjour SDK, if installed. If unset and not found in
#                      the default location, a minimal stand-in is generated (UxPlay
#                      loads dnssd.dll at runtime, so it only needs the header and
#                      a link stub at build time).
#   SKIP_PACMAN=1      Don't install MSYS2 packages (CI installs them up front).
set -euo pipefail

UXPLAY_REF="${UXPLAY_REF:-b29411ffe30eb6b62b755af5ce6a03b2a25c2371}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$(realpath -m "${1:-$ROOT/dist/uxplay}")"
WORK="$ROOT/.build"
PREFIX="${MINGW_PREFIX:?Run this from an MSYS2 UCRT64 shell}"   # e.g. /ucrt64
PKG="${MINGW_PACKAGE_PREFIX:?Run this from an MSYS2 UCRT64 shell}" # e.g. mingw-w64-ucrt-x86_64

if [[ "${SKIP_PACMAN:-0}" != 1 ]]; then
  pacman -S --needed --noconfirm \
    git "$PKG-cmake" "$PKG-ninja" "$PKG-gcc" "$PKG-pkgconf" "$PKG-ntldd" \
    "$PKG-openssl" "$PKG-libplist" \
    "$PKG-gstreamer" "$PKG-gst-plugins-base" "$PKG-gst-plugins-good" \
    "$PKG-gst-plugins-bad" "$PKG-gst-libav" \
    "$PKG-libsoup3" "$PKG-glib-networking" "$PKG-ca-certificates"
fi

mkdir -p "$WORK"

# --- Bonjour SDK (build-time only) -------------------------------------------
if [[ -z "${BONJOUR_SDK_HOME:-}" ]]; then
  if [[ -f "/c/Program Files/Bonjour SDK/Include/dns_sd.h" ]]; then
    export BONJOUR_SDK_HOME="C:\\Program Files\\Bonjour SDK"
  else
    echo ">> Bonjour SDK not found; generating a stand-in from Apple's open-source mDNSResponder"
    SDK="$WORK/bonjour-sdk"
    mkdir -p "$SDK/Include" "$SDK/Lib/x64"
    curl -fsSL -o "$SDK/Include/dns_sd.h" \
      https://raw.githubusercontent.com/apple-oss-distributions/mDNSResponder/main/mDNSShared/dns_sd.h
    cat > "$SDK/dnssd.def" <<'EOF'
LIBRARY dnssd.dll
EXPORTS
DNSServiceRegister
DNSServiceRefDeallocate
DNSServiceRefSockFD
DNSServiceProcessResult
TXTRecordCreate
TXTRecordSetValue
TXTRecordGetLength
TXTRecordGetBytesPtr
TXTRecordDeallocate
EOF
    dlltool -d "$SDK/dnssd.def" -l "$SDK/Lib/x64/dnssd.lib"
    export BONJOUR_SDK_HOME="$(cygpath -w "$SDK")"
  fi
fi
echo ">> BONJOUR_SDK_HOME=$BONJOUR_SDK_HOME"

# --- Build UxPlay ------------------------------------------------------------
SRC="$WORK/UxPlay"
# init + fetch (rather than clone --branch) so UXPLAY_REF can also be a commit hash.
if [[ ! -d "$SRC/.git" ]]; then
  git init -q "$SRC"
  git -C "$SRC" remote add origin https://github.com/FDH2/UxPlay.git
fi
git -C "$SRC" fetch -q --depth 1 origin "$UXPLAY_REF"
git -C "$SRC" checkout -q --force FETCH_HEAD

# USE_DNS_SD: newer UxPlay defaults to its own mDNS code on Windows; keep using the
# Bonjour service instead, which is what discovery has been tested with.
rm -rf "$SRC/build"
cmake -S "$SRC" -B "$SRC/build" -G Ninja -DCMAKE_BUILD_TYPE=Release -DUSE_DNS_SD=1
cmake --build "$SRC/build"

# --- Package -----------------------------------------------------------------
rm -rf "$OUT"
mkdir -p "$OUT/bin" "$OUT/lib/gstreamer-1.0" "$OUT/libexec/gstreamer-1.0" "$OUT/licenses" \
  "$OUT/lib/gio/modules" "$OUT/etc/ssl/certs" "$OUT/share/glib-2.0/schemas"

cp "$SRC/build/uxplay.exe" "$OUT/bin/"
cp "$PREFIX"/lib/gstreamer-1.0/*.dll "$OUT/lib/gstreamer-1.0/"
cp "$PREFIX/libexec/gstreamer-1.0/gst-plugin-scanner.exe" "$OUT/libexec/gstreamer-1.0/"

# YouTube streaming (-hls): the soup plugin loads libsoup at runtime (invisible to
# ntldd), and HTTPS needs GLib's TLS module plus a CA bundle.
cp "$PREFIX"/bin/libsoup-3.0-*.dll "$OUT/bin/"
# Only the TLS backend: glib-networking's GNOME/libproxy proxy modules need desktop
# GSettings schemas and abort the engine with "No GSettings schemas are installed".
shopt -s nullglob
tls_modules=("$PREFIX"/lib/gio/modules/libgioopenssl*.dll "$PREFIX"/lib/gio/modules/libgiognutls*.dll)
shopt -u nullglob
(( ${#tls_modules[@]} )) || { echo "!! no GIO TLS module found in $PREFIX/lib/gio/modules"; exit 1; }
cp "${tls_modules[@]}" "$OUT/lib/gio/modules/"
cp "$PREFIX/etc/ssl/certs/ca-bundle.crt" "$OUT/etc/ssl/certs/"
# GLib's own schemas, so anything else that touches GSettings finds them too.
if [[ -f "$PREFIX/share/glib-2.0/schemas/gschemas.compiled" ]]; then
  cp "$PREFIX/share/glib-2.0/schemas/gschemas.compiled" "$OUT/share/glib-2.0/schemas/"
fi

# Collect every MSYS2 DLL that uxplay.exe, the scanner or any plugin depends on.
# (ntldd -R walks the dependency tree; system DLLs from C:\Windows are skipped.)
win_prefix="$(cygpath -w "$PREFIX" | tr '\\' '/' | tr '[:upper:]' '[:lower:]')"
{
  ntldd -R "$OUT/bin/uxplay.exe" "$OUT/libexec/gstreamer-1.0/gst-plugin-scanner.exe" \
    "$OUT"/lib/gstreamer-1.0/*.dll "$OUT"/lib/gio/modules/*.dll "$OUT"/bin/libsoup-3.0-*.dll \
    2>/dev/null || true
} | tr '\\' '/' | grep -oiE "[a-z]:/[^ ]+\.dll" | sort -u | while read -r dll; do
  if [[ "$(echo "$dll" | tr '[:upper:]' '[:lower:]')" == "$win_prefix"/bin/* ]]; then
    cp -n "$(cygpath -u "$dll")" "$OUT/bin/"
  fi
done

# No DLL copies are needed next to the plugins or the scanner: the tray app puts
# bin/ on PATH before starting uxplay.exe, and child processes inherit it.

# Fail the build rather than ship an engine that can't play YouTube video.
for f in bin/libsoup-3.0-0.dll lib/gstreamer-1.0/libgstsoup.dll lib/gstreamer-1.0/libgstplayback.dll \
         etc/ssl/certs/ca-bundle.crt; do
  [[ -f "$OUT/$f" ]] || { echo "!! missing $f (needed for YouTube streaming)"; exit 1; }
done
if ls "$OUT"/lib/gio/modules/ | grep -qiE "proxy"; then
  echo "!! a GIO proxy module was bundled; it crashes the engine without GSettings schemas"; exit 1
fi
echo ">> GIO modules: $(ls "$OUT"/lib/gio/modules/ | tr '\n' ' ')"

cp "$SRC/LICENSE" "$OUT/licenses/UxPlay-LICENSE.txt"
git -C "$SRC" rev-parse HEAD > "$OUT/licenses/UxPlay-VERSION.txt"
echo "$UXPLAY_REF" >> "$OUT/licenses/UxPlay-VERSION.txt"

echo ">> Packaged UxPlay $UXPLAY_REF into $OUT ($(du -sh "$OUT" | cut -f1))"
