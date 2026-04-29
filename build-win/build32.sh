#!/bin/bash

# Version
buildVersion=$(<../build-resources/version.txt)

# Clean-up
rm -rf ./staging_folder32/

# .NET publish
# self-contained is recommended, so final users won't need to install .NET
dotnet publish ../CraftHub/CraftHub.csproj --configuration Release --runtime win-x86 --self-contained -f net9.0
echo "Published"

# Staging directory
mkdir -p staging_folder32

# Other files
cp -f -a ../CraftHub/bin/Release/net9.0/win-x86/publish/. ./staging_folder32/ # copies all files from publish dir
echo "CraftHub copied"

# Make .exe file
makensis -V1 -DVERSION=$buildVersion ./nsis-setupper32.nsi
echo ".exe created"

