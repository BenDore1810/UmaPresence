/* GPL-3.0. Small host for existing UmaPresence readers. No translations,
   downloader, browser, arbitrary plugin loading or diagnostic file interface. */
#include "runtime.h"
#include "generated/game-profile.h"
#include "pe-identity.h"
#include <bcrypt.h>
#include <string.h>
#include <tlhelp32.h>
#include <wchar.h>
#include <sddl.h>

#pragma comment(lib,"bcrypt.lib")
void *original_unitymain, *original_unitymain2;
volatile RuntimeStatus *rt_status;
volatile RuntimeAudioTrace *rt_audio_trace;
static HMODULE original,assembly,module;
static wchar_t root[MAX_PATH],runtime_directory[MAX_PATH];
static HANDLE status_mapping,command_event;
static volatile LONG booted,scheduled,command_pending;
static RuntimeCommand pending_command;
static void (*volatile sample_callback)(void*);
static void (*present_callback)(void*,void*);
static void *present_userdata;
static RP domain,main_thread,context,post_method,callback_delegate;
static uint32_t callback_handle;
static RP (*object_class)(RP),(*invoke)(RP,RP,RP*,RP*),(*method_from_name)(RP,const char*,int),(*parent_class)(RP);
static const char *(*class_name)(RP);
static RP (*field_from_name)(RP,const char*);
static void (*field_value)(RP,RP,RP);
static int (*field_offset)(RP),(*class_size)(RP);
static uint32_t (*gc_handle_new)(RP,int);
static RP (*object_new)(RP),(*unbox)(RP);
static void (*detach_thread)(RP);
static RP (*attach_thread)(RP);
static RP (*current_thread)(void);
static int bootstrap_verified;
static int equivalent_game_build;
static HANDLE verified_assembly = INVALID_HANDLE_VALUE;
static const char*bootstrap_failure="Standalone bootstrap was not validated";
typedef struct { RP attached; } ManagedScope;
static int enter_managed(ManagedScope*scope){
    scope->attached=0;if(!rt_vm_enter())return 0;
    if(!current_thread||!attach_thread||!detach_thread||!domain){rt_vm_leave();return 0;}
    if(!current_thread()){
        RP attached=attach_thread(domain);
        if(!attached||current_thread()!=attached){rt_detail(RT_INCOMPATIBLE,"IL2CPP thread attachment could not be verified; managed access stopped");rt_vm_leave();return 0;}
        scope->attached=attached;
    }
    return 1;
}
static void leave_managed(ManagedScope*scope){if(scope->attached)detach_thread(scope->attached);rt_vm_leave();}
int rt_managed_thread(void){return rt_vm_state()==1&&current_thread&&current_thread()!=0;}
static volatile LONG ownership_blocked;
int rt_owned(void){return !InterlockedCompareExchange(&ownership_blocked,0,0);}
static SECURITY_ATTRIBUTES private_security;
static PSECURITY_DESCRIPTOR private_descriptor;
static int prepare_security(void){
    HANDLE token=0;DWORD size=0;TOKEN_USER *user=0;LPWSTR sid=0;wchar_t sddl[256];int ok=0;
    if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token))return 0;
    GetTokenInformation(token,TokenUser,0,0,&size);if(!size||size>65536)goto done;
    user=HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,size);if(!user||!GetTokenInformation(token,TokenUser,user,size,&size)||!ConvertSidToStringSidW(user->User.Sid,&sid))goto done;
    if(lstrlenW(sid)>190)goto done;wsprintfW(sddl,L"D:P(A;;GA;;;%s)",sid);
    if(!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl,SDDL_REVISION_1,&private_descriptor,0))goto done;
    private_security=(SECURITY_ATTRIBUTES){sizeof(SECURITY_ATTRIBUTES),private_descriptor,FALSE};ok=1;
done:if(sid)LocalFree(sid);if(user)HeapFree(GetProcessHeap(),0,user);if(token)CloseHandle(token);return ok;
}

/* Inspect loaded modules, never on-disk configuration: a pending profile change
   does not describe the owner in this running process. Unknown competing Unity
   proxies also fail closed. Original Unity and our own forwarding proxy are exempt. */
