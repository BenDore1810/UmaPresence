/* Umapyon Hachimi API-v3 read-only concert telemetry. No hooks or game writes. */
typedef unsigned int u32;
typedef signed int i32;
typedef unsigned long long u64;
typedef unsigned long long usize;
typedef unsigned short u16;
typedef void *Ptr;
#define IMPORT __declspec(dllimport)
#define EXPORT __declspec(dllexport)
IMPORT Ptr __stdcall CreateFileMappingW(Ptr, Ptr, u32, u32, u32, const u16 *);
IMPORT Ptr __stdcall MapViewOfFile(Ptr, u32, u32, u32, usize);
IMPORT i32 __stdcall UnmapViewOfFile(Ptr);
IMPORT i32 __stdcall CloseHandle(Ptr);
IMPORT u32 __stdcall GetCurrentProcessId(void);
IMPORT u64 __stdcall GetTickCount64(void);
IMPORT Ptr __stdcall GetModuleHandleW(const u16 *);
IMPORT Ptr __stdcall GetProcAddress(Ptr,const char *);
static i32 (*seh_handler)(Ptr,u64,Ptr,Ptr);
i32 __C_specific_handler(Ptr record,u64 frame,Ptr context,Ptr dispatcher) { return seh_handler?seh_handler(record,frame,context,dispatcher):1; }
int _fltused = 0;
void *memset(void *p, int value, usize count) { unsigned char *b=p; while(count--) *b++=(unsigned char)value; return p; }
void *memcpy(void *p, const void *src, usize count) { unsigned char *b=p; const unsigned char *s=src; while(count--) *b++=*s++; return p; }

/* Fixed offsets: magic0,version4,sequence8,pid12,tick16,active24,paused28,
   music32,count36,position40,duration48,cast IDs56..127,status128. */
typedef struct {
    u32 magic, version; volatile i32 sequence; u32 pid; u64 tick;
    i32 active, paused, music, count; double position, duration; i32 cast[18];
    i32 status, dresses[18]; u32 outfit_marker; unsigned char reserved[48];
} Packet;
typedef char packet_size_is_256[(sizeof(Packet)==256)?1:-1];
typedef Ptr (*GetApi)(const char *);
#include "runtime-services.h"
typedef Ptr (*GetImage)(const char *);
typedef Ptr (*GetClass)(Ptr,const char *,const char *);
typedef Ptr (*GetMethod)(Ptr,const char *,i32);
typedef Ptr (*FindNested)(Ptr,const char *);
typedef Ptr (*ObjectClass)(Ptr);
typedef Ptr (*Singleton)(Ptr);
typedef Ptr (*GetThread)(void);
typedef void (*Schedule)(Ptr,void(*)(void));
typedef _Bool (*RegisterPresent)(void(*)(Ptr,Ptr),Ptr);
typedef void (*LogFn)(i32,const char*,const char*);

static Ptr mapping;
static Packet *shared;
static GetApi get_api;
static GetImage get_image;
static GetClass get_class;
static GetMethod get_method;
static FindNested find_nested;
static ObjectClass object_class;
static Singleton singleton;
static GetThread main_thread;
static Schedule schedule;
static LogFn log_fn;
static Ptr director_class;
static Ptr (*get_settings)(Ptr);
static float (*get_position)(Ptr);
static float (*get_duration)(Ptr);
static _Bool (*get_pause)(Ptr);
static i32 (*get_music)(Ptr);
static Ptr (*get_cast)(Ptr);
static i32 (*get_chara)(Ptr);
static i32 (*get_dress)(Ptr);
static Ptr (*get_dress_array)(Ptr);
static u32 (*array_length)(Ptr);
static i32 (*array_element_size)(Ptr);
static u32 (*array_header_size)(void);
static Ptr (*class_fields)(Ptr,Ptr*);
static Ptr (*class_parent)(Ptr);
static const char *(*field_name)(Ptr);
static Ptr (*field_type)(Ptr);
static i32 (*type_code)(Ptr);
static u32 (*field_flags)(Ptr);
static void (*field_value)(Ptr,Ptr,Ptr);
static char outfit_diagnostic[2048];
static i32 diagnostic_length;
static i32 (*get_scene)(void);
static Ptr (*get_scene_name)(i32);
static i32 (*string_length)(Ptr);
static u16 *(*string_chars)(Ptr);
static volatile i32 pending;
static u64 last_sample;
static i32 failed;

