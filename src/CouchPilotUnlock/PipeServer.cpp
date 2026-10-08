#include "PipeServer.h"
#include "Common.h"

#include <ntsecapi.h>
#include <sddl.h>
#include <aclapi.h>
#include <bcrypt.h>
#include <wincrypt.h>
#include <vector>
#include <string>

#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "crypt32.lib")

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
    SecureZeroMemory(data->Buffer, data->MaximumLength);
    LsaFreeMemory(data);
    return !value.empty();
}

// ---- crypto -----------------------------------------------------------------------------------

static std::string NarrowAscii(const std::wstring &w)
{
    std::string s;
    s.reserve(w.size());
    for (wchar_t c : w) s.push_back((char)(c & 0x7f));
    return s;
}

static std::string Utf8Of(const std::wstring &w)
{
    if (w.empty()) return std::string();
    int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), nullptr, 0, nullptr, nullptr);
    std::string s(n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), &s[0], n, nullptr, nullptr);
    return s;
}

static std::string Base64(const BYTE *data, DWORD len)
{
    DWORD cch = 0;
    if (!CryptBinaryToStringA(data, len, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, nullptr, &cch))
        return std::string();
    std::string out(cch, '\0');
    if (!CryptBinaryToStringA(data, len, CRYPT_STRING_BASE64 | CRYPT_STRING_NOCRLF, &out[0], &cch))
        return std::string();
    out.resize(cch);
    return out;
}

// HMAC-SHA256(key, data) → 32 bytes, base64. The key is the UTF-8 of the base64 secret; the data
// is the client's nonce. Only the real provider, holding the LSA secret, can produce this, which
// is how CouchPilot tells the genuine server from a pipe squatter before it sends the password.
static std::string HmacProof(const std::string &key, const std::string &data)
{
    BCRYPT_ALG_HANDLE alg = nullptr;
    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, BCRYPT_ALG_HANDLE_HMAC_FLAG) != 0)
        return std::string();

    std::string proof;
    BCRYPT_HASH_HANDLE hash = nullptr;
    if (BCryptCreateHash(alg, &hash, nullptr, 0, (PUCHAR)key.data(), (ULONG)key.size(), 0) == 0)
    {
        BYTE out[32];
        if (BCryptHashData(hash, (PUCHAR)data.data(), (ULONG)data.size(), 0) == 0 &&
            BCryptFinishHash(hash, out, sizeof(out), 0) == 0)
        {
            proof = Base64(out, sizeof(out));
        }
        BCryptDestroyHash(hash);
    }
    BCryptCloseAlgorithmProvider(alg, 0);
    return proof;
}

// ---- tiny JSON (requests are produced by CouchPilot, flat objects of strings and one int) ------

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

// ---- the server -------------------------------------------------------------------------------

PipeServer::~PipeServer()
{
    Stop();
    if (_userSid) LocalFree(_userSid);
}

bool PipeServer::Start(Handler handler)
{
    if (_running) return true;

    if (!ReadLsaSecret(COUCHPILOT_SECRET_KEY, _secret)) return false;
    if (!ReadLsaSecret(COUCHPILOT_USER_KEY, _userSidString)) return false;
    if (!ConvertStringSidToSidW(_userSidString.c_str(), &_userSid)) return false;

    _handler = handler;
    _stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);  // manual-reset
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
    if (_stopEvent) SetEvent(_stopEvent);     // wakes the overlapped waits and the retry sleep
    if (_thread) { WaitForSingleObject(_thread, INFINITE); CloseHandle(_thread); _thread = nullptr; }
    if (_stopEvent) { CloseHandle(_stopEvent); _stopEvent = nullptr; }
    if (!_secret.empty()) SecureZeroMemory(&_secret[0], _secret.size() * sizeof(wchar_t));
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

// Waits for an overlapped op, or for Stop. Returns 1 done (bytes in *transferred), 0 stop, -1 error.
static int AwaitOverlapped(HANDLE pipe, HANDLE stopEvent, OVERLAPPED *ov, DWORD *transferred)
{
    HANDLE waits[2] = { ov->hEvent, stopEvent };
    DWORD w = WaitForMultipleObjects(2, waits, FALSE, INFINITE);
    if (w == WAIT_OBJECT_0 + 1) { CancelIoEx(pipe, ov); return 0; }
    if (w != WAIT_OBJECT_0) return -1;
    DWORD n = 0;
    if (!GetOverlappedResult(pipe, ov, &n, FALSE)) return -1;
    if (transferred) *transferred = n;
    return 1;
}

