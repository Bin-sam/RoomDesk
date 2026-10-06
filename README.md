# 栖间 RoomDesk

面向单前台酒店的本地房态管理原型，使用 C#、WPF 和 SQLite。Windows 安装包在本仓库 [Releases](https://github.com/Bin-sam/RoomDesk/releases/tag/roomdesk-v0.8.2-preview.1) 中下载，推荐选择 `RoomDesk-Setup-0.8.2-win-x64.exe`。

- 房态总览、入住人显示、入住/退房/清扫/预订/维修/停用。
- 房间默认价和本次实际售价；预订人及平台、住客历史匹配、搜索、CSV 导出。
- 操作密码保护导出、删除和备份；账单起止时间精确到分钟。
- Windows 桌面程序；Mac 可使用共用 C# 逻辑的本机浏览器预览。

本版为 **0.8.2 预览版**，首次使用含 33 间默认客房（4、5、6 楼各 11 间），价格未设置。尚无完整收款、押金、退款、续住或换房流程，已移除读卡功能。

0.8.2 放大房态卡片文字、突出待清扫状态并提高颜色对比度；延续默认房间清单、软件内更新、房间清单快捷编辑、入住与预订资料编辑，以及验证后 5 分钟免重复输入密码。0.7 及更早版本需先手动安装本版一次，之后可在“数据与安全 → 软件更新”升级。项目使用 [MIT 许可证](LICENSE)。

## 使用与验证

- [操作说明与构建](RoomDesk/README.md)
- [Windows 安装说明](RoomDesk/installer/INSTALL-WINDOWS.txt)
- [单前台场景测试报告](RoomDesk/FRONT-DESK-TEST-REPORT.md)
- [发布说明](RoomDesk/installer/RELEASE-NOTES.md)
- [Windows 发布验证结果](RoomDesk/installer/RELEASE-VALIDATION.md)

发布流水线在 Windows Server 2022 上测试、编译、生成安装 EXE，并检查安装/启动/重装/卸载。Windows 10 的实际电脑、显示缩放、流畅度和硬件设备仍需实机验收。数据位于 `%LOCALAPPDATA%\RoomDeskPrototype\rooms-v1.db`，升级、卸载均保留数据。仓库不包含使用者的本地数据库。

## 项目结构

`RoomDesk/Core` 共享业务与 SQLite；`Windows` 中文 WPF；`Preview` Mac 本机预览；`Tests` 回归与前台场景；`installer` Windows 安装器。

基于 [Lantel](https://github.com/GideonLartey/hotel-management-system) C# 桌面模型，基线 `3550191b505817534f0980c115595968a5a1e468`。原模型文件和上游源码保留，原始说明见 [README-UPSTREAM.md](README-UPSTREAM.md)。保留上游 [MIT LICENSE](LICENSE)。
