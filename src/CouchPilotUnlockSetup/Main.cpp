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
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <ntsecapi.h>
#include <shlobj.h>
#include <string>
#include <vector>

#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")

static const wchar_t *CLSID_STR = L"{B6F4C3A2-7E51-4A90-9C3D-1F8E2A6B5D04}";
static const wchar_t *FRIENDLY = L"CouchPilot Unlock";
static const wchar_t *CP_KEY =
    L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Authentication\\Credential Providers\\{B6F4C3A2-7E51-4A90-9C3D-1F8E2A6B5D04}";
static const wchar_t *SECRET_KEY = L"L$CouchPilotUnlockSecret";
static const wchar_t *USER_KEY = L"L$CouchPilotUnlockUser";

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
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, CP_KEY, 0, KEY_READ, &key) == ERROR_SUCCESS)
    {
        RegCloseKey(key);
        return true;
    }
    return false;
}

static int DoRegister(const std::wstring &userSid, const std::wstring &secretFile)
{
    std::wstring installDir = InstallDir();
    CreateDirectoryW(installDir.c_str(), nullptr);
    std::wstring dest = installDir + L"\\CouchPilotUnlock.dll";
    std::wstring src = ExeDir() + L"\\CouchPilotUnlock.dll";

    // A DLL already loaded on the lock screen can't be overwritten now; schedule it for the next
    // boot. A fresh install copies straight away. Either way the registry points at `dest`.
    if (!CopyFileW(src.c_str(), dest.c_str(), FALSE))
    {
        if (GetLastError() == ERROR_SHARING_VIOLATION || GetLastError() == ERROR_ACCESS_DENIED)
            MoveFileExW(src.c_str(), dest.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_DELAY_UNTIL_REBOOT | MOVEFILE_COPY_ALLOWED);
        else
            return 2;
    }

    std::wstring clsidKey = std::wstring(L"CLSID\\") + CLSID_STR;
    std::wstring inproc = clsidKey + L"\\InprocServer32";
    bool ok = WriteStringValue(HKEY_CLASSES_ROOT, clsidKey.c_str(), nullptr, FRIENDLY) &&
        WriteStringValue(HKEY_CLASSES_ROOT, inproc.c_str(), nullptr, dest.c_str()) &&
        WriteStringValue(HKEY_CLASSES_ROOT, inproc.c_str(), L"ThreadingModel", L"Apartment") &&
        WriteStringValue(HKEY_LOCAL_MACHINE, CP_KEY, nullptr, FRIENDLY);
    if (!ok) return 2;

    std::wstring secret = ReadFileUtf8ToWide(secretFile.c_str());
    if (secret.empty()) return 2;
    ok = StoreLsaSecret(SECRET_KEY, secret.c_str()) && StoreLsaSecret(USER_KEY, userSid.c_str());
    SecureZeroMemory(&secret[0], secret.size() * sizeof(wchar_t));
    // CouchPilot wrote this file for us alone; it's done its job.
    DeleteFileW(secretFile.c_str());
    return ok ? 0 : 2;
}

static int DoUnregister()
{
    std::wstring clsidKey = std::wstring(L"CLSID\\") + CLSID_STR;
    RegDeleteTreeW(HKEY_CLASSES_ROOT, clsidKey.c_str());
    RegDeleteKeyW(HKEY_LOCAL_MACHINE, CP_KEY);
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