static int exclusive_owner(void){
    if(InterlockedCompareExchange(&ownership_blocked,0,0))return 0;
    HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPMODULE,GetCurrentProcessId());
    int conflict=0,valid=0;MODULEENTRY32W entry;ZeroMemory(&entry,sizeof(entry));entry.dwSize=sizeof(entry);
    if(snapshot!=INVALID_HANDLE_VALUE){
        if(Module32FirstW(snapshot,&entry)){valid=1;do{
            if(entry.hModule==module||entry.hModule==original)continue;
            wchar_t lower[MAX_PATH];lstrcpynW(lower,entry.szModule,MAX_PATH);CharLowerBuffW(lower,lstrlenW(lower));
            if(wcsstr(lower,L"hachimi")||wcsstr(lower,L"unityplayer")){conflict=1;break;}
        }while(Module32NextW(snapshot,&entry));if(!conflict&&GetLastError()!=ERROR_NO_MORE_FILES)valid=0;}
        CloseHandle(snapshot);
    }
    if(!valid||conflict){
        InterlockedExchange(&ownership_blocked,1);
        if(rt_status){rt_status->capabilities=0;rt_status->backend=2;rt_detail(RT_CONFLICT,conflict?"Competing game integration detected; standalone controls and hooks suspended until game exit":"Loaded integration ownership could not be verified; standalone suspended until game exit");}
        return 0;
    }
    return 1;
}

int rt_same(const char*a,const char*b){return a&&b&&strcmp(a,b)==0;}
static void path_join(wchar_t*out,const wchar_t*a,const wchar_t*b){lstrcpynW(out,a,MAX_PATH);if(lstrlenW(out)+lstrlenW(b)+2>=MAX_PATH){out[0]=0;return;}lstrcatW(out,L"\\");lstrcatW(out,b);}
static void directory_of(wchar_t*path){int n=lstrlenW(path);while(n>0&&path[n-1]!=L'\\')n--;if(n)path[n-1]=0;}
void rt_boot_log(const char*text){
    if(!root[0])return;wchar_t path[MAX_PATH];path_join(path,root,L"umapresence\\runtime-startup.log");DWORD attributes=GetFileAttributesW(path);if(attributes!=INVALID_FILE_ATTRIBUTES&&(attributes&FILE_ATTRIBUTE_REPARSE_POINT))return;
    HANDLE file=CreateFileW(path,FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,0,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,0);if(file==INVALID_HANDLE_VALUE)return;
    LARGE_INTEGER size;if(GetFileSizeEx(file,&size)&&size.QuadPart<65536){SYSTEMTIME now;GetSystemTime(&now);char message[320];int n=wsprintfA(message,"%04u-%02u-%02u %02u:%02u:%02u UTC pid=%lu thread=%lu | ",now.wYear,now.wMonth,now.wDay,now.wHour,now.wMinute,now.wSecond,GetCurrentProcessId(),GetCurrentThreadId());lstrcpynA(message+n,text,192);lstrcatA(message,"\r\n");DWORD written;WriteFile(file,message,lstrlenA(message),&written,0);}
    CloseHandle(file);
}
void rt_detail(uint32_t state,const char*text){rt_boot_log(text);if(!rt_status)return;InterlockedIncrement((volatile LONG*)&rt_status->sequence);rt_status->state=state;lstrcpynA((char*)rt_status->detail,text,192);MemoryBarrier();InterlockedIncrement((volatile LONG*)&rt_status->sequence);}
static int read_hash(HANDLE file,char result[65]){
    BCRYPT_ALG_HANDLE algorithm=0;BCRYPT_HASH_HANDLE hash=0;unsigned char digest[32],buffer[65536];DWORD read=0;int good=0;
    if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,0,0)<0)goto done;
    if(BCryptCreateHash(algorithm,&hash,0,0,0,0,0)<0)goto done;
    for(;;){
        if(!ReadFile(file,buffer,sizeof(buffer),&read,0))goto done;
        if(!read)break;
        if(BCryptHashData(hash,buffer,read,0)<0)goto done;
    }
    if(BCryptFinishHash(hash,digest,32,0)<0)goto done;
    static const char digits[]="0123456789abcdef";
    for(int i=0;i<32;i++){result[i*2]=digits[digest[i]>>4];result[i*2+1]=digits[digest[i]&15];}
    result[64]=0;good=1;
