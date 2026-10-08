// The one elevated step. CouchPilot shells out to this (one UAC prompt) to register or
// unregister the credential provider: writing under HKLM/HKCR, dropping the DLL where SYSTEM can
// load it, and keeping the pipe's shared secret and the enrolled user's SID as LSA secrets that
// only SYSTEM can read. See docs/unlock.md.
//
//   CouchPilotUnlockSetup.exe register   <userSid> <secretFile>
//   CouchPilotUnlockSetup.exe unregister
//   CouchPilotUnlockSetup.exe status          (exit code 0 registered, 1 not)
//
// The shared secret is passed in a file (CouchPilot writes it, we read and delete it) rather than
// on the command line, so it never shows up in a process listing.
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <ntsecapi.h>
#include <shlobj.h>
#include <objbase.h>   // StringFromGUID2
#include <string>
#include <vector>

// The CLSID and the LSA secret names are shared with the DLL: take them from its own headers so
// the two can't drift apart (a drift would silently stop unlocking from ever working).
#include "../CouchPilotUnlock/Guid.h"
#include "../CouchPilotUnlock/Common.h"

#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")

static const wchar_t *FRIENDLY = L"CouchPilot Unlock";
static const wchar_t *SECRET_KEY = COUCHPILOT_SECRET_KEY;
static const wchar_t *USER_KEY = COUCHPILOT_USER_KEY;

// The CLSID as a "{…}" string, from the one GUID definition in Guid.h.
static std::wstring ClsidString()
{
    wchar_t buf[64] = {};
    StringFromGUID2(CLSID_CouchPilotUnlock, buf, 64);
    return buf;
}

// HKLM key where Windows lists credential providers, plus our CLSID.
static std::wstring CredentialProviderKey()
{
    return L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Authentication\\Credential Providers\\" + ClsidString();
}

static int DoUnregister();   // DoRegister rolls back through it on failure

static std::wstring ExeDir()
{
    wchar_t path[MAX_PATH];
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    std::wstring s(path);
    size_t slash = s.find_last_of(L'\\');
    return slash == std::wstring::npos ? L"." : s.substr(0, slash);
}

static std::wstring InstallDir()
{
    wchar_t *pf = nullptr;
    std::wstring dir = L"C:\\Program Files";
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_ProgramFiles, 0, nullptr, &pf)))
    {
        dir = pf;
        CoTaskMemFree(pf);
    }
    return dir + L"\\CouchPilot";
}

static bool WriteStringValue(HKEY root, const wchar_t *subkey, const wchar_t *name, const wchar_t *value)
{
    HKEY key = nullptr;
    if (RegCreateKeyExW(root, subkey, 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) != ERROR_SUCCESS)
        return false;
    LONG r = RegSetValueExW(key, name, 0, REG_SZ, (const BYTE *)value, (DWORD)((wcslen(value) + 1) * sizeof(wchar_t)));
    RegCloseKey(key);
    return r == ERROR_SUCCESS;
}

static void InitLsa(LSA_UNICODE_STRING *s, const wchar_t *str)
{
    size_t len = str ? wcslen(str) : 0;
    s->Length = (USHORT)(len * sizeof(wchar_t));
    s->MaximumLength = (USHORT)((len + 1) * sizeof(wchar_t));
    s->Buffer = (PWSTR)str;
}

static bool StoreLsaSecret(const wchar_t *key, const wchar_t *value /* null to delete */)
{
    LSA_OBJECT_ATTRIBUTES attrs; ZeroMemory(&attrs, sizeof(attrs));
    LSA_HANDLE policy = nullptr;
    if (LsaOpenPolicy(nullptr, &attrs, POLICY_CREATE_SECRET, &policy) != 0) return false;

    LSA_UNICODE_STRING keyName, data;
    InitLsa(&keyName, key);
    if (value) InitLsa(&data, value);

    NTSTATUS status = LsaStorePrivateData(policy, &keyName, value ? &data : nullptr);
    LsaClose(policy);
    return status == 0;
}

static std::wstring ReadFileUtf8ToWide(const wchar_t *path)
{
    HANDLE f = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
    if (f == INVALID_HANDLE_VALUE) return L"";
    DWORD size = GetFileSize(f, nullptr);
    std::string bytes;
    bytes.resize(size);
    DWORD read = 0;
    ReadFile(f, &bytes[0], size, &read, nullptr);
    CloseHandle(f);
    bytes.resize(read);
    // Trim trailing whitespace/newlines.
    while (!bytes.empty() && (bytes.back() == '\n' || bytes.back() == '\r' || bytes.back() == ' '))
        bytes.pop_back();

    int wlen = MultiByteToWideChar(CP_UTF8, 0, bytes.data(), (int)bytes.size(), nullptr, 0);
    std::wstring w(wlen, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, bytes.data(), (int)bytes.size(), &w[0], wlen);
    return w;
}

