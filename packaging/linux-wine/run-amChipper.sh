#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP="$ROOT/amChipper/amChipper.exe"

if ! command -v wine >/dev/null 2>&1; then
  echo "Wine is required to run the WPF build on Linux." >&2
  echo "Install Wine, then run this script again." >&2
  exit 127
fi

if [ ! -f "$APP" ]; then
  echo "Could not find $APP" >&2
  exit 2
fi

if ! wine dotnet --list-runtimes 2>/dev/null | grep -E "Microsoft\\.WindowsDesktop\\.App 10\\." >/dev/null; then
  echo "Microsoft .NET Desktop Runtime 10.x is required inside this Wine prefix." >&2
  echo "Install the Windows x64 Desktop Runtime in Wine, then rerun this launcher." >&2
  echo "Download: https://dotnet.microsoft.com/download/dotnet/10.0" >&2
  exit 126
fi

export WINEDEBUG="${WINEDEBUG:--all}"
wine "$APP" "$@"
