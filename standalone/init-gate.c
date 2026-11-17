/* Observe the real game's IL2CPP initialization return, before permitting any
   managed work. Patch only the verified original Unity module's import slot;
   never force-load GameAssembly or initialize the VM on our own thread. */
#include "runtime.h"
#include <string.h>
#include <wchar.h>
static FARPROC(WINAPI*resolve_original)(HMODULE,LPCSTR);
static const char *init_name,*utf16_name,*shutdown_name;
static int(*initialize_original)(const char*);
static int(*initialize_utf16_original)(const wchar_t*);
static void(*shutdown_original)(void);
static volatile LONG vm_state;
static SRWLOCK lifecycle=SRWLOCK_INIT;
int rt_vm_state(void){return InterlockedCompareExchange(&vm_state,0,0);}
int rt_vm_enter(void){
    if(rt_vm_state()!=1)return 0;AcquireSRWLockShared(&lifecycle);
    if(rt_vm_state()!=1){ReleaseSRWLockShared(&lifecycle);return 0;}return 1;
}
void rt_vm_leave(void){ReleaseSRWLockShared(&lifecycle);}
static int initialize(const char*name){rt_boot_log("Game IL2CPP initializer entered");int result=initialize_original(name);rt_boot_log(result?"Game IL2CPP initializer returned success":"Game IL2CPP initializer returned failure");InterlockedExchange(&vm_state,result?1:-1);return result;}
static int initialize_utf16(const wchar_t*name){rt_boot_log("Game UTF16 IL2CPP initializer entered");int result=initialize_utf16_original(name);rt_boot_log(result?"Game UTF16 IL2CPP initializer returned success":"Game UTF16 IL2CPP initializer returned failure");InterlockedExchange(&vm_state,result?1:-1);return result;}
static void shutdown_vm(void){
    InterlockedExchange(&vm_state,-2);AcquireSRWLockExclusive(&lifecycle);
    if(rt_status){rt_status->capabilities=0;rt_detail(RT_STOPPED,"Game VM is shutting down; standalone managed access stopped");}
    shutdown_original();ReleaseSRWLockExclusive(&lifecycle);
}
static FARPROC WINAPI resolve_gate(HMODULE target,LPCSTR symbol){
    FARPROC result=resolve_original(target,symbol);if(!result||(uintptr_t)symbol<=65535)return result;
    wchar_t path[MAX_PATH];DWORD length=GetModuleFileNameW(target,path,MAX_PATH);if(!length||length>=MAX_PATH)return result;
    wchar_t*name=wcsrchr(path,L'\\');name=name?name+1:path;if(lstrcmpiW(name,L"GameAssembly.dll"))return result;
    if(!strcmp(symbol,init_name)){initialize_original=(void*)result;return (FARPROC)initialize;}
    if(!strcmp(symbol,utf16_name)){initialize_utf16_original=(void*)result;return (FARPROC)initialize_utf16;}
    if(!strcmp(symbol,shutdown_name)){shutdown_original=(void*)result;return (FARPROC)shutdown_vm;}
    return result;
}
static int fits(uint32_t rva,size_t bytes,uint32_t size){return rva<size&&bytes<=size-rva;}
static const char*image_string(unsigned char*base,uint32_t rva,uint32_t size){if(!fits(rva,1,size))return 0;return memchr(base+rva,0,(size-rva)>256?256:size-rva)?(const char*)base+rva:0;}
int rt_init_gate_install(HMODULE unity,const char*ascii,const char*utf16,const char*stop){
    if(!unity||!ascii||!utf16||!stop||resolve_original)return 0;
    unsigned char*base=(unsigned char*)unity;IMAGE_DOS_HEADER*dos=(void*)base;
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<64||dos->e_lfanew>4096)return 0;
    IMAGE_NT_HEADERS64*nt=(void*)(base+dos->e_lfanew);
    if(nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64)return 0;
    uint32_t size=nt->OptionalHeader.SizeOfImage;IMAGE_DATA_DIRECTORY imports=nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if(size>128*1024*1024||!fits(imports.VirtualAddress,imports.Size,size)||imports.Size<sizeof(IMAGE_IMPORT_DESCRIPTOR))return 0;
    void**slot=0;
    for(uint32_t n=0;n<imports.Size/sizeof(IMAGE_IMPORT_DESCRIPTOR);n++){
        IMAGE_IMPORT_DESCRIPTOR*entry=(void*)(base+imports.VirtualAddress+n*sizeof(*entry));if(!entry->Name)break;
        const char*library=image_string(base,entry->Name,size);if(!library)return 0;
        if(lstrcmpiA(library,"KERNEL32.dll")&&lstrcmpiA(library,"KERNELBASE.dll"))continue;
        if(!entry->OriginalFirstThunk||!entry->FirstThunk)return 0;
        for(uint32_t i=0;i<8192;i++){
            if(!fits(entry->OriginalFirstThunk+i*8,8,size)||!fits(entry->FirstThunk+i*8,8,size))return 0;
            uint64_t value=*(uint64_t*)(base+entry->OriginalFirstThunk+i*8);if(!value)break;if(value&IMAGE_ORDINAL_FLAG64)continue;
            if(value>UINT32_MAX-2)return 0;const char*name=image_string(base,(uint32_t)value+2,size);if(!name)return 0;
            if(!strcmp(name,"GetProcAddress")){if(slot)return 0;slot=(void**)(base+entry->FirstThunk+i*8);}
        }
    }
    if(!slot||!*slot)return 0;
    init_name=ascii;utf16_name=utf16;shutdown_name=stop;resolve_original=(void*)*slot;
    DWORD previous=0,ignored=0;if(!VirtualProtect(slot,sizeof(*slot),PAGE_READWRITE,&previous)){resolve_original=0;return 0;}
    InterlockedExchangePointer((void*volatile*)slot,resolve_gate);
    if(!VirtualProtect(slot,sizeof(*slot),previous,&ignored)){InterlockedExchangePointer((void*volatile*)slot,(void*)resolve_original);resolve_original=0;return 0;}
    return 1;
}
