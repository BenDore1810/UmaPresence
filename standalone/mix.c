/* GPL-3.0. Mix only currently owned vocal players. Never decode or record audio.
   CRI SetVolume/GetParameterFloat32/Update sources are recorded in README. */
#include "runtime.h"
#include "audio-policy.h"
#include <string.h>
typedef struct {RP player,sheet,acb;uint32_t id,weak,acb_weak;int cue,voice;float baseline,factor;int certified;} Gain;
static Gain gains[64];
typedef struct {RP player;uint32_t weak;int known,count;uint64_t names[8],labels[8];} Selectors;
static Selectors selectors[64];
static SRWLOCK gain_lock=SRWLOCK_INIT;
#if defined(UMAPRESENCE_PUBLIC_RELEASE) || defined(UMAPRESENCE_PISA_DEV)
static uint32_t flags=0,voice1,voice2;
#else
static uint32_t flags=2,voice1,voice2;
#endif
static RP (*invoke)(RP,RP,RP*,RP*),(*unbox)(RP),(*parameter)(RP,uint32_t),(*class_from_type)(RP),(*returns)(RP);
static int(*value_size)(RP,uint32_t*),(*string_length)(RP);
static wchar_t*(*string_chars)(RP);
static void(*static_value)(RP,RP);
static int volume_enum,polyphonic_enum,selector_enum=-1;
static void(*native_volume)(RP,float,RP),(*native_stop)(RP,RP);
static uint32_t(*native_start)(RP,RP);
static float(*get_volume)(RP,int,RP);
static void(*update)(RP,uint32_t,RP);
static RP volume_method,start_method,stop_method,volume_get_method,update_method;
static void(*native_selector)(RP,RP,RP,RP),(*native_reset)(RP,RP);
static RP selector_method,reset_method,usable_controls;
static int selector_hooks;
static uint32_t(*weak_object)(RP,int);
static RP(*weak_target)(uint32_t);
static void(*free_weak)(uint32_t);

