#include "CredPacking.h"

#define SECURITY_WIN32
#include <security.h>     // NEGOSSP_NAME_A
#include <intsafe.h>
#include <wincred.h>

#pragma comment(lib, "secur32.lib")
#pragma comment(lib, "credui.lib")

static HRESULT DupString(_In_ PCWSTR in, _Outptr_ PWSTR *out)
{
    size_t cb = (wcslen(in) + 1) * sizeof(wchar_t);
    *out = (PWSTR)CoTaskMemAlloc(cb);
    if (!*out) return E_OUTOFMEMORY;
    memcpy(*out, in, cb);
    return S_OK;
}

// A UNICODE_STRING pointing into an existing buffer (no allocation).
static void InitUnicodeString(_Out_ UNICODE_STRING *us, _In_opt_ PCWSTR src)
{
    if (src)
    {
        size_t len = wcslen(src);
        us->Length = (USHORT)(len * sizeof(wchar_t));
        us->MaximumLength = (USHORT)((len + 1) * sizeof(wchar_t));
        us->Buffer = (PWSTR)src;
    }
    else
    {
        us->Length = 0;
        us->MaximumLength = 0;
        us->Buffer = nullptr;
    }
}

HRESULT SplitDomainAndUsername(_In_ PCWSTR qualified, _Outptr_ PWSTR *domain, _Outptr_ PWSTR *user)
{
    *domain = nullptr;
    *user = nullptr;

    const wchar_t *slash = wcschr(qualified, L'\\');
    if (slash)
    {
        size_t dlen = (size_t)(slash - qualified);
        // "." stands for the local machine: leave the domain empty so LSA uses this computer.
        bool localDot = (dlen == 1 && qualified[0] == L'.');

        PWSTR d = (PWSTR)CoTaskMemAlloc((dlen + 1) * sizeof(wchar_t));
        if (!d) return E_OUTOFMEMORY;
        if (localDot) { d[0] = L'\0'; }
        else { memcpy(d, qualified, dlen * sizeof(wchar_t)); d[dlen] = L'\0'; }

        HRESULT hr = DupString(slash + 1, user);
        if (FAILED(hr)) { CoTaskMemFree(d); return hr; }
        *domain = d;
        return S_OK;
    }

    // No domain: empty domain, the whole string is the user.
    PWSTR d = (PWSTR)CoTaskMemAlloc(sizeof(wchar_t));
    if (!d) return E_OUTOFMEMORY;
    d[0] = L'\0';
    HRESULT hr = DupString(qualified, user);
    if (FAILED(hr)) { CoTaskMemFree(d); return hr; }
    *domain = d;
    return S_OK;
}

HRESULT RetrieveNegotiateAuthPackage(_Out_ ULONG *authPackage)
{
    *authPackage = 0;
    HANDLE lsa = nullptr;
    NTSTATUS status = LsaConnectUntrusted(&lsa);
    if (status != 0) return HRESULT_FROM_NT(status);

    ULONG package = 0;
    LSA_STRING name;
    const char *neg = NEGOSSP_NAME_A;
    name.Buffer = (PCHAR)neg;
    name.Length = (USHORT)strlen(neg);
    name.MaximumLength = (USHORT)(name.Length + 1);

    status = LsaLookupAuthenticationPackage(lsa, &name, &package);
    LsaDeregisterLogonProcess(lsa);
    if (status != 0) return HRESULT_FROM_NT(status);

    *authPackage = package;
    return S_OK;
}

