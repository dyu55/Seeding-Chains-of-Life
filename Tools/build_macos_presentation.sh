#!/bin/zsh
set -euo pipefail
UNITY="/Applications/Unity/Hub/Editor/6000.3.5f1/Unity.app/Contents/MacOS/Unity"
PROJECT="/Users/donglingyu/My project"
LOG="$PROJECT/Builds/macos_build.log"
mkdir -p "$PROJECT/Builds"
rm -rf "$PROJECT/Builds/macOS/ChainsOfLife.app"
"$UNITY" -batchmode -quit -projectPath "$PROJECT" -executeMethod CodexBuild.BuildMacPresentation -logFile "$LOG"
echo "Build finished. Output: $PROJECT/Builds/macOS/ChainsOfLife.app"
echo "Log: $LOG"
