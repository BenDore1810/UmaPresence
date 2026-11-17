/* GPL-3.0. Based on Hachimi Edge live_utils and typed playback accessors.
   Preserve all stem ownership; validate the complete seek before any mutation. */
#include "runtime.h"
#include <math.h>
#include <string.h>
#include "playback-policy.h"
#include "playback-origin.h"
#include "start-parameters.h"

typedef struct {uint32_t id;unsigned char error;unsigned char pad1[3];int group;unsigned char spatial;unsigned char pad2[3];int source_index;unsigned char pad3[4];RP sheet,name;int cue;unsigned char pad4[4];} AudioPlayback;
typedef struct {AudioPlayback *record;AudioPlayback captured;RP source,player;float offset,duration;uint32_t replacement;int resolved_cue;int64_t original_start;int original_micro;} SeekStem;
static RP director_class,scene_class,audio_class,cri_class;
static RP (*object_class)(RP),(*invoke)(RP,RP,RP*,RP*),(*unbox)(RP);
static void (*field_set)(RP,RP,RP);
static int(*field_offset)(RP),(*type_code)(RP),(*value_size)(RP,uint32_t*);
static RP(*field_type)(RP),(*class_from_type)(RP),(*method_return)(RP);
static RP(*class_type)(RP),(*element_class)(RP);
static _Bool(*is_value_class)(RP);
static RP verified_playback_class;
static RP(*method_param)(RP,uint32_t);
static uint32_t(*array_length)(RP),(*array_header)(void);
static int(*array_stride)(RP);
static _Bool (*same_playback)(RP,AudioPlayback,RP),(*played_samples)(AudioPlayback*,int64_t*,int*,RP);
static void (*stop_player)(RP,RP),(*set_start)(RP,int64_t,RP),(*update_player)(RP,uint32_t,RP),(*pause_player)(RP,unsigned char,RP),(*source_playback)(RP,uint32_t,RP);
static uint32_t(*start_player)(RP,RP);
static RP same_method,samples_method,stop_method,start_time_method,update_method,pause_method,source_method,start_method;
static RP cue_length_method;
static RP get_view;
static RP get_current,get_total,get_pause,get_settings,get_music, pause_live, resume_live;
static int dragging,drag_was_paused,current_song;
static SeekStem stems[65];
static int initialized;
static PlaybackOrigins origins;
static StartParameters start_parameters;
static SRWLOCK start_lock=SRWLOCK_INIT;
static uint32_t(*start_weak)(RP,int);
static RP(*start_target)(uint32_t);
static void(*start_free)(uint32_t),(*native_start_time)(RP,int64_t,RP),(*native_start_micro)(RP,int64_t,RP);
static RP start_micro_method;
static int start_hooks,reset_observed;
static StartParameter* parameter_find(RP player,int create){
    for(int i=0;i<128;i++)if(start_parameters.rows[i].player&&start_target(start_parameters.rows[i].weak)!=(RP)start_parameters.rows[i].player){start_free(start_parameters.rows[i].weak);memset(&start_parameters.rows[i],0,sizeof(StartParameter));}
    StartParameter* row=start_parameter_find(&start_parameters,(uintptr_t)player);
    if(!row&&create){row=start_parameter_empty(&start_parameters);uint32_t weak=row?start_weak(player,0):0;if(!weak)return 0;*row=(StartParameter){(uintptr_t)player,weak,0,0};}return row;
}
static void remember_start(RP player,int64_t value,int micro){
    AcquireSRWLockExclusive(&start_lock);StartParameter* row=parameter_find(player,1);if(row){row->value=value;row->micro=micro;row->known=1;}ReleaseSRWLockExclusive(&start_lock);
}
static void forget_start(RP player){AcquireSRWLockExclusive(&start_lock);StartParameter* row=start_parameter_find(&start_parameters,(uintptr_t)player);if(row)row->known=0;ReleaseSRWLockExclusive(&start_lock);}
static void start_time_hook(RP player,int64_t value,RP method){native_start_time(player,value,method);if(rt_managed_thread())remember_start(player,value,0);else forget_start(player);}
static void start_micro_hook(RP player,int64_t value,RP method){native_start_micro(player,value,method);if(rt_managed_thread())remember_start(player,value,1);else forget_start(player);}
void rt_playback_parameters_reset(RP player){if(start_hooks&&rt_managed_thread())remember_start(player,0,0);else forget_start(player);}
void rt_playback_reset_hook_ready(void){reset_observed=1;}
static int original_start(RP player,int64_t*value,int*micro){
    if(!start_hooks||!reset_observed)return 0;AcquireSRWLockExclusive(&start_lock);StartParameter* row=parameter_find(player,0);int known=row&&row->known;if(known){*value=row->value;*micro=row->micro;}ReleaseSRWLockExclusive(&start_lock);return known;
}
static void restore_start(SeekStem* stem){
    if(stem->original_micro)native_start_micro(stem->player,stem->original_start,start_micro_method);
    else native_start_time(stem->player,stem->original_start,start_time_method);
}

