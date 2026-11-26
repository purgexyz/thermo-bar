#!/usr/bin/env bash
# Cross-compile the Windows exe from Linux/macOS. Pass --self-contained for a build with no runtime prerequisite.
set -euo pipefail
cd "$(dirname "$0")"

sc=false
[[ "${1:-}" == "--self-contained" ]] && sc=true

dotnet publish src/TempBar/TempBar.csproj \
  -c Release -r win-x64 --self-contained "$sc" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile="$sc" \
  -p:DebugType=none \
  -o dist

echo "Built dist/TempBar.exe"
