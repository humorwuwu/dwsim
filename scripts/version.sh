#!/usr/bin/env bash
# 用法：bash scripts/version.sh major|minor|patch
# 改 VERSION，同步 AssemblyInfo 的 AssemblyVersion，并在 CHANGELOG.md 插入新节。
set -euo pipefail
cd "$(dirname "$0")/.."

LEVEL="${1:-}"
case "$LEVEL" in
  major|minor|patch) ;;
  *) echo "用法: bash scripts/version.sh major|minor|patch" >&2; exit 2 ;;
esac

OLD=$(tr -d '[:space:]' < VERSION)
IFS=. read -r MAJOR MINOR PATCH <<< "$OLD"
case "$LEVEL" in
  major) MAJOR=$((MAJOR + 1)); MINOR=0; PATCH=0 ;;
  minor) MINOR=$((MINOR + 1)); PATCH=0 ;;
  patch) PATCH=$((PATCH + 1)) ;;
esac
NEW="$MAJOR.$MINOR.$PATCH"
DATE=$(date +%Y-%m-%d)

# 同步 H2sim 自己的程序集。首次运行把上游基线 9.0.5.0 一并改写。
find . \( -name 'AssemblyInfo.vb' -o -name 'AssemblyInfo.cs' \) \
  -not -path './.git/*' -print0 |
while IFS= read -r -d '' f; do
  if grep -qE "Assembly(File)?Version\(\"($OLD\.0|9\.0\.5\.0)\"\)" "$f"; then
    perl -0pi -e "s/AssemblyVersion\(\"(?:\Q$OLD\E\.0|9\.0\.5\.0)\"\)/AssemblyVersion(\"$NEW.0\")/g; s/AssemblyFileVersion\(\"(?:\Q$OLD\E\.0|9\.0\.5\.0)\"\)/AssemblyFileVersion(\"$NEW.0\")/g" "$f"
  fi
done

printf '%s\n' "$NEW" > VERSION

awk -v ver="$NEW" -v date="$DATE" '
  !done && /^## \[/ {
    print "## [" ver "] - " date
    print ""
    print "### Added"
    print ""
    print "- "
    print ""
    done = 1
  }
  { print }
' CHANGELOG.md > CHANGELOG.md.tmp
mv CHANGELOG.md.tmp CHANGELOG.md

echo "VERSION: $OLD -> $NEW"