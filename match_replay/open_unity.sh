#!/bin/sh
set -eu
replay_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ -z "${UNITY_EDITOR:-}" ] || [ ! -x "$UNITY_EDITOR" ]; then
  echo 'Set UNITY_EDITOR to the Unity 6000.3.25f1 Editor executable, or add match_replay/unity using Unity Hub.' >&2
  exit 1
fi
exec "$UNITY_EDITOR" -projectPath "$replay_dir/unity"
