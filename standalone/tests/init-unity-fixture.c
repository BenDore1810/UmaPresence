#include <windows.h>
__declspec(dllexport) FARPROC fixture_resolve(HMODULE module,const char*name){return GetProcAddress(module,name);}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID reserved){(void)instance;(void)reason;(void)reserved;return TRUE;}
