# 个人控制中心

2026-09-26 · **B4 第四轮候选 0.4.0**。

已实现 WPF 托盘、声音/静音、电源方案、临时保持唤醒、代理检测、快捷入口，以及配置迁移/恢复、模块排序与隐藏、可选全局快捷键、当前用户自启。**151 项非交互测试通过，Release 编译 0 警告/0 错误。真机与界面仍未验收。**

用户游戏期间只做代码检查和测试；本轮没有启动应用或窗口，没有修改真实音频、电源、自启或快捷键，没有实际申请唤醒或代理探测。

## 资料

- [第四轮验收](docs/acceptance/B4.md)、[人工清单](docs/MANUAL_CHECKLIST_B4.md)
- [配置与恢复](docs/CONFIGURATION_GUIDE.md)、[配置 schema](docs/config.schema.json)
- [音频兼容门](docs/AUDIO_COMPATIBILITY.md)
- [实施状态](docs/IMPLEMENTATION_STATUS.md)、[分批计划](docs/DELIVERY_PLAN.md)
- [设计](docs/PRODUCT_DESIGN.md)、[架构](docs/ARCHITECTURE.md)、[决策](docs/DECISIONS.md)

## 构建

项目内 .tools/dotnet SDK 10.0.401，不修改系统 PATH。依赖已恢复时：

```powershell
./scripts/build.ps1 -Offline -Publish
./scripts/package.ps1
```

构建采用低优先级、单节点，不启动应用。测试用假热键/注册表/唤醒/网络/音频/电源接口和临时目录。两个 tools 不随脚本执行；UiAcceptance 必须显式 --allow-ui 才弹出演示窗口。

## 运行与边界

方便时手动解压 artifacts/PersonalControlCenter-B4-CANDIDATE-framework-dependent.zip，运行 PersonalControlCenter.exe。需要 Windows 11 x64 build 22621+、.NET 10 Desktop Runtime x64；不是自包含包。--show / --tray；托盘右键退出，托盘失败时普通窗口可退出。

声音/静音和电源方案是真实控制；切方案可能影响亮度/睡眠。默认输出原生切换尚未通过兼容门，继续使用系统声音设置。

保持唤醒需手动开始，1–480 分钟，可选屏幕常亮；不恢复上次活动请求，不清除其他程序请求。本机代理可见时最多每 10 秒检测一次、500 ms 超时；隐藏取消。手动网站目标默认空，用户点击才通过显式 HTTP 代理测试，5 秒预算，无 Cookie/凭据/自动重定向/TLS 绕过。

设置支持模块排序/显示。导入与备份先进入草稿，保存才应用。便携迁移去掉本机/程序/网页入口、测试目标、快捷键和未知字段；程序信任始终本机独立确认。

快捷键默认关，用户填写保存后注册，冲突保留原键和托盘。自启必须点独立按钮，只操作当前用户的本程序登记；不随配置导入或普通保存启用。遇到不同位置的同名登记不盲目覆盖。

配置保存在 %LOCALAPPDATA%/PersonalControlCenter，最近 5 份备份；生命周期日志不含路径/网址/正文。退出后可删程序目录；启用过自启则先在原位置关闭。

按用户要求删除旧 B1–B3 六个 ZIP，保留历史验收记录。旧发布/暂存目录递归清理被自动审批拒绝，暂留。最新交付为 B4；第五轮未开始。

未签名自用候选，许可证待确认。未提交/推送 Git，未公开发布，不捆绑个人配置、字体或 SDK。
