#include "Credential.h"
#include "Common.h"
#include "Guid.h"
#include "CredPacking.h"

#include <ntsecapi.h>

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
    : _cRef(1), _cpus(CPUS_INVALID), _armed(false), _user(nullptr), _password(nullptr)
{
    InitializeCriticalSection(&_lock);
}

CouchPilotCredential::~CouchPilotCredential()
{
    ClearCredentials();
    DeleteCriticalSection(&_lock);
}

void CouchPilotCredential::ClearCredentials()
{
    EnterCriticalSection(&_lock);
    if (_password)
    {
        SecureZeroMemory(_password, wcslen(_password) * sizeof(wchar_t));
        CoTaskMemFree(_password);
        _password = nullptr;
    }
    if (_user) { CoTaskMemFree(_user); _user = nullptr; }
    _armed = false;
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
    LeaveCriticalSection(&_lock);
    return ok;
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
        pcpcs->cb = cb;
        *pcpgsr = CPGSR_RETURN_CREDENTIAL_FINISHED;
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
    // Not tied to a particular user tile: the account comes from the phone at submit time.
    *ppszSid = nullptr;
    return S_FALSE;
}
