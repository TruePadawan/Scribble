#!/bin/bash

# Clean up
rm -rf ./out/
rm -rf ./staging/
rm -rf ./releases/linux

# .NET publish
# self-contained is recommended, so final users won't need to install .NET
dotnet publish "./Scribble.Desktop/Scribble.Desktop.csproj" \
  --verbosity quiet \
  --nologo  \
  --configuration Release \
  --self-contained true \
  --runtime linux-x64 \
  --output "./out/linux-x64"  \
  /p:PublishSingleFile=true

# --- SHARED METADATA ---
NAME="scribble"
VERSION="0.1.4-alpha"
MAINTAINER="Chisom Hermes Chigoziri <hermeschigoziri@gmail.com>"
DESC="A cross-platform digital whiteboard engineered with C# and Avalonia UI"
URL="https://github.com/TruePadawan/Scribble"
ARCH="amd64"
CATEGORY="Graphics"

# 1. Prepare Staging
rm -rf ./staging
mkdir -p ./staging/usr/bin
mkdir -p ./staging/usr/lib/scribble
mkdir -p ./staging/usr/share/applications
mkdir -p ./staging/usr/share/pixmaps
mkdir -p ./staging/usr/share/icons/hicolor/scalable/apps
mkdir -p ./staging/usr/share/metainfo
mkdir -p ./releases/linux

# 2. Copy Files
# Copy binary to lib
cp -r ./out/linux-x64/* ./staging/usr/lib/scribble/
# Copy wrapper script to bin
cp ./Scribble.Desktop/DEBIAN/scribble.sh ./staging/usr/bin/scribble
# Copy Desktop assets
cp ./Scribble.Desktop/DEBIAN/Scribble.desktop ./staging/usr/share/applications/
cp ./Scribble.Desktop/DEBIAN/scribble.png ./staging/usr/share/pixmaps/
cp ./Scribble.Desktop/DEBIAN/scribble.svg ./staging/usr/share/icons/hicolor/scalable/apps/
cp ./Scribble.Desktop/DEBIAN/scribble.metainfo.xml ./staging/usr/share/metainfo/

# 3. Set Permissions
chmod +x ./staging/usr/bin/scribble
chmod +x ./staging/usr/lib/scribble/Scribble.Desktop

# 4. PACKAGE EVERYTHING
echo "Packaging DEB..."
fpm -s dir -t deb \
    -n "$NAME" -v "$VERSION" -a "$ARCH" \
    --license gpl3 \
    -m "$MAINTAINER" \
    --description "$DESC" \
    --category "$CATEGORY" \
    --url "$URL" \
    -C ./staging \
    -p ./releases/linux/scribble_amd64.deb \
    usr/

echo "Packaging RPM..."
fpm -s dir -t rpm \
    -n "$NAME" -v "$VERSION" -a "x86_64" \
    --license gpl3 \
    -m "$MAINTAINER" \
    --description "$DESC" \
    --category "$CATEGORY" \
    --url "$URL" \
    -C ./staging \
    -p ./releases/linux/scribble.x86_64.rpm \
    usr/

echo "Packaging Pacman..."
fpm -s dir -t pacman \
    -n "$NAME" -v "$VERSION" -a "x86_64" \
    --license gpl3 \
    -m "$MAINTAINER" \
    --description "$DESC" \
    --category "$CATEGORY" \
    --url "$URL" \
    -C ./staging \
    -p ./releases/linux/scribble.pkg.tar.zst \
    usr/

echo "Done!"