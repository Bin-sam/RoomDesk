栖间 RoomDesk 首个 Windows 安装预览版。

## 下载

- **RoomDesk-Setup-0.6.0-win-x64.exe**：安装版，包含运行环境、快捷方式、卸载入口。推荐下载此文件。
- RoomDesk-Portable-0.6.0-win-x64.zip：免安装版，解压后运行 RoomDesk.exe。
- SHA256SUMS.txt：下载文件校验值。

目标 Windows 10 22H2 / Windows 11 x64，中文程序界面，安装向导为英文。无需另装 .NET。程序未签名。

## 功能

房态总览与入住人显示；入住/退房/清扫；独立房间管理和默认价；实际售出总价；历史搜索及 CSV；操作密码保护导出、备份、删除；精确到分钟的时段账单；历史住客匹配与通用键盘读卡文本入口。

数据保存在 `%LOCALAPPDATA%\RoomDeskPrototype\rooms-v1.db`，升级和卸载保留数据。首次运行含示例房间；没有预设操作密码。

## 验证与限制

发布工作流在 Windows Server 2022 上执行共享核心、HTTP、34 个前台业务场景，并验证安装、WPF 窗口启动、重装及卸载的数据保留。详情见对应 GitHub Actions 日志。

这不等同 Windows 10 目标电脑的人工 UI、缩放或流畅度验收。实体读卡器仍需型号与 SDK 联调；当前只有文本接收。账单按每次入住售出总价汇总，不是实收账；收款、押金、退款、续住、换房尚未实现。

基于 Lantel C# 桌面模型，保留上游 MIT 许可证。
