/* GPL-3.0. Coverage-aware vocal arrangement; visual formation is untouched.
   Array ownership adaptation from pinned Hachimi LiveUtil; unlike upstream,
   input arrays are cloned and unsupported singer-part graphs stay original. */
#include "runtime.h"
#include "generated/vocal-coverage.h"
#include <string.h>
#include "routing-policy.h"
static unsigned char available[sizeof(coverage)/sizeof(coverage[0])];
static uint32_t mode,first,second;
static uint32_t(*array_length)(RP),(*array_header)(void),(*root_object)(RP,int);
static void(*free_root)(uint32_t);
static int(*array_stride)(RP);
static RP(*object_class)(RP),(*new_array)(RP,uint32_t);
static RP(*original_singers)(int,int,RP,RP,RP,RP);
void rt_routing_prepare(void){for(size_t i=0;i<sizeof(coverage)/sizeof(coverage[0]);i++)available[i]=rt_verify_asset(coverage[i].acb,coverage[i].sha,0)&&rt_verify_asset(coverage[i].awb,0,coverage[i].awb_bytes);}
static const Coverage*find(int song,int voice,int part){for(size_t i=0;i<sizeof(coverage)/sizeof(coverage[0]);i++)if(available[i]&&coverage[i].song==song&&coverage[i].voice==voice&&coverage[i].part==part)return &coverage[i];return 0;}
static int supported(int song,int part,int old_voice,int requested){const Coverage*from=find(song,old_voice,part),*to=find(song,requested,part);return from&&to&&strcmp(from->graph,to->graph)==0;}
static RP singers(int song,int part,RP all,RP vocals,RP shuffled,RP method){
    if(!rt_owned()||!rt_managed_thread()||!(mode&24)||!first||!all||!vocals)return original_singers(song,part,all,vocals,shuffled,method);
    uint32_t count=array_length(vocals),full_count=array_length(all),header=array_header();
    // Refuse ambiguous slot correspondence instead of replacing unrelated roles.
    if(!count||count>256||full_count<count||full_count>256||array_stride(object_class(all))!=4||array_stride(object_class(vocals))!=4||header<24||header>128){rt_detail(RT_READY,"Arrangement retained original voices: slot correspondence could not be verified");return original_singers(song,part,all,vocals,shuffled,method);}
    int*source=(int*)((char*)vocals+header),*all_source=(int*)((char*)all+header);int targets[256],changed=0,fallbacks=0;
    if(!routing_prefix(all_source,full_count,source,count)){rt_detail(RT_READY,"Arrangement retained original voices: vocal slots do not match the formation prefix");return original_singers(song,part,all,vocals,shuffled,method);}
    for(uint32_t i=0;i<count;i++){
        int requested=(mode&16)&&second&&(i&1)?(int)second:(int)first;
        targets[i]=source[i];
        if(source[i]<=0)continue;
        if(supported(song,part,source[i],requested)&&supported(song,part,all_source[i],requested)){targets[i]=requested;changed|=requested!=source[i];}
        else if(requested!=source[i])fallbacks++;
    }
    if(rt_audio_trace){rt_audio_trace->routing_song=song;rt_audio_trace->routing_part=part;rt_audio_trace->routing_fallbacks=fallbacks;rt_audio_trace->routing_replacements=0;}
    if(fallbacks)rt_detail(RT_READY,"Arrangement retained original singer for unavailable or incompatible cue parts");
    if(!changed)return original_singers(song,part,all,vocals,shuffled,method);
    RP all_copy=new_array(object_class(all),full_count);uint32_t all_root=all_copy?root_object(all_copy,0):0;
    RP vocal_copy=all_root?new_array(object_class(vocals),count):0;uint32_t vocal_root=vocal_copy?root_object(vocal_copy,0):0;
    if(!all_root||!vocal_root){if(all_root)free_root(all_root);if(vocal_root)free_root(vocal_root);return original_singers(song,part,all,vocals,shuffled,method);}
    memcpy((char*)all_copy+header,all_source,full_count*4);memcpy((char*)vocal_copy+header,source,count*4);
    for(uint32_t i=0;i<count;i++)if(targets[i]!=source[i]){((int*)((char*)all_copy+header))[i]=targets[i];((int*)((char*)vocal_copy+header))[i]=targets[i];}
    RP result=original_singers(song,part,all_copy,vocal_copy,shuffled,method);
    if(result&&rt_audio_trace){uint32_t replacements=0;for(uint32_t i=0;i<count;i++)replacements+=targets[i]!=source[i];rt_audio_trace->routing_replacements=replacements;}
    free_root(vocal_root);free_root(all_root);return result;
}
void rt_routing_initialize(void){
    RP klass=rt_class(rt_image("umamusume"),"Gallop","LiveUtil"),method=rt_method_info(klass,"GetSingCharaIdList",5);if(!method)return;
    RP(*param)(RP,uint32_t)=rt_resolve("il2cpp_method_get_param"),(*result)(RP)=rt_resolve("il2cpp_method_get_return_type"),(*class_from_type)(RP)=rt_resolve("il2cpp_class_from_type"),(*element)(RP)=rt_resolve("il2cpp_class_get_element_class"),(*class_type)(RP)=rt_resolve("il2cpp_class_get_type");
    int(*type)(RP)=rt_resolve("il2cpp_type_get_type");uint32_t(*flags)(RP,uint32_t*)=rt_resolve("il2cpp_method_get_flags");uint32_t implementation=0;
    if(!param||!result||!class_from_type||!element||!class_type||!type||!flags||(flags(method,&implementation)&0x10)==0||type(param(method,0))!=8||type(param(method,1))!=8||type(param(method,2))!=29||type(param(method,3))!=29)return;
    if(type(class_type(element(class_from_type(param(method,2)))))!=8||type(class_type(element(class_from_type(param(method,3)))))!=8)return;
    int output=type(result(method)),last=type(param(method,4));if((output!=18&&output!=21&&output!=29)||(last!=18&&last!=21))return;
    array_length=rt_resolve("il2cpp_array_length");array_header=rt_resolve("il2cpp_array_object_header_size");array_stride=rt_resolve("il2cpp_array_element_size");object_class=rt_resolve("il2cpp_object_get_class");new_array=rt_resolve("il2cpp_array_new_specific");root_object=rt_resolve("il2cpp_gchandle_new");free_root=rt_resolve("il2cpp_gchandle_free");
    if(!array_length||!array_header||!array_stride||!object_class||!new_array||!root_object||!free_root)return;
    int any=0;for(size_t i=0;i<sizeof(available);i++)any|=available[i];
    if(any&&rt_install_hook(*(RP*)method,singers,(RP*)&original_singers))rt_status->capabilities|=CAP_ROUTING;
}
void rt_routing_configure(uint32_t value,uint32_t voice1,uint32_t voice2){mode=value;first=voice1;second=voice2;}
