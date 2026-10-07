# The credential provider DLL. Built with MSVC on the windows-latest runner; see the build
# workflow. Produces CouchPilotUnlock.dll, which the setup helper registers.
cmake_minimum_required(VERSION 3.20)
project(CouchPilotUnlock CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(CouchPilotUnlock SHARED
    Dll.cpp
    Provider.cpp
    Credential.cpp
    CredPacking.cpp
    PipeServer.cpp
    CouchPilotUnlock.def
)

target_compile_definitions(CouchPilotUnlock PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN)

# Unicode entry point aside, the libraries the provider leans on: COM, LSA/secur32, credentials,
# and ACL/SID helpers (advapi32, pulled by PipeServer and CredPacking's pragmas too).
target_link_libraries(CouchPilotUnlock PRIVATE
    advapi32
    secur32
    credui
    ole32
)

set_target_properties(CouchPilotUnlock PROPERTIES
    OUTPUT_NAME "CouchPilotUnlock"
    PREFIX ""
)
