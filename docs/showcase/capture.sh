#!/usr/bin/env bash
set -euo pipefail

fail() {
    echo "capture: $*" >&2
    exit 1
}

for tool in dotnet playwright magick fc-match fc-query awk; do
    command -v "$tool" >/dev/null || fail "required tool '$tool' was not found"
done

[[ $(playwright --version) == "Version 1.59.0" ]] ||
    fail "Playwright 1.59.0 is required; install it with 'uv tool install playwright==1.59.0'"

magick_version=$(magick --version)
[[ ${magick_version%%$'\n'*} == "Version: ImageMagick 7.1.2-32 "* ]] ||
    fail "ImageMagick 7.1.2-32 is required"

for font in "DejaVu Sans" "DejaVu Sans Mono"; do
    font_path=$(fc-match --format='%{file}' "$font" 2>/dev/null)
    [[ -n $font_path ]] || fail "$font was not found"
    font_version=$(fc-query --format='%{fontversion}' "$font_path")
    [[ $font_version == "155320" ]] || fail "$font 2.37 is required"
done

browser_plan=$(playwright install --dry-run chromium)
[[ $browser_plan == *"Chrome for Testing 147.0.7727.15 (playwright chromium v1217)"* ]] ||
    fail "Playwright Chromium v1217 is required; run 'playwright install chromium'"
headless_dir=$(awk '/Chrome Headless Shell 147[.]0[.]7727[.]15/{found=1; next} found && /Install location:/{sub(/^[^:]+:[[:space:]]*/, ""); print; exit}' <<<"$browser_plan")
[[ -x $headless_dir/chrome-headless-shell-linux64/chrome-headless-shell ]] ||
    fail "Playwright Chromium v1217 is not installed; run 'playwright install chromium'"

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
output_dir="$repo_root/docs/showcase/output"
asset_dir="$repo_root/docs/assets"
capture_dir=$(mktemp -d)
trap 'rm -rf "$capture_dir"' EXIT

mask="$capture_dir/rounded-mask.png"
magick -size 1164x639 xc:black -fill white \
    -draw 'roundrectangle 0,0 1163,638 24,24' \
    "$mask"

cd "$repo_root"
dotnet run --project docs/showcase --no-restore

capture() {
    local page=$1
    local asset=$2
    local raw="$capture_dir/$asset"
    local clipped="$capture_dir/clipped-$asset"

    playwright screenshot \
        --viewport-size="1164,639" \
        --color-scheme=dark \
        "file://$output_dir/$page" \
        "$raw"

    magick composite -compose CopyOpacity "$mask" "$raw" "$clipped"
    magick "$clipped" -bordercolor none -border 18 "$asset_dir/$asset"
}

capture box-drawing.html showcase-box-drawing.png
capture image-drawing.html showcase-image-drawing.png
