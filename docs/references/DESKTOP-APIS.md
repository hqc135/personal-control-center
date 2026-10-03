# 0.7.0 桌面能力参考

- [桌面应用调用 WinRT](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)：采用明确的 Windows 目标框架，SDK 投影版本固定。
- [系统媒体会话管理器](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager)：读取会话并向指定会话发送控制请求。
- [WMI 亮度方法](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorbrightnessmethods)：内屏能力探测与设置。
- [外屏亮度接口](https://learn.microsoft.com/en-us/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-setmonitorbrightness)：按可读范围换算，写入后回读；不同显示器实现可能不同。
- [Windows 设置 URI](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)：仅使用固定白名单，不接受任意输入 URI。
- [EarTrumpet PolicyConfig 接口](https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IPolicyConfig.cs)：兼容接口布局参考，隔离在 AudioDeviceControls.cs；接口失败可回退系统设置。
