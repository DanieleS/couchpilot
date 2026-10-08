#include "Credential.h"
#include "Common.h"
#include "Guid.h"
#include "CredPacking.h"

#include <ntsecapi.h>
#include <new>

// Defined in Dll.cpp: keep the module loaded while a credential (or its watchdog thread) lives.
extern void DllAddRef();
extern void DllRelease();

// The one field: a hidden label. LogonUI requires at least one descriptor; the user never sees it.
// const_cast so the string literal fits pszLabel's LPWSTR without a conversion warning; the array
// is const and nothing writes through it.
static const CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR s_fields[CPUF_NUM_FIELDS] =
{
    { CPUF_LABEL, CPFT_LARGE_TEXT, const_cast<LPWSTR>(L"CouchPilot") },
};

const CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR *CouchPilotFieldDescriptors() { return s_fields; }

static HRESULT CopyString(PCWSTR in, PWSTR *out)
{
    size_t cb = (wcslen(in) + 1) * sizeof(wchar_t);
    *out = (PWSTR)CoTaskMemAlloc(cb);
    if (!*out) return E_OUTOFMEMORY;
    memcpy(*out, in, cb);
    return S_OK;
}

CouchPilotCredential::CouchPilotCredential()
    : _cRef(1), _cpus(CPUS_INVALID), _armed(false), _armGen(0),
      _user(nullptr), _password(nullptr), _userSid(nullptr)
{
    DllAddRef();
    InitializeCriticalSection(&_lock);
}

CouchPilotCredential::~CouchPilotCredential()
{
    ClearCredentials();
    if (_userSid) { CoTaskMemFree(_userSid); _userSid = nullptr; }
    DeleteCriticalSection(&_lock);
    DllRelease();
}

void CouchPilotCredential::ClearLocked()
{
    if (_password)
    {
        SecureZeroMemory(_password, wcslen(_password) * sizeof(wchar_t));
        CoTaskMemFree(_password);
        _password = nullptr;
    }
    if (_user) { CoTaskMemFree(_user); _user = nullptr; }
    _armed = false;
}

void CouchPilotCredential::ClearCredentials()
{
    EnterCriticalSection(&_lock);
    ClearLocked();
    LeaveCriticalSection(&_lock);
}

void CouchPilotCredential::Disarm()
{
    EnterCriticalSection(&_lock);
    _armGen++;      // make any pending watchdog a no-op
    ClearLocked();
    LeaveCriticalSection(&_lock);
}

void CouchPilotCredential::SetUserSid(PCWSTR sid)
{
    EnterCriticalSection(&_lock);
    if (_userSid) { CoTaskMemFree(_userSid); _userSid = nullptr; }
    if (sid && *sid) CopyString(sid, &_userSid);
    LeaveCriticalSection(&_lock);
}

struct WatchdogArg { CouchPilotCredential *self; LONG gen; };

DWORD WINAPI CouchPilotCredential::WatchdogThunk(LPVOID param)
{
    WatchdogArg *arg = (WatchdogArg *)param;
    arg->self->Watchdog(arg->gen);
    arg->self->Release();   // the ref Arm took for this thread
    delete arg;
    return 0;
}

// Give LogonUI a while to consume the armed credential; if it never does (no Advise yet, the
// screen dimmed, another tile was chosen), clear it so the next request isn't refused as "busy".
void CouchPilotCredential::Watchdog(LONG gen)
{
    Sleep(ArmTimeoutMs);
    EnterCriticalSection(&_lock);
    if (_armed && _armGen == gen) ClearLocked();
    LeaveCriticalSection(&_lock);
}

bool CouchPilotCredential::Arm(PCWSTR user, PCWSTR password)
{
    EnterCriticalSection(&_lock);
    if (_armed) { LeaveCriticalSection(&_lock); return false; }

    if (_user) { CoTaskMemFree(_user); _user = nullptr; }
    if (_password) { CoTaskMemFree(_password); _password = nullptr; }

    bool ok = SUCCEEDED(CopyString(user, &_user)) && SUCCEEDED(CopyString(password, &_password));
    _armed = ok;
    LONG gen = ++_armGen;
    LeaveCriticalSection(&_lock);
    if (!ok) { ClearCredentials(); return false; }

    // Watchdog holds a ref so it can't touch a freed credential.
    AddRef();
    WatchdogArg *arg = new (std::nothrow) WatchdogArg{ this, gen };
    HANDLE t = arg ? CreateThread(nullptr, 0, WatchdogThunk, arg, 0, nullptr) : nullptr;
    if (t) CloseHandle(t);
    else { if (arg) delete arg; Release(); }
    return true;
}

