# 批量新增与房型气泡：本地验证

日期：2026-10-05，macOS。仅本地预览，未 push / Release。

- 197 项核心集成检查通过；76 项实际 HTTP / 进程检查通过。
- Preview、Windows WPF Release 编译通过，0 warning / 0 error；没有在 Windows 实机运行，不代表 Windows UI 或性能验证。
- 浏览器 5190 独立演示库：新增常用“亲子房”；单间和批量模式点选气泡填入成功；删除打开密码保护并取消成功。
- 输入 101、401，提交显示已有房号且整批未添加；随后输入 401-403、405，统一 4 楼 / 亲子房 / 288 元，成功新增四间待清扫房，筛选 4 楼逐一核对。
- 自动检查覆盖非法范围、重复/重叠房号、200 间上限、无效楼层/价格/房型、并发重复提交、逐房操作日志、重启持久化、房型气泡删除鉴权、删除后已有房型不变。
- 原库与修改前在线备份比较：Rooms、BoardStates 的原有字段、GuestRegistrations、Activities、SecuritySettings、PlatformPresets 完全一致。
- JavaScript 语法检查和 git diff --check 通过。
