#!/bin/bash

# Clean up
rm -rf ./out/

# Ensure the releases folder exists
mkdir -p ./releases/windows

VERSION="${1:-0.1.0}"

# .NET publish
# self-contained is recommended, so final users won't need to install .NET
dotnet publish "./Scribble.Desktop/Scribble.Desktop.csproj" \
  --verbosity quiet \
  --nologo \
  --configuration Release \
  --self-contained true \
  --runtime win-x64 \
  --output "./out/win-x64" \
  /p:PublishSingleFile=true

zip -j "./releases/windows/Scribble-${VERSION}.zip" ./out/win-x64/*