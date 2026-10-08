# The native half of CouchPilot: the lock-screen credential provider and its elevated setup
# helper. The C# extension in CouchPilot/ builds with dotnet, not this. The build workflow
# configures and builds this with MSVC on windows-latest and drops the two binaries into the
# extension's output so they ride along in the .pext.
cmake_minimum_required(VERSION 3.20)
project(CouchPilotNative CXX)

add_subdirectory(CouchPilotUnlock)
add_subdirectory(CouchPilotUnlockSetup)
