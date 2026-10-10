# Login QR lifecycle source candidate

Based on isolated client source `07ad126daf5e752756999084273ca238ee8a6322`.
No change was made to the original checkout or installed Legion game.

LoginManager owns an authorization attempt for the selected endpoint. Refresh,
cancel, scene exit, destroy and application quit retire it; local QR/NFC is
cleared before cleanup. Server revocation is bounded to ten seconds and does
not block scene teardown. A registration returning after retirement revokes
its newly returned ticket instead of leaving it active. Late polls, user data
and avatar responses cannot reopen NFC, import scores or update the displayed
account. Repeated teardown is idempotent. Successful QR login marks its attempt
accepted and retains the delivered local session through normal scene exit;
password success retires only the unused QR ticket.

Online.GetUserInfoAsync checks cancellation after its network response and
again before changing login statistics under its lock. This prevents a late
response from changing IsLoggedIn after the owning flow was canceled.

The linked production LoginManager/NFC fixture passes 79 checks. All network,
Unity scene, QR and serial device dependencies use existing local test doubles;
the late-response doubles deliberately return even after cancellation. Checks
include scene teardown, returning after request/poll/user-info/avatar waits,
refresh, server switching, expiry, single NFC ownership and retaining an
accepted session. The fixture compiles LoginManager and NFC sources; the three
Online cancellation guards are source changes in the full Unity network class
and are not compiled as part of this focused fixture.

Run: `dotnet run --project Tools/Tests/Pn532Login/Pn532Login.csproj`.
A complete Unity build matching the installed version, real HTTP transport
checks and on-device lifecycle acceptance remain before deployment. No new
Assembly-CSharp.dll was produced or installed; no game/NFC process was touched.
The server candidate's 0003 D1 migration and issuance/revocation changes are
needed to close the poll/revoke race fully. Offline/forced termination still
depends on server ticket expiry and the NFC bridge's existing lease expiry.

Branch: `codex/munet-ticket-lifecycle` in this client repository. The separate
server repository uses a branch with the same name.
