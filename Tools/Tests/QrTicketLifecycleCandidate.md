# QR authorization lifecycle candidate

This is an unreleased source candidate. Configuration and reader requirements
are documented in [PN532 tap-to-login](../PN532-Login.md).

`LoginManager` owns each endpoint's authorization attempt. Refresh, cancel,
scene exit and application quit retire unfinished attempts and clear local
QR/NFC. Server revocation uses a ten-second cancellation timeout and does not
block scene teardown. A late registration response retires its returned ticket;
late poll, user-info and avatar responses cannot reopen NFC or import scores.
An accepted QR session is retained through normal scene exit. Password login
retires only its unused QR attempt.

The fixture compiles the login/NFC sources with mocked Unity, HTTP and serial
interfaces, including late responses that ignore cancellation. It checks
cleanup, refresh, server switching, expiry, device ownership and accepted
session retention. The `Online.GetUserInfoAsync` cancellation guards in the
full network class are outside this focused fixture.

```sh
dotnet run --project Tools/Tests/Pn532Login/Pn532Login.csproj
```

Closing the server poll/revoke race also requires the matching server issuance
and revocation changes and its `0003_ticket_lifecycle.sql` migration. Network
failure or forced termination can leave a server ticket until expiry. The
direct PN532 worker has its own NFC lease; there is no bridge service.

A matching full Unity build, real HTTP cancellation checks and phone/reader
cleanup tests are still required before deployment. Passing mocks alone does
not prove that a copied login URL becomes unusable immediately on exit.