// IUnknown
IFACEMETHODIMP_(ULONG) CouchPilotCredential::AddRef() { return InterlockedIncrement(&_cRef); }
IFACEMETHODIMP_(ULONG) CouchPilotCredential::Release()
{
    LONG n = InterlockedDecrement(&_cRef);
    if (n == 0) delete this;
    return n;
}
IFACEMETHODIMP CouchPilotCredential::QueryInterface(REFIID riid, void **ppv)
{
    if (!ppv) return E_POINTER;
    if (riid == IID_IUnknown ||
        riid == IID_ICredentialProviderCredential ||
        riid == IID_ICredentialProviderCredential2)
    {
        *ppv = static_cast<ICredentialProviderCredential2 *>(this);
        AddRef();
        return S_OK;
    }
    *ppv = nullptr;
    return E_NOINTERFACE;
}

IFACEMETHODIMP CouchPilotCredential::Advise(ICredentialProviderCredentialEvents *) { return S_OK; }
IFACEMETHODIMP CouchPilotCredential::UnAdvise() { return S_OK; }

IFACEMETHODIMP CouchPilotCredential::SetSelected(BOOL *pbAutoLogon)
{
    // When armed, selecting the tile means "go": LogonUI will call GetSerialization at once.
    *pbAutoLogon = _armed ? TRUE : FALSE;
    return S_OK;
}
IFACEMETHODIMP CouchPilotCredential::SetDeselected() { return S_OK; }

IFACEMETHODIMP CouchPilotCredential::GetFieldState(DWORD dwFieldID,
    CREDENTIAL_PROVIDER_FIELD_STATE *pcpfs, CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE *pcpfis)
{
    if (dwFieldID >= CPUF_NUM_FIELDS) return E_INVALIDARG;
    *pcpfs = CPFS_HIDDEN;            // nothing of ours ever shows
    *pcpfis = CPFIS_NONE;
    return S_OK;
}

IFACEMETHODIMP CouchPilotCredential::GetStringValue(DWORD dwFieldID, PWSTR *ppwsz)
{
    if (dwFieldID >= CPUF_NUM_FIELDS || !ppwsz) return E_INVALIDARG;
    return CopyString(L"", ppwsz);
}

IFACEMETHODIMP CouchPilotCredential::GetBitmapValue(DWORD, HBITMAP *) { return E_NOTIMPL; }
IFACEMETHODIMP CouchPilotCredential::GetCheckboxValue(DWORD, BOOL *, PWSTR *) { return E_NOTIMPL; }
IFACEMETHODIMP CouchPilotCredential::GetComboBoxValueCount(DWORD, DWORD *, DWORD *) { return E_NOTIMPL; }
IFACEMETHODIMP CouchPilotCredential::GetComboBoxValueAt(DWORD, DWORD, PWSTR *) { return E_NOTIMPL; }
IFACEMETHODIMP CouchPilotCredential::GetSubmitButtonValue(DWORD, DWORD *) { return E_NOTIMPL; }
IFACEMETHODIMP CouchPilotCredential::SetStringValue(DWORD, PCWSTR) { return E_NOTIMPL; }
IFACEMETHODIMP CouchPilotCredential::SetCheckboxValue(DWORD, BOOL) { return E_NOTIMPL; }
IFACEMETHODIMP CouchPilotCredential::SetComboBoxSelectedValue(DWORD, DWORD) { return E_NOTIMPL; }
IFACEMETHODIMP CouchPilotCredential::CommandLinkClicked(DWORD) { return E_NOTIMPL; }

