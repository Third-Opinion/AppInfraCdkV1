#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
java -jar "$SCRIPT_DIR/synthea.jar" "$@"
