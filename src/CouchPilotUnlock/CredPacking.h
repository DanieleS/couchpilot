// Turning a username and password into the blob LSA unlocks with. These are the well-trodden
// helpers from Microsoft's credential-provider samples, trimmed to what the unlock path needs:
// an interactive KERB logon, the password run through CredProtect first, marshalled into one
// contiguous buffer for CPGSR_RETURN_CREDENTIAL_FINISHED.
#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <credentialprovider.h>
#include <ntsecapi.h>

// Splits "DOMAIN\\user" (or "user", or ".\\user") into its parts. A bare name yields an empty
// domain, which the caller turns into the local machine.
HRESULT SplitDomainAndUsername(_In_ PCWSTR qualified, _Outptr_ PWSTR *domain, _Outptr_ PWSTR *user);

// Fills a KERB_INTERACTIVE_UNLOCK_LOGON for the scenario, pointing its UNICODE_STRINGs at the
// given strings (no copy yet; Pack does the copying).
HRESULT KerbInteractiveUnlockLogonInit(
    _In_ PCWSTR domain, _In_ PCWSTR username, _In_ PCWSTR password,
    _In_ CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus,
    _Out_ KERB_INTERACTIVE_UNLOCK_LOGON *pkiul);

// Marshals the logon into one self-contained buffer (CoTaskMemAlloc'd) with the strings packed
// after the struct and the UNICODE_STRINGs' buffers turned into offsets, the way LSA expects.
HRESULT KerbInteractiveUnlockLogonPack(
    _In_ const KERB_INTERACTIVE_UNLOCK_LOGON &kiulIn,
    _Outptr_result_bytebuffer_(*pcb) BYTE **prgb, _Out_ DWORD *pcb);

// Runs the password through CredProtect if it isn't already protected, copying the result into
// the destination field. Keeps the plaintext from sitting in the marshalled buffer in the clear.
HRESULT ProtectIfNecessaryAndCopyPassword(
    _In_ PCWSTR password,
    _In_ CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus,
    _Outptr_ PWSTR *protectedPassword);

// Retrieves the authentication package id of the negotiate package, needed in the logon blob.
HRESULT RetrieveNegotiateAuthPackage(_Out_ ULONG *authPackage);
