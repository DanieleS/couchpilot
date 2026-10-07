// The hidden credential tile. It carries no visible fields and waits, doing nothing, until the
// provider hands it a username and password that arrived over the pipe; then its GetSerialization
// returns the packed KERB logon and Windows unlocks. See docs/unlock.md.
#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <credentialprovider.h>

class CouchPilotCredential : public ICredentialProviderCredential2
{
public:
    CouchPilotCredential();

    // IUnknown
    IFACEMETHODIMP_(ULONG) AddRef();
    IFACEMETHODIMP_(ULONG) Release();
    IFACEMETHODIMP QueryInterface(REFIID riid, void **ppv);

    // ICredentialProviderCredential
    IFACEMETHODIMP Advise(ICredentialProviderCredentialEvents *pcpce);
    IFACEMETHODIMP UnAdvise();
    IFACEMETHODIMP SetSelected(BOOL *pbAutoLogon);
    IFACEMETHODIMP SetDeselected();
    IFACEMETHODIMP GetFieldState(DWORD dwFieldID, CREDENTIAL_PROVIDER_FIELD_STATE *pcpfs,
        CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE *pcpfis);
    IFACEMETHODIMP GetStringValue(DWORD dwFieldID, PWSTR *ppwsz);
    IFACEMETHODIMP GetBitmapValue(DWORD dwFieldID, HBITMAP *phbmp);
    IFACEMETHODIMP GetCheckboxValue(DWORD dwFieldID, BOOL *pbChecked, PWSTR *ppwszLabel);
    IFACEMETHODIMP GetComboBoxValueCount(DWORD dwFieldID, DWORD *pcItems, DWORD *pdwSelectedItem);
    IFACEMETHODIMP GetComboBoxValueAt(DWORD dwFieldID, DWORD dwItem, PWSTR *ppwszItem);
    IFACEMETHODIMP GetSubmitButtonValue(DWORD dwFieldID, DWORD *pdwAdjacentTo);
    IFACEMETHODIMP SetStringValue(DWORD dwFieldID, PCWSTR pwz);
    IFACEMETHODIMP SetCheckboxValue(DWORD dwFieldID, BOOL bChecked);
    IFACEMETHODIMP SetComboBoxSelectedValue(DWORD dwFieldID, DWORD dwSelectedItem);
    IFACEMETHODIMP CommandLinkClicked(DWORD dwFieldID);
    IFACEMETHODIMP GetSerialization(CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE *pcpgsr,
        CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION *pcpcs, PWSTR *ppwszOptionalStatusText,
        CREDENTIAL_PROVIDER_STATUS_ICON *pcpsiOptionalStatusIcon);
    IFACEMETHODIMP ReportResult(NTSTATUS ntsStatus, NTSTATUS ntsSubstatus,
        PWSTR *ppwszOptionalStatusText, CREDENTIAL_PROVIDER_STATUS_ICON *pcpsiOptionalStatusIcon);

    // ICredentialProviderCredential2
    IFACEMETHODIMP GetUserSid(PWSTR *ppszSid);

    // Called from the provider (on the pipe thread). Stores the credentials and arms the tile;
    // the provider then signals LogonUI to submit. Returns false if an unlock is already armed.
    bool Arm(PCWSTR user, PCWSTR password);
    bool IsArmed() const { return _armed; }

    void SetScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus) { _cpus = cpus; }

private:
    ~CouchPilotCredential();
    void ClearCredentials();

    LONG _cRef;
    CREDENTIAL_PROVIDER_USAGE_SCENARIO _cpus;
    CRITICAL_SECTION _lock;
    volatile bool _armed;
    PWSTR _user;
    PWSTR _password;
};
