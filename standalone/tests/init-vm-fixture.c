#include <windows.h>
#include <string.h>
__declspec(dllexport) int test_vm_init(const char*name){Sleep(100);return strcmp(name,"fail")!=0;}
__declspec(dllexport) int test_vm_init_utf16(const wchar_t*name){(void)name;return 1;}
__declspec(dllexport) void test_vm_shutdown(void){}
__declspec(dllexport) int test_other(void){return 123;}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID reserved){(void)instance;(void)reason;(void)reserved;return TRUE;}
