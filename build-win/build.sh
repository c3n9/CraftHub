#!/bin/bash

# Version
buildVersion=$(<../build-resources/version.txt)

# Clean-up
rm -rf ./staging_folder/

# .NET publish
# self-contained is recommended, so final users won't need to install .NET
dotnet publish ../CraftHub/CraftHub.csproj --configuration Release --runtime win-x64 --self-contained -f net9.0
echo "Published"

# Staging directory
mkdir staging_folder

# Other files
cp -f -a ../CraftHub/bin/Release/net9.0/win-x64/publish/. ./staging_folder/ # copies all files from publish dir
echo "CraftHub copied"

# Make .exe file
makensis -V1 -DVERSION=$buildVersion ./nsis-setupper.nsi
echo ".exe created"

