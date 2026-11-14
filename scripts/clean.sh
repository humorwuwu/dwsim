#!/usr/bin/env bash
# H2sim 清理脚本
# 默认删除：build/ dist/ DistPackages/ target/ .build/ .tools/、各工程 bin/ obj/、
#           __pycache__、.pytest_cache、.DS_Store、*sync-conflict*、*.user、*.suo
# 默认保留：release/、packages/、源码、docs/、PlatformFiles/、fixtures/、packaging/
# --deep 额外删除：packages/、release/ 中非最新版本的文件
# 绝不删除：.git/、.verysync/、PlatformFiles/、docs/、fixtures/、reference/、证书/密钥/数据库
set -euo pipefail
cd "$(dirname "$0")/.."

DRY_RUN=0
DEEP=0
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=1 ;;
    --deep) DEEP=1 ;;
    -h|--help) sed -n '2,7p' "$0"; exit 0 ;;
    *) echo "未知参数: $arg（支持 --dry-run / --deep）" >&2; exit 2 ;;
  esac
done

remove() {
  if [ ! -e "$1" ] && [ ! -L "$1" ]; then return 0; fi
  if [ "$DRY_RUN" -eq 1 ]; then
    echo "would remove: $1"
  else
    rm -rf -- "$1"
    echo "removed: $1"
  fi
}

# 1. 顶层构建目录
for d in build dist DistPackages target .build .tools; do
  remove "$d"
done

# 2. 各工程的 bin/ obj/（不碰 release/、packages/、PlatformFiles/）
while IFS= read -r -d '' d; do
  remove "$d"
done < <(find . -type d \( -name bin -o -name obj \) \
  -not -path './.git/*' -not -path './release/*' -not -path './packages/*' \
  -not -path './PlatformFiles/*' -print0)

# 3. 编辑器 / 同步 / 缓存垃圾
while IFS= read -r -d '' f; do
  remove "$f"
done < <(find . \( -name '.DS_Store' -o -name '*sync-conflict*' -o -name '__pycache__' \
     -o -name '.pytest_cache' -o -name '*.user' -o -name '*.suo' \) \
  -not -path './.git/*' -not -path './PlatformFiles/*' -print0)

# 4. --deep：还原缓存与旧发行版
if [ "$DEEP" -eq 1 ]; then
  remove packages
  if [ -d release ]; then
    while IFS= read -r dir; do
      latest=$(ls -1 "$dir" 2>/dev/null \
        | sed -nE 's/^[^-]+-([0-9]+(\.[0-9]+)+)-.*/\1/p' \
        | sort -V | tail -1)
      [ -n "$latest" ] || continue
      while IFS= read -r f; do
        v=$(basename "$f" | sed -nE 's/^[^-]+-([0-9]+(\.[0-9]+)+)-.*/\1/p')
        if [ -n "$v" ] && [ "$v" != "$latest" ]; then
          remove "$f"
        fi
      done < <(find "$dir" -maxdepth 1 -type f)
    done < <(find release -mindepth 1 -maxdepth 1 -type d)
  fi
fi