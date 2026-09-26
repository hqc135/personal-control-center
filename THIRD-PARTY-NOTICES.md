# 第三方组件说明

B2 framework-dependent 候选包仅包含本项目编译产物和自行绘制的托盘图标，不捆绑 .NET 运行时、字体或测试依赖。保留的 B1 历史包说明见其包内文件。

运行需要 Microsoft .NET 10 Windows Desktop Runtime；微软 .NET 仓库采用 MIT 许可，组件的第三方声明由安装的运行时提供。运行时官方来源：https://dotnet.microsoft.com/download/dotnet/10.0

构建工具 .NET SDK 10.0.401 保留在项目 .tools/dotnet，其 LICENSE.txt 和 ThirdPartyNotices.txt 为工具自身许可；不进入演示 ZIP。

测试依赖版本固定于 csproj/packages.lock.json：Microsoft.NET.Test.Sdk 18.10.1、xunit 2.9.3、xunit.runner.visualstudio 2.8.2。它们不进入运行包；相应许可证与传递依赖见恢复后的 NuGet 包元数据和许可证文件。

