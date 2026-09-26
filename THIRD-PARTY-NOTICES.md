# 第三方组件与许可记录

B5 framework-dependent 包仅捆绑本项目编译产物、自绘托盘图标、文档及发布清单。无第三方运行 NuGet 包、测试依赖、字体或 SDK。运行需要 Microsoft .NET 10 Windows Desktop Runtime，由系统安装及其许可文件提供。

本轮未获得自包含运行时包。若以后交付 self-contained，打包脚本必须复制两份 runtime pack 的 LICENSE.TXT/THIRD-PARTY-NOTICES.TXT；缺失则拒绝打包，不把本说明当作完整运行时许可附件。

离线核对本地 NuGet 元数据和锁文件共 12 项，详见包内 DEPENDENCIES.json：
- Microsoft.NET.Test.Sdk、Microsoft.CodeCoverage、Microsoft.TestPlatform.ObjectModel/TestHost 18.10.1：MIT。
- xunit 2.9.3、xunit.core/assert/extensibility.core/extensibility.execution 2.9.3、xunit.analyzers 1.18.0、xunit.runner.visualstudio 2.8.2：元数据标 Apache-2.0。
- xunit.abstractions 2.0.3：旧元数据没有 SPDX license 字段，仅提供上游 licenseUrl；保留链接与元数据哈希，不从当前 master 内容推定旧版本许可。

上述全部为构建/测试用途，不进入运行目录。SDK 10.0.401 位于 .tools/dotnet，自带 LICENSE.txt/ThirdPartyNotices.txt，不分发。

在线漏洞审计未完成。项目自身 LICENSE 仍待用户确定版权所有者与授权条款；没有代替用户选择开源许可证，不能因此宣称已完成公开分发许可审核。

