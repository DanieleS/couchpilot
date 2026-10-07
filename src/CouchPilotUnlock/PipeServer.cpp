#include "PipeServer.h"
#include "Common.h"

#include <ntsecapi.h>
#include <sddl.h>
#include <aclapi.h>
#include <vector>

#pragma comment(lib, "advapi32.lib")

// ---- LSA secret retrieval ---------------------------------------------------------------------

static void InitLsaString(_Out_ LSA_UNICODE_STRING *s, _In_ PCWSTR str)
{
    size_t len = wcslen(str);
    s->Length = (USHORT)(len * sizeof(wchar_t));
    s->MaximumLength = (USHORT)((len + 1) * sizeof(wchar_t));
    s->Buffer = (PWSTR)str;
}

static bool ReadLsaSecret(_In_ PCWSTR key, _Out_ std::wstring &value)
{
    value.clear();
    LSA_OBJECT_ATTRIBUTES attrs;
    ZeroMemory(&attrs, sizeof(attrs));

    LSA_HANDLE policy = nullptr;
    if (LsaOpenPolicy(nullptr, &attrs, POLICY_GET_PRIVATE_INFORMATION, &policy) != 0)
        return false;

    LSA_UNICODE_STRING keyName;
    InitLsaString(&keyName, key);

    PLSA_UNICODE_STRING data = nullptr;
    NTSTATUS status = LsaRetrievePrivateData(policy, &keyName, &data);
    LsaClose(policy);
    if (status != 0 || !data)
        return false;

    value.assign(data->Buffer, data->Length / sizeof(wchar_t));
    // Wipe and free the copy LSA handed us.
    SecureZeroMemory(data->Buffer, data->MaximumLength);
    LsaFreeMemory(data);
    return !value.empty();
}

// ---- tiny JSON (the request is produced by CouchPilot, a flat object of strings and one int) ---

static std::string Utf8Of(HANDLE pipe, DWORD cb, const BYTE *buf)
{
    (void)pipe;
    return std::string((const char *)buf, cb);
}

// Finds "key" and returns its string value, JSON-unescaped, as UTF-16. Returns false if absent.
static bool JsonString(const std::string &json, const char *key, std::wstring &out)
{
    std::string needle = std::string("\"") + key + "\"";
    size_t k = json.find(needle);
    if (k == std::string::npos) return false;
    size_t c = json.find(':', k + needle.size());
    if (c == std::string::npos) return false;
    size_t q = json.find('"', c + 1);
    if (q == std::string::npos) return false;

    std::string val;
    for (size_t i = q + 1; i < json.size(); ++i)
    {
        char ch = json[i];
        if (ch == '\\' && i + 1 < json.size())
        {
            char e = json[++i];
            switch (e)
            {
            case 'n': val.push_back('\n'); break;
            case 't': val.push_back('\t'); break;
            case 'r': val.push_back('\r'); break;
            case 'b': val.push_back('\b'); break;
            case 'f': val.push_back('\f'); break;
            case '/': val.push_back('/'); break;
            case '\\': val.push_back('\\'); break;
            case '"': val.push_back('"'); break;
            case 'u':
            {
                if (i + 4 < json.size())
                {
                    wchar_t code = (wchar_t)wcstoul(std::wstring(json.begin() + i + 1, json.begin() + i + 5).c_str(), nullptr, 16);
                    // Re-encode the single code unit as UTF-8 into val (BMP only; enough here).
                    char u8[4]; int n = WideCharToMultiByte(CP_UTF8, 0, &code, 1, u8, sizeof(u8), nullptr, nullptr);
                    val.append(u8, n);
                    i += 4;
                }
                break;
            }
            default: val.push_back(e); break;
            }
        }
        else if (ch == '"')
        {
            int wlen = MultiByteToWideChar(CP_UTF8, 0, val.data(), (int)val.size(), nullptr, 0);
            out.assign(wlen, L'\0');
            MultiByteToWideChar(CP_UTF8, 0, val.data(), (int)val.size(), &out[0], wlen);
            return true;
        }
        else
        {
            val.push_back(ch);
        }
    }
    return false;
}

static bool JsonInt(const std::string &json, const char *key, int &out)
{
    std::string needle = std::string("\"") + key + "\"";
    size_t k = json.find(needle);
    if (k == std::string::npos) return false;
    size_t c = json.find(':', k + needle.size());
    if (c == std::string::npos) return false;
    out = atoi(json.c_str() + c + 1);
    return true;
}

