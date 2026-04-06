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

# Stables
cp ../build-resources/computed_stables.txt ./staging_folder32/computed_stables.txt
echo "Stables copied"

# Make .exe file
makensis -V1 -DVERSION=$buildVersion ./nsis-setupper32.nsi
echo ".exe created"

# Moving files to volumes
mkdir -p /home/build
mkdir -p /home/build/inst32
mkdir -p /home/build/full32
yes | cp ./crafthub_x86.exe /home/build/inst32/crafthub_x86.exe
yes | cp -f -a ./staging_folder32/. /home/build/full32/ # copies all files from publish dir
echo "All files copied to volumes"
