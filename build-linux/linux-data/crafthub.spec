# RPM spec for CraftHub.
#
# Built from the same staging tree the .deb is built from — see make-rpm.sh. There is no compiling
# here: .NET has already published a self-contained tree and this only packages it, which is why
# every one of the globals below switches off a step rpmbuild would normally perform on something
# it compiled itself.
#
# Version, architecture and the staging tree come in with --define, so one spec serves x86_64 and
# aarch64.

# Do not post-process the payload. The default install post pass strips ELF binaries, and stripping
# a self-contained .NET apphost corrupts it — the single-file bundle is appended to the executable
# and the strip discards it.
%global __os_install_post %{nil}
%global __strip /bin/true
%global debug_package %{nil}
%global _build_id_links none

# Pin the payload compressor rather than inheriting the build machine's default. Left alone, a
# modern rpmbuild writes a zstd payload and stamps the package "Requires: rpmlib(PayloadIsZstd)",
# which an older rpm refuses to install — which is issue #21 all over again, just wearing a
# different extension. Verified: without this the built package really does carry that Requires.
# xz has been understood by rpm since 4.8 (2010) and compresses this payload well.
%define _binary_payload w7.xzdio

Name:           crafthub
Version:        %{appversion}
Release:        1
Summary:        JSON editor with a table view, formulas and class generation
# Localised metadata. rpm keeps one Summary/description per locale and hands the caller the one
# matching its LC_MESSAGES, falling back to the untagged English above — so `dnf info crafthub`
# under a Russian locale reads in Russian. The locale tag matches the one the .desktop file uses.
Summary(ru):    Редактор JSON с табличным представлением, формулами и генерацией классов
License:        MIT
URL:            https://github.com/c3n9/CraftHub
# Architecture comes from rpmbuild --target, not from BuildArch here: the x86_64 runner also builds
# the aarch64 package, and --target is the switch that cross-builds. BuildArch would fight it.

# Off deliberately. The payload is a self-contained .NET publish carrying ~200 of its own shared
# objects; automatic scanning would emit a Provides for every one of them (polluting the system's
# name space with names like libcoreclr.so) and a matching pile of Requires. The runtime
# dependencies that actually matter are the handful of system libraries listed below.
AutoReqProv:    no

# Named for the RHEL family, which is where this was asked for (RED OS). Deliberately short: each
# name is one that can fail to resolve on a distribution that spells it differently, and a package
# that will not install is worse than one that installs and reports a missing library at startup.
Requires:       glibc
Requires:       libstdc++
Requires:       zlib
Requires:       fontconfig
Requires:       libicu

%description
CraftHub edits JSON as a typed table: define columns, edit rows in a data grid or as raw JSON,
compute cells with spreadsheet-style formulas, compare documents, and import or export C# classes.

%description -l ru
CraftHub редактирует JSON как типизированную таблицу: задайте колонки, правьте строки в таблице
или как сырой JSON, вычисляйте ячейки формулами в стиле электронных таблиц, сравнивайте документы,
импортируйте и экспортируйте классы C#.

%install
# The payload is copied here rather than into the buildroot beforehand, and that is not a style
# choice. rpmbuild runs its own install prologue before this section, and that prologue begins by
# deleting the buildroot — so a buildroot populated by the caller is gone before the file list is
# read, and every line of it then fails with "File not found". A spec with no install section at
# all dodges the prologue on rpm 6 but not on rpm 4.x, which is how this built by hand on a
# machine with rpm 6 and produced nothing on the ubuntu-24.04 runner, where rpm is 4.18.
# Populating the buildroot from in here happens after the prologue on both.
#
# Careful with comments in this section: it is a shell script and rpm expands macros in it first,
# so a percent-brace reference inside a comment lands in the script as multiple lines and only the
# first stays commented out.
rm -rf %{buildroot}
mkdir -p %{buildroot}
cp -a %{stagingdir}/. %{buildroot}/

# DEBIAN/ is the .deb's metadata directory. It has no business inside an rpm, and %files does not
# reference it — leaving it in the buildroot would make rpmbuild fail the unpackaged-files check.
rm -rf %{buildroot}/DEBIAN

%files
/usr/bin/crafthub
/usr/share/crafthub
/usr/share/applications/CraftHub.desktop
/usr/share/pixmaps/crafthub.png
/usr/share/icons/hicolor/scalable/apps/crafthub.svg

%changelog
