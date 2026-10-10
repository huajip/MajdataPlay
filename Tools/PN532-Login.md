# NFC 扫码登录

适配兼容的pn532读卡器例如hinata：在登录页通过 NFC 打开当前二维码网页，手机仍按网页提示确认登录。Majplay 直接控制设备，无需另开桥接程序，默认启用，直接运行游戏即可。

在现有 `settings.json` 添加 `NFC` 配置（COM1 替换成设备实际端口）：

```json
"NFC": {
  "Enabled": true,
  "Port": "COM1",
  "Diagnostics": true
}
```

目前支持 CDC 串口、115200、Type 4 NDEF；兼容固件必须能在 DTR 拉低时退出模拟。原生 USB HID 和其他 UART 固件尚未适配。USB 插头本身不代表 CDC 协议。

旧配置缺少 `NFC` 时默认启用；`Port` 空字符串会自动选择唯一在线的 Hinata CDC 设备，多个设备时请填写端口。其他兼容 PN532 串口设备需显式配置端口。`Enabled: false` 关闭功能；无设备或错误端口不会中断网页登录。NFC 配置随游戏设置正常保存，修改后重启游戏生效。

NFC 使用当前服务器用于显示二维码的同一个 HTTP/HTTPS 登录 URL，不另设域名白名单，不修改票据。刷新、切换服务器、登录成功、退出或服务器状态超过 8 秒未确认时清除旧模拟；掉线后需刷新二维码重试。模拟期间该设备暂停普通读卡。诊断只记录事件类型，不记录登录链接或票据。

已有 `MAJ_PN532_NFC`、`MAJ_PN532_PORT` 可作为可选覆盖；不需要启动脚本。旧 `MAJ_PN532_AUTH_HOST` 不再限制 NFC 链接。

模拟测试：`dotnet run --project Tools/Tests/Pn532Login/Pn532Login.csproj`（.NET 10）。验证的是协议和登录生命周期，不代表手机实测通过。
