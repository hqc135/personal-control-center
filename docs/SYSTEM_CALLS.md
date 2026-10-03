# 系统调用与维护边界

0.8.1 删除 DesktopFeatures 对麦克风静音、默认输出切换、亮度设置的原样转发。DesktopViewModel 直接持有实际音频/亮度服务；App 创建并拥有同一份服务实例，状态读取与写入共用它们，退出时集中释放。

| 操作 | 调用路径 |
| --- | --- |
| 麦克风静音、输出切换 | DesktopViewModel → CoreAudioService → Core Audio / PolicyConfig |
| 亮度设置 | DesktopViewModel → BrightnessService → WMI / DDC |
| 媒体控制 | DesktopViewModel → DesktopFeatures → Windows 媒体会话 API |
| 音量、静音、电源 | PanelViewModel → ControlCoordinator → 对应 Windows 服务 → 系统 API |

IAudioDevices、IBrightnessService、IDesktopFeatures 只描述实际服务边界，直接由服务实现，没有额外适配器或转发对象。假服务测试使用相同边界，无需调用宿主电脑。DesktopFeatures 保留多项状态的汇总和媒体 API 调用，不再拥有亮度服务或转发设备写入。界面私有 RunAsync 只统一忙碌状态、错误展示与刷新，不作为跨模块执行框架。

ControlCoordinator 不是空包装：它合并连续音量/电源请求，处理设备失效及过期回读，串行化相关写入，并验证真实结果。场景执行还需要条件恢复，配置保存还需要原子替换、备份与冲突检测；这些有业务行为的代码继续独立维护。COM 专用线程、句柄释放和取消逻辑保留。

新增功能先寻找可用 Windows API，将调用放进对应现有服务；只有独立的资源生命周期或业务职责才新建服务。不要为了统一形式添加工厂、服务定位器、空适配器或通用动作管道。CSS 仍映射为 WPF 资源，未增加网页引擎。

这次是调用结构简化，不宣称未经测量的性能提升。扩展状态读取仍由 DesktopFeatures 汇总；没有把全部 P/Invoke 散落到按钮事件里。
