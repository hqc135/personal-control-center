# 个人控制中心

2026-09-26 · **B5 第五轮候选 0.5.0**。当前为自用、框架依赖候选，**不是最终验收通过的正式版**。

声音/静音、电源、临时唤醒、代理检测、快捷入口、配置迁移恢复、模块设置、快捷键及用户自启已实现。默认音频输出切换仍降级到系统声音设置。

本轮 160 项非交互测试通过，Release 编译无警告/错误；修复唤醒线程异常导致命令悬挂，补显示/DPI 变化时面板重新定位、退出日志关联编号/耗时，并减少字体枚举。没有启动窗口、执行宿主控制或真实性能压测。

## 交付与记录

- [B5 验收与限制](docs/acceptance/B5.md)
- [发布检查及待验收矩阵](docs/RELEASE_CHECKLIST.md)
- [构建与 Git 回退](docs/RELEASE_GUIDE.md)
- [配置说明](docs/CONFIGURATION_GUIDE.md)、[音频兼容门](docs/AUDIO_COMPATIBILITY.md)
- [进度](docs/IMPLEMENTATION_STATUS.md)、[迭代计划](docs/DELIVERY_PLAN.md)

Git 分支 codex/b5-release，B4 基线已提交；B5 源码、构建收据和包内 RELEASE.json 关联同一提交。不推送远端，SDK、依赖缓存、构建和个人运行数据不进入 Git。

```powershell
# 项目内 SDK 10.0.401，低优先级、单节点、不开窗口
./scripts/build.ps1 -Offline -Publish
./scripts/dependency-inventory.ps1
# 需源码已提交、工作区干净，并由当前提交构建
./scripts/package.ps1
./scripts/verify-package.ps1
```

本轮交付 artifacts/PersonalControlCenter-B5-CANDIDATE-framework-dependent.zip 与 B5-source.zip。运行需要 Windows 11 x64 build 22621+ 和 .NET 10 Windows Desktop Runtime。--show / --tray；托盘右键退出，托盘失败时普通窗口提供退出。

自包含包所需 NuGet 下载连续 TLS 握手失败，未绕过证书校验；本轮**未生成自包含包**。后续网络恢复可显式运行 restore-runtime.ps1，再构建/打包 self-contained 模式；运行时仅放项目，不安装系统。

真实 DPI/多屏、睡眠恢复、驱动、热键、自启、CPU/内存/句柄预算均待验收；500 次假状态循环不等于真实 UI 性能测试。项目许可证未决定，在线漏洞审计未完成，不公开发布。

旧包不作版本历史使用，以 Git 源码提交回退；历史证据保留。B1–B3 ZIP 已删除，旧发布/暂存目录此前清理被自动审批拒绝，暂留。
