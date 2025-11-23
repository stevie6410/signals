#!/usr/bin/env bash
set -euo pipefail

cd /home/signals

echo "[deploy] Pulling latest changes..."
git fetch origin
git reset --hard origin/main

echo "[deploy] Pulling latest images..."
docker compose pull

echo "[deploy] Recreating containers..."
docker compose up -d --remove-orphans

echo "[deploy] Done."
