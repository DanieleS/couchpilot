// Names and small bits shared across the DLL. The pipe name and the LSA secret names are a
// contract with the extension (the pipe) and the setup helper (the secrets); keep them in step
// with docs/unlock.md and CouchPilot's UnlockService.
#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

// The duplex, message-mode pipe the provider serves on the lock screen and CouchPilot connects to.
#define COUCHPILOT_PIPE_NAME L"\\\\.\\pipe\\CouchPilotUnlock"

// Set by the setup helper (register) as LSA private data, readable only by SYSTEM.
//   secret: 32 random bytes, base64, shared with CouchPilot's DPAPI-sealed copy.
//   user:   the enrolled Windows account's SID, as a string — the only non-SYSTEM caller allowed.
#define COUCHPILOT_SECRET_KEY L"L$CouchPilotUnlockSecret"
#define COUCHPILOT_USER_KEY   L"L$CouchPilotUnlockUser"

// The pipe protocol this build speaks. Bumped only with a breaking wire change.
#define COUCHPILOT_PROTOCOL_VERSION 1

// The one field our hidden credential tile carries. LogonUI wants at least one.
enum COUCHPILOT_FIELD_ID
{
    CPUF_LABEL = 0,
    CPUF_NUM_FIELDS,
};
