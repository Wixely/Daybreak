#!/bin/sh
set -eu

mkdir -p /data
chown -R "$APP_UID:$APP_UID" /data

exec setpriv --reuid="$APP_UID" --regid="$APP_UID" --clear-groups "$@"
