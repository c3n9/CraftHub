#!/bin/bash

# Version
buildVersion=$(<../build-resources/version.txt)

# Clean-up
rm -rf ./staging_folder_arm64/

# .NET publish
# self-contained is recommended, so final users won't need to install .NET
dotnet publish ../CraftHub/CraftHub.csproj --configuration Release --runtime osx-arm64 --self-contained -f net9.0
echo "Published"

# Staging directory
mkdir -p staging_folder_arm64

# App folder
mkdir -p ./staging_folder_arm64/CraftHub.app
mkdir -p ./staging_folder_arm64/CraftHub.app/Contents
cp ./mac-data-arm64/Info.plist ./staging_folder_arm64/CraftHub.app/Contents/Info.plist
sed -i "s/currentVersionIsPlacedHere/${buildVersion}/g" ./staging_folder_arm64/CraftHub.app/Contents/Info.plist
echo "Plist copied"

# Other files
mkdir -p ./staging_folder_arm64/CraftHub.app/Contents/MacOS
cp -f -aL ../CraftHub/bin/Release/net9.0/osx-arm64/publish/. ./staging_folder_arm64/CraftHub.app/Contents/MacOS/ # copies all files from publish dir
chmod -R a+rX ./staging_folder_arm64/CraftHub.app/Contents/MacOS/ # set read permissions to all files
chmod a+x ./staging_folder_arm64/CraftHub.app/Contents/MacOS/CraftHub # set executable permissions to main executable
echo "CraftHub copied"

# Desktop icon
mkdir -p ./staging_folder_arm64/CraftHub.app/Contents/Resources
cp ../build-resources/logo.icns ./staging_folder_arm64/CraftHub.app/Contents/Resources/CraftHubIcon.icns
echo "Icon copied"

# Signing
mkdir -p staging_folder_arm64/CraftHub.app/Contents/_CodeSignature
echo "Signing done"

echo "Signing application..."
chmod a+x ./staging_folder_arm64/CraftHub.app/Contents/MacOS/CraftHub
codesign --force --deep --sign - ./staging_folder_arm64/CraftHub.app
echo "Signing done"
echo "Checking:"
codesign --verify --verbose ./staging_folder_arm64/CraftHub.app || true
# Install create-dmg
echo "Installing create-dmg..."
brew install create-dmg

# Install create-dmg
cd staging_folder_arm64
echo "Creating DMG..."
mkdir -p dmg_temp
cp -R CraftHub.app dmg_temp/

# Create dmg
ln -s /Applications dmg_temp/Applications
VOLICON="--volicon ./CraftHub.app/Contents/Resources/CraftHubIcon.icns"
create-dmg \
--volname "CraftHub Installer" \
$VOLICON \
--window-pos 200 120 \
--window-size 500 300 \
--icon-size 100 \
--icon "CraftHub.app" 130 110 \
--hide-extension "CraftHub.app" \
--app-drop-link 360 110 \
"CraftHub_arm64.dmg" \
"dmg_temp/"

echo "DMG created"
cd ..

# Moving files to volumes
mkdir -p /home/build
mkdir -p /home/build/inst
mkdir -p /home/build/full
yes | cp ./staging_folder_arm64/CraftHub_arm64.dmg /home/build/inst/crafthub_arm64.dmg
yes | cp -f -a ./staging_folder_arm64/CraftHub.app/Contents/MacOS/. /home/build/full/
echo "All files copied to volumes"