// Length-independent compare, so a reject doesn't leak where it differed.
static bool SecretEquals(const std::wstring &a, const std::wstring &b)
{
    if (a.size() != b.size()) return false;
    unsigned diff = 0;
    for (size_t i = 0; i < a.size(); ++i) diff |= (unsigned)(a[i] ^ b[i]);
    return diff == 0;
}

// ---- the server -------------------------------------------------------------------------------

PipeServer::~PipeServer()
{
    Stop();
    if (_userSid) LocalFree(_userSid);
}

bool PipeServer::Start(Handler handler)
{
    if (_running) return true;

    std::wstring sidStr;
    if (!ReadLsaSecret(COUCHPILOT_SECRET_KEY, _secret)) return false;
    if (!ReadLsaSecret(COUCHPILOT_USER_KEY, sidStr)) return false;
    if (!ConvertStringSidToSidW(sidStr.c_str(), &_userSid)) return false;

    _handler = handler;
    _stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!_stopEvent) return false;

    _running = true;
    _thread = CreateThread(nullptr, 0, [](LPVOID p) -> DWORD {
        ((PipeServer *)p)->Run();
        return 0;
    }, this, 0, nullptr);
    if (!_thread) { _running = false; return false; }
    return true;
}

void PipeServer::Stop()
{
    if (!_running) return;
    _running = false;
    if (_stopEvent) SetEvent(_stopEvent);
    // Nudge a blocked ConnectNamedPipe by poking the pipe.
    HANDLE poke = CreateFileW(COUCHPILOT_PIPE_NAME, GENERIC_READ, 0, nullptr, OPEN_EXISTING, 0, nullptr);
    if (poke != INVALID_HANDLE_VALUE) CloseHandle(poke);
    if (_thread) { WaitForSingleObject(_thread, 5000); CloseHandle(_thread); _thread = nullptr; }
    if (_stopEvent) { CloseHandle(_stopEvent); _stopEvent = nullptr; }
    SecureZeroMemory(&_secret[0], _secret.size() * sizeof(wchar_t));
    _secret.clear();
}

// A security descriptor that lets only SYSTEM and the enrolled user open the pipe.
static PSECURITY_DESCRIPTOR BuildPipeSd(PSID userSid, PACL *aclOut)
{
    PSID systemSid = nullptr;
    SID_IDENTIFIER_AUTHORITY nt = SECURITY_NT_AUTHORITY;
    if (!AllocateAndInitializeSid(&nt, 1, SECURITY_LOCAL_SYSTEM_RID, 0, 0, 0, 0, 0, 0, 0, &systemSid))
        return nullptr;

    EXPLICIT_ACCESSW ea[2];
    ZeroMemory(ea, sizeof(ea));
    ea[0].grfAccessPermissions = FILE_ALL_ACCESS;
    ea[0].grfAccessMode = SET_ACCESS;
    ea[0].grfInheritance = NO_INHERITANCE;
    ea[0].Trustee.TrusteeForm = TRUSTEE_IS_SID;
    ea[0].Trustee.TrusteeType = TRUSTEE_IS_USER;
    ea[0].Trustee.ptstrName = (LPWSTR)systemSid;

    ea[1].grfAccessPermissions = FILE_GENERIC_READ | FILE_GENERIC_WRITE;
    ea[1].grfAccessMode = SET_ACCESS;
    ea[1].grfInheritance = NO_INHERITANCE;
    ea[1].Trustee.TrusteeForm = TRUSTEE_IS_SID;
    ea[1].Trustee.TrusteeType = TRUSTEE_IS_USER;
    ea[1].Trustee.ptstrName = (LPWSTR)userSid;

    PACL acl = nullptr;
    if (SetEntriesInAclW(2, ea, nullptr, &acl) != ERROR_SUCCESS) { FreeSid(systemSid); return nullptr; }

    PSECURITY_DESCRIPTOR sd = (PSECURITY_DESCRIPTOR)LocalAlloc(LPTR, SECURITY_DESCRIPTOR_MIN_LENGTH);
    if (!sd || !InitializeSecurityDescriptor(sd, SECURITY_DESCRIPTOR_REVISION) ||
        !SetSecurityDescriptorDacl(sd, TRUE, acl, FALSE))
    {
        if (sd) LocalFree(sd);
        LocalFree(acl);
        FreeSid(systemSid);
        return nullptr;
    }
    FreeSid(systemSid);
    *aclOut = acl;
    return sd;
}

