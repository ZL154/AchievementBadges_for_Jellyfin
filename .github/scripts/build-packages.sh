#!/usr/bin/env bash
# [issue #143] The release's build and packaging, in one place so CI runs
# exactly what a release runs. CI used to stop at dotnet build, so the
# release's own publish was first exercised by a release: a change there
# could pass every check and break only then, as #142 would have.
#
# Run from anywhere in the repository. Writes two packages to dist/:
#   Jellyfin.Plugin.AchievementBadges_X.Y.Z.0.zip, net9.0 build, Jellyfin 10.11
#   Jellyfin.Plugin.AchievementBadges_X.Y.Z.1.zip, net10.0 build, Jellyfin 12
# each with a .md5 and a .sha256 beside it. Under GitHub Actions it also
# sets the step outputs the release reads: version, tag, zipver, zipver12,
# zipname, zipname12, zippath and zippath12.
#
# Needs the .NET 10 SDK, zip, and pwsh, which reads the version each DLL
# was stamped with.
set -euo pipefail

cd "$(dirname "$0")/../.."

CSPROJ=Jellyfin.Plugin.AchievementBadges/Jellyfin.Plugin.AchievementBadges.csproj
OUTPUTS="${GITHUB_OUTPUT:-/dev/null}"

VERSION="$(grep -oPm1 '(?<=<Version>)[^<]+' "$CSPROJ" || true)"
# [issue #143] The csproj builds X.Y.Z.0 and X.Y.Z.1 out of a three-part
# Version and refuses a fourth part. A release takes no prerelease suffix
# either: the zip names and the manifest carry plain four-part numbers, as
# releases always have (1.9.6.0).
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "::error::A release needs Version X.Y.Z in the csproj, found: $VERSION"
  exit 1
fi
ZIPVER="${VERSION}.0"
# [issue #109] Two zips per release, one per Jellyfin line. The 4th segment
# tells them apart: .0 is the net9.0 build for 10.11, .1 is the net10.0
# build for 12. Jellyfin's catalog filters by targetAbi and then takes the
# highest version number, so a 12 server lands on .1 and a 10.11 server can
# only ever see .0.
ZIPVER12="${ZIPVER%.*}.1"
ZIPNAME="Jellyfin.Plugin.AchievementBadges_${ZIPVER}.zip"
ZIPNAME12="Jellyfin.Plugin.AchievementBadges_${ZIPVER12}.zip"

if ! command -v pwsh >/dev/null; then
  echo "::error::pwsh is needed to read the version stamped into each DLL"
  exit 1
fi

# Only what this script writes, so a rerun starts clean: zip -r adds to an
# archive that is already there instead of replacing it.
rm -rf dist/build-net9.0 dist/build-net10.0 dist/net9.0 dist/net10.0 \
  "dist/$ZIPNAME" "dist/$ZIPNAME.md5" "dist/$ZIPNAME.sha256" \
  "dist/$ZIPNAME12" "dist/$ZIPNAME12.md5" "dist/$ZIPNAME12.sha256"

dotnet restore "$CSPROJ" --locked-mode

for TFM in net9.0 net10.0; do
  case "$TFM" in
    net9.0) PKGVER="$ZIPVER" ;;
    net10.0) PKGVER="$ZIPVER12" ;;
  esac

  # [issue #109] The csproj multi-targets net9.0 and net10.0, and dotnet
  # publish refuses a multi-target project without an explicit framework
  # (NETSDK1129). One publish per line, into separate folders so the two
  # DLLs never overwrite each other.
  dotnet publish "$CSPROJ" \
    --configuration Release \
    --framework "$TFM" \
    --no-restore \
    --output "dist/build-$TFM"

  # [issue #140] Jellyfin shows the version stamped into the DLL but finds
  # the plugin by the version of its package, so a DLL stamped with any
  # other number than its zip cannot be uninstalled from the dashboard.
  DLL="dist/build-$TFM/Jellyfin.Plugin.AchievementBadges.dll"
  # The path goes through the environment rather than into the command
  # text, and $env:DLL_PATH is PowerShell's to expand, not the shell's.
  # shellcheck disable=SC2016
  STAMPED="$(DLL_PATH="$PWD/$DLL" pwsh -NoLogo -NoProfile -NonInteractive -Command \
    '[System.Reflection.AssemblyName]::GetAssemblyName($env:DLL_PATH).Version.ToString()')"
  if [ "$STAMPED" != "$PKGVER" ]; then
    echo "::error::$DLL is stamped $STAMPED, but its zip is published as $PKGVER"
    exit 1
  fi
  echo "$DLL is stamped $STAMPED, the version of its zip"

  # Copy only the plugin DLL: Jellyfin.Controller / Jellyfin.Model are
  # ExcludeAssets=runtime in the csproj, so they're not emitted. Anything
  # else in dist/build-* is either a transitive ref of the SDK base or the
  # app host (unused by plugins). Both zips carry the same wrapper folder
  # name, so a manual install flattens the same way.
  mkdir -p "dist/$TFM/Jellyfin.Plugin.AchievementBadges"
  cp "$DLL" "dist/$TFM/Jellyfin.Plugin.AchievementBadges/"
done

(cd dist/net9.0 && zip -r "../$ZIPNAME" "Jellyfin.Plugin.AchievementBadges")
(cd dist/net10.0 && zip -r "../$ZIPNAME12" "Jellyfin.Plugin.AchievementBadges")
(
  cd dist
  for Z in "$ZIPNAME" "$ZIPNAME12"; do
    md5sum "$Z" | awk '{print toupper($1)}' > "${Z}.md5"
    sha256sum "$Z" > "${Z}.sha256"
  done
)

{
  echo "version=$VERSION"
  echo "tag=v${VERSION}"
  echo "zipver=$ZIPVER"
  echo "zipver12=$ZIPVER12"
  echo "zipname=$ZIPNAME"
  echo "zipname12=$ZIPNAME12"
  echo "zippath=dist/$ZIPNAME"
  echo "zippath12=dist/$ZIPNAME12"
} >> "$OUTPUTS"

echo "Packaged dist/$ZIPNAME and dist/$ZIPNAME12"