done:if(hash)BCryptDestroyHash(hash);if(algorithm)BCryptCloseAlgorithmProvider(algorithm,0);return good;
}
static HANDLE pin_file(const wchar_t *path){
    DWORD attributes=GetFileAttributesW(path);
    if(attributes==INVALID_FILE_ATTRIBUTES||(attributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT)))return INVALID_HANDLE_VALUE;
    return CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,0,OPEN_EXISTING,FILE_FLAG_SEQUENTIAL_SCAN,0);
}
static int file_hash(const wchar_t *path,const char *expected){
    HANDLE file=pin_file(path);
    if(file==INVALID_HANDLE_VALUE)return 0;
    char digest[65];int match=read_hash(file,digest)&&rt_same(digest,expected);
    CloseHandle(file);return match;
}
static void release_verified_assembly(void){
    if(verified_assembly!=INVALID_HANDLE_VALUE)CloseHandle(verified_assembly);
    verified_assembly=INVALID_HANDLE_VALUE;
}
static int loaded_verified_assembly(HMODULE image){
    wchar_t path[MAX_PATH];
    DWORD length=image?GetModuleFileNameW(image,path,MAX_PATH):0;
    if(!length||length>=MAX_PATH)return 0;
    HANDLE loaded=pin_file(path);
    if(loaded==INVALID_HANDLE_VALUE)return 0;
    BY_HANDLE_FILE_INFORMATION expected,actual;
    int match=GetFileInformationByHandle(verified_assembly,&expected)
        &&GetFileInformationByHandle(loaded,&actual)
        &&expected.dwVolumeSerialNumber==actual.dwVolumeSerialNumber
        &&expected.nFileIndexHigh==actual.nFileIndexHigh&&expected.nFileIndexLow==actual.nFileIndexLow;
    CloseHandle(loaded);
    return match;
}
static int game_image_equivalent(HANDLE file,const char *expected){
    LARGE_INTEGER length;
    if(!expected||!GetFileSizeEx(file,&length)||length.QuadPart<64||length.QuadPart>0x40000000)return 0;
    HANDLE mapping=CreateFileMappingW(file,0,PAGE_READONLY,0,0,0);
    if(!mapping)return 0;
    const unsigned char *data=MapViewOfFile(mapping,FILE_MAP_READ,0,0,0);
    PeIdentity id;
    BCRYPT_ALG_HANDLE algorithm=0;BCRYPT_HASH_HANDLE hash=0;
    unsigned char digest[32];int match=0;
    if(data&&pe_identity_ranges(data,(size_t)length.QuadPart,&id)
        &&BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,0,0)>=0
        &&BCryptCreateHash(algorithm,&hash,0,0,0,0,0)>=0
        &&BCryptHashData(hash,(PUCHAR)data,(ULONG)id.checksum,0)>=0
        &&BCryptHashData(hash,(PUCHAR)data+id.checksum+4,(ULONG)(id.certificate_directory-id.checksum-4),0)>=0
        &&BCryptHashData(hash,(PUCHAR)data+id.certificate_directory+8,(ULONG)(id.content_end-id.certificate_directory-8),0)>=0
        &&BCryptFinishHash(hash,digest,sizeof(digest),0)>=0){
        static const char digits[]="0123456789abcdef";
        char actual[65];
        for(int i=0;i<32;i++){actual[i*2]=digits[digest[i]>>4];actual[i*2+1]=digits[digest[i]&15];}
        actual[64]=0;match=rt_same(actual,expected);
    }
    if(hash)BCryptDestroyHash(hash);
    if(algorithm)BCryptCloseAlgorithmProvider(algorithm,0);
    if(data)UnmapViewOfFile(data);
    CloseHandle(mapping);
    return match;
}
/* DotLocal redirects UnityPlayer.dll even by absolute path. Load the current
   official file through a different basename outside the immutable profile. */
