#!/bin/sh

set -e

VINTAGE_STORY=/opt/vintagestory dotnet build -c Release VinCord/
cp -v VinCord/bin/VinCord.zip ../data-discord/Mods/
