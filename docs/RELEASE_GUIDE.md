# 构建、验证与 Git 回退

B6 / 0.6.2。项目 SDK 固定 10.0.401，.tools/dotnet，不改变系统 PATH。普通构建使用 Release、单 MSBuild 节点、BelowNormal、关闭共享编译；不会启动应用。测试不调用宿主音频、电源、热键、Run 登记、唤醒或网络接口。

## 框架依赖候选

1. 新机器先安装/准备固定 SDK 并按锁文件恢复；本工作区依赖已缓存，可直接 -Offline。
2. 完成源码修改，检查 Git diff，提交。打包要求干净工作区。
3. scripts/build.ps1 -Offline -Publish；构建、174 项测试与发布连续执行。
4. scripts/dependency-inventory.ps1：本地锁文件与 NuGet 元数据清单。
5. scripts/package.ps1；scripts/verify-package.ps1。

build-framework-dependent.json 记录提交、脏状态、SDK 与发布文件 SHA256；打包拒绝脏提交、提交不匹配或文件变化。源码只取 Git 跟踪文件，运行包排除 PDB/验收工具/个人配置。RELEASE.json 记录提交、模式和全部其他包内文件的 SHA256；源码包附 SOURCE_COMMIT.txt。

ZIP 压缩时间戳会变化，因此不承诺两次 ZIP 字节相同。B5 曾二次 Release 构建比较项目 DLL/EXE 哈希，证据在 artifacts/evidence/B5/reproducibility.json；该历史验证不代表 B6 或 0.6.2 已重复验证；仅证明同目录/同 SDK 的项目二进制复现，不宣称不同绝对路径、不同 SDK 或操作系统均相同。

## 自包含发布路径

scripts/restore-runtime.ps1 显式限速 512 KiB/s，从官方 NuGet 下载两份 10.0.12 win-x64 runtime pack，核对响应中的 SHA512；无 TLS 绕过。下载后可用：

```powershell
./scripts/build.ps1 -Offline -Publish -SelfContained
./scripts/dependency-inventory.ps1
./scripts/package.ps1 -Mode self-contained
./scripts/verify-package.ps1 -Mode self-contained
```

不裁剪、不使用 ReadyToRun。此前下载曾遇到 TLS 失败；本次重新获取官方运行时包并验证 SHA512 后生成自包含包。自包含打包要求 coreclr 和 Desktop Runtime，同时复制运行时 LICENSE/THIRD-PARTY-NOTICES；缺少许可证则拒绝打包。打包验证不代替真实系统集成验收。

框架依赖包使用系统安装的 Desktop Runtime；自包含包须由项目后续升级其捆绑运行时。这两者不能混淆。

## 源码回退

B4 基线提交为 744b804；B6 提交由 RELEASE.json 和 Git 历史确定。先保存当前工作，可用 git log --oneline 查看历史，在单独 worktree/新分支构建旧提交；不需要保存每一轮 ZIP。不要硬重置或清理用户未提交修改。

源码回退不回退 Windows 音量/方案/自启，也不强行降级高版本用户配置。移动/删除已启用自启的安装目录前，先在原位置关闭本程序自启。配置和最近 5 份备份仍独立保留。

## 依据

[微软发布模式说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/) 与 [裁剪不兼容说明](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)。本轮核对资料，不以文档替代真机测试。