static int method_type(RP method,int result,int count,const int*parameters){
    if(!method||type_code(method_return(method))!=result)return 0;
    for(int i=0;i<count;i++)if(type_code(method_param(method,i))!=parameters[i])return 0;return 1;
}
static RP call(RP object,RP method,RP*args,int*success){RP exception=0;if(!method){*success=0;return 0;}RP value=invoke(method,object,args,&exception);*success=exception==0;return value;}
static float scalar(RP object,RP method,int*success){RP value=call(object,method,0,success);if(!value||!unbox(value)){*success=0;return 0;}float result=*(float*)unbox(value);if(!isfinite(result))*success=0;return result;}
static RP live_controller(void){RP manager=rt_singleton(scene_class);int ok=0;RP controller=manager?call(manager,get_view,0,&ok):0;const char*(*name)(RP)=rt_resolve("il2cpp_class_get_name");return ok&&controller&&name&&strcmp(name(object_class(controller)),"LiveViewController")==0?controller:0;}
int rt_playback_snapshot(int*song,double*position,double*duration,int*paused){
    if(!initialized||!live_controller())return 0;RP director=rt_singleton(director_class);int ok=0;if(!director)return 0;
    *position=scalar(director,get_current,&ok);if(!ok)return 0;*duration=scalar(director,get_total,&ok);if(!ok||*duration<=0||*position<0||*position>*duration+1)return 0;
    RP boxed=call(director,get_pause,0,&ok);if(!ok||!boxed||!unbox(boxed))return 0;*paused=*(unsigned char*)unbox(boxed)!=0;
    RP settings=call(director,get_settings,0,&ok);if(!ok||!settings)return 0;boxed=call(settings,get_music,0,&ok);if(!ok||!boxed||!unbox(boxed))return 0;*song=*(int*)unbox(boxed);return *song>0;
}
static int set_paused(int paused){RP controller=live_controller();if(!controller)return 0;RP method=paused?pause_live:resume_live;if(!method)return 0;int ok=0;call(controller,method,0,&ok);return ok;}
static int reference_array(RP collection){
    if(!collection||!class_type||!element_class||!is_value_class)return 0;RP klass=object_class(collection);
    return type_code(class_type(klass))==29&&array_stride(klass)==8&&!is_value_class(element_class(klass));
}
static RP item(RP collection,int index){
    if(!collection||index<0)return 0;
    if(reference_array(collection)){uint32_t count=array_length(collection);if(count>1024||index>=(int)count)return 0;RP value=0;memcpy(&value,(char*)collection+array_header()+index*8,8);return value;}
    RP values=rt_object_field(collection,"_items");int count=0;
    if(values&&rt_int_field(collection,"_size",&count)&&index<count&&count<=1024&&array_length(values)>=(uint32_t)count&&reference_array(values)){RP value=0;memcpy(&value,(char*)values+array_header()+index*8,8);return value;}
    RP method=rt_method_info(object_class(collection),"get_Item",1),args[]={&index};int ok=0;return call(collection,method,args,&ok);
}
static int list_count(RP collection){if(reference_array(collection)){uint32_t n=array_length(collection);return n<=1024?(int)n:-1;}int count=0;if(rt_int_field(collection,"_size",&count)&&count>=0&&count<=1024)return count;int ok=0;RP method=rt_method_info(object_class(collection),"get_Count",0);if(!method_type(method,8,0,0))return -1;RP value=call(collection,method,0,&ok);return ok&&value&&unbox(value)?*(int*)unbox(value):-1;}
static int playback_layout(RP klass){
    if(klass&&klass==verified_playback_class)return 1;
    uint32_t align=0;
    int size=klass?value_size(klass,&align):0;
    if(size!=sizeof(AudioPlayback)){
        char detail[128];wsprintfA(detail,"AudioPlayback layout: value size %d, expected %d",size,(int)sizeof(AudioPlayback));
        rt_boot_log(detail);return 0;
    }
    RP(*next_field)(RP,RP*)=rt_resolve("il2cpp_class_get_fields");
    uint32_t(*flags)(RP)=rt_resolve("il2cpp_field_get_flags");
    RP(*enum_base)(RP)=rt_resolve("il2cpp_class_enum_basetype");
    _Bool(*is_enum)(RP)=rt_resolve("il2cpp_class_is_enum");
    if(!next_field||!flags||!enum_base||!is_enum)return 0;
    RP first=rt_field(klass,"criAtomExPlayback");if(!first)return 0;
    int base=field_offset(first);if(base!=0&&base!=16)return 0;
    const int offsets[]={0,4,8,12,16,24,32,40};
    const int types[]={17,2,8,2,8,14,14,8};
    unsigned slots=0;
    RP iterator=0,field;
    /* The pinned game build may rename private fields. Require the complete
       instance layout, including its handle anchor and enum storage type. */
    while((field=next_field(klass,&iterator))){
        if(flags(field)&0x10)continue;
        int offset=field_offset(field)-base,slot=0;
        while(slot<8&&offsets[slot]!=offset)slot++;
        RP type=field_type(field);
        int kind=type_code(type);
        if(slot==2&&kind==17){
            RP enum_class=class_from_type(type);
            RP underlying=enum_class&&is_enum(enum_class)?enum_base(enum_class):0;
            if(underlying)kind=type_code(underlying);
        }
        if(slot==8||(slots&(1u<<slot))||kind!=types[slot]||(!slot&&field!=first)){
            char detail[128];wsprintfA(detail,"AudioPlayback layout: unexpected instance field at offset %d, type %d",offset,kind);
            rt_boot_log(detail);return 0;
        }
        slots|=1u<<slot;
    }
    if(slots!=255){rt_boot_log("AudioPlayback layout: incomplete set of instance fields");return 0;}
    verified_playback_class=klass;
    return 1;
}
static int graph_fail(int stage,int trace){if(trace&&rt_audio_trace)rt_audio_trace->playback_stage=stage;return 0;}
static int collect_stem(RP manager,RP dictionary,AudioPlayback *record,float current,SeekStem*out,int trace){
    if(!record||record->error||record->id==0)return graph_fail(36,trace);
    int group=record->group,ok=0;RP args[]={&group};RP control=call(dictionary,rt_method_info(object_class(dictionary),"get_Item",1),args,&ok);
    RP pool=ok?rt_object_field(control,"pool"):0,list=rt_object_field(pool,"sourceList");int count=list?list_count(list):-1;RP source=0;
    if(count<0||count>128)return graph_fail(41,trace);
    for(int i=0;i<count;i++){RP candidate=item(list,i);if(candidate&&same_playback(candidate,*record,same_method)){int index=-1;if(!rt_int_field(candidate,"usingIndex",&index)||index<0)return 0;source=item(rt_object_field(candidate,"sourceList"),index);break;}}
    if(!source)return graph_fail(42,trace);RP player_method=rt_method_info(object_class(source),"get_player",0),player=call(source,player_method,0,&ok);if(!ok||!player)return graph_fail(43,trace);
    int64_t samples=0;int rate=0;if(!played_samples(record,&samples,&rate,samples_method)||samples<0||rate<8000||rate>384000)return graph_fail(44,trace);
    float offset=(float)playback_origin(&origins,(uintptr_t)player,record->id,current_song,current,(double)samples/rate);
    RuntimeCue cue;float duration=0;int cue_id=record->cue;
    if(rt_cue_query(player,record->sheet,record->name,record->cue,&cue)){duration=(float)((double)cue.length_ms/1000);cue_id=cue.id;}
    else if(cue_length_method&&record->cue>=0){RP cue_args[]={record->sheet,&record->cue};RP value=call(manager,cue_length_method,cue_args,&ok);RP data=ok&&value?unbox(value):0;duration=data?*(float*)data:0;}
    if(!isfinite(duration)||duration<=0||duration>7200||!isfinite(offset))return graph_fail(45,trace);
    *out=(SeekStem){record,*record,source,player,offset,duration,0,cue_id};return 1;
}
static int collect_stems(float current,int strict){
    RP manager=rt_singleton(audio_class);if(!manager)return graph_fail(30,strict);RP get_cri=rt_method_info(audio_class,"get_CriAudioManager",0);int ok=0;RP cri=call(0,get_cri,0,&ok),dictionary=ok?rt_object_field(cri,"audioCtrlDict"):0;
    if(!cri)return graph_fail(31,strict);
    RP song=rt_field(audio_class,"_songPlayback"),voices=rt_object_field(manager,"_songCharaPlaybacks");if(!dictionary)return graph_fail(32,strict);if(!song||!voices)return graph_fail(34,strict);
    RP playback_class=class_from_type(field_type(song));if(!playback_layout(playback_class))return graph_fail(33,strict);
    int offset=field_offset(song);if(offset<16||offset>4096)return 0;int count=0;
    if(!collect_stem(manager,dictionary,(AudioPlayback*)((char*)manager+offset),current,&stems[count++],strict))return 0;
    uint32_t n=array_length(voices),header=array_header();if(n>64||header<24||header>128||array_stride(object_class(voices))!=sizeof(AudioPlayback))return graph_fail(35,strict);
    for(uint32_t i=0;i<n;i++){
        AudioPlayback *record=(AudioPlayback*)((char*)voices+header+i*sizeof(AudioPlayback));
        if(record->id==0&&!record->sheet&&!record->name&&!record->error)continue; // Unassigned/backdancer slot, not a missing vocal.
        if(record->id==0||record->error){if(strict)return graph_fail(46,strict);continue;}
        if(!collect_stem(manager,dictionary,record,current,&stems[count],strict)){if(strict)return 0;continue;}
        // Multiple slots may reference one playing handle: restart it only once.
        int duplicate=0;for(int j=0;j<count;j++)if(stems[j].player==stems[count].player){duplicate=1;break;}
        if(!duplicate)count++;
    }
    if(strict)rt_audio_trace->playback_stage=50;return count;
}
static uint32_t seek_to(double target,int restore_playing){
    if(!(rt_status->capabilities&CAP_SEEK)||!same_playback||!played_samples||!source_playback||!stop_player||!set_start||!start_player||!update_player||!pause_player)return RESULT_UNAVAILABLE;
    int song=0,paused=0;double current=0,total=0;if(!rt_playback_snapshot(&song,&current,&total,&paused))return RESULT_STALE;
    if(current_song!=song){playback_origins_clear(&origins);current_song=song;}
    if(!isfinite(target)||target<0||target>total)return RESULT_BAD_ARGUMENT;
    int count=collect_stems((float)current,1);if(count<1)return RESULT_UNAVAILABLE;
    for(int i=0;i<count;i++)if(!original_start(stems[i].player,&stems[i].original_start,&stems[i].original_micro))return RESULT_UNAVAILABLE;
    // Never partially seek a segmented part outside its known available window.
    double offsets[65],durations[65],minimum=0,maximum=0;for(int i=0;i<count;i++){offsets[i]=stems[i].offset;durations[i]=stems[i].duration;}
    if(!playback_window(total,current,offsets,durations,count,&minimum,&maximum))return RESULT_UNAVAILABLE;
    if(target<minimum-.01||target>maximum+.01)return RESULT_BAD_ARGUMENT;
    if(target<minimum)target=minimum;if(target>maximum)target=maximum;
    RP director=rt_singleton(director_class),time_controller=0;int ok=0;
    time_controller=call(director,rt_method_info(director_class,"get_LiveTimeController",0),0,&ok);
    RP current_field=rt_field(director_class,"_liveCurrentTime"),elapsed=time_controller?rt_field(object_class(time_controller),"_elapsedTime"):0;
    RP set_time=time_controller?rt_method_info(object_class(time_controller),"set_CurrentTime",1):0;int float_parameter[]={12};
    if(!ok||!time_controller||!current_field||!elapsed||type_code(field_type(current_field))!=12||type_code(field_type(elapsed))!=12||!method_type(set_time,1,1,float_parameter))return RESULT_UNAVAILABLE;
    if(!paused&&!set_paused(1))return RESULT_FAILED;
    for(int i=0;i<count;i++){
        double local=target-stems[i].offset;if(local<0)local=0;
        stop_player(stems[i].player,stop_method);set_start(stems[i].player,(int64_t)llround(local*1000),start_time_method);
        uint32_t id=start_player(stems[i].player,start_method);restore_start(&stems[i]);if(!id){
            int recovered=1;
            for(int j=0;j<=i;j++){
                stop_player(stems[j].player,stop_method);double previous=current-stems[j].offset;if(previous<0)previous=0;
                set_start(stems[j].player,(int64_t)llround(previous*1000),start_time_method);uint32_t restored=start_player(stems[j].player,start_method);restore_start(&stems[j]);
                if(!restored){recovered=0;continue;}update_player(stems[j].player,restored,update_method);pause_player(stems[j].player,1,pause_method);stems[j].record->id=restored;source_playback(stems[j].source,restored,source_method);playback_origin_store(&origins,(uintptr_t)stems[j].player,restored,song,stems[j].offset);
            }
            if(recovered&&restore_playing)recovered=set_paused(0);
            rt_detail(RT_READY,recovered?"Seek restart failed; previous audio position restored":"Seek rollback incomplete; concert held paused for recovery");return RESULT_FAILED;
        }
        update_player(stems[i].player,id,update_method);pause_player(stems[i].player,1,pause_method);stems[i].replacement=id;
        stems[i].record->id=id;source_playback(stems[i].source,id,source_method);
        playback_origin_store(&origins,(uintptr_t)stems[i].player,id,song,stems[i].offset);
    }
    float value=(float)target;field_set(director,current_field,&value);field_set(time_controller,elapsed,&value);RP args[]={&value};call(time_controller,set_time,args,&ok);
    if(!ok){rt_detail(RT_READY,"Timeline seek update failed; concert held paused for recovery");return RESULT_FAILED;}
    if(restore_playing&&!set_paused(0))return RESULT_FAILED;
    return RESULT_OK;
}
int rt_audio_graph(RuntimeStem*out,int capacity){
    int song=0,paused=0;double position=0,total=0;if(!same_playback||!played_samples||!rt_playback_snapshot(&song,&position,&total,&paused))return 0;
    int count=collect_stems((float)position,0);if(count<1||count-1>capacity)return 0;
    for(int i=1;i<count;i++)out[i-1]=(RuntimeStem){stems[i].player,stems[i].captured.sheet,stems[i].captured.name,stems[i].captured.id,stems[i].resolved_cue,stems[i].offset,stems[i].duration};
    return count-1;
}
void rt_playback_initialize(void){
    rt_audio_trace->playback_stage=0;rt_audio_trace->seek_reason=2;
    object_class=rt_resolve("il2cpp_object_get_class");invoke=rt_resolve("il2cpp_runtime_invoke");unbox=rt_resolve("il2cpp_object_unbox");field_set=rt_resolve("il2cpp_field_set_value");field_offset=rt_resolve("il2cpp_field_get_offset");type_code=rt_resolve("il2cpp_type_get_type");field_type=rt_resolve("il2cpp_field_get_type");class_from_type=rt_resolve("il2cpp_class_from_type");method_return=rt_resolve("il2cpp_method_get_return_type");method_param=rt_resolve("il2cpp_method_get_param");value_size=rt_resolve("il2cpp_class_value_size");array_length=rt_resolve("il2cpp_array_length");array_header=rt_resolve("il2cpp_array_object_header_size");array_stride=rt_resolve("il2cpp_array_element_size");
    RP image=rt_image("umamusume");director_class=rt_class(image,"Gallop.Live","Director");scene_class=rt_class(image,"Gallop","SceneManager");audio_class=rt_class(image,"Gallop","AudioManager");
    if(!object_class||!invoke||!unbox||!field_set||!field_offset||!type_code||!field_type||!class_from_type||!method_return||!method_param||!value_size||!array_length||!array_header||!array_stride||!director_class||!scene_class||!audio_class)return;
    class_type=rt_resolve("il2cpp_class_get_type");element_class=rt_resolve("il2cpp_class_get_element_class");is_value_class=rt_resolve("il2cpp_class_is_valuetype");
    get_view=rt_checked_method(scene_class,"GetCurrentViewController",0,18,0,0);if(!get_view){rt_boot_log("Playback binding failed: non-generic GetCurrentViewController unavailable");return;}rt_audio_trace->playback_stage=10;
    get_current=rt_method_info(director_class,"get_LiveCurrentTime",0);get_total=rt_method_info(director_class,"get_LiveTotalTime",0);get_pause=rt_method_info(director_class,"IsPauseLive",0);get_settings=rt_method_info(director_class,"get_LoadSettings",0);
    RP settings=rt_class(image,"Gallop.Live","Director/LiveLoadSettings");
    // Nested type lookup through the same exact metadata iterator as the bridge.
    RP(*nested)(RP,RP*)=rt_resolve("il2cpp_class_get_nested_types");const char*(*name)(RP)=rt_resolve("il2cpp_class_get_name");RP it=0,c;
    while(nested&&director_class&&(c=nested(director_class,&it)))if(strcmp(name(c),"LiveLoadSettings")==0){settings=c;break;}
    get_music=rt_method_info(settings,"get_MusicId",0);
    if(!method_type(get_current,12,0,0)||!method_type(get_total,12,0,0)||!method_type(get_pause,2,0,0)||!method_type(get_music,8,0,0)||!method_type(get_settings,18,0,0))return;
    initialized=1;
    rt_audio_trace->playback_stage=20;
    RP controller=rt_class(image,"Gallop","LiveViewController");
    pause_live=rt_checked_method(controller,"PauseLive",0,1,0,0);resume_live=rt_checked_method(controller,"ResumeLive",0,1,0,0);
    if(rt_status&&pause_live&&resume_live)rt_status->capabilities|=CAP_PLAYBACK;
    RP cute=rt_image("Cute.Cri.Assembly"),cri=rt_image("CriMw.CriWare.Runtime");
    RP source=rt_class(cute,"Cute.Cri","CuteAudioSource"),playback=rt_class(cute,"Cute.Cri","AudioPlayback"),atom=rt_class(cute,"Cute.Cri","AtomSourceEx");cri_class=rt_class(cri,"CriWare","CriAtomExPlayer");
    if(!cri_class)cri_class=rt_class(cri,"","CriAtomExPlayer");
    int structure[]={17},start_parameter[]={10},boolean[]={2},samples[]={10,8},cue_parameters[]={14,8};
    cue_length_method=rt_checked_method(audio_class,"GetCueLength",2,12,cue_parameters,0);if(!cue_length_method&&!rt_cue_available())return;
    rt_audio_trace->playback_stage=21;
    RP same=rt_checked_method(source,"IsSamePlaybackId",1,2,structure,0),played=rt_checked_method(playback,"GetNumPlayedSamples",2,2,samples,0),assign=rt_checked_method(atom,"set_Playback",1,1,structure,0);
    RP stop=rt_checked_method(cri_class,"StopWithoutReleaseTime",0,1,0,0),start_time=rt_checked_method(cri_class,"SetStartTime",1,1,start_parameter,0),start=rt_checked_method(cri_class,"Start",0,17,0,0),update=rt_checked_method(cri_class,"Update",1,1,structure,0),pause=rt_checked_method(cri_class,"Pause",1,1,boolean,0);
    _Bool(*byref)(RP)=rt_resolve("il2cpp_type_is_byref");uint32_t align=0;
    if(!same||!played||!assign||!stop||!start_time||!start||!update||!pause||!byref){rt_boot_log("Playback binding failed: typed audio methods unavailable");return;}rt_audio_trace->playback_stage=22;
    if(!byref(method_param(played,0))||!byref(method_param(played,1))){rt_boot_log("Playback binding failed: GetNumPlayedSamples requires two by-reference outputs");return;}
    if(!playback_layout(playback)){rt_boot_log("Playback binding failed: AudioPlayback layout could not be verified");return;}
    if(value_size(class_from_type(method_return(start)),&align)!=4){rt_boot_log("Playback binding failed: Start does not return a four-byte playback handle");return;}
    if(class_from_type(method_param(same,0))!=playback){rt_boot_log("Playback binding failed: IsSamePlaybackId does not accept AudioPlayback");return;}
    rt_audio_trace->playback_stage=23;
    if(class_from_type(method_param(assign,0))!=class_from_type(method_return(start))||class_from_type(method_param(update,0))!=class_from_type(method_return(start)))return;
    same_playback=*(RP*)same;played_samples=*(RP*)played;source_playback=*(RP*)assign;
    stop_player=*(RP*)stop;set_start=*(RP*)start_time;start_player=*(RP*)start;update_player=*(RP*)update;pause_player=*(RP*)pause;
    same_method=same;samples_method=played;source_method=assign;stop_method=stop;start_time_method=start_time;start_method=start;update_method=update;pause_method=pause;
    start_weak=rt_resolve("il2cpp_gchandle_new_weakref");start_target=rt_resolve("il2cpp_gchandle_get_target");start_free=rt_resolve("il2cpp_gchandle_free");
    start_micro_method=rt_checked_method(cri_class,"SetStartTimeMicro",1,1,start_parameter,0);
    if(start_weak&&start_target&&start_free&&rt_install_hook(*(RP*)start_time,start_time_hook,(RP*)&native_start_time)){
        if(!start_micro_method||rt_install_hook(*(RP*)start_micro_method,start_micro_hook,(RP*)&native_start_micro))start_hooks=1;
        set_start=native_start_time; // Our temporary seek value must not replace the observed game setting.
    }
    rt_audio_trace->playback_stage=24;
}
void rt_playback_tick(void){int song=0,paused=0;double current=0,total=0;if(!rt_playback_snapshot(&song,&current,&total,&paused)){dragging=0;current_song=0;playback_origins_clear(&origins);rt_audio_trace->seek_reason=initialized?1:2;if(rt_status)rt_status->capabilities&=~(uint64_t)CAP_SEEK;return;}if(current_song!=song)playback_origins_clear(&origins);current_song=song;
    int bound=pause_live&&resume_live&&same_playback&&played_samples&&source_playback&&stop_player&&set_start&&start_player&&update_player&&pause_player;
    int count=bound?collect_stems((float)current,1):0;double offsets[65],durations[65],minimum=0,maximum=0;
    for(int i=0;i<count;i++)if(!original_start(stems[i].player,&stems[i].original_start,&stems[i].original_micro)){bound=0;break;}
    for(int i=0;i<count;i++){offsets[i]=stems[i].offset;durations[i]=stems[i].duration;}
    int ready=bound&&count>0&&playback_window(total,current,offsets,durations,count,&minimum,&maximum);
    rt_audio_trace->seek_reason=ready?0:!bound?2:count<1?3:4;
    rt_audio_trace->seek_min=ready?(float)minimum:0;rt_audio_trace->seek_max=ready?(float)maximum:0;
    if(ready){rt_audio_trace->playback_stage=60;rt_status->capabilities|=CAP_SEEK;}else rt_status->capabilities&=~(uint64_t)CAP_SEEK;
}
uint32_t rt_playback_command(const RuntimeCommand*command){
#ifdef UMAPRESENCE_PUBLIC_RELEASE
    if(command->command==CMD_CONFIGURE&&((command->flags&~1u)||command->voice1||command->voice2))return RESULT_UNAVAILABLE;
#endif
    if(command->command==CMD_CONFIGURE){if((command->flags&~31u)||command->voice1>1999||command->voice2>1999||((command->flags&24)&&!command->voice1)||((command->flags&16)&&(!command->voice2||command->voice1==command->voice2)))return RESULT_BAD_ARGUMENT;if((command->flags&1)&&!(rt_status->capabilities&CAP_FORMATION))return RESULT_UNAVAILABLE;if((command->flags&6)&&!(rt_status->capabilities&CAP_MIX))return RESULT_UNAVAILABLE;if((command->flags&24)&&!(rt_status->capabilities&CAP_ROUTING))return RESULT_UNAVAILABLE;rt_formation_configure(command->flags,command->voice1,command->voice2);return RESULT_OK;}
    int song=0,paused=0;double position=0,total=0;if(!rt_playback_snapshot(&song,&position,&total,&paused)||song!=(int)command->song)return RESULT_STALE;
    switch(command->command){
        case CMD_PLAY:return set_paused(0)?RESULT_OK:RESULT_FAILED;
        case CMD_PAUSE:return set_paused(1)?RESULT_OK:RESULT_FAILED;
        case CMD_BEGIN_DRAG:if(!(rt_status->capabilities&CAP_SEEK))return RESULT_UNAVAILABLE;if(!dragging){drag_was_paused=paused;dragging=1;}if(paused||set_paused(1))return RESULT_OK;dragging=0;return RESULT_FAILED;
        case CMD_END_DRAG:{int resume=dragging?!drag_was_paused:!paused;dragging=0;uint32_t result=seek_to(command->position,resume);if(resume&&(result==RESULT_UNAVAILABLE||result==RESULT_BAD_ARGUMENT))set_paused(0);return result;}
        case CMD_SEEK:return seek_to(command->position,!paused);
        case CMD_CANCEL_DRAG:{int resume=dragging&&!drag_was_paused;dragging=0;return !resume||set_paused(0)?RESULT_OK:RESULT_FAILED;}
        default:return RESULT_BAD_ARGUMENT;
    }
}
