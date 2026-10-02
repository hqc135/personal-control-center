# 个人控制中心

一个放在 Windows 托盘里的轻量控制面板，集中管理音量、电源方案、临时保持唤醒、代理检测和常用入口。

## 下载与使用

**[下载 Windows x64 免安装 ZIP](https://github.com/hqc135/personal-control-center/releases/download/v0.6.1/PersonalControlCenter-0.6.1-win-x64.zip)** · [查看发布说明](https://github.com/hqc135/personal-control-center/releases/tag/v0.6.1)

1. 下载 ZIP，完整解压到你想放置的目录。
2. 双击 `PersonalControlCenter.exe`，即可使用。
3. 点击托盘图标打开或收起面板，右键托盘图标可退出。

支持 **Windows 11 x64（内部版本 22621 及以上）**。ZIP 已包含 .NET 桌面运行时，**无需另装 .NET、无需管理员权限**。不要只拷贝 EXE，也不要在压缩包里直接运行。

仓库目前为私有仓库，下载时请先登录有访问权限的 GitHub 账号。请下载上面的应用 ZIP；GitHub 自动生成的 `Source code` 是源码。

## 能做什么

- **声音**：调节当前输出音量、静音，查看输出设备。
- **电源**：切换 Windows 电源方案。
- **保持唤醒**：按时长临时保持电脑唤醒，可选择屏幕常亮。
- **代理**：查看本机代理端口状态，手动测试指定目标。
- **常用入口**：集中打开文件夹、网页和确认过的程序。
- **个性化**：浅色/深色、模块排序与隐藏、全局快捷键、可选登录自启。

自启默认关闭。电源方案切换会影响系统设置；默认音频输出切换目前通过系统声音设置完成。

## 更新与数据

更新前从托盘退出，解压新版替换程序文件。配置独立保存在 `%LOCALAPPDATA%/PersonalControlCenter`，不会因替换程序文件而被删除。移动程序目录前，如已启用自启，请先关闭自启，再在新位置重新启用。

## 文档

- [使用说明](docs/USER_GUIDE.md)：详细操作、备份迁移、更新卸载与常见问题。
- [维护说明](docs/MAINTENANCE.md)：源码结构、开发环境、构建测试和发布流程。
- [验收记录](docs/acceptance/UI-2026-10-02.md) · [发布检查](docs/RELEASE_CHECKLIST.md)

当前版本 **0.6.1 预发布版**。172 项自动测试和假数据界面验收通过；真实系统控制、性能、多屏/DPI 等完整矩阵仍待验收。程序未签名，当前用于个人试用。