void PipeServer::Run()
{
    while (_running)
    {
        PACL acl = nullptr;
        PSECURITY_DESCRIPTOR sd = BuildPipeSd(_userSid, &acl);
        if (!sd) { WaitForSingleObject(_stopEvent, 500); continue; }

        SECURITY_ATTRIBUTES sa;
        sa.nLength = sizeof(sa);
        sa.bInheritHandle = FALSE;
        sa.lpSecurityDescriptor = sd;

        // FIRST_PIPE_INSTANCE: if someone squatted the name, this fails rather than sharing it —
        // we never serve, so nothing is disclosed, and the normal sign-in methods still work.
        HANDLE pipe = CreateNamedPipeW(
            COUCHPILOT_PIPE_NAME,
            PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            1, 8192, 8192, 0, &sa);

        LocalFree(sd);
        LocalFree(acl);

        if (pipe == INVALID_HANDLE_VALUE) { WaitForSingleObject(_stopEvent, 500); continue; }

        HANDLE ioEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!ioEvent) { CloseHandle(pipe); WaitForSingleObject(_stopEvent, 500); continue; }

        OVERLAPPED ov;
        ZeroMemory(&ov, sizeof(ov));
        ov.hEvent = ioEvent;

        BOOL connected = ConnectNamedPipe(pipe, &ov);
        DWORD err = GetLastError();
        bool ready = false;
        if (connected) ready = true;
        else if (err == ERROR_PIPE_CONNECTED) ready = true;
        else if (err == ERROR_IO_PENDING)
        {
            int r = AwaitOverlapped(pipe, _stopEvent, &ov, nullptr);
            if (r == 0) { CloseHandle(ioEvent); CloseHandle(pipe); break; }  // stop
            ready = (r == 1);
        }

        if (ready && _running) ServeOne(pipe, ioEvent);

        DisconnectNamedPipe(pipe);
        CloseHandle(ioEvent);
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

// Reads one message into a std::string, honouring Stop. Returns false on stop or error.
static bool ReadMsg(HANDLE pipe, HANDLE stopEvent, HANDLE ioEvent, std::string &out)
{
    BYTE buf[8192];
    ResetEvent(ioEvent);
    OVERLAPPED ov; ZeroMemory(&ov, sizeof(ov)); ov.hEvent = ioEvent;
    DWORD read = 0;
    BOOL ok = ReadFile(pipe, buf, sizeof(buf), &read, &ov);
    if (!ok && GetLastError() == ERROR_IO_PENDING)
    {
        if (AwaitOverlapped(pipe, stopEvent, &ov, &read) != 1) return false;
        ok = TRUE;
    }
    if (!ok || read == 0) { SecureZeroMemory(buf, sizeof(buf)); return false; }
    out.assign((const char *)buf, read);
    SecureZeroMemory(buf, sizeof(buf));
    return true;
}

static bool WriteMsg(HANDLE pipe, HANDLE stopEvent, HANDLE ioEvent, const char *json)
{
    ResetEvent(ioEvent);
    OVERLAPPED ov; ZeroMemory(&ov, sizeof(ov)); ov.hEvent = ioEvent;
    DWORD written = 0;
    BOOL ok = WriteFile(pipe, json, (DWORD)strlen(json), &written, &ov);
    if (!ok && GetLastError() == ERROR_IO_PENDING)
        ok = (AwaitOverlapped(pipe, stopEvent, &ov, &written) == 1);
    return ok == TRUE;
}

void PipeServer::ServeOne(HANDLE pipe, HANDLE ioEvent)
{
    // 1. The client's hello with its nonce.
    std::string hello;
    if (!ReadMsg(pipe, _stopEvent, ioEvent, hello)) return;

    if (!CallerIsEnrolledUser(pipe, _userSid))
    {
        WriteMsg(pipe, _stopEvent, ioEvent, "{\"ok\":false,\"code\":\"denied\"}");
        return;
    }

    int v = 0;
    std::wstring nonce;
    if (!JsonInt(hello, "v", v) || !JsonString(hello, "nonce", nonce))
    {
        WriteMsg(pipe, _stopEvent, ioEvent, "{\"ok\":false,\"code\":\"bad-request\"}");
        return;
    }
    if (v != COUCHPILOT_PROTOCOL_VERSION)
    {
        WriteMsg(pipe, _stopEvent, ioEvent, "{\"ok\":false,\"code\":\"bad-version\"}");
        return;
    }

    // 2. Prove we hold the secret over the client's nonce, so it knows we're the real provider
    //    before it sends anything sensitive.
    std::string proof = HmacProof(Utf8Of(_secret), NarrowAscii(nonce));
    if (proof.empty()) return;
    std::string challenge = "{\"proof\":\"" + proof + "\"}";
    if (!WriteMsg(pipe, _stopEvent, ioEvent, challenge.c_str())) return;

    // 3. The credentials, now that we're trusted.
    std::string creds;
    if (!ReadMsg(pipe, _stopEvent, ioEvent, creds)) return;

    std::wstring user, password;
    bool parsed = JsonString(creds, "user", user) && JsonString(creds, "password", password);
    SecureZeroMemory(&creds[0], creds.size());

    if (!parsed)
    {
        WriteMsg(pipe, _stopEvent, ioEvent, "{\"ok\":false,\"code\":\"bad-request\"}");
        if (!password.empty()) SecureZeroMemory(&password[0], password.size() * sizeof(wchar_t));
        return;
    }

    UnlockOutcome outcome = _handler ? _handler(user, password) : UnlockOutcome::Busy;
    SecureZeroMemory(&password[0], password.size() * sizeof(wchar_t));

    WriteMsg(pipe, _stopEvent, ioEvent,
        outcome == UnlockOutcome::Ok ? "{\"ok\":true}" : "{\"ok\":false,\"code\":\"busy\"}");
}
