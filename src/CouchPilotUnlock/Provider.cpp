#include "Provider.h"
#include "Common.h"

#include <new>

// Defined in Credential.cpp.
const CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR *CouchPilotFieldDescriptors();
// Defined in Dll.cpp: keep the module loaded while any provider is alive.
extern void DllAddRef();
extern void DllRelease();

CouchPilotProvider::CouchPilotProvider()
    : _cRef(1), _cpus(CPUS_INVALID), _cred(nullptr), _pEvents(nullptr), _adviseContext(0)
{
    DllAddRef();
    InitializeCriticalSection(&_lock);
}

CouchPilotProvider::~CouchPilotProvider()
{
    _pipe.Stop();
    if (_cred) { _cred->Release(); _cred = nullptr; }
    if (_pEvents) { _pEvents->Release(); _pEvents = nullptr; }
    DeleteCriticalSection(&_lock);
    DllRelease();
}

// IUnknown
IFACEMETHODIMP_(ULONG) CouchPilotProvider::AddRef() { return InterlockedIncrement(&_cRef); }
IFACEMETHODIMP_(ULONG) CouchPilotProvider::Release()
{
    LONG n = InterlockedDecrement(&_cRef);
    if (n == 0) delete this;
    return n;
}
IFACEMETHODIMP CouchPilotProvider::QueryInterface(REFIID riid, void **ppv)
{
    if (!ppv) return E_POINTER;
    if (riid == IID_IUnknown || riid == IID_ICredentialProvider)
    {
        *ppv = static_cast<ICredentialProvider *>(this);
        AddRef();
        return S_OK;
    }
    *ppv = nullptr;
    return E_NOINTERFACE;
}

bool CouchPilotProvider::HandlesScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus) const
{
    return cpus == CPUS_LOGON || cpus == CPUS_UNLOCK_WORKSTATION;
}

IFACEMETHODIMP CouchPilotProvider::SetUsageScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, DWORD)
{
    // Only sign-in and unlock. CredUI and change-password are none of our business.
    if (!HandlesScenario(cpus))
        return E_NOTIMPL;

    _cpus = cpus;
    if (!_cred)
    {
        _cred = new (std::nothrow) CouchPilotCredential();
        if (!_cred) return E_OUTOFMEMORY;
    }
    _cred->SetScenario(cpus);

    // Start listening. If it can't (unlocking isn't enrolled, or the secret's gone), we simply
    // never arm: the tile stays hidden and the normal sign-in methods are unaffected.
    _pipe.Start([this](const std::wstring &user, const std::wstring &password) {
        return OnUnlock(user, password);
    });
    return S_OK;
}

IFACEMETHODIMP CouchPilotProvider::SetSerialization(const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION *)
{
    return E_NOTIMPL;
}

IFACEMETHODIMP CouchPilotProvider::Advise(ICredentialProviderEvents *pcpe, UINT_PTR upAdviseContext)
{
    EnterCriticalSection(&_lock);
    if (_pEvents) _pEvents->Release();
    _pEvents = pcpe;
    if (_pEvents) _pEvents->AddRef();
    _adviseContext = upAdviseContext;
    LeaveCriticalSection(&_lock);
    return S_OK;
}

IFACEMETHODIMP CouchPilotProvider::UnAdvise()
{
    EnterCriticalSection(&_lock);
    if (_pEvents) { _pEvents->Release(); _pEvents = nullptr; }
    _adviseContext = 0;
    LeaveCriticalSection(&_lock);
    return S_OK;
}

IFACEMETHODIMP CouchPilotProvider::GetFieldDescriptorCount(DWORD *pdwCount)
{
    *pdwCount = CPUF_NUM_FIELDS;
    return S_OK;
}

IFACEMETHODIMP CouchPilotProvider::GetFieldDescriptorAt(DWORD dwIndex,
    CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR **ppcpfd)
{
    if (dwIndex >= CPUF_NUM_FIELDS || !ppcpfd) return E_INVALIDARG;

    const CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR &src = CouchPilotFieldDescriptors()[dwIndex];
    CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR *out =
        (CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR *)CoTaskMemAlloc(sizeof(*out));
    if (!out) return E_OUTOFMEMORY;

    out->dwFieldID = src.dwFieldID;
    out->cpft = src.cpft;
    out->guidFieldType = src.guidFieldType;
    out->pszLabel = nullptr;
    if (src.pszLabel)
    {
        size_t cb = (wcslen(src.pszLabel) + 1) * sizeof(wchar_t);
        out->pszLabel = (LPWSTR)CoTaskMemAlloc(cb);
        if (!out->pszLabel) { CoTaskMemFree(out); return E_OUTOFMEMORY; }
        memcpy(out->pszLabel, src.pszLabel, cb);
    }
    *ppcpfd = out;
    return S_OK;
}

IFACEMETHODIMP CouchPilotProvider::GetCredentialCount(DWORD *pdwCount, DWORD *pdwDefault,
    BOOL *pbAutoLogonWithDefault)
{
    *pdwCount = (_cred != nullptr) ? 1 : 0;

    // Armed: become the default tile and ask LogonUI to log on with it straight away. That is what
    // turns "credentials arrived over the pipe" into an actual unlock, no click involved.
    if (_cred && _cred->IsArmed())
    {
        *pdwDefault = 0;
        *pbAutoLogonWithDefault = TRUE;
    }
    else
    {
        *pdwDefault = CREDENTIAL_PROVIDER_NO_DEFAULT;
        *pbAutoLogonWithDefault = FALSE;
    }
    return S_OK;
}

IFACEMETHODIMP CouchPilotProvider::GetCredentialAt(DWORD dwIndex, ICredentialProviderCredential **ppcpc)
{
    if (dwIndex != 0 || !_cred || !ppcpc) return E_INVALIDARG;
    return _cred->QueryInterface(IID_ICredentialProviderCredential, (void **)ppcpc);
}

UnlockOutcome CouchPilotProvider::OnUnlock(const std::wstring &user, const std::wstring &password)
{
    if (!_cred) return UnlockOutcome::Busy;
    if (!_cred->Arm(user.c_str(), password.c_str()))
        return UnlockOutcome::Busy;

    // Wake LogonUI: it re-reads GetCredentialCount, sees us armed and auto-logon, and submits.
    EnterCriticalSection(&_lock);
    ICredentialProviderEvents *events = _pEvents;
    UINT_PTR ctx = _adviseContext;
    if (events) events->AddRef();
    LeaveCriticalSection(&_lock);

    if (events)
    {
        events->CredentialsChanged(ctx);
        events->Release();
    }
    return UnlockOutcome::Ok;
}