static void report(const char *message) { if (log_fn) log_fn(3,"UmapyonPresence",message); }
static void diagnostic(const char *text) {
    if(!text) return;
    while(*text && diagnostic_length<2046) outfit_diagnostic[diagnostic_length++]=*text++;
    outfit_diagnostic[diagnostic_length]=0;
}
static i32 same(const char *a,const char *b) { while(*a && *b && *a==*b) {a++;b++;} return *a==*b; }
static void normalized(const char *name,char *result) {
    i32 i=0;
    if(*name=='<') name++;
    while(*name && *name!='>' && i<63) {
        char c=*name++; if(c>='A'&&c<='Z') c+=32;
        if((c>='a'&&c<='z')||(c>='0'&&c<='9')) result[i++]=c;
    }
    result[i]=0;
}
static i32 read_dress_array(Ptr array) {
    if(!array || !array_length || !array_element_size || !array_header_size) return 0;
    u32 count=array_length(array), header=array_header_size();
    if(count<1 || count>32 || header<24 || header>128 || array_element_size(object_class(array))!=4) return 0;
    for(u32 i=0;i<count;i++) {
        i32 value=0; memcpy(&value,(unsigned char*)array+header+i*4,4);
        if(value>0 && value<1000000) return value;
    }
    return 0;
}
/* Resolve typed fields through IL2CPP metadata, never guessed object offsets. */
static i32 read_dress_fields(Ptr object,i32 depth,i32 describe) {
    if(!object || !class_fields || !field_name || !field_type || !type_code || !field_value) return 0;
    Ptr klass=object_class(object); i32 best=0, best_rank=0;
    for(i32 level=0;klass && level<4;level++,klass=class_parent?class_parent(klass):0) {
        Ptr iterator=0,field; i32 visited=0;
        while((field=class_fields(klass,&iterator)) && visited++<128) {
            if(field_flags && (field_flags(field)&0x10)) continue;
            const char *name=field_name(field); if(!name) continue;
            char key[64]; normalized(name,key);
            i32 type=type_code(field_type(field));
            if(describe) { diagnostic(name); diagnostic("; "); }
            i32 rank=same(key,"dressid")?4:same(key,"dressid1")?3:same(key,"dressid2")?1:depth>0&&same(key,"id")?2:0;
            if(rank && (type==8||type==9)) {
                i32 value=0; field_value(object,field,&value);
                if(value>0 && value<1000000 && rank>best_rank) { best=value; best_rank=rank; }
            }
            if(same(key,"dressidarray") && (type==0x1d||type==0x14)) {
                Ptr array=0; field_value(object,field,&array);
                i32 value=read_dress_array(array);
                if(value && best_rank<5) { best=value; best_rank=5; }
            }
            if(depth<2 && (type==0x12||type==0x1c||type==0x15) &&
               (same(key,"dress")||same(key,"dressdata")||same(key,"dressinfo")||same(key,"modelinfo")||same(key,"charainfo"))) {
                Ptr nested=0; field_value(object,field,&nested);
                if(describe) diagnostic("{ ");
                i32 value=read_dress_fields(nested,depth+1,describe);
                if(describe) diagnostic("} ");
                if(value && best_rank<2) {best=value;best_rank=2;}
            }
        }
    }
    return best;
}
#include "career.h"
#include "race.h"
#include "race-metrics.h"
#include "career-result.h"
#include "career-extras.h"
#include "menu.h"
#include "inspect.h"
static void publish(Packet *value) {
    if (!shared) return;
    __atomic_add_fetch(&shared->sequence,1,__ATOMIC_SEQ_CST);
    shared->tick=value->tick; shared->active=value->active; shared->paused=value->paused;
    shared->music=value->music; shared->count=value->count; shared->position=value->position; shared->duration=value->duration;
    for(i32 i=0;i<18;i++) shared->cast[i]=value->cast[i];
    shared->status=value->status;
    for(i32 i=0;i<18;i++) shared->dresses[i]=value->dresses[i];
    shared->outfit_marker=0x44525331;
    ((u32*)shared->reserved)[0]=0x314e4143; /* CAN1: native result/Legacy capabilities */
    ((u32*)shared->reserved)[1]=63;
    unsigned char *debug=(unsigned char*)shared+256;
    ((u32*)debug)[0]=0x4f444247; ((u32*)debug)[1]=(u32)diagnostic_length;
    memcpy(debug+8,outfit_diagnostic,(usize)diagnostic_length);
    memcpy((unsigned char*)shared+2320,&career,sizeof(career));
    memcpy((unsigned char*)shared+3904,&career_result,sizeof(career_result));
    ((u32*)((unsigned char*)shared+2376))[0]=(u32)career_debug_len;
    memcpy((unsigned char*)shared+2380,career_debug,(usize)career_debug_len);
    memcpy((unsigned char*)shared+4096,&race_packet,sizeof(race_packet));
    memcpy((unsigned char*)shared+7424,&menu_packet,sizeof(menu_packet));
    memcpy((unsigned char*)shared+7680,&legacy_details,sizeof(legacy_details));
    memcpy((unsigned char*)shared+8192,&legacy_grandparents,sizeof(legacy_grandparents));
    memcpy((unsigned char*)shared+9216,&race_metrics,sizeof(race_metrics));
    memcpy((unsigned char*)shared+9728,&career_extras,sizeof(career_extras));
    memcpy((unsigned char*)shared+10240,&race_rivals,sizeof(race_rivals));
    memcpy((unsigned char*)shared+12576,&rival_detail,sizeof(rival_detail));
    ((u32*)((unsigned char*)shared+4352))[0]=(u32)race_debug_length;
    memcpy((unsigned char*)shared+4356,race_debug,(usize)race_debug_length);
    __atomic_add_fetch(&shared->sequence,1,__ATOMIC_SEQ_CST);
}
static void sample_main(void) {
    Packet value={0};
    value.tick=GetTickCount64();
    __try {
        Ptr name=get_scene_name(get_scene());
        sample_career(name);
        sample_career_result(name);
        sample_career_extras();
        sample_menu();
        sample_inspect();
        sample_race(name);
        sample_rival_legacy_data();
        sample_race_metrics();
        __try {sample_rival_profile();} __except(1) {rival_detail.active=0;}
        if (name && string_length(name)==4) {
            u16 *s=string_chars(name);
            if (s && s[0]=='L' && s[1]=='i' && s[2]=='v' && s[3]=='e') {
                Ptr director=singleton(director_class);
                if (director) {
                    double duration=get_duration(director), position=get_position(director);
                    Ptr settings=get_settings(director);
                    if (settings && duration>0 && duration<7200 && position>=0 && position<=duration+5) {
                        value.active=1; value.duration=duration; value.position=position;
                        value.paused=get_pause(director)?1:0; value.music=get_music(settings);
                        Ptr list=get_cast(settings);
                        if(list) {
                            Ptr klass=object_class(list);
                            i32 (*count)(Ptr)=(i32(*)(Ptr))get_method(klass,"get_Count",0);
                            Ptr (*item)(Ptr,i32)=(Ptr(*)(Ptr,i32))get_method(klass,"get_Item",1);
                            if(count && item) {
                                i32 n=count(list); if(n<0) n=0; if(n>18) n=18;
                                for(i32 i=0;i<n;i++) {
                                    Ptr character=item(list,i); value.cast[i]=character?get_chara(character):0;
                                    if(character && get_dress) {
                                        __try { value.dresses[i]=get_dress(character); }
                                        __except(1) { get_dress=0; value.dresses[i]=0; }
                                    }
                                    if(character && value.dresses[i]<=0 && get_dress_array) {
                                        __try { value.dresses[i]=read_dress_array(get_dress_array(character)); }
                                        __except(1) { get_dress_array=0; value.dresses[i]=0; }
                                    }
                                    if(character && value.dresses[i]<=0) {
                                        __try {
                                            i32 describe=diagnostic_length<100 && i==0;
                                            if(describe) { diagnostic(get_dress?"Getter returned 0; fields: ":"Getter unavailable; fields: "); }
                                            value.dresses[i]=read_dress_fields(character,0,describe);
                                        } __except(1) { value.dresses[i]=0; diagnostic(" [optional field read unavailable]"); }
                                    }
                                }
                                value.count=n;
                            }
                        }
                    }
                }
            }
        }
    } __except(1) {
        /* Fail closed on incompatible game metadata instead of repeatedly touching it. */
        failed=1; value.active=0; value.status=2;
        report("Read-only bridge stopped: incompatible game metadata. No further reads will be attempted.");
    }
    publish(&value);
    __atomic_store_n(&pending,0,__ATOMIC_RELEASE);
}
static void on_present(Ptr swapchain, Ptr userdata) {
    (void)swapchain; (void)userdata;
    u64 now=GetTickCount64();
    if(failed || now-last_sample<500) return;
    if(__sync_val_compare_and_swap(&pending,0,1)!=0) return;
    last_sample=now;
    Ptr thread=main_thread();
    if(thread) schedule(thread,sample_main); else __atomic_store_n(&pending,0,__ATOMIC_RELEASE);
}
static Ptr method(Ptr klass,const char *name,i32 argc) { return klass?get_method(klass,name,argc):0; }

