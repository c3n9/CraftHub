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

# rpmbuild owns its buildroot: it wipes it before %install on rpm 4.x. So it is pointed at a
# scratch directory of ours and the spec's %install fills it from the staging tree — copying into
# the buildroot from here instead would just be deleted again, which is exactly how this silently
# produced no package. It also means a failed run cannot take the .deb's staging tree with it.
rpmRoot="$scriptDir/rpm_build"
rm -rf "$rpmRoot"
mkdir -p "$rpmRoot/BUILDROOT" "$rpmRoot/RPMS"

rpmbuild -bb "$scriptDir/linux-data/crafthub.spec" \
    --define "appversion $buildVersion" \
    --define "apparch $rpmArch" \
    --define "stagingdir $stagingAbs" \
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
