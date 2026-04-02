#!/bin/bash
# ErsatzTV Build & Publish Script
# Usage: 
#   ./build.sh           - 仅构建 (debug)
#   ./build.sh release   - 仅构建 (release)  
#   ./build.sh publish   - 构建并发布 (release)
#   ./build.sh publish debug - 构建并发布 (debug)

set -e

MODE="${1:-debug}"
ACTION="${2:-build}"

PARALLELISM="-p:MaxParallelism=1"

build() {
    local cfg="$1"
    echo "[$cfg] Building..."
    
    dotnet build ErsatzTV/ErsatzTV.csproj -c "$cfg" $PARALLELISM -v q
}

publish() {
    local cfg="$1"
    local out="publish/$cfg"
    
    echo "Publishing to $out..."
    
    # 清理旧文件
    rm -rf "$out"
    
    dotnet publish ErsatzTV/ErsatzTV.csproj \
        -c "$cfg" \
        -o "$out" \
        -p:PublishSingleFile=true \
        -p:SelfContained=true \
        -p:MaxParallelism=2 \
        -v q
    
    echo ""
    echo "=========================================="
    echo "Output: $out"
    echo "=========================================="
    ls -la "$out" | head -20
}

case "$ACTION" in
    build)
        build "$MODE"
        ;;
    publish)
        publish "$MODE"
        ;;
    *)
        echo "Usage: $0 [debug|release] [build|publish]"
        ;;
esac
