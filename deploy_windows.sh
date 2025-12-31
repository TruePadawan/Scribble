#!/bin/bash

# Clean up
rm -rf ./out/

# .NET publish
# self-contained is recommended, so final users won't need to install .NET
dotnet publish "./Scribble.Desktop/Scribble.Desktop.csproj" \
  --verbosity quiet \
  --nologo  \
  --configuration Release \
  --self-contained true \
  --runtime win-x64 \
  --output "./out/win-x64"  \
  /p:PublishSingleFile=true

zip -j Scribble_Win64.zip ./out/win-x64/*