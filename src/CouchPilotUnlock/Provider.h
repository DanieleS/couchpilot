// CouchPilot's credential provider. It takes part only in signing in and unlocking, contributes
// one hidden credential, and runs the pipe server. When CouchPilot sends credentials over the
// pipe it arms the credential and tells LogonUI to submit it, with no tile ever shown or clicked.
#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <credentialprovider.h>

#include "Credential.h"
#include "PipeServer.h"

class CouchPilotProvider : public ICredentialProvider
{
public:
    CouchPilotProvider();

    // IUnknown
    IFACEMETHODIMP_(ULONG) AddRef();
    IFACEMETHODIMP_(ULONG) Release();
    IFACEMETHODIMP QueryInterface(REFIID riid, void **ppv);

    // ICredentialProvider
    IFACEMETHODIMP SetUsageScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, DWORD dwFlags);
    IFACEMETHODIMP SetSerialization(const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION *pcpcs);
    IFACEMETHODIMP Advise(ICredentialProviderEvents *pcpe, UINT_PTR upAdviseContext);
    IFACEMETHODIMP UnAdvise();
    IFACEMETHODIMP GetFieldDescriptorCount(DWORD *pdwCount);
    IFACEMETHODIMP GetFieldDescriptorAt(DWORD dwIndex, CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR **ppcpfd);
    IFACEMETHODIMP GetCredentialCount(DWORD *pdwCount, DWORD *pdwDefault, BOOL *pbAutoLogonWithDefault);
    IFACEMETHODIMP GetCredentialAt(DWORD dwIndex, ICredentialProviderCredential **ppcpc);

private:
    ~CouchPilotProvider();

    // The pipe handler: arm the credential and wake LogonUI.
    UnlockOutcome OnUnlock(const std::wstring &user, const std::wstring &password);
    bool HandlesScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus) const;

    LONG _cRef;
    CREDENTIAL_PROVIDER_USAGE_SCENARIO _cpus;
    CouchPilotCredential *_cred;
    PipeServer _pipe;

    CRITICAL_SECTION _lock;
    ICredentialProviderEvents *_pEvents;
    UINT_PTR _adviseContext;
};