EXPORT i32 hachimi_init_v3(GetApi api,i32 version) {
    if(!api || version<3) return 0;
    get_api=api;
    log_fn=(LogFn)api("log");
    Ptr (*resolve)(const char*)=(Ptr(*)(const char*))api("il2cpp_resolve_symbol");
    get_image=(GetImage)api("il2cpp_get_assembly_image"); get_class=(GetClass)api("il2cpp_get_class");
    get_method=(GetMethod)api("il2cpp_get_method_addr"); find_nested=(FindNested)api("il2cpp_find_nested_class");
    singleton=(Singleton)api("il2cpp_get_singleton_like_instance");
    main_thread=(GetThread)api("il2cpp_get_main_thread"); schedule=(Schedule)api("il2cpp_schedule_on_thread");
    RegisterPresent present=(RegisterPresent)api("hachimi_register_present_callback");
    if(!resolve||!get_image||!get_class||!get_method||!find_nested||!singleton||!main_thread||!schedule||!present) { report("Bridge API unavailable; not enabled."); return 0; }
    object_class=(ObjectClass)resolve("il2cpp_object_get_class");
    array_length=(u32(*)(Ptr))resolve("il2cpp_array_length");
    array_element_size=(i32(*)(Ptr))resolve("il2cpp_array_element_size");
    array_header_size=(u32(*)(void))resolve("il2cpp_array_object_header_size");
    class_fields=(Ptr(*)(Ptr,Ptr*))resolve("il2cpp_class_get_fields");
    class_parent=(Ptr(*)(Ptr))resolve("il2cpp_class_get_parent");
    field_name=(const char*(*)(Ptr))resolve("il2cpp_field_get_name");
    field_type=(Ptr(*)(Ptr))resolve("il2cpp_field_get_type");
    type_code=(i32(*)(Ptr))resolve("il2cpp_type_get_type");
    field_flags=(u32(*)(Ptr))resolve("il2cpp_field_get_flags");
    field_value=(void(*)(Ptr,Ptr,Ptr))resolve("il2cpp_field_get_value");
    string_length=(i32(*)(Ptr))resolve("il2cpp_string_length"); string_chars=(u16*(*)(Ptr))resolve("il2cpp_string_chars");
    Ptr image=get_image("umamusume"); Ptr unity=get_image("UnityEngine.CoreModule");
    class_name=(const char*(*)(Ptr))resolve("il2cpp_class_get_name");
    class_method=(Ptr(*)(Ptr,const char*,i32))resolve("il2cpp_class_get_method_from_name");
    method_return=(Ptr(*)(Ptr))resolve("il2cpp_method_get_return_type");
    runtime_invoke=(Ptr(*)(Ptr,Ptr,Ptr*,Ptr*))resolve("il2cpp_runtime_invoke");
    object_unbox=(Ptr(*)(Ptr))resolve("il2cpp_object_unbox");
    class_methods=(Ptr(*)(Ptr,Ptr*))resolve("il2cpp_class_get_methods");
    method_name=(const char*(*)(Ptr))resolve("il2cpp_method_get_name");
    method_params=(u32(*)(Ptr))resolve("il2cpp_method_get_param_count");
    method_param=(Ptr(*)(Ptr,u32))resolve("il2cpp_method_get_param");
    class_from_type=(Ptr(*)(Ptr))resolve("il2cpp_class_from_type");
    class_type=(Ptr(*)(Ptr))resolve("il2cpp_class_get_type");
    race_class_is_enum=(_Bool(*)(Ptr))resolve("il2cpp_class_is_enum");
    race_static_value=(void(*)(Ptr,Ptr))resolve("il2cpp_field_static_get_value");
    metric_element_class=(Ptr(*)(Ptr))resolve("il2cpp_class_get_element_class");
    metric_value_type=(_Bool(*)(Ptr))resolve("il2cpp_class_is_valuetype");
    metric_field_offset=(i32(*)(Ptr))resolve("il2cpp_field_get_offset");
    official_method_flags=(u32(*)(Ptr,u32*))resolve("il2cpp_method_get_flags");
    race_condition_method_flags=official_method_flags;
#ifdef UMAPRESENCE_INSPECT
    inspect_class_count=(usize(*)(Ptr))resolve("il2cpp_image_get_class_count");
    inspect_image_class=(Ptr(*)(Ptr,usize))resolve("il2cpp_image_get_class");
    inspect_namespace=(const char*(*)(Ptr))resolve("il2cpp_class_get_namespace");
    inspect_valuetype=(_Bool(*)(Ptr))resolve("il2cpp_class_is_valuetype");
    inspect_element_class=(Ptr(*)(Ptr))resolve("il2cpp_class_get_element_class");
#endif
    work_class=image?get_class(image,"Gallop","WorkDataManager"):0;
    menu_manager_class=image?get_class(image,"Gallop","SceneManager"):0;
    race_class=image?get_class(image,"Gallop","RaceManager"):0;
    Ptr race_define=image?get_class(image,"Gallop","RaceDefine"):0;
    race_type_class=race_define?find_nested(race_define,"RaceType"):0;
    if(!race_type_class&&image)race_type_class=get_class(image,"Gallop","RaceType");
    if(!race_type_class&&image)race_type_class=get_class(image,"Gallop.RaceDefine","RaceType");
    race_info_fn=race_class?(Ptr(*)(void))get_method(race_class,"get_RaceInfo",0):0;
    career_describe=1;
    director_class=image?get_class(image,"Gallop.Live","Director"):0;
    Ptr settings=director_class?find_nested(director_class,"LiveLoadSettings"):0;
    Ptr character=settings?find_nested(settings,"CharacterInfo"):0;
    Ptr scene_manager=unity?get_class(unity,"UnityEngine.SceneManagement","SceneManager"):0;
    Ptr scene=unity?get_class(unity,"UnityEngine.SceneManagement","Scene"):0;
    get_settings=(Ptr(*)(Ptr))method(director_class,"get_LoadSettings",0);
    get_position=(float(*)(Ptr))method(director_class,"get_LiveCurrentTime",0);
    get_duration=(float(*)(Ptr))method(director_class,"get_LiveTotalTime",0);
    get_pause=(_Bool(*)(Ptr))method(director_class,"IsPauseLive",0);
    get_music=(i32(*)(Ptr))method(settings,"get_MusicId",0);
    get_cast=(Ptr(*)(Ptr))method(settings,"get_CharacterInfoList",0);
    get_chara=(i32(*)(Ptr))method(character,"get_CharaId",0);
    get_dress=(i32(*)(Ptr))method(character,"get_DressId",0);
    get_dress_array=(Ptr(*)(Ptr))method(character,"get_DressIdArray",0);
    get_scene=(i32(*)(void))method(scene_manager,"GetActiveScene",0);
    get_scene_name=(Ptr(*)(i32))method(scene,"GetNameInternal",1);
    if(!object_class||!string_length||!string_chars||!get_settings||!get_position||!get_duration||!get_pause||!get_music||!get_cast||!get_chara||!get_scene||!get_scene_name) { report("Concert getters unavailable; bridge not enabled."); return 0; }
    u16 name[64]; const char *prefix="Local\\UmapyonLive-"; i32 n=0;
    while(prefix[n]) { name[n]=(u16)prefix[n]; n++; }
    u32 pid=GetCurrentProcessId(), x=pid; char digits[16]; i32 k=0;
    do { digits[k++]=(char)('0'+x%10); x/=10; } while(x);
    while(k) name[n++]=(u16)digits[--k]; name[n]=0;
    mapping=CreateFileMappingW((Ptr)(usize)-1,0,4,0,16384,name);
    if(!mapping) { report("Could not create local telemetry mapping."); return 0; }
    shared=(Packet*)MapViewOfFile(mapping,2,0,0,16384);
    if(!shared) { CloseHandle(mapping); mapping=0; return 0; }
    memset(shared,0,16384); shared->magic=0x554d4150; shared->version=1; shared->pid=pid; shared->tick=GetTickCount64();
    if(!present(on_present,0)) { UnmapViewOfFile(shared); shared=0; CloseHandle(mapping); mapping=0; return 0; }
    report("Umapyon concert bridge ready (read-only, 2 samples/second, local memory only).");
    return 1;
}
EXPORT const char *umapyon_bridge_version(void) { return "1.23.0 / Hachimi API 3 / game result margins; no estimated result lengths"; }
EXPORT i32 umapresence_init_v1(const UmapresenceRuntimeServicesV1 *services) {
    if(!services||services->size<sizeof(*services)||services->version!=UMAPRESENCE_SERVICE_ABI||!services->lookup)return 0;
    return hachimi_init_v3(services->lookup,3);
}
int __stdcall DllMain(Ptr module,u32 reason,Ptr reserved) {
    (void)module;(void)reserved;
    if(reason==1) { static const u16 ntdll[]={'n','t','d','l','l','.','d','l','l',0}; seh_handler=(i32(*)(Ptr,u64,Ptr,Ptr))GetProcAddress(GetModuleHandleW(ntdll),"__C_specific_handler"); }
    return 1;
}
