#!/bin/bash
# Build script for hello-plugin WASM

set -e

echo "Building hello-plugin for WebAssembly..."

# Ensure the wasm target is installed
rustup target add wasm32-unknown-unknown 2>/dev/null || true

# Build in release mode
cargo build --target wasm32-unknown-unknown --release

# Output location
WASM_FILE="target/wasm32-unknown-unknown/release/hello_plugin.wasm"

if [ -f "$WASM_FILE" ]; then
    SIZE=$(du -h "$WASM_FILE" | cut -f1)
    echo ""
    echo "Build successful!"
    echo "Output: $WASM_FILE"
    echo "Size: $SIZE"
    echo ""
    echo "To use with Oluso:"
    echo "  1. Copy to your plugins directory: cp $WASM_FILE /path/to/plugins/"
    echo "  2. Or upload via Admin API: POST /api/admin/plugins with the .wasm file"
else
    echo "Build failed - WASM file not found"
    exit 1
fi
