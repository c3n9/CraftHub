#!/bin/bash

# Fail the whole script on the first failing command. Without this the .rpm step could fail and the
# script still exit 0, which is how a release went out with a .deb and no .rpm: the packaging error
# scrolled past, "echo .rpm created" ran anyway, and the upload step skipped the file it could not
# find.
set -eo pipefail

# Version
buildVersion=$(<../build-resources/version.txt)

# Clean-up
rm -rf ./out/
rm -rf ./staging_folder/

# .NET publish
# self-contained is recommended, so final users won't need to install .NET
dotnet publish ../CraftHub/CraftHub.csproj --configuration Release --runtime linux-x64 --self-contained -f net10.0
echo "Published"

# Staging directory
mkdir -p staging_folder

# Debian control file
mkdir -p ./staging_folder/DEBIAN
cp ./linux-data/control ./staging_folder/DEBIAN
sed -i "s/currentVersionIsPlacedHere/${buildVersion}/g" ./staging_folder/DEBIAN/control
# Maintainer scripts: they rebuild the MIME and desktop caches after install/remove, without which
# the .crhb type stays unregistered however correct crafthub.xml is. Not duplicated per
# architecture — there is nothing arch-specific in them. dpkg refuses a maintainer script that is
# not executable, and dpkg-deb takes the mode straight from the staging tree, hence the chmod.
cp ./linux-data/postinst ./linux-data/postrm ./staging_folder/DEBIAN/
chmod 755 ./staging_folder/DEBIAN/postinst ./staging_folder/DEBIAN/postrm
echo "Maintainer scripts copied"

# Starter script
mkdir -p ./staging_folder/usr
mkdir -p ./staging_folder/usr/bin
cp ./linux-data/crafthub ./staging_folder/usr/bin/crafthub
chmod +x ./staging_folder/usr/bin/crafthub # set executable permissions to starter script
echo "Started copied"

# Other files
mkdir -p ./staging_folder/usr/share
mkdir -p ./staging_folder/usr/share/crafthub
cp -f -a ../CraftHub/bin/Release/net10.0/linux-x64/publish/. ./staging_folder/usr/share/crafthub/ # copies all files from publish dir
chmod -R a+rX ./staging_folder/usr/share/crafthub/ # set read permissions to all files
chmod a+x ./staging_folder/usr/share/crafthub/CraftHub # set executable permissions to main executable
echo "CraftHub copied"

# Desktop shortcut
mkdir -p ./staging_folder/usr/share/applications
cp ./linux-data/CraftHub.desktop ./staging_folder/usr/share/applications/CraftHub.desktop
echo "Shortcut copied"

# MIME type for .crhb bundles. Arch-independent, so both build scripts copy the same file from
# linux-data/ rather than keeping a second copy under linux-data-arm64/ that could drift.
mkdir -p ./staging_folder/usr/share/mime
mkdir -p ./staging_folder/usr/share/mime/packages
cp ./linux-data/crafthub-mime.xml ./staging_folder/usr/share/mime/packages/crafthub.xml
echo "MIME type copied"

# Desktop icon
# A 1024px x 1024px PNG, like VS Code uses for its icon
mkdir -p ./staging_folder/usr/share/pixmaps
cp ../build-resources/logo.png ./staging_folder/usr/share/pixmaps/crafthub.png
echo "Icon copied"

# Hicolor icons
mkdir -p ./staging_folder/usr/share/icons
mkdir -p ./staging_folder/usr/share/icons/hicolor
mkdir -p ./staging_folder/usr/share/icons/hicolor/scalable
mkdir -p ./staging_folder/usr/share/icons/hicolor/scalable/apps
# craftHub.svg, not logo.svg: the latter has never existed in this repository, so this copy had
# been failing since the day it was written — silently, until set -e. The .deb shipped without a
# scalable icon and the .rpm could not be built at all, because the spec's file list names it.
cp ../build-resources/craftHub.svg ./staging_folder/usr/share/icons/hicolor/scalable/apps/crafthub.svg
echo "Another icon copied"

# Make .deb file
dpkg-deb --root-owner-group --build ./staging_folder/ ./crafthub_amd64.deb
echo ".deb created"
# Make .rpm file from the very same staging tree, so the two packages cannot drift in content.
chmod a+x ./make-rpm.sh
./make-rpm.sh ./staging_folder x86_64 ./crafthub_x86_64.rpm
echo ".rpm created"

