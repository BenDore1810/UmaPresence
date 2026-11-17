/* Shared cue metadata, independent of formation/routing/mix presentation.
   Observe the real player's SetCue binding: custom Cute.Cri players need not
   register their ACBs in CriAtom.GetAcb's global registry. No audio decoding. */
#include "runtime.h"
#include <string.h>
typedef struct {uint32_t player,acb;int cue;ULONGLONG seen;} Binding;
static Binding bindings[128];
static SRWLOCK cache_lock=SRWLOCK_INIT;
static RP get_by_id,get_by_name,get_global_acb;
static RP (*invoke)(RP,RP,RP*,RP*),(*unbox)(RP),(*target)(uint32_t);
static uint32_t(*weak)(RP,int);
static void(*release)(uint32_t);
static int(*string_length)(RP);
static int id_offset,type_offset,length_offset,tracks_offset,waves_offset,ready;
static void(*set_id_original)(RP,RP,int,RP),(*set_name_original)(RP,RP,RP,RP);
static int read_cue(RP acb,RP name,int id,RuntimeCue*out){
    if(!ready||!acb)return 0;int named=name&&string_length(name)>0;RP method=named?get_by_name:get_by_id;
    if(!method||(!named&&id<0))return 0;
    unsigned char info[512]={0};RP args[]={named?name:&id,info},exception=0,boxed=invoke(method,acb,args,&exception);
    if(exception||!boxed||!unbox(boxed)||!*(unsigned char*)unbox(boxed))return 0;
    memset(out,0,sizeof(*out));out->acb=acb;memcpy(&out->id,info+id_offset,4);memcpy(&out->type,info+type_offset,4);memcpy(&out->length_ms,info+length_offset,8);memcpy(&out->tracks,info+tracks_offset,2);memcpy(&out->waves,info+waves_offset,2);
    return out->id>=0&&out->length_ms>0&&out->length_ms<=7200000;
}
static void capture(RP player,RP acb,int id){
    if(!player||!acb||!rt_managed_thread())return;
    uint32_t p=weak(player,0),a=weak(acb,0);if(!p||!a){if(p)release(p);if(a)release(a);return;}
    AcquireSRWLockExclusive(&cache_lock);int slot=-1,empty=-1,oldest=0;ULONGLONG age=~0ULL;
    for(int i=0;i<128;i++){
        if(bindings[i].player&&target(bindings[i].player)==player){slot=i;break;}
        if((!bindings[i].player||!target(bindings[i].player))&&empty<0)empty=i;
        if(bindings[i].seen<age){age=bindings[i].seen;oldest=i;}
    }
    if(slot<0)slot=empty>=0?empty:oldest;if(bindings[slot].player)release(bindings[slot].player);if(bindings[slot].acb)release(bindings[slot].acb);
    bindings[slot]=(Binding){p,a,id,GetTickCount64()};ReleaseSRWLockExclusive(&cache_lock);
}
static void set_id(RP player,RP acb,int id,RP method){set_id_original(player,acb,id,method);capture(player,acb,id);}
static void set_name(RP player,RP acb,RP name,RP method){set_name_original(player,acb,name,method);if(!rt_managed_thread())return;RuntimeCue cue;capture(player,acb,read_cue(acb,name,-1,&cue)?cue.id:-1);}
int rt_cue_available(void){return ready;}
int rt_cue_query(RP player,RP sheet,RP name,int requested,RuntimeCue*out){
    if(!ready)return 0;RP acb=0;int id=requested;AcquireSRWLockExclusive(&cache_lock);
    for(int i=0;i<128;i++)if(bindings[i].player&&target(bindings[i].player)==player){acb=target(bindings[i].acb);if(id<0)id=bindings[i].cue;bindings[i].seen=GetTickCount64();break;}
    ReleaseSRWLockExclusive(&cache_lock);
    if(!acb&&get_global_acb&&sheet){RP args[]={sheet},exception=0;acb=invoke(get_global_acb,0,args,&exception);if(exception)acb=0;}
    return read_cue(acb,name,id,out);
}
static RP nested(RP parent,const char*name){RP(*next)(RP,RP*)=rt_resolve("il2cpp_class_get_nested_types");const char*(*get_name)(RP)=rt_resolve("il2cpp_class_get_name");RP it=0,c;if(!parent||!next||!get_name)return 0;while((c=next(parent,&it)))if(rt_same(get_name(c),name))return c;return 0;}
void rt_cue_initialize(void){
    invoke=rt_resolve("il2cpp_runtime_invoke");unbox=rt_resolve("il2cpp_object_unbox");weak=rt_resolve("il2cpp_gchandle_new_weakref");target=rt_resolve("il2cpp_gchandle_get_target");release=rt_resolve("il2cpp_gchandle_free");string_length=rt_resolve("il2cpp_string_length");
    if(!invoke||!unbox||!weak||!target||!release||!string_length)return;
    RP image=rt_image("CriMw.CriWare.Runtime"),acb=rt_class(image,"CriWare","CriAtomExAcb"),player=rt_class(image,"CriWare","CriAtomExPlayer"),atom=rt_class(image,"CriWare","CriAtom"),ex=rt_class(image,"CriWare","CriAtomEx");
    int by_id[]={8,17},by_name[]={14,17},string[]={14};get_by_id=rt_checked_method(acb,"GetCueInfo",2,2,by_id,0);get_by_name=rt_checked_method(acb,"GetCueInfo",2,2,by_name,0);get_global_acb=rt_checked_method(atom,"GetAcb",1,18,string,1);
    RP info=nested(ex,"CueInfo");RP(*param)(RP,uint32_t)=rt_resolve("il2cpp_method_get_param"),(*from_type)(RP)=rt_resolve("il2cpp_class_from_type"),(*field_type)(RP)=rt_resolve("il2cpp_field_get_type");
    int(*offset)(RP)=rt_resolve("il2cpp_field_get_offset"),(*value_size)(RP,uint32_t*)=rt_resolve("il2cpp_class_value_size"),(*type)(RP)=rt_resolve("il2cpp_type_get_type");_Bool(*byref)(RP)=rt_resolve("il2cpp_type_is_byref");uint32_t alignment=0;
    if(!get_by_id||!get_by_name||!info||!param||!from_type||!field_type||!offset||!value_size||!type||!byref||from_type(param(get_by_id,1))!=info||from_type(param(get_by_name,1))!=info||!byref(param(get_by_id,1))||!byref(param(get_by_name,1)))return;
    int size=value_size(info,&alignment);if(size<1||size>512)return;
    const char*names[]={"id","type","length","numTracks","numRelatedWaveForms"};int expected[]={8,17,10,7,7},widths[]={4,4,8,2,2},fields[5];RP id_field=rt_field(info,"id");if(!id_field)return;int base=offset(id_field);if(base!=0&&base!=16)return;
    for(int i=0;i<5;i++){RP field=rt_field(info,names[i]);if(!field||type(field_type(field))!=expected[i])return;fields[i]=offset(field)-base;if(fields[i]<0||fields[i]+widths[i]>size)return;}
    id_offset=fields[0];type_offset=fields[1];length_offset=fields[2];tracks_offset=fields[3];waves_offset=fields[4];ready=1;
    int cue_id[]={18,8},cue_name[]={18,14};RP id_method=rt_checked_method(player,"SetCue",2,1,cue_id,0),name_method=rt_checked_method(player,"SetCue",2,1,cue_name,0);
    if(id_method&&from_type(param(id_method,0))==acb)rt_install_hook(*(RP*)id_method,set_id,(RP*)&set_id_original);
    if(name_method&&from_type(param(name_method,0))==acb)rt_install_hook(*(RP*)name_method,set_name,(RP*)&set_name_original);
    if(!set_id_original&&!set_name_original&&!get_global_acb)ready=0;
}