static bool IsRegistered()
{
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, CredentialProviderKey().c_str(), 0, KEY_READ, &key) == ERROR_SUCCESS)
    {
        RegCloseKey(key);
        return true;
    }
    return false;
}

// Puts CouchPilotUnlock.dll at `dest`. If the old one is loaded on the lock screen and can't be
// replaced now, stage the new one beside it and have Windows swap it in at the next boot — leaving
// the source (the extension's own copy) untouched. Returns false only on a real failure.
static bool InstallDll(const std::wstring &src, const std::wstring &dest)
{
    if (CopyFileW(src.c_str(), dest.c_str(), FALSE)) return true;

    DWORD err = GetLastError();
    if (err != ERROR_SHARING_VIOLATION && err != ERROR_ACCESS_DENIED) return false;

    // Copy next to the destination (same volume: a delayed rename needs no COPY_ALLOWED), then
    // schedule that staged file to replace dest at reboot.
    std::wstring staged = dest + L".new";
    if (!CopyFileW(src.c_str(), staged.c_str(), FALSE)) return false;
    return MoveFileExW(staged.c_str(), dest.c_str(),
        MOVEFILE_REPLACE_EXISTING | MOVEFILE_DELAY_UNTIL_REBOOT) != FALSE;
}

static int DoRegister(const std::wstring &userSid, const std::wstring &secretFile)
{
    // Lift the secret and remove the file at once, so a failure further down never leaves it on
    // disk in the clear.
    std::wstring secret = ReadFileUtf8ToWide(secretFile.c_str());
    DeleteFileW(secretFile.c_str());
    if (secret.empty()) return 2;

    std::wstring installDir = InstallDir();
    CreateDirectoryW(installDir.c_str(), nullptr);
    std::wstring dest = installDir + L"\\CouchPilotUnlock.dll";
    std::wstring src = ExeDir() + L"\\CouchPilotUnlock.dll";

    int rc = 2;
    if (InstallDll(src, dest))
    {
        std::wstring clsidKey = std::wstring(L"CLSID\\") + ClsidString();
        std::wstring inproc = clsidKey + L"\\InprocServer32";
        bool ok = WriteStringValue(HKEY_CLASSES_ROOT, clsidKey.c_str(), nullptr, FRIENDLY) &&
            WriteStringValue(HKEY_CLASSES_ROOT, inproc.c_str(), nullptr, dest.c_str()) &&
            WriteStringValue(HKEY_CLASSES_ROOT, inproc.c_str(), L"ThreadingModel", L"Apartment") &&
            WriteStringValue(HKEY_LOCAL_MACHINE, CredentialProviderKey().c_str(), nullptr, FRIENDLY) &&
            StoreLsaSecret(SECRET_KEY, secret.c_str()) &&
            StoreLsaSecret(USER_KEY, userSid.c_str());
        rc = ok ? 0 : 2;
    }

    SecureZeroMemory(&secret[0], secret.size() * sizeof(wchar_t));
    // A half-done registration is worse than none: undo everything on failure.
    if (rc != 0) DoUnregister();
    return rc;
}

static int DoUnregister()
{
    std::wstring clsidKey = std::wstring(L"CLSID\\") + ClsidString();
    RegDeleteTreeW(HKEY_CLASSES_ROOT, clsidKey.c_str());
    RegDeleteKeyW(HKEY_LOCAL_MACHINE, CredentialProviderKey().c_str());
    StoreLsaSecret(SECRET_KEY, nullptr);
    StoreLsaSecret(USER_KEY, nullptr);

    std::wstring dest = InstallDir() + L"\\CouchPilotUnlock.dll";
    if (!DeleteFileW(dest.c_str()) && GetLastError() == ERROR_SHARING_VIOLATION)
        MoveFileExW(dest.c_str(), nullptr, MOVEFILE_DELAY_UNTIL_REBOOT);
    return 0;
}

int wmain(int argc, wchar_t **argv)
{
    if (argc >= 2 && _wcsicmp(argv[1], L"status") == 0)
        return IsRegistered() ? 0 : 1;

    if (argc >= 4 && _wcsicmp(argv[1], L"register") == 0)
        return DoRegister(argv[2], argv[3]);

    if (argc >= 2 && _wcsicmp(argv[1], L"unregister") == 0)
        return DoUnregister();

    return 64; // bad usage
}