IFACEMETHODIMP CouchPilotCredential::GetSerialization(
    CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE *pcpgsr,
    CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION *pcpcs,
    PWSTR *ppwszOptionalStatusText, CREDENTIAL_PROVIDER_STATUS_ICON *pcpsiOptionalStatusIcon)
{
    if (ppwszOptionalStatusText) *ppwszOptionalStatusText = nullptr;
    if (pcpsiOptionalStatusIcon) *pcpsiOptionalStatusIcon = CPSI_NONE;

    EnterCriticalSection(&_lock);
    if (!_armed || !_user || !_password)
    {
        LeaveCriticalSection(&_lock);
        *pcpgsr = CPGSR_NO_CREDENTIAL_NOT_FINISHED;
        return S_OK;
    }

    PWSTR domain = nullptr, user = nullptr, protectedPassword = nullptr;
    HRESULT hr = SplitDomainAndUsername(_user, &domain, &user);
    if (SUCCEEDED(hr))
        hr = ProtectIfNecessaryAndCopyPassword(_password, _cpus, &protectedPassword);
    LeaveCriticalSection(&_lock);

    KERB_INTERACTIVE_UNLOCK_LOGON kiul;
    if (SUCCEEDED(hr))
        hr = KerbInteractiveUnlockLogonInit(domain, user, protectedPassword, _cpus, &kiul);

    ULONG authPackage = 0;
    if (SUCCEEDED(hr))
        hr = RetrieveNegotiateAuthPackage(&authPackage);

    BYTE *rgb = nullptr;
    DWORD cb = 0;
    if (SUCCEEDED(hr))
        hr = KerbInteractiveUnlockLogonPack(kiul, &rgb, &cb);

    if (SUCCEEDED(hr))
    {
        pcpcs->ulAuthenticationPackage = authPackage;
        pcpcs->clsidCredentialProvider = CLSID_CouchPilotUnlock;
        pcpcs->rgbSerialization = rgb;
        pcpcs->cbSerialization = cb;
        *pcpgsr = CPGSR_RETURN_CREDENTIAL_FINISHED;
    }
    else
    {
        // Packing failed: tell LogonUI there's nothing to submit (so it doesn't act on an
        // uninitialised response) and show why. Returning S_OK keeps the lock screen healthy.
        *pcpgsr = CPGSR_NO_CREDENTIAL_NOT_FINISHED;
        if (ppwszOptionalStatusText && pcpsiOptionalStatusIcon &&
            SUCCEEDED(CopyString(L"CouchPilot couldn't prepare the sign-in.", ppwszOptionalStatusText)))
        {
            *pcpsiOptionalStatusIcon = CPSI_ERROR;
        }
        if (rgb) CoTaskMemFree(rgb);
        hr = S_OK;
    }

    if (domain) CoTaskMemFree(domain);
    if (user) CoTaskMemFree(user);
    if (protectedPassword)
    {
        SecureZeroMemory(protectedPassword, wcslen(protectedPassword) * sizeof(wchar_t));
        CoTaskMemFree(protectedPassword);
    }
    // Spent, win or lose: a single request never submits twice.
    ClearCredentials();
    return hr;
}

IFACEMETHODIMP CouchPilotCredential::ReportResult(NTSTATUS, NTSTATUS,
    PWSTR *ppwszOptionalStatusText, CREDENTIAL_PROVIDER_STATUS_ICON *pcpsiOptionalStatusIcon)
{
    if (ppwszOptionalStatusText) *ppwszOptionalStatusText = nullptr;
    if (pcpsiOptionalStatusIcon) *pcpsiOptionalStatusIcon = CPSI_NONE;
    ClearCredentials();
    return S_OK;
}

IFACEMETHODIMP CouchPilotCredential::GetUserSid(PWSTR *ppszSid)
{
    *ppszSid = nullptr;
    EnterCriticalSection(&_lock);
    HRESULT hr = S_FALSE;   // no SID known: a generic, "other user" credential
    if (_userSid)
    {
        if (SUCCEEDED(CopyString(_userSid, ppszSid))) hr = S_OK;
    }
    LeaveCriticalSection(&_lock);
    // Binding to the enrolled user's SID keeps the tile off "Other user" and lets auto-logon
    // land on the locked user's own tile.
    return hr;
}
