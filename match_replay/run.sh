#!/bin/sh
set -eu
replay_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
binary="$replay_dir/builds/linux/TCRReplay.x86_64"
if [ -x "$replay_dir/TCRReplay.x86_64" ]; then binary="$replay_dir/TCRReplay.x86_64"; fi
if [ ! -x "$binary" ]; then
  echo 'Build with Unity: TCR Replay → Build Linux, or download the full demo release directory.' >&2
  exit 1
fi
if command -v nvidia-smi >/dev/null 2>&1 && nvidia-smi -L >/dev/null 2>&1; then
  export __NV_PRIME_RENDER_OFFLOAD=1 __GLX_VENDOR_LIBRARY_NAME=nvidia __VK_LAYER_NV_optimus=NVIDIA_only
fi
exec "$binary" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 "$@"