void PipeServer::Run()
{
    while (_running)
    {
        PACL acl = nullptr;
        PSECURITY_DESCRIPTOR sd = BuildPipeSd(_userSid, &acl);
        if (!sd) { Sleep(500); continue; }

        SECURITY_ATTRIBUTES sa;
        sa.nLength = sizeof(sa);
        sa.bInheritHandle = FALSE;
        sa.lpSecurityDescriptor = sd;

        HANDLE pipe = CreateNamedPipeW(
            COUCHPILOT_PIPE_NAME,
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            1, 8192, 8192, 0, &sa);

        LocalFree(sd);
        LocalFree(acl);

        if (pipe == INVALID_HANDLE_VALUE) { Sleep(500); continue; }

        if (ConnectNamedPipe(pipe, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED)
        {
            if (_running) ServeOne(pipe);
        }
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }
}

// Confirms the connected client runs as the enrolled user. The DACL already keeps others out;
// this is the second lock the doc promises.
static bool CallerIsEnrolledUser(HANDLE pipe, PSID enrolled)
{
    if (!ImpersonateNamedPipeClient(pipe)) return false;

    HANDLE token = nullptr;
    bool ok = false;
    if (OpenThreadToken(GetCurrentThread(), TOKEN_QUERY, TRUE, &token))
    {
        DWORD cb = 0;
        GetTokenInformation(token, TokenUser, nullptr, 0, &cb);
        if (cb)
        {
            std::vector<BYTE> buf(cb);
            if (GetTokenInformation(token, TokenUser, buf.data(), cb, &cb))
            {
                TOKEN_USER *tu = (TOKEN_USER *)buf.data();
                ok = EqualSid(tu->User.Sid, enrolled) == TRUE;
            }
        }
        CloseHandle(token);
    }
    RevertToSelf();
    return ok;
}

static void WriteResponse(HANDLE pipe, const char *json)
{
    DWORD written = 0;
    WriteFile(pipe, json, (DWORD)strlen(json), &written, nullptr);
    FlushFileBuffers(pipe);
}

void PipeServer::ServeOne(HANDLE pipe)
{
    BYTE buf[8192];
    DWORD read = 0;
    if (!ReadFile(pipe, buf, sizeof(buf) - 1, &read, nullptr) || read == 0)
        return;

    std::string json = Utf8Of(pipe, read, buf);

    if (!CallerIsEnrolledUser(pipe, _userSid))
    {
        WriteResponse(pipe, "{\"ok\":false,\"code\":\"denied\"}");
        return;
    }

    int v = 0;
    std::wstring secret, user, password;
    bool parsed = JsonInt(json, "v", v) &&
        JsonString(json, "secret", secret) &&
        JsonString(json, "user", user) &&
        JsonString(json, "password", password);

    // Wipe the raw request now that we've lifted what we need.
    SecureZeroMemory(buf, sizeof(buf));
    SecureZeroMemory(&json[0], json.size());

    if (!parsed)
    {
        WriteResponse(pipe, "{\"ok\":false,\"code\":\"bad-request\"}");
        if (!password.empty()) SecureZeroMemory(&password[0], password.size() * sizeof(wchar_t));
        return;
    }
    if (v != COUCHPILOT_PROTOCOL_VERSION)
    {
        WriteResponse(pipe, "{\"ok\":false,\"code\":\"bad-version\"}");
        SecureZeroMemory(&password[0], password.size() * sizeof(wchar_t));
        return;
    }
    if (!SecretEquals(secret, _secret))
    {
        WriteResponse(pipe, "{\"ok\":false,\"code\":\"denied\"}");
        SecureZeroMemory(&password[0], password.size() * sizeof(wchar_t));
        return;
    }

    UnlockOutcome outcome = _handler ? _handler(user, password) : UnlockOutcome::Busy;

    // The handler copied what it needs; clear ours.
    SecureZeroMemory(&password[0], password.size() * sizeof(wchar_t));
    SecureZeroMemory(&secret[0], secret.size() * sizeof(wchar_t));

    WriteResponse(pipe, outcome == UnlockOutcome::Ok ? "{\"ok\":true}" : "{\"ok\":false,\"code\":\"busy\"}");
}
