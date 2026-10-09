#!/usr/bin/env sh
# Quality gate for the mobile app. Run from anywhere:  sh mobile/tool/check.sh
set -e
cd "$(dirname "$0")/.."
flutter pub get
flutter analyze
flutter test
echo "Quality gate passed."