static HMODULE load_current_engine(int *compatible){
    wchar_t official[MAX_PATH],directory[MAX_PATH],alias[MAX_PATH],staging[MAX_PATH];
    path_join(official,root,L"UnityPlayer.dll");
    path_join(directory,root,L"umapresence");
    path_join(alias,directory,L"UnityPlayer_current.dll");
    DWORD attributes=GetFileAttributesW(directory);
    if(!alias[0]||attributes==INVALID_FILE_ATTRIBUTES||!(attributes&FILE_ATTRIBUTE_DIRECTORY)||(attributes&FILE_ATTRIBUTE_REPARSE_POINT))return 0;
    HANDLE source=pin_file(official);
    if(source==INVALID_HANDLE_VALUE)return 0;
    HMODULE engine=0;
    char digest[65];
    if(read_hash(source,digest)){
        if(!file_hash(alias,digest)&&GetTempFileNameW(directory,L"upe",0,staging)){
            if(CopyFileW(official,staging,FALSE)&&file_hash(staging,digest))
                MoveFileExW(staging,alias,MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH);
            DeleteFileW(staging);
        }
        HANDLE current=pin_file(alias);
        if(current!=INVALID_HANDLE_VALUE){
            char actual[65];
            if(read_hash(current,actual)&&rt_same(digest,actual)){
                *compatible=rt_same(actual,PROFILE_UNITY_SHA256)?1:
                    game_image_equivalent(current,PROFILE_UNITY_COMPATIBILITYSHA256)?2:0;
                engine=LoadLibraryExW(alias,0,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_APPLICATION_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
            }
            CloseHandle(current);
        }
    }
    CloseHandle(source);return engine;
}
RP rt_resolve(const char*name){for(size_t i=0;i<sizeof(runtime_symbols)/sizeof(runtime_symbols[0]);i++)if(rt_same(name,runtime_symbols[i].name))return (RP)GetProcAddress(assembly,runtime_symbols[i].export_name);return 0;}
int rt_verify_asset(const char*digest,const char*sha,uint64_t expected_bytes){
    if(!digest||lstrlenA(digest)!=32)return 0;wchar_t relative[128]=L"umamusume_Data\\Persistent\\dat\\",path[MAX_PATH];int start=lstrlenW(relative);
    for(int i=0;i<32;i++)if(!((digest[i]>='A'&&digest[i]<='Z')||(digest[i]>='2'&&digest[i]<='7')))return 0;
    relative[start++]=digest[0];relative[start++]=digest[1];relative[start++]=L'\\';for(int i=0;i<32;i++)relative[start++]=digest[i];relative[start]=0;path_join(path,root,relative);
    WIN32_FILE_ATTRIBUTE_DATA info;if(!GetFileAttributesExW(path,GetFileExInfoStandard,&info)||(info.dwFileAttributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT)))return 0;
    uint64_t bytes=((uint64_t)info.nFileSizeHigh<<32)|info.nFileSizeLow;
    return sha?bytes<=8*1024*1024&&file_hash(path,sha):bytes==expected_bytes;
}
RP rt_image(const char*name){RP(*open)(RP,const char*)=rt_resolve("il2cpp_domain_assembly_open");RP(*image)(RP)=rt_resolve("il2cpp_assembly_get_image");if(!open||!image||!domain)return 0;RP a=open(domain,name);if(!a){char qualified[256];if(lstrlenA(name)>240)return 0;lstrcpyA(qualified,name);lstrcatA(qualified,".dll");a=open(domain,qualified);}return a?image(a):0;}
RP rt_class(RP image,const char*space,const char*name){RP(*find)(RP,const char*,const char*)=rt_resolve("il2cpp_class_from_name");return image&&find?find(image,space,name):0;}
RP rt_method_info(RP klass,const char*name,int count){return klass&&method_from_name?method_from_name(klass,name,count):0;}
RP rt_method(RP klass,const char*name,int count){RP method=rt_method_info(klass,name,count);return method?*(RP*)method:0;}
RP rt_field(RP klass,const char*name){for(int i=0;klass&&i<8;i++,klass=parent_class(klass)){RP f=field_from_name(klass,name);if(f)return f;}return 0;}
RP rt_object_field(RP object,const char*name){RP value=0,f=object?rt_field(object_class(object),name):0;RP(*field_type)(RP)=rt_resolve("il2cpp_field_get_type");int(*type)(RP)=rt_resolve("il2cpp_type_get_type");if(f&&field_type&&type){int t=type(field_type(f));if(t==0x12||t==0x15||t==0x1c||t==0x1d||t==0x0e)field_value(object,f,&value);}return value;}
int rt_int_field(RP object,const char*name,int*out){RP f=object?rt_field(object_class(object),name):0;RP(*field_type)(RP)=rt_resolve("il2cpp_field_get_type");int(*type)(RP)=rt_resolve("il2cpp_type_get_type");if(!f||!field_type||!type||type(field_type(f))!=8)return 0;field_value(object,f,out);return 1;}
RP rt_singleton(RP klass){RP method=rt_method_info(klass,"get_Instance",0),exception=0,value=method?invoke(method,0,0,&exception):0;return exception?0:value;}
static RP nested_class(RP klass,const char*name){RP(*next)(RP,RP*)=rt_resolve("il2cpp_class_get_nested_types");RP iterator=0,c;if(!next)return 0;while((c=next(klass,&iterator)))if(rt_same(class_name(c),name))return c;return 0;}
static RP get_main_thread(void){return main_thread;}
static void runtime_dispatch(RP ignored){
    (void)ignored;
    ManagedScope scope;if(!enter_managed(&scope)){InterlockedExchange(&scheduled,0);return;}
    if(current_thread()!=main_thread){rt_status->capabilities=0;rt_detail(RT_INCOMPATIBLE,"Managed dispatch arrived on an unexpected thread; game access disabled");InterlockedExchange(&scheduled,0);leave_managed(&scope);return;}
    if(InterlockedCompareExchange(&ownership_blocked,0,0)){
        if(InterlockedExchange(&command_pending,0)){rt_status->command_result=RESULT_UNAVAILABLE;SetEvent(command_event);}
        sample_callback=0;InterlockedExchange(&scheduled,0);leave_managed(&scope);return;
    }
    if(InterlockedCompareExchange(&command_pending,2,1)==1){
        RuntimeCommand command=pending_command;uint32_t result=rt_playback_command(&command);
        if(rt_status){rt_status->command_sequence=command.sequence;rt_status->command_result=result;}InterlockedExchange(&command_pending,0);SetEvent(command_event);
    }
    void(*callback)(void*)=InterlockedExchangePointer((void*volatile*)&sample_callback,0);if(callback)callback(0);
    rt_playback_tick();rt_mix_tick();InterlockedExchange(&scheduled,0);leave_managed(&scope);
}
static int post_dispatch(void){
    if(!context||!callback_delegate||InterlockedCompareExchange(&scheduled,1,0))return 0;
    ManagedScope scope;if(!enter_managed(&scope)){InterlockedExchange(&scheduled,0);return 0;}
    RP args[2]={callback_delegate,0},exception=0;invoke(post_method,context,args,&exception);
    leave_managed(&scope);if(exception){InterlockedExchange(&scheduled,0);return 0;}return 1;
}
static void schedule_callback(RP thread,void(*callback)(void)){if(thread!=main_thread||!rt_owned())return;InterlockedCompareExchangePointer((void*volatile*)&sample_callback,callback,0);}
static int register_present(void(*callback)(void*,void*),void*userdata){if(present_callback)return 0;present_callback=callback;present_userdata=userdata;return 1;}
static void host_log(int level,const char*target,const char*message){(void)level;(void)target;OutputDebugStringA(message);}
static RP host_lookup(const char*name){
    if(rt_same(name,"log"))return host_log;
    if(rt_same(name,"il2cpp_resolve_symbol"))return rt_resolve;
    if(rt_same(name,"il2cpp_get_assembly_image"))return rt_image;
    if(rt_same(name,"il2cpp_get_class"))return rt_class;
    if(rt_same(name,"il2cpp_get_method_addr"))return rt_method;
    if(rt_same(name,"il2cpp_find_nested_class"))return nested_class;
    if(rt_same(name,"il2cpp_get_singleton_like_instance"))return rt_singleton;
    if(rt_same(name,"il2cpp_get_main_thread"))return get_main_thread;
    if(rt_same(name,"il2cpp_schedule_on_thread"))return schedule_callback;
    if(rt_same(name,"hachimi_register_present_callback"))return register_present;
    return 0;
}
static int prepare_context(void){
    size_t count=0;RP*(*threads)(size_t*)=rt_resolve("il2cpp_thread_get_all_attached_threads");if(!threads)return 0;
    RP*all=threads(&count);if(!all||count<1||count>1024)return 0;
    for(size_t i=0;i<count;i++){
        RP thread=all[i],exception=0,method=rt_method_info(object_class(thread),"GetMutableExecutionContext",0);
        RP execution=method?invoke(method,thread,0,&exception):0;if(!execution||exception)continue;
        RP candidate=rt_object_field(execution,"_syncContext");if(!candidate||!rt_same(class_name(object_class(candidate)),"UnitySynchronizationContext"))continue;
        method=rt_method_info(object_class(thread),"get_ManagedThreadId",0);RP boxed=method?invoke(method,thread,0,&exception):0;
        int expected=0,actual=boxed&&unbox(boxed)?*(int*)unbox(boxed):0;
        if(exception||!actual||!rt_int_field(candidate,"m_MainThreadID",&expected)||actual!=expected)continue;
        main_thread=thread;context=candidate;break;
    }
    if(!context)return 0;
    post_method=rt_method_info(object_class(context),"Post",2);
    RP klass=rt_class(rt_image("mscorlib"),"System.Threading","SendOrPostCallback");
    RP constructor_info=rt_method_info(klass,".ctor",2),constructor=constructor_info?*(RP*)constructor_info:0,invoke_method=rt_method_info(klass,"Invoke",1);
    RP method_pointer=rt_field(klass,"method_ptr"),invoke_pointer=rt_field(klass,"invoke_impl");
    if(!post_method||!constructor||!invoke_method||!method_pointer||!invoke_pointer)return 0;
    int first=field_offset(method_pointer),second=field_offset(invoke_pointer),size=class_size(klass);
    if(first!=16||second!=24||size<32)return 0;
    callback_delegate=object_new(klass);if(!callback_delegate)return 0;
    ((void(*)(RP,RP,RP,RP))constructor)(callback_delegate,callback_delegate,invoke_method,constructor_info);
    *(RP*)((unsigned char*)callback_delegate+first)=runtime_dispatch;*(RP*)((unsigned char*)callback_delegate+second)=runtime_dispatch;
    callback_handle=gc_handle_new(callback_delegate,0);return callback_handle!=0;
}
static void initialize_bridge(void*ignored){
    if(!exclusive_owner())return;
    (void)ignored;wchar_t path[MAX_PATH];path_join(path,runtime_directory,L"umapyon_bridge.dll");HMODULE bridge=LoadLibraryW(path);
    int(*initialize)(const RuntimeServices*)=bridge?(int(*)(const RuntimeServices*))GetProcAddress(bridge,"umapresence_init_v1"):0;
    RuntimeServices services={sizeof(services),1,host_lookup,CAP_TELEMETRY};
    if(!initialize||!initialize(&services)){rt_detail(RT_INCOMPATIBLE,"Native reader initialization failed; game continues without telemetry");return;}
    rt_cue_initialize();rt_playback_initialize();rt_mix_initialize();
    rt_formation_initialize();
#ifdef UMAPRESENCE_PUBLIC_RELEASE
    rt_status->capabilities&=~(uint64_t)(CAP_MIX|CAP_ROUTING);
    rt_status->capabilities|=CAP_TELEMETRY;
    rt_detail(RT_READY,"UmaPresence 0.1.0-beta.1 Standalone; original audio; experimental features disabled");
#else
    rt_routing_initialize();rt_status->capabilities|=CAP_TELEMETRY;rt_detail(RT_READY,"Standalone vm-gate-v2 concert-v5 host initialized; live capability verification pending");
#endif
}
static DWORD WINAPI command_worker(void*unused){
    (void)unused;wchar_t name[128];wsprintfW(name,L"\\\\.\\pipe\\UmaPresence-Control-%lu",GetCurrentProcessId());
    for(;;){HANDLE pipe=CreateNamedPipeW(name,PIPE_ACCESS_DUPLEX,PIPE_TYPE_MESSAGE|PIPE_READMODE_MESSAGE|PIPE_WAIT|PIPE_REJECT_REMOTE_CLIENTS,1,256,256,1000,&private_security);if(pipe==INVALID_HANDLE_VALUE)return 0;
        if(ConnectNamedPipe(pipe,0)||GetLastError()==ERROR_PIPE_CONNECTED){RuntimeCommand command;DWORD read=0,written=0;
            if(ReadFile(pipe,&command,sizeof(command),&read,0)&&read==sizeof(command)){
                uint32_t result=RESULT_STALE;
                if(InterlockedCompareExchange(&ownership_blocked,0,0)||rt_vm_state()!=1)result=RESULT_UNAVAILABLE;
                else if(command.magic==0x55504331&&command.version==1&&command.pid==GetCurrentProcessId()&&rt_status&&command.nonce==rt_status->nonce){
                    if(command.command<CMD_PLAY||command.command>CMD_CANCEL_DRAG)result=RESULT_BAD_ARGUMENT;
                    else if(InterlockedCompareExchange(&command_pending,0,0))result=RESULT_UNAVAILABLE;
                    // Raw pipe thread never enters IL2CPP. The managed worker
                    // notices this queue within 25 ms and posts in an attach scope.
                    else {ResetEvent(command_event);pending_command=command;InterlockedExchange(&command_pending,1);if(WaitForSingleObject(command_event,2000)==WAIT_OBJECT_0)result=rt_status->command_result;else {InterlockedCompareExchange(&command_pending,0,1);result=RESULT_FAILED;}}
                }
                WriteFile(pipe,&result,4,&written,0);
            }
        }DisconnectNamedPipe(pipe);CloseHandle(pipe);
    }
}
static DWORD WINAPI worker(void*unused){
    (void)unused;wchar_t name[128];wsprintfW(name,L"Local\\UmaPresenceRuntime-%lu",GetCurrentProcessId());
    if(!prepare_security()){release_verified_assembly();return 0;}
    status_mapping=CreateFileMappingW(INVALID_HANDLE_VALUE,&private_security,PAGE_READWRITE,0,16384,name);rt_status=status_mapping?MapViewOfFile(status_mapping,FILE_MAP_ALL_ACCESS,0,0,16384):0;
    if(!rt_status){release_verified_assembly();return 0;}ZeroMemory((void*)rt_status,16384);rt_audio_trace=(RuntimeAudioTrace*)((char*)rt_status+256);rt_audio_trace->magic=0x55504131;rt_audio_trace->version=2;
    rt_status->magic=0x55505231;rt_status->version=1;rt_status->pid=GetCurrentProcessId();rt_status->backend=1;BCryptGenRandom(0,(PUCHAR)&rt_status->nonce,8,BCRYPT_USE_SYSTEM_PREFERRED_RNG);rt_detail(RT_STARTING,"Validating installed game build");
    if(!exclusive_owner()){release_verified_assembly();return 0;}
    if(!bootstrap_verified){rt_detail(RT_PROFILE_FAILED,bootstrap_failure);return 0;}
#ifndef UMAPRESENCE_PUBLIC_RELEASE
    rt_routing_prepare(); // File checks run on this worker, never a render/audio callback.
#endif
    rt_detail(RT_WAITING,equivalent_game_build?"Verified game image is unchanged; waiting for game initialization":"Waiting for the game's IL2CPP initialization to finish; no managed calls yet");
    for(int i=0;i<1200;i++){assembly=GetModuleHandleW(L"GameAssembly.dll");if(assembly)break;Sleep(100);}
    int correct_assembly=loaded_verified_assembly(assembly);
    release_verified_assembly();
    if(!correct_assembly){rt_detail(RT_INCOMPATIBLE,"The verified GameAssembly did not load; managed access disabled");return 0;}
    RP(*domain_get)(void)=rt_resolve("il2cpp_domain_get");attach_thread=rt_resolve("il2cpp_thread_attach");detach_thread=rt_resolve("il2cpp_thread_detach");current_thread=rt_resolve("il2cpp_thread_current");
    if(!domain_get||!attach_thread||!detach_thread||!current_thread){rt_detail(RT_INCOMPATIBLE,"Required IL2CPP thread exports are missing");return 0;}
    for(int i=0;i<1200&&rt_vm_state()==0;i++)Sleep(100);
    if(!rt_vm_enter()){rt_detail(RT_INCOMPATIBLE,"Completed IL2CPP initialization was not observed; managed access disabled");return 0;}
    domain=domain_get();rt_vm_leave();if(!domain){rt_detail(RT_INCOMPATIBLE,"Game VM initialized without a usable domain");return 0;}
    object_class=rt_resolve("il2cpp_object_get_class");invoke=rt_resolve("il2cpp_runtime_invoke");method_from_name=rt_resolve("il2cpp_class_get_method_from_name");parent_class=rt_resolve("il2cpp_class_get_parent");class_name=rt_resolve("il2cpp_class_get_name");field_from_name=rt_resolve("il2cpp_class_get_field_from_name");field_value=rt_resolve("il2cpp_field_get_value");field_offset=rt_resolve("il2cpp_field_get_offset");class_size=rt_resolve("il2cpp_class_instance_size");gc_handle_new=rt_resolve("il2cpp_gchandle_new");object_new=rt_resolve("il2cpp_object_new");unbox=rt_resolve("il2cpp_object_unbox");
    if(!object_class||!invoke||!method_from_name||!parent_class||!class_name||!field_from_name||!field_value||!field_offset||!class_size||!gc_handle_new||!object_new||!unbox)return 0;
    rt_detail(RT_WAITING,"Game VM initialized; validating Unity main-thread context in attached scopes");
    for(int i=0;i<600;i++){ManagedScope scope;if(!enter_managed(&scope))return 0;int ready=prepare_context();leave_managed(&scope);if(ready)break;Sleep(100);}if(!callback_handle){rt_detail(RT_INCOMPATIBLE,"Unity main-thread context could not be validated");return 0;}
    if(!exclusive_owner())return 0;
    command_event=CreateEventW(0,TRUE,FALSE,0);sample_callback=initialize_bridge;post_dispatch();
    HANDLE commands=CreateThread(0,0,command_worker,0,0,0);if(commands)CloseHandle(commands);
    ULONGLONG sampled=0;
    ULONGLONG ownership_checked=0;
    for(;;){if(rt_vm_state()!=1){rt_status->capabilities=0;rt_detail(RT_STOPPED,"Game VM stopped; managed worker detached");return 0;}ULONGLONG now=GetTickCount64();if(now-ownership_checked>=500){ownership_checked=now;if(!exclusive_owner())return 0;}if(present_callback&&now-sampled>=500){sampled=now;present_callback(0,present_userdata);}if(command_pending==1||sample_callback)post_dispatch();Sleep(25);}
}
void runtime_bootstrap(void){
    if(InterlockedCompareExchange(&booted,1,0))return;
    GetModuleFileNameW(0,root,MAX_PATH);directory_of(root);GetModuleFileNameW(module,runtime_directory,MAX_PATH);directory_of(runtime_directory);
    rt_boot_log("Standalone bootstrap vm-gate-v2 entered; validating original engine before any managed work");
    int engine_compatible=0;original=load_current_engine(&engine_compatible);
    original_unitymain=original?(void*)GetProcAddress(original,"UnityMain"):0;original_unitymain2=original?(void*)GetProcAddress(original,"UnityMain2"):0;
    if(original_unitymain){
        wchar_t path[MAX_PATH];path_join(path,root,L"GameAssembly.dll");
        verified_assembly=engine_compatible?pin_file(path):INVALID_HANDLE_VALUE;
        char digest[65];int assembly_compatible=0;
        if(verified_assembly!=INVALID_HANDLE_VALUE&&read_hash(verified_assembly,digest))
            assembly_compatible=rt_same(digest,PROFILE_ASSEMBLY_SHA256)?1:
                game_image_equivalent(verified_assembly,PROFILE_ASSEMBLY_COMPATIBILITYSHA256)?2:0;
        if(engine_compatible&&assembly_compatible){
            equivalent_game_build=engine_compatible==2||assembly_compatible==2;
            const char*init=0,*utf16=0,*stop=0;for(size_t i=0;i<sizeof(runtime_symbols)/sizeof(runtime_symbols[0]);i++){if(rt_same(runtime_symbols[i].name,"il2cpp_init"))init=runtime_symbols[i].export_name;else if(rt_same(runtime_symbols[i].name,"il2cpp_init_utf16"))utf16=runtime_symbols[i].export_name;else if(rt_same(runtime_symbols[i].name,"il2cpp_shutdown"))stop=runtime_symbols[i].export_name;}
            bootstrap_verified=rt_init_gate_install(original,init,utf16,stop);if(!bootstrap_verified)bootstrap_failure="Unity initialization gate could not be installed; managed access disabled";
        }else bootstrap_failure="Game files changed beyond verified image metadata; forwarding to the official game engine without the bridge";
        if(!bootstrap_verified)release_verified_assembly();
        HANDLE thread=CreateThread(0,0,worker,0,0,0);
        if(thread)CloseHandle(thread);
        else release_verified_assembly();
    }else rt_boot_log("Unable to load the current official UnityPlayer.dll; stale engine was not loaded");
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID unused){(void)unused;if(reason==DLL_PROCESS_ATTACH){module=instance;DisableThreadLibraryCalls(instance);}return TRUE;}