static int locate(RP player){if(!player)return -1;for(int i=0;i<64;i++){
    if(gains[i].player&&weak_target(gains[i].weak)!=gains[i].player){free_weak(gains[i].weak);if(gains[i].acb_weak)free_weak(gains[i].acb_weak);memset(&gains[i],0,sizeof(Gain));}
    if(gains[i].player==player)return i;
}return -1;}
static Selectors*selector_state(RP player,int create){Selectors*empty=0;for(int i=0;i<64;i++){
    if(selectors[i].player&&weak_target(selectors[i].weak)!=selectors[i].player){if(create){free_weak(selectors[i].weak);memset(&selectors[i],0,sizeof(Selectors));}else continue;}
    if(selectors[i].player==player)return &selectors[i];if(!selectors[i].player&&!empty)empty=&selectors[i];
}if(create&&empty){uint32_t weak=weak_object(player,0);if(!weak)return 0;memset(empty,0,sizeof(*empty));empty->player=player;empty->weak=weak;return empty;}return 0;}
static uint64_t text_hash(RP text){int count=text?string_length(text):0;if(count<1||count>128)return 0;wchar_t*chars=string_chars(text);uint64_t hash=1469598103934665603ULL;if(!chars)return 0;for(int i=0;i<count;i++){hash^=chars[i];hash*=1099511628211ULL;}return hash?hash:1;}
static void selector_hook(RP player,RP name,RP label,RP method){
    if(!rt_managed_thread()){native_selector(player,name,label,method);return;}
    uint64_t n=text_hash(name),l=text_hash(label);AcquireSRWLockExclusive(&gain_lock);Selectors*state=selector_state(player,1);
    if(state){int i=0;while(i<state->count&&state->names[i]!=n)i++;if(!n||!l||i>=8)state->known=0;else {if(i==state->count)state->count++;state->names[i]=n;state->labels[i]=l;}}
    ReleaseSRWLockExclusive(&gain_lock);native_selector(player,name,label,method);
}
static uint64_t selector_identity(RP player,int*known){
    uint64_t names[8],labels[8];int count=0;*known=0;AcquireSRWLockShared(&gain_lock);Selectors*state=selector_state(player,0);
    if(state){*known=state->known;count=state->count;memcpy(names,state->names,count*8);memcpy(labels,state->labels,count*8);}ReleaseSRWLockShared(&gain_lock);
    // Hash canonical name/label pairs together: XOR of separate name and label
    // hashes would incorrectly equate labels swapped between two selectors.
    return audio_selector_identity(names,labels,count);
}
static void volume_hook(RP player,float value,RP method){
    if(!rt_managed_thread()){native_volume(player,value,method);return;}
    float factor=1;AcquireSRWLockExclusive(&gain_lock);int i=locate(player);
    if(i>=0&&isfinite(value)&&value>=0){gains[i].baseline=value;factor=rt_owned()?gains[i].factor:1;}
    ReleaseSRWLockExclusive(&gain_lock);native_volume(player,value*factor,method);
}
static void restore_player(RP player){
    float baseline=0;int restore=0;AcquireSRWLockExclusive(&gain_lock);int i=locate(player);
    if(i>=0){baseline=gains[i].baseline;restore=gains[i].factor!=1;free_weak(gains[i].weak);if(gains[i].acb_weak)free_weak(gains[i].acb_weak);memset(&gains[i],0,sizeof(Gain));}ReleaseSRWLockExclusive(&gain_lock);
    if(restore&&rt_owned())native_volume(player,baseline,volume_method);
}
static uint32_t start_hook(RP player,RP method){if(rt_managed_thread())restore_player(player);return native_start(player,method);}
static void stop_hook(RP player,RP method){if(rt_managed_thread())restore_player(player);native_stop(player,method);}
static void reset_hook(RP player,RP method){if(!rt_managed_thread()){native_reset(player,method);rt_playback_parameters_reset(player);return;}restore_player(player);native_reset(player,method);rt_playback_parameters_reset(player);AcquireSRWLockExclusive(&gain_lock);Selectors*state=selector_state(player,1);if(state){uint32_t weak=state->weak;memset(state,0,sizeof(*state));state->player=player;state->weak=weak;state->known=1;}ReleaseSRWLockExclusive(&gain_lock);}
static RP nested(RP parent,const char*name){RP(*next)(RP,RP*)=rt_resolve("il2cpp_class_get_nested_types");const char*(*class_name)(RP)=rt_resolve("il2cpp_class_get_name");RP it=0,c;if(!parent||!next||!class_name)return 0;while((c=next(parent,&it)))if(rt_same(class_name(c),name))return c;return 0;}
static int enum_value(RP klass,const char*name,int*out){RP field=rt_field(klass,name);if(!field)return 0;static_value(field,out);return 1;}
static int singer(RP sheet){
    int length=sheet?string_length(sheet):0;if(length<1||length>128)return 0;wchar_t*text=string_chars(sheet);if(!text)return 0;
    for(int i=0;i+12<=length;i++)if(!memcmp(text+i,L"_chara_",14)){int id=0;for(int j=0;j<4;j++){wchar_t ch=text[i+7+j];if(ch<L'0'||ch>L'9')return 0;id=id*10+ch-L'0';}return text[i+11]==L'_'?id:0;}return 0;
}
static int certify(RuntimeStem*stem,RP*acb){
    RuntimeCue cue;*acb=0;if(!rt_cue_query(stem->player,stem->sheet,stem->name,stem->cue,&cue))return 0;
    *acb=cue.acb;int type=cue.type;uint16_t tracks=cue.tracks,waves=cue.waves;
    if(type==polyphonic_enum&&tracks==1&&waves==1)return 1;
    if(selector_hooks&&type==selector_enum&&tracks>0&&waves>0&&usable_controls){
        RP exception=0,cue_args[]={&cue.id};RP boxed=invoke(usable_controls,*acb,cue_args,&exception);
        if(!exception&&boxed&&unbox(boxed)&&*(int*)unbox(boxed)==0)return 2;
    }
    return 0;
}
void rt_mix_initialize(void){
    invoke=rt_resolve("il2cpp_runtime_invoke");unbox=rt_resolve("il2cpp_object_unbox");parameter=rt_resolve("il2cpp_method_get_param");class_from_type=rt_resolve("il2cpp_class_from_type");returns=rt_resolve("il2cpp_method_get_return_type");value_size=rt_resolve("il2cpp_class_value_size");string_length=rt_resolve("il2cpp_string_length");string_chars=rt_resolve("il2cpp_string_chars");static_value=rt_resolve("il2cpp_field_static_get_value");
    if(!invoke||!unbox||!parameter||!class_from_type||!returns||!value_size||!string_length||!string_chars||!static_value)return;
    weak_object=rt_resolve("il2cpp_gchandle_new_weakref");weak_target=rt_resolve("il2cpp_gchandle_get_target");free_weak=rt_resolve("il2cpp_gchandle_free");if(!weak_object||!weak_target||!free_weak)return;
    RP image=rt_image("CriMw.CriWare.Runtime"),ex=rt_class(image,"CriWare","CriAtomEx"),acb=rt_class(image,"CriWare","CriAtomExAcb"),player=rt_class(image,"CriWare","CriAtomExPlayer");
    int single[]={12},enumeration[]={17},playback[]={17};
    volume_method=rt_checked_method(player,"SetVolume",1,1,single,0);volume_get_method=rt_checked_method(player,"GetParameterFloat32",1,12,enumeration,0);update_method=rt_checked_method(player,"Update",1,1,playback,0);start_method=rt_checked_method(player,"Start",0,17,0,0);stop_method=rt_checked_method(player,"StopWithoutReleaseTime",0,1,0,0);
    if(!rt_cue_available()||!volume_method||!volume_get_method||!update_method||!start_method||!stop_method)return;
    uint32_t alignment=0;
    if(!enum_value(nested(ex,"Parameter"),"Volume",&volume_enum)||!enum_value(nested(ex,"CueType"),"Polyphonic",&polyphonic_enum))return;
    if(class_from_type(parameter(volume_get_method,0))!=nested(ex,"Parameter"))return;
    enum_value(nested(ex,"CueType"),"SwitchSelector",&selector_enum);
    if(value_size(class_from_type(returns(start_method)),&alignment)!=4||class_from_type(parameter(update_method,0))!=class_from_type(returns(start_method)))return;
    get_volume=*(RP*)volume_get_method;update=*(RP*)update_method;
    if(!rt_install_hook(*(RP*)volume_method,volume_hook,(RP*)&native_volume))return;
    if(!rt_install_hook(*(RP*)start_method,start_hook,(RP*)&native_start))return;
    if(!rt_install_hook(*(RP*)stop_method,stop_hook,(RP*)&native_stop))return;
    int strings[]={14,14},integer[]={8};selector_method=rt_checked_method(player,"SetSelectorLabel",2,1,strings,0);reset_method=rt_checked_method(player,"ResetParameters",0,1,0,0);usable_controls=rt_checked_method(acb,"GetNumUsableAisacControls",1,8,integer,0);
    if(selector_method&&reset_method&&usable_controls&&rt_install_hook(*(RP*)selector_method,selector_hook,(RP*)&native_selector)&&rt_install_hook(*(RP*)reset_method,reset_hook,(RP*)&native_reset)){selector_hooks=1;rt_playback_reset_hook_ready();}
    rt_status->capabilities|=CAP_MIX;
}
void rt_mix_configure(uint32_t value,uint32_t first,uint32_t second){flags=value;voice1=first;voice2=second;}
void rt_mix_tick(void){
    if(!rt_owned())return;
    RuntimeStem graph[64];PolicyVoice voices[64];int indexes[64],count=rt_audio_graph(graph,64);
    if(rt_audio_trace){InterlockedIncrement((volatile LONG*)&rt_audio_trace->sequence);rt_audio_trace->tick=GetTickCount64();rt_audio_trace->count=count;}
    if(count<1||!(rt_status->capabilities&CAP_MIX)){if(rt_audio_trace){rt_audio_trace->count=0;MemoryBarrier();InterlockedIncrement((volatile LONG*)&rt_audio_trace->sequence);}return;}
    for(int i=0;i<count;i++){
        Gain value;AcquireSRWLockExclusive(&gain_lock);int index=locate(graph[i].player);
        if(index<0){for(int j=0;j<64;j++)if(!gains[j].player){index=j;break;}}
        value=index>=0?gains[index]:(Gain){0};ReleaseSRWLockExclusive(&gain_lock);indexes[i]=index;
        if(index<0){voices[i]=(PolicyVoice){0};continue;}
        if(value.id!=graph[i].id||value.player!=graph[i].player){
            RP acb=0;int certified=certify(&graph[i],&acb);float baseline=value.player==graph[i].player?value.baseline:get_volume(graph[i].player,volume_enum,volume_get_method);
            if(!isfinite(baseline)||baseline<0||baseline>16){indexes[i]=-1;voices[i]=(PolicyVoice){0};continue;}
            if(value.player==graph[i].player&&value.factor!=1){native_volume(graph[i].player,baseline,volume_method);update(graph[i].player,graph[i].id,update_method);}
            uint32_t weak=weak_object(graph[i].player,0);if(!weak){indexes[i]=-1;voices[i]=(PolicyVoice){0};continue;}
            uint32_t acb_weak=acb?weak_object(acb,0):0;if(acb&&!acb_weak)certified=0;
            value=(Gain){.player=graph[i].player,.sheet=graph[i].sheet,.acb=acb,.id=graph[i].id,.weak=weak,.acb_weak=acb_weak,.cue=graph[i].cue,.voice=singer(graph[i].sheet),.baseline=baseline,.factor=1,.certified=certified};
            AcquireSRWLockExclusive(&gain_lock);
            if(gains[index].player&&gains[index].player!=graph[i].player){ReleaseSRWLockExclusive(&gain_lock);free_weak(weak);if(acb_weak)free_weak(acb_weak);indexes[i]=-1;voices[i]=(PolicyVoice){0};continue;}
            if(gains[index].weak)free_weak(gains[index].weak);if(gains[index].acb_weak)free_weak(gains[index].acb_weak);gains[index]=value;ReleaseSRWLockExclusive(&gain_lock);
        }
        int selector_known=0;uint64_t routing=selector_identity(graph[i].player,&selector_known);
        int acb_alive=value.acb_weak&&weak_target(value.acb_weak)==value.acb;
        voices[i]=(PolicyVoice){value.certified&&acb_alive&&(value.certified==1||selector_known)?(uintptr_t)value.acb:0,value.cue,value.voice,graph[i].offset,value.baseline,routing};
    }
    for(int i=0;i<count;i++)if(indexes[i]>=0){
        int lead=(int)voice1;if(!lead)for(int j=0;j<count;j++)if(voices[j].voice>0){lead=voices[j].voice;break;}
        float factor=audio_factor(voices,count,i,(flags&2)!=0,(flags&4)!=0,lead,(int)voice2);float effective=0;int changed=0;
        AcquireSRWLockExclusive(&gain_lock);Gain*value=&gains[indexes[i]];
        if(value->player==graph[i].player&&value->id==graph[i].id&&fabsf(value->factor-factor)>.00001f){value->factor=factor;effective=value->baseline*factor;changed=1;}
        ReleaseSRWLockExclusive(&gain_lock);
        if(changed){native_volume(graph[i].player,effective,volume_method);update(graph[i].player,graph[i].id,update_method);}
    }
    if(rt_audio_trace){
        for(int i=0;i<count;i++){
            RuntimeAudioRow row={0};row.id=graph[i].id;row.voice=voices[i].voice;row.cue=graph[i].cue;row.flags=voices[i].source?1:0;row.offset=graph[i].offset;row.native_gain=voices[i].native;
            AcquireSRWLockShared(&gain_lock);row.factor=indexes[i]>=0?gains[indexes[i]].factor:1;ReleaseSRWLockShared(&gain_lock);row.effective=row.native_gain*row.factor;
            int length=string_length(graph[i].sheet);wchar_t*chars=string_chars(graph[i].sheet);for(int j=0;chars&&j<length&&j<95;j++)row.sheet[j]=chars[j]<128?(char)chars[j]:'?';
            if(lstrlenA(row.sheet)<70){char suffix[24];wsprintfA(suffix," duration=%lums",(unsigned long)(graph[i].duration*1000));lstrcatA(row.sheet,suffix);}
            memcpy((void*)&rt_audio_trace->rows[i],&row,sizeof(row));
        }
        MemoryBarrier();InterlockedIncrement((volatile LONG*)&rt_audio_trace->sequence);
    }
}
