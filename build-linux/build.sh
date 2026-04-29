#!/bin/bash

# Version
buildVersion=$(<../build-resources/version.txt)

# Clean-up
rm -rf ./out/
rm -rf ./staging_folder/

# .NET publish
# self-contained is recommended, so final users won't need to install .NET
dotnet publish ../CraftHub/CraftHub.csproj --configuration Release --runtime linux-x64 --self-contained -f net9.0
echo "Published"

# Staging directory
mkdir -p staging_folder

# Debian control file
mkdir -p ./staging_folder/DEBIAN
cp ./linux-data/control ./staging_folder/DEBIAN
sed -i "s/currentVersionIsPlacedHere/${buildVersion}/g" ./staging_folder/DEBIAN/control
echo "Control copied"

# Starter script
mkdir -p ./staging_folder/usr
mkdir -p ./staging_folder/usr/bin
cp ./linux-data/crafthub ./staging_folder/usr/bin/crafthub
chmod +x ./staging_folder/usr/bin/crafthub # set executable permissions to starter script
echo "Started copied"

# Other files
mkdir -p ./staging_folder/usr/share
mkdir -p ./staging_folder/usr/share/crafthub
cp -f -a ../CraftHub/bin/Release/net9.0/linux-x64/publish/. ./staging_folder/usr/share/crafthub/ # copies all files from publish dir
chmod -R a+rX ./staging_folder/usr/share/crafthub/ # set read permissions to all files
chmod a+x ./staging_folder/usr/share/crafthub/CraftHub # set executable permissions to main executable
echo "CraftHub copied"

# Desktop shortcut
mkdir -p ./staging_folder/usr/share/applications
cp ./linux-data/CraftHub.desktop ./staging_folder/usr/share/applications/CraftHub.desktop
echo "Shortcut copied"

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
cp ../build-resources/logo.svg ./staging_folder/usr/share/icons/hicolor/scalable/apps/crafthub.svg
echo "Another icon copied"

# Make .deb file
dpkg-deb --root-owner-group --build ./staging_folder/ ./crafthub_amd64.deb
echo ".deb created"

