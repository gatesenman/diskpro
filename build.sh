#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
dotnet test tests/DiskPro.Tests -c Release
dotnet publish src/DiskPro.App/DiskPro.App.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -p:EnableWindowsTargeting=true -o publish/win-x64
mkdir -p release
cp publish/win-x64/DiskPro.exe release/DiskPro.exe
rm -f release/DiskPro-win-x64.zip
(cd release && cp ../README.md README.md && zip -9 -q DiskPro-win-x64.zip DiskPro.exe README.md && rm README.md)
ls -la release
