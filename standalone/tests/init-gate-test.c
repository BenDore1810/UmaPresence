/* Native fixtures only; never load the installed game's modules. */
#include "../runtime.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
#undef assert
#define assert(condition) do{if(!(condition)){fprintf(stderr,"FAIL line %d: %s (Win32=%lu)\n",__LINE__,#condition,GetLastError());return 1;}}while(0)
volatile RuntimeStatus*rt_status;
void rt_detail(uint32_t state,const char*text){(void)state;(void)text;}
void rt_boot_log(const char*text){(void)text;}
static int(*initialize)(const char*);
static DWORD WINAPI invoke_init(void*ignored){(void)ignored;assert(initialize("fixture")==1);return 0;}
static HANDLE scope_started;
static DWORD WINAPI hold_scope(void*ignored){(void)ignored;assert(rt_vm_enter());SetEvent(scope_started);Sleep(100);rt_vm_leave();return 0;}
int main(int argc,char**argv){
    setvbuf(stdout,0,_IONBF,0);
    HMODULE vm=LoadLibraryW(L"dist\\init-gate-fixture\\GameAssembly.dll"),unity=LoadLibraryW(L"dist\\init-gate-fixture\\fake-unity.dll");assert(vm&&unity);
    FARPROC(*resolve)(HMODULE,const char*)=(void*)GetProcAddress(unity,"fixture_resolve");assert(resolve);
    assert(!rt_vm_enter()&&rt_vm_state()==0);puts("PASS: managed scope is unavailable before VM initialization");
    assert(rt_init_gate_install(unity,"test_vm_init","test_vm_init_utf16","test_vm_shutdown"));puts("PASS: bounded Unity import patch installs on own fixture DLL");
    int(*other)(void)=(void*)resolve(vm,"test_other");assert(other()==123);puts("PASS: unrelated export resolution is unchanged");
    if(argc>1&&!strcmp(argv[1],"--utf16")){
        int(*wide)(const wchar_t*)=(void*)resolve(vm,"test_vm_init_utf16");assert(wide(L"fixture")==1&&rt_vm_state()==1);puts("PASS: UTF16 initializer is also observed after its actual return");return 0;
    }
    initialize=(void*)resolve(vm,"test_vm_init");assert((void*)initialize!=(void*)GetProcAddress(vm,"test_vm_init"));
    if(argc>1&&!strcmp(argv[1],"--fail")){assert(initialize("fail")==0&&rt_vm_state()==-1&&!rt_vm_enter());puts("PASS: initializer failure never enables managed work");return 0;}
    HANDLE thread=CreateThread(0,0,invoke_init,0,0,0);assert(thread);Sleep(20);assert(rt_vm_state()==0&&!rt_vm_enter());puts("PASS: VM stays unavailable while the real initializer is still running");
    assert(WaitForSingleObject(thread,2000)==WAIT_OBJECT_0);CloseHandle(thread);assert(rt_vm_state()==1&&rt_vm_enter());rt_vm_leave();puts("PASS: managed work is permitted only after successful initializer return");
    scope_started=CreateEventW(0,TRUE,FALSE,0);HANDLE active=CreateThread(0,0,hold_scope,0,0,0);assert(active&&WaitForSingleObject(scope_started,1000)==WAIT_OBJECT_0);
    void(*shutdown)(void)=(void*)resolve(vm,"test_vm_shutdown");ULONGLONG before=GetTickCount64();shutdown();assert(GetTickCount64()-before>=50&&WaitForSingleObject(active,1000)==WAIT_OBJECT_0);CloseHandle(active);CloseHandle(scope_started);puts("PASS: shutdown waits for outstanding managed scopes to leave");
    assert(rt_vm_state()==-2&&!rt_vm_enter());puts("PASS: managed work is blocked during and after VM shutdown");
    FreeLibrary(unity);FreeLibrary(vm);return 0;
}