HRESULT ProtectIfNecessaryAndCopyPassword(
    _In_ PCWSTR password,
    _In_ CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus,
    _Outptr_ PWSTR *protectedPassword)
{
    *protectedPassword = nullptr;
    if (!password || !*password)
    {
        return DupString(L"", protectedPassword);
    }

    // Already protected (we never hand it one, but the contract allows it): copy as is.
    CRED_PROTECTION_TYPE protectionType;
    if (CredIsProtectedW((PWSTR)password, &protectionType) &&
        (protectionType == CredUsernameTarget || protectionType == CredTrustedProtection))
    {
        return DupString(password, protectedPassword);
    }

    // CredUI and change-password want the plaintext; logon and unlock want it protected.
    bool wantProtected = (cpus != CPUS_CREDUI && cpus != CPUS_CHANGE_PASSWORD);
    if (!wantProtected)
    {
        return DupString(password, protectedPassword);
    }

    DWORD cch = 0;
    CredProtectW(FALSE, (PWSTR)password, (DWORD)wcslen(password) + 1, nullptr, &cch, nullptr);
    if (cch == 0) return HRESULT_FROM_WIN32(GetLastError());

    PWSTR out = (PWSTR)CoTaskMemAlloc(cch * sizeof(wchar_t));
    if (!out) return E_OUTOFMEMORY;

    if (!CredProtectW(FALSE, (PWSTR)password, (DWORD)wcslen(password) + 1, out, &cch, nullptr))
    {
        CoTaskMemFree(out);
        return HRESULT_FROM_WIN32(GetLastError());
    }
    *protectedPassword = out;
    return S_OK;
}

HRESULT KerbInteractiveUnlockLogonInit(
    _In_ PCWSTR domain, _In_ PCWSTR username, _In_ PCWSTR password,
    _In_ CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus,
    _Out_ KERB_INTERACTIVE_UNLOCK_LOGON *pkiul)
{
    ZeroMemory(pkiul, sizeof(*pkiul));

    KERB_INTERACTIVE_LOGON *pkil = &pkiul->Logon;
    InitUnicodeString(&pkil->LogonDomainName, domain);
    InitUnicodeString(&pkil->UserName, username);
    InitUnicodeString(&pkil->Password, password);

    switch (cpus)
    {
    case CPUS_UNLOCK_WORKSTATION:
        pkil->MessageType = KerbWorkstationUnlockLogon;
        break;
    case CPUS_LOGON:
        pkil->MessageType = KerbInteractiveLogon;
        break;
    default:
        pkil->MessageType = KerbInteractiveLogon;
        break;
    }
    return S_OK;
}

// Copies a UNICODE_STRING's buffer into the packed region and rebases its pointer to an offset.
static void PackUnicodeString(
    _In_ const UNICODE_STRING &src, _Inout_ UNICODE_STRING &dst,
    _In_ BYTE *base, _Inout_ ULONG_PTR &offset)
{
    dst.Length = src.Length;
    dst.MaximumLength = src.Length;
    if (src.Length)
    {
        memcpy(base + offset, src.Buffer, src.Length);
        dst.Buffer = (PWSTR)offset;   // offset from the start of the blob; LSA rebases it.
        offset += src.Length;
    }
    else
    {
        dst.Buffer = nullptr;
    }
}

HRESULT KerbInteractiveUnlockLogonPack(
    _In_ const KERB_INTERACTIVE_UNLOCK_LOGON &kiulIn,
    _Outptr_result_bytebuffer_(*pcb) BYTE **prgb, _Out_ DWORD *pcb)
{
    *prgb = nullptr;
    *pcb = 0;

    const KERB_INTERACTIVE_LOGON &in = kiulIn.Logon;

    DWORD cb = sizeof(KERB_INTERACTIVE_UNLOCK_LOGON) +
        in.LogonDomainName.Length + in.UserName.Length + in.Password.Length;

    BYTE *rgb = (BYTE *)CoTaskMemAlloc(cb);
    if (!rgb) return E_OUTOFMEMORY;

    KERB_INTERACTIVE_UNLOCK_LOGON *out = (KERB_INTERACTIVE_UNLOCK_LOGON *)rgb;
    ZeroMemory(out, cb);
    out->Logon.MessageType = in.MessageType;

    ULONG_PTR offset = sizeof(KERB_INTERACTIVE_UNLOCK_LOGON);
    PackUnicodeString(in.LogonDomainName, out->Logon.LogonDomainName, rgb, offset);
    PackUnicodeString(in.UserName, out->Logon.UserName, rgb, offset);
    PackUnicodeString(in.Password, out->Logon.Password, rgb, offset);

    *prgb = rgb;
    *pcb = cb;
    return S_OK;
}
