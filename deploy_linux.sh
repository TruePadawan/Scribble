#!/bin/bash

# Clean up
rm -rf ./out/
rm -rf ./staging/

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

# create the staging directory
mkdir staging

# Collect Debian control file
# The control file describes general aspects of the program - name, version, author, etc
mkdir ./staging/DEBIAN
cp ./Scribble.Desktop/DEBIAN/control ./staging/DEBIAN

# Collect starter script
# This enables the app to be runnable from the terminal
mkdir -p ./staging/usr/bin
cp ./Scribble.Desktop/DEBIAN/scribble.sh ./staging/usr/bin/scribble
chmod +x ./staging/usr/bin/scribble # set executable permissions to starter script

# Deal with published files
mkdir -p ./staging/usr/lib/scribble
cp -f -a ./out/linux-x64/. ./staging/usr/lib/scribble # copy the files published by .NET
chmod -R a+rX ./staging/usr/lib/scribble/ # set read permissions to all files
chmod +x ./staging/usr/lib/scribble/Scribble.Desktop # set executable permissions to main executable

# Desktop shortcuts
mkdir -p ./staging/usr/share/applications
cp ./Scribble.Desktop/DEBIAN/Scribble.desktop ./staging/usr/share/applications/Scribble.desktop

# Desktop icon
mkdir ./staging/usr/share/pixmaps
cp ./Scribble.Desktop/DEBIAN/scribble.png ./staging/usr/share/pixmaps/scribble.png

# Hicolor icons
mkdir -p ./staging/usr/share/icons/hicolor/scalable/apps
cp ./Scribble.Desktop/DEBIAN/scribble.svg ./staging/usr/share/icons/hicolor/scalable/apps/scribble.svg

dpkg-deb --root-owner-group --build ./staging/ ./scribble_amd64.deb