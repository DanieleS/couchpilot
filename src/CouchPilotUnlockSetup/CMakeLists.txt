# The elevated setup helper. Manifested requireAdministrator, so launching it raises one UAC
# prompt; CouchPilot runs it to register or unregister the credential provider.
cmake_minimum_required(VERSION 3.20)
project(CouchPilotUnlockSetup CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_executable(CouchPilotUnlockSetup Main.cpp)

target_compile_definitions(CouchPilotUnlockSetup PRIVATE UNICODE _UNICODE)
target_link_libraries(CouchPilotUnlockSetup PRIVATE advapi32 shell32 ole32)

# Require elevation and keep it a console app with a Unicode entry point.
set_target_properties(CouchPilotUnlockSetup PROPERTIES
    OUTPUT_NAME "CouchPilotUnlockSetup"
    LINK_FLAGS "/MANIFESTUAC:\"level='requireAdministrator' uiAccess='false'\" /ENTRY:wmainCRTStartup"
)
