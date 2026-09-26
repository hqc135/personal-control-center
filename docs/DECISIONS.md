# 实施决策记录

设计阶段 ADR-001–006 见 ARCHITECTURE.md。

| ID | 本轮决策 | 原因与影响 |
|---|---|---|
| ADR-007 | SDK 10.0.401 放 .tools/dotnet | 用户已明确同意；官方 SHA512 核对一致；不改 PATH/系统 SDK |
| ADR-008 | B1 演示配置独立放 LocalAppData/PersonalControlCenter.Demo | 不污染未来真实模式配置；不持久化演示音量/电源/唤醒 |
| ADR-009 | 不引入 MVVM/托盘第三方运行依赖 | 小型手写命令/通知与 Shell_NotifyIcon 足够当前范围；xUnit 仅测试使用 |
| ADR-010 | 当前 net10.0-windows，入口显式检查 Windows build >=22621 | B1 不引用 Windows SDK 投影包；P/Invoke 直接封装。没有承诺支持 Windows 7；后续需要 SDK 投影时统一升级 TFM |
| ADR-011 | B1 先交 framework-dependent 演示 ZIP | 自包含运行时 NuGet 下载停滞，已停止；本机已有 10.0.10 Desktop Runtime，满足包请求的 10.0.0 及补丁滚动规则。不是自包含包；M5 自包含目标保留 |
| ADR-012 | 用户游戏期间不运行 UI 或宿主控制验收 | 用户明确要求仅代码检查/测试；后续修改只编译和非交互测试，最终界面验收挂起 |
| ADR-013 | 按用户“开始第二轮”继续 B2a/B2b | 覆盖此前必须先完成人工体验再开发的计划门，但不虚构 B1 验收通过；游戏期间的限制仍有效 |
| ADR-014 | B2 默认真实模式，移除主面板的假唤醒/代理状态 | 应用名 PersonalControlCenter，配置独立于 Demo；原演示包保留；未实现模块明确显示尚未接入 |
| ADR-015 | 声音固定 MTA worker；COM 回调只发失效通知 | 生命周期同线程；RCW 每次获取配对 ReleaseComObject，避免 FinalRelease 销毁仍被订阅持有的共享包装 |
| ADR-016 | 电源通知加可见时 5 秒补读 | personality 通知不覆盖所有同类方案切换；仅可见时低频 native 查询，隐藏停止；无 WMI/PowerShell 轮询 |
| ADR-017 | 程序信任保存在独立本机文件 | 只比较目标/参数/工作目录指纹；配置中的 confirmed 字段不生效；确认来自设置窗口的明确用户动作 |
| ADR-018 | 入口限本机路径、EXE、HTTP/HTTPS | 参数使用 ArgumentList；UNC/脚本目标不进入本轮；截图和项目目录由设置添加 |
| ADR-019 | B2 假服务测试也覆盖 ViewModel，但不创建窗口 | 测试工程引用 WPF 仅用于程序集依赖；不运行 UI 自动化或真实原生服务 |
| ADR-020 | 按用户“开始第三轮”实现 B3a/B3b | 继续游戏期间限制；不宣称前轮人工验收通过 |
| ADR-021 | AwakeSession 状态机与固定请求线程分离 | 假时钟/后端可测；UTC 与单调截止同时约束；原生申请/清除均由固定线程执行 |
| ADR-022 | 释放和恢复失败使用 ReleaseUnconfirmed | 不假报关闭，禁止直接续期；可显式重试结束，退出仍尝试清除 |
| ADR-023 | 代理设置默认无手动目标，入口复用可信快捷入口 | 不发自动外网探测，不另建启动信任后门；不读 Clash token |
| ADR-024 | 远端只读响应头，禁止自动重定向 | 指定目标范围清楚，无响应体/认证/隐式系统代理；根据 HttpRequestError 分类 |
| ADR-025 | 退出先清唤醒，逐模块有界处理 | 超时不挡住后续资源释放；最小日志仅记录固定枚举，无敏感文本；完整操作诊断留后续 |
| ADR-026 | 按用户要求开始 B4，不保留旧 ZIP | 删除 B1–B3 六个 ZIP；验收证据继续保留；旧目录递归删除受审批阻止 |
| ADR-027 | 导入/备份先载入草稿，保存才应用 | 不在导入时执行系统控制、注册热键或写自启；高版本不覆盖 |
| ADR-028 | 便携迁移移除程序/路径/网页入口、目标、热键和扩展字段 | 网页及未知字段可能带私人数据；迁移后重新绑定，避免导入程序继承本机信任 |
| ADR-029 | 自启不进入可移植配置，单独读取真实登记 | 独立按钮只改当前用户单个 Run 值；登记不代表 Windows 实际一定启动；不同内容拒绝覆盖 |
| ADR-030 | 全局热键默认空，不支持 Win/F12/PrintScreen | 显式用户选择 Ctrl/Alt 字母数字；MOD_NOREPEAT；冲突保留现有入口，失败释放不假报关闭 |
| ADR-031 | B4b 没有验证白名单，原生切换维持禁用 | 补三角色只读快照和矩阵；未进行宿主写测试，不连接未经验证的默认设备写接口 |
| ADR-032 | 用户授权 Git 版本管理 | 创建 codex/b5-release，B4 基线和 B5 分别提交；不推送远端，不跟踪 SDK/缓存/运行数据 |
| ADR-033 | B5 打包必须匹配干净提交和构建收据 | 源码跟踪清单、包内 RELEASE.json、逐文件 SHA256；不把旧二进制和新源码混包 |
| ADR-034 | 自包含下载失败不绕过 TLS、不伪称交付 | 本轮交付 FDD；准备脚本固定 runtime 10.0.12 并验证官方 SHA512，SCD 留阻塞项 |
| ADR-035 | B5 假状态循环与真实性能严格区分 | 500 次循环仅验状态/调度；CPU/内存/DPI/休眠全部待授权真机验收 |
| ADR-036 | 离线依赖盘点不等于漏洞或公开许可审核 | 12 个测试/构建包；旧许可元数据缺口、项目 LICENSE、在线审计和签名仍保留 |

B4 依据：[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey) 与 [Run/RunOnce 登记](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)。本轮只核对官方接口语义，假接口测试不代表真实操作已验收。

B3 依据：[SetThreadExecutionState](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate) 与 [.NET HttpRequestError](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httprequesterror?view=net-10.0)。本轮已核对文档，未执行对应真实系统或网络操作。

B2 技术依据：已核对 [Core Audio 主音量](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nn-endpointvolume-iaudioendpointvolume)、[设备变化回调](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient)、[电源方案管理](https://learn.microsoft.com/en-us/windows/win32/power/managing-power-schemes)、[电源 personality 通知](https://learn.microsoft.com/en-us/windows/win32/power/power-setting-guids)。这些资料用于接口设计，不替代本机验证。

两个 tools 下的控制台程序仅为显式验收工具，不加入应用运行包：ReadOnlyProbe 只读枚举；UiAcceptance 会显示窗口，因此游戏期间禁止执行。

首次 SDK 命令自动生成了 ASP.NET 开发证书；已按本次时间与精确指纹删除，回读证书不存在。后续构建设置 DOTNET_GENERATE_ASPNET_CERTIFICATE=false。细节见 artifacts/evidence/sdk-certificate-cleanup.json。

