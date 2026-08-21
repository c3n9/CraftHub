#!/bin/bash

# Fail the whole script on the first failing command. Without this the .rpm step could fail and the
# script still exit 0, which is how a release went out with a .deb and no .rpm: the packaging error
# scrolled past, "echo .rpm created" ran anyway, and the upload step skipped the file it could not
# find.
set -eo pipefail

# Version
buildVersion=$(<../build-resources/version.txt)

# Clean-up
rm -rf ./staging_folder_arm64/

# .NET publish
# self-contained is recommended, so final users won't need to install .NET
dotnet publish ../CraftHub/CraftHub.csproj --configuration Release --runtime linux-arm64 --self-contained -f net10.0
echo "Published"

# Staging directory
mkdir -p staging_folder_arm64

# Debian control file
mkdir -p ./staging_folder_arm64/DEBIAN
cp ./linux-data-arm64/control ./staging_folder_arm64/DEBIAN
sed -i "s/currentVersionIsPlacedHere/${buildVersion}/g" ./staging_folder_arm64/DEBIAN/control
echo "Control copied"

# Starter script
mkdir -p ./staging_folder_arm64/usr
mkdir -p ./staging_folder_arm64/usr/bin
cp ./linux-data-arm64/crafthub ./staging_folder_arm64/usr/bin/crafthub
chmod +x ./staging_folder_arm64/usr/bin/crafthub # set executable permissions to starter script
echo "Started copied"

# Other files
mkdir -p ./staging_folder_arm64/usr/share
mkdir -p ./staging_folder_arm64/usr/share/crafthub
cp -f -a ../CraftHub/bin/Release/net10.0/linux-arm64/publish/. ./staging_folder_arm64/usr/share/crafthub/ # copies all files from publish dir
chmod -R a+rX ./staging_folder_arm64/usr/share/crafthub/ # set read permissions to all files
chmod a+x ./staging_folder_arm64/usr/share/crafthub/CraftHub # set executable permissions to main executable
echo "CraftHub copied"

# Desktop shortcut
mkdir -p ./staging_folder_arm64/usr/share/applications
cp ./linux-data-arm64/CraftHub.desktop ./staging_folder_arm64/usr/share/applications/CraftHub.desktop
echo "Shortcut copied"

# Desktop icon
# A 1024px x 1024px PNG, like VS Code uses for its icon
mkdir -p ./staging_folder_arm64/usr/share/pixmaps
cp ../build-resources/logo.png ./staging_folder_arm64/usr/share/pixmaps/crafthub.png
echo "Icon copied"

# Hicolor icons
mkdir -p ./staging_folder_arm64/usr/share/icons
mkdir -p ./staging_folder_arm64/usr/share/icons/hicolor
mkdir -p ./staging_folder_arm64/usr/share/icons/hicolor/scalable
mkdir -p ./staging_folder_arm64/usr/share/icons/hicolor/scalable/apps
# craftHub.svg, not logo.svg: the latter has never existed in this repository, so this copy had
# been failing since the day it was written — silently, until set -e. The .deb shipped without a
# scalable icon and the .rpm could not be built at all, because the spec's file list names it.
cp ../build-resources/craftHub.svg ./staging_folder_arm64/usr/share/icons/hicolor/scalable/apps/crafthub.svg
echo "Another icon copied"

# Make .deb file
dpkg-deb --root-owner-group --build ./staging_folder_arm64/ ./crafthub_arm64.deb
echo ".deb created"
# Make .rpm file from the very same staging tree, so the two packages cannot drift in content.
chmod a+x ./make-rpm.sh
./make-rpm.sh ./staging_folder_arm64 aarch64 ./crafthub_aarch64.rpm
echo ".rpm created"
