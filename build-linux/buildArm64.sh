#!/bin/bash

# Version
buildVersion=$(<../build-resources/version.txt)

# Clean-up
rm -rf ./staging_folder_arm64/

# .NET publish
# self-contained is recommended, so final users won't need to install .NET
dotnet publish ../CraftHub/CraftHub.csproj --configuration Release --runtime linux-arm64 --self-contained -f net9.0
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
cp -f -a ../CraftHub/bin/Release/net9.0/linux-arm64/publish/. ./staging_folder_arm64/usr/share/crafthub/ # copies all files from publish dir
chmod -R a+rX ./staging_folder_arm64/usr/share/crafthub/ # set read permissions to all files
chmod a+x ./staging_folder_arm64/usr/share/crafthub/CraftHub # set executable permissions to main executable
echo "CraftHub copied"

# Desktop shortcut
mkdir -p ./staging_folder_arm64/usr/share/applications
cp ./linux-data-arm64/CraftHub.desktop ./staging_folder_arm64/usr/share/applications/CraftHub.desktop
echo "Shortcut copied"

# Stables
cp ../build-resources/computed_stables.txt ./staging_folder_arm64/usr/share/crafthub/computed_stables.txt
echo "Stables copied"

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
cp ../build-resources/logo.svg ./staging_folder_arm64/usr/share/icons/hicolor/scalable/apps/crafthub.svg
echo "Another icon copied"

# Make .deb file
dpkg-deb --root-owner-group --build ./staging_folder_arm64/ ./crafthub_arm64.deb
echo ".deb created"

# Moving files to volumes
mkdir -p /home/build
mkdir -p /home/build/instArm64
mkdir -p /home/build/fullArm64
yes | cp ./crafthub_arm64.deb /home/build/instArm64/crafthub_arm64.deb
yes | cp -f -a ./staging_folder_arm64/usr/share/crafthub/. /home/build/fullArm64/ # copies all files from publish dir
echo "All files copied to volumes"