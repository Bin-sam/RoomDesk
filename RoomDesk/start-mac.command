#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if command -v dotnet >/dev/null 2>&1; then
  roomdesk_dotnet="$(command -v dotnet)"
elif [ -x "../../../work/dotnet/dotnet" ]; then
  roomdesk_dotnet="$(cd ../../../work/dotnet && pwd)/dotnet"
else
  echo "请先安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0"
  read -r -p "按回车退出…"
  exit 1
fi
echo "Mac 功能预览：http://127.0.0.1:5188"
echo "此页面验证共享逻辑，不代表 Windows WPF 的渲染效果。"
echo "保持此终端打开；按 Control-C 停止。"
exec "$roomdesk_dotnet" run --project Preview/Preview.csproj -c Release -- --data "$PWD/artifacts/mac-preview/rooms.db"
