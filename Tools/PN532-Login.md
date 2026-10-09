# NFC 扫码登录

适配兼容的pn532读卡器例如hinata：在登录页通过 NFC 打开当前二维码网页，手机仍按网页提示确认登录。Majplay 直接控制设备，无需另开桥接程序，默认关闭。

Windows 启动示例（COM1 替换成设备实际端口）：

```powershell
$env:MAJ_PN532_NFC = "1"
$env:MAJ_PN532_PORT = "COM1"
.\MajdataPlay.exe
```

目前支持 CDC 串口、115200、Type 4 NDEF；兼容固件必须能在 DTR 拉低时退出模拟。原生 USB HID 和其他 UART 固件尚未适配。USB 插头本身不代表 CDC 协议。

默认只允许 `https://majdata.net/qrauth?auth-id=…`；其他兼容站点可设置 `MAJ_PN532_AUTH_HOST` 为其精确域名。刷新、登录成功、退出或服务器状态超过 8 秒未确认时停止模拟；掉线后需刷新二维码重试。模拟期间该设备暂停普通读卡。

模拟测试：`dotnet run --project Tools/Tests/Pn532Login/Pn532Login.csproj`（.NET 10）。验证的是协议和登录生命周期，不代表手机实测通过。
