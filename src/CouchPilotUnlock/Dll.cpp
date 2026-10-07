// The DLL's COM shell: the module ref count, the class factory, and the two exports LogonUI's
// host calls to make a provider. Registration lives in the setup helper, not here, so the DLL
// stays free of self-registration code it would run as SYSTEM.
#include "Guid.h"
#include "Provider.h"

#include <new>

static LONG g_cRef = 0;     // outstanding objects + server locks; DllCanUnloadNow watches it
HINSTANCE g_hInst = nullptr;

void DllAddRef() { InterlockedIncrement(&g_cRef); }
void DllRelease() { InterlockedDecrement(&g_cRef); }

class ClassFactory : public IClassFactory
{
public:
    ClassFactory() : _cRef(1) {}

    IFACEMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&_cRef); }
    IFACEMETHODIMP_(ULONG) Release()
    {
        LONG n = InterlockedDecrement(&_cRef);
        if (n == 0) delete this;
        return n;
    }
    IFACEMETHODIMP QueryInterface(REFIID riid, void **ppv)
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory)
        {
            *ppv = static_cast<IClassFactory *>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }

    IFACEMETHODIMP CreateInstance(IUnknown *pUnkOuter, REFIID riid, void **ppv)
    {
        if (pUnkOuter) return CLASS_E_NOAGGREGATION;
        CouchPilotProvider *p = new (std::nothrow) CouchPilotProvider();
        if (!p) return E_OUTOFMEMORY;
        HRESULT hr = p->QueryInterface(riid, ppv);
        p->Release();
        return hr;
    }

    IFACEMETHODIMP LockServer(BOOL lock)
    {
        if (lock) DllAddRef(); else DllRelease();
        return S_OK;
    }

private:
    ~ClassFactory() {}
    LONG _cRef;
};

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void **ppv)
{
    if (rclsid != CLSID_CouchPilotUnlock)
        return CLASS_E_CLASSNOTAVAILABLE;

    ClassFactory *factory = new (std::nothrow) ClassFactory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(riid, ppv);
    factory->Release();
    return hr;
}

STDAPI DllCanUnloadNow()
{
    return (g_cRef == 0) ? S_OK : S_FALSE;
}

BOOL WINAPI DllMain(HINSTANCE hInst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_hInst = hInst;
        DisableThreadLibraryCalls(hInst);
    }
    return TRUE;
}
