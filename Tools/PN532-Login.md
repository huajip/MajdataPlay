# PN532 tap-to-login

Tap a compatible reader with your phone to open the current login QR URL,
then complete the normal web authorization. MajdataPlay controls the reader
directly; no bridge or launcher script is needed.

## Setup

With the game closed, merge this section into the existing `settings.json`:

```json
{
  "NFC": {
    "Enabled": true,
    "Port": "",
    "Diagnostics": true
  }
}
```

These are the defaults, including when `NFC` is absent. An empty `Port` selects
one connected Hinata Lite CDC interface; other compatible PN532 readers or
multiple devices need an explicit port, such as `"COM1"`. No unrelated ports
are probed. Set `Enabled` to `false` to disable NFC. The settings are saved with
the game but hidden from its settings UI; restart after editing.

`MAJ_PN532_NFC` (`1`/`true` or `0`/`false`) and `MAJ_PN532_PORT` are optional
JSON overrides. `MAJ_PN532_AUTH_HOST` is unused in this revision.

## Login and cleanup

Set `Online.Enable` to `true` and keep your servers in `Online.ApiEndpoints`.
The login scene visits them in order. Each server must support machine
registration and QR authorization; NFC uses that server's displayed HTTP/HTTPS
QR URL unchanged, with no separate server list or host allowlist.

Refresh, server changes, successful login and leaving the login scene stop the
old NFC target. Pending authorization checks renew an eight-second NFC lease;
without a timely pending response, emulation stops. Clearing NFC does not
invalidate a URL already opened or copied on a phone.

This unreleased lifecycle candidate retires unfinished attempts on scene exit
and starts server revocation with a ten-second cancellation timeout, including
late registration responses. Accepted QR logins keep their delivered session.
Revocation may fail or race server issuance; immediate invalidation also needs
the matching server changes. Forced termination falls back to server expiry.
See [candidate validation](Tests/QrTicketLifecycleCandidate.md).

## Reader requirements

The reader must support PN532 command pass-through and target-mode commands
`TgInitAsTarget`, `TgGetData` and `TgSetData`, along with `SAMConfiguration`,
`RFConfiguration` and `SetParameters`. Not every PN532 reader or firmware
supports this feature; compatible Hinata readers are one example.

The current transport is Windows CDC/COM at 115200 baud. Firmware must release
target mode when DTR goes low; HID NFC and non-Windows transports are not
implemented. Emulation temporarily occupies the reader, so ordinary card
reading and other PN532 pass-through clients cannot use it concurrently.
Cleanup releases the port with DTR low; normal card-reading recovery depends
on the firmware and still needs hardware validation.

## Troubleshooting and validation

With `Diagnostics` enabled, `[MajNfc]` logs record events, not URLs or tickets.
Device errors leave QR/web login available.

| Symptom | Check |
| --- | --- |
| No NFC target | Check `Enabled`, the Windows COM port and any environment overrides. Close other serial clients. |
| Multiple readers or a different PN532 device | Set its actual port in `NFC.Port`; automatic selection is limited to the matching CDC interface. |
| Device fault or unplug | Reconnect the reader and refresh the QR. Check firmware support for the required commands and DTR cleanup. |
| The QR URL itself is invalid | Check the server's authorization flow. NFC uses the same URL and cannot repair a server ticket. |

Run the protocol and login fixture with .NET 10:

```sh
dotnet run --project Tools/Tests/Pn532Login/Pn532Login.csproj
```

It uses mocked network, Unity and serial dependencies; it does not establish a
complete Unity build or successful phone/reader testing.
