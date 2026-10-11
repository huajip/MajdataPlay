Offline public-classification candidate test. It compiles 11 methods extracted
verbatim from production SongStorage/Online, plus the real per-server capability
helper, against minimal Unity/network doubles. Generate before running; no game,
real network, accounts or physical reader is used.

```sh
python3 Tools/Tests/PublicCollections/generate.py
dotnet run --project Tools/Tests/PublicCollections/PublicCollections.csproj -- /path/to/system-collections.json /path/to/list.json
```

The tested input is the reviewed 37-collection identity-repair file and unchanged
1893-item catalog. Tests cover initial signed-out scanning, refresh, login/logout,
private/public deduplication, source-specific hash lookup, failed/empty/capability
responses, cancellation and online disabled settings. A second host/port/API root
also works when it advertises the capability; no domain whitelist is used.
Undeclared sources retain private favorites' login gate. Public metadata 401 never
logs a user out. No new private-library policy is implemented.

This test compiles only those exact methods, not the whole Unity application.
The candidate still requires full Unity/Windows build and on-device verification
before producing/installing a game DLL. No QR/ticket lifecycle changes are included.
