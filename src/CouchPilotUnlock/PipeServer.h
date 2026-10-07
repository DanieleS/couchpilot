// The lock-screen end of the pipe. While a logon/unlock scenario is up, this listens on
// \\.\pipe\CouchPilotUnlock for the one request CouchPilot sends, checks the shared secret and
// the caller's identity, and hands the username and password to the credential. See
// docs/unlock.md for the wire format.
#pragma once

#define WIN32_LEAN_AND_MEAN
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

private:
    void Run();
    void ServeOne(HANDLE pipe);

    Handler _handler;
    HANDLE _thread = nullptr;
    HANDLE _stopEvent = nullptr;
    volatile bool _running = false;

    std::wstring _secret;   // base64, from the LSA secret
    PSID _userSid = nullptr; // the only non-SYSTEM caller allowed; owns its memory
};
