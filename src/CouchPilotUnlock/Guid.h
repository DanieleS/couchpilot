// CouchPilot's own credential provider identity. Fixed: the extension and the setup helper
// register and look it up by this CLSID, so it must never change.
//
//   {B6F4C3A2-7E51-4A90-9C3D-1F8E2A6B5D04}
#pragma once

#include <initguid.h>

DEFINE_GUID(CLSID_CouchPilotUnlock,
    0xb6f4c3a2, 0x7e51, 0x4a90, 0x9c, 0x3d, 0x1f, 0x8e, 0x2a, 0x6b, 0x5d, 0x04);
