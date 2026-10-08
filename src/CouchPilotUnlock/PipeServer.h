// The lock-screen end of the pipe. While a logon/unlock scenario is up, this listens on
// \\.\pipe\CouchPilotUnlock. CouchPilot opens with a nonce; the server proves it holds the shared
// secret (HMAC) before CouchPilot sends the account over, so a process that squats the pipe name
// learns nothing. The caller's identity is checked too. See docs/unlock.md for the wire format.
#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <functional>
#include <string>

// What the credential makes of a well-formed, authenticated request. Transport- and
// auth-level outcomes (denied, bad-request, bad-version) are decided in the pipe server itself.
enum class UnlockOutcome
{
    Ok,
    Busy,
};

class PipeServer
{
public:
    // handler runs on the pipe thread; it stores the credentials and asks LogonUI to submit.
    using Handler = std::function<UnlockOutcome(const std::wstring &user, const std::wstring &password)>;

    PipeServer() = default;
    ~PipeServer();

    // Idempotent. Starts the listener thread; returns false if it couldn't read the secret the
    // setup helper stored (so unlocking isn't actually enrolled).
    bool Start(Handler handler);
    void Stop();

    // The enrolled account's SID, for the provider to bind its tile to. Empty until Start.
    std::wstring UserSid() const { return _userSidString; }

private:
    void Run();
    void ServeOne(HANDLE pipe, HANDLE ioEvent);

    Handler _handler;
    HANDLE _thread = nullptr;
    HANDLE _stopEvent = nullptr;
    volatile bool _running = false;

    std::wstring _secret;         // base64, from the LSA secret; the HMAC key
    std::wstring _userSidString;  // the enrolled account's SID, as a string
    PSID _userSid = nullptr;      // the only non-SYSTEM caller allowed; owns its memory
};
