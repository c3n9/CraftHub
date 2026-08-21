#!/bin/bash
#
# Packages an already-built staging tree as an .rpm.
#
# Why this exists: the .deb is not a package on RPM distributions, it is a file they have to
# convert. Issue #21 came from RED OS, where there is no dpkg and no apt at all — for those users
# "download the .deb and run alien" is a workaround, not an installation. This produces the real
# thing from the exact same tree build.sh already assembled for the .deb, so the two artifacts
# cannot drift apart in content.
#
# Usage: ./make-rpm.sh <staging-dir> <rpm-arch> <output.rpm>
#   e.g. ./make-rpm.sh ./staging_folder x86_64 ./crafthub_x86_64.rpm

set -euo pipefail

stagingDir=$1
rpmArch=$2
outputFile=$3

buildVersion=$(<../build-resources/version.txt)
buildVersion=$(echo "$buildVersion" | tr -d '\n\r')

scriptDir=$(cd "$(dirname "$0")" && pwd)
stagingAbs=$(cd "$stagingDir" && pwd)

# rpmbuild refuses a buildroot it thinks it might own, and it will happily delete one; point it at
# a copy so a failed run cannot take the .deb's staging tree with it.
rpmRoot="$scriptDir/rpm_build"
rm -rf "$rpmRoot"
mkdir -p "$rpmRoot/BUILDROOT" "$rpmRoot/RPMS"
cp -a "$stagingAbs/." "$rpmRoot/BUILDROOT/"

# DEBIAN/ is the .deb's metadata directory. It has no business inside an rpm, and %files does not
# reference it — but leaving it in the buildroot makes rpmbuild warn about unpackaged files.
rm -rf "$rpmRoot/BUILDROOT/DEBIAN"

rpmbuild -bb "$scriptDir/linux-data/crafthub.spec" \
    --define "appversion $buildVersion" \
    --define "apparch $rpmArch" \
    --define "_topdir $rpmRoot" \
    --define "_rpmdir $rpmRoot/RPMS" \
    --buildroot "$rpmRoot/BUILDROOT" \
    --target "$rpmArch"

built=$(find "$rpmRoot/RPMS" -name '*.rpm' -type f | head -1)
if [ -z "$built" ]; then
    echo "make-rpm.sh: rpmbuild produced no package" >&2
    exit 1
fi

mv "$built" "$outputFile"
rm -rf "$rpmRoot"
echo "Built $outputFile"
