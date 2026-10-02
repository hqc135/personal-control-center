# 维护说明

## 仓库与结构

私有仓库：hqc135/personal-control-center。保留本地迭代提交历史，主分支为 main；功能分支使用 codex/ 前缀。项目许可证尚未确定，上传私有仓库不等于开源授权。

| 目录 | 职责 |
|---|---|
| src/ControlCenter.Core | 配置、状态机、服务接口、调度与业务规则 |
| src/ControlCenter.Windows | 音频、电源、唤醒、托盘、启动登记及文件存储实现 |
| src/ControlCenter.App | WPF 面板、设置、主题与 ViewModel |
| tests/ControlCenter.Tests | 假接口与临时目录自动测试 |
| tools/UiAcceptance | 明确授权后运行的假数据演示窗口与截图工具 |
| tools/ReadOnlyProbe | 独立真机读取工具；普通构建测试不执行 |
| scripts | 构建、依赖盘点、打包与包校验 |
| docs | 设计、架构、决策及历史验收记录 |

源码和文档进入 Git；.tools、artifacts、bin、obj、测试输出不上传。不要把用户配置、信任记录、日志、访问令牌或 SDK 加入源码。历史文档中的旧路径/批次是历史记录，当前使用入口以 README 为准。

## 新机器准备

需要 Windows、Git、PowerShell，以及 global.json 固定的 .NET SDK 10.0.401。当前构建脚本要求项目本地 SDK：从微软官方渠道获取对应 Windows x64 SDK ZIP，将内容解压到 `.tools/dotnet`，使 `.tools/dotnet/dotnet.exe` 存在；不要求修改系统 PATH。

在仓库根目录执行：

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.tools/packages'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& ./.tools/dotnet/dotnet.exe --version
& ./.tools/dotnet/dotnet.exe restore ControlCenter.slnx --locked-mode
./scripts/build.ps1 -Offline
```

首次恢复需要联网；锁文件不匹配时查明依赖变化，不直接删除锁文件。离线构建要求依赖已缓存。脚本用 BelowNormal 优先级、单 MSBuild 节点和关闭共享编译；不会启动产品窗口或操作真实系统。SDK/依赖版本升级应单独提交并更新锁文件与验收结果。

## 日常改动与测试

1. `git pull --ff-only` 后创建 `codex/简短任务名` 分支。
2. 改动前读 ARCHITECTURE.md、DECISIONS.md 和相关验收记录；系统调用保留在 Windows 层，业务逻辑经接口可替换。
3. 对状态竞态、文件冲突、取消、超时和退出错误路径补有意义的测试；UI 小修不必堆叠镜像测试。
4. 运行 `./scripts/build.ps1 -Offline`，检查 `git diff --check` 与差异；当前基线 174 项测试。
5. 提交并推送分支，审查后合入 main；不强推覆盖他人提交，不上传本机依赖/产物。

重要约束：音量写入针对捕获的设备；隐藏面板停止相应轮询；外部代理请求必须由用户主动触发；唤醒请求由所属线程释放；配置冲突拒绝静默覆盖；普通配置保存不自动开启自启。共享文件锁不能防止不合作的外部编辑器在最终比较与替换间竞争。

## UI 验收（会弹窗）

仅在用户允许且不打扰其当前工作时执行。先准备工具依赖：

```powershell
& ./.tools/dotnet/dotnet.exe restore tools/UiAcceptance --locked-mode
& ./.tools/dotnet/dotnet.exe build tools/UiAcceptance -c Release --no-restore -m:1 -p:UseSharedCompilation=false
& ./.tools/dotnet/dotnet.exe tools/UiAcceptance/bin/Release/net10.0-windows/UiAcceptance.dll --allow-ui artifacts/evidence/UI-local
```

最后一个参数启用自动截图/断言/退出；省略输出目录则为手动演示。检查 result.txt、退出码和实际截图，不能只看构建成功。所有服务均为假实现，设置使用内存仓库；不证明真实音量/电源、性能、DPI 或读屏通过。ReadOnlyProbe 和生产 App 是另外的验收范围，不随此命令启动。

## 生成候选包

先提交全部改动并保持工作区干净，再运行：

```powershell
./scripts/build.ps1 -Offline -Publish
./scripts/dependency-inventory.ps1
./scripts/package.ps1
./scripts/verify-package.ps1
```

现有脚本使用 B6 路径；当前版本 0.6.2。新增迭代时同步修改 App csproj、package.ps1 的版本、各脚本产物/证据目录与当前文档。不要批量替换历史验收记录。

构建收据、包内 RELEASE.json 与源码包 SOURCE_COMMIT.txt 必须指向同一干净提交。依赖清单是本地许可元数据盘点，不等同在线漏洞审计。ZIP 时间戳不同，不承诺压缩包字节级复现；B5 曾完成同目录二次二进制比较，不能外推为后续每版验证。

GitHub Release 默认交付 self-contained win-x64，运行时来自官方 NuGet 包并核对 SHA512；准备好运行时后构建时加 -SelfContained，打包与校验时加 -Mode self-contained。framework-dependent 保留为开发选项。详情见 RELEASE_GUIDE.md。普通提交不把 ZIP 放进 Git；如需发布下载包，单独创建候选 Release，附运行包、SHA256 和验收边界，并回读验证附件。当前发布标签 v0.6.2，应用附件名 PersonalControlCenter-0.6.2-win-x64.zip，采用预发布状态。Release 应先以 draft 上传并校验附件，再发布；首页链接须匹配实际附件名。

## 故障与回退

先记录包版本/提交、复现步骤、Windows build、相关日志及最后确认状态；不要为排查默认重置代理、电源、自启或重启 Explorer。运行数据位置及备份恢复见 USER_GUIDE.md。日志含退出关联 ID、耗时和结果，不能把这些字段当作所有操作的完整审计。

已共享改动优先 `git revert <commit>`，保留可追溯历史；构建旧版本应另开分支/隔离工作区，避免覆盖未提交修改。源码回退不会恢复 Windows 设置；恢复外部状态前必须确认当前值未被用户或其他应用修改。配置备份单独处理，不强制降级未来版本 schema。

## 持续验收清单

每次候选记录：源码提交、SDK、构建/测试退出码、测试数、包 SHA256、模式、已测与未测场景。仍待完成的发布门包括真实系统集成、实际 CPU/内存/句柄、DPI/多屏、休眠恢复、可访问性、在线漏洞审计、项目许可证与签名。不要用假接口测试替代这些结论。
