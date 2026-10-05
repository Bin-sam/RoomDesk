# Windows 发布验证 — 2026-10-05

Release：[roomdesk-v0.6.0-preview.2](https://github.com/Bin-sam/RoomDesk/releases/tag/roomdesk-v0.6.0-preview.2)

源码提交：`014229a`。完整工作流：[GitHub Actions 37286931695](https://github.com/Bin-sam/RoomDesk/actions/runs/37286931695)，结果 success。

验证环境为 GitHub 托管 Windows Server 2022 x64（系统版本 10.0.20348），不是 Windows 10 用户电脑。

- 116 项共享 C# / SQLite 检查通过。
- 47 项真实 HTTP / 进程检查通过。
- 34 个单前台业务场景通过。
- 自包含 WPF EXE 发布、Inno Setup 安装器编译通过。
- 安装器静默安装成功；安装后的 EXE 打开 WPF 窗口，完成 24 间示例房初始化。
- 重装后原数据文件 SHA-256 保持一致。
- 卸载移除程序，原数据文件保留且 SHA-256 保持一致。
- 安装 EXE、Portable ZIP、SHA256SUMS.txt 已作为非草稿预览版附件发布。
- Mac 下载两个发布附件并与校验文件核对，一致；Portable ZIP 含程序与许可证，不含数据库。

安装版 `RoomDesk-Setup-0.6.0-win-x64.exe`：51,380,485 字节；SHA-256：
`22f02acdf126ec45f01b491d5c9a83ef60b9887c55d0e63178e62ea47b2f6f34`

免安装版 `RoomDesk-Portable-0.6.0-win-x64.zip`：70,703,326 字节；SHA-256：
`9dfbe263a2957131eb541073409a4e5fa3ef49d6b6ebbd76e61b65c5b56fcaf6`

限制：上述启动测试只核对窗口和数据库初始化，未代替人工检查每个 WPF 窗口。Windows 10 目标机的显示缩放、性能、交互、实体读卡器及安全软件提示仍需实机验收。安装包未做代码签名。

首轮 preview.1 工作流在检查 Windows 文件句柄处理时主动取消，无 Release 产物；发布使用修复后的 preview.2，没有覆写标签。
