/* GPL-3.0. Formation only: visual duplication never rewrites vocal arrays.
   Adapted from pinned Hachimi CheckSwapChara; native metadata is validated. */
#include "runtime.h"
static volatile LONG requested_duplicates;
static int formation_ready;
static RP success_method;
static void(*local_success)(RP,RP,RP);
static void(*original_change)(RP,RP);
/* Pinned upstream LiveTheaterViewController::ChangeLive keeps duplicate
   formations local. Its JP success callback does not consume the server
   response. Selection alone is insufficient: submitting duplicates is rejected
   by the normal formation save request (reported as error 205). */
static void change_live(RP self,RP method){
    if(formation_ready&&rt_owned()&&rt_managed_thread()&&InterlockedCompareExchange(&requested_duplicates,0,0)){
        local_success(self,0,success_method);return;
    }
    original_change(self,method);
}
static void(*original_swap)(RP,int,int,int,int,int,int,int,RP);
static void swap(RP self,int index,int old_character,int old_dress,int old_color,int old_dress2,int old_color2,int new_character,RP method){
    if(rt_audio_trace)InterlockedIncrement((volatile LONG*)&rt_audio_trace->formation_checks);
    if(rt_owned()&&InterlockedCompareExchange(&requested_duplicates,0,0)){if(rt_audio_trace)InterlockedIncrement((volatile LONG*)&rt_audio_trace->formation_bypasses);return;}
    original_swap(self,index,old_character,old_dress,old_color,old_dress2,old_color2,new_character,method);
}
void rt_formation_initialize(void){
    RP image=rt_image("umamusume");
    RP controller=rt_class(image,"Gallop","LiveTheaterViewController");int response[]={18};
    RP confirm=rt_checked_method(controller,"ChangeLive",0,1,0,0);
    success_method=rt_checked_method(controller,"<ChangeLive>b__48_1",1,1,response,0);
    if(!confirm||!success_method||!*(RP*)success_method){rt_boot_log("Duplicate casting unavailable: JP local confirmation callback not validated");return;}
    local_success=*(RP*)success_method;
    if(!rt_install_hook(*(RP*)confirm,change_live,(RP*)&original_change))return;
    RP klass=rt_class(image,"Gallop","LiveTheaterCharaSelect");int ints[]={8,8,8,8,8,8,8};
    RP method=rt_checked_method(klass,"CheckSwapChara",7,1,ints,0);
    if(method&&rt_install_hook(*(RP*)method,swap,(RP*)&original_swap)){formation_ready=1;rt_status->capabilities|=CAP_FORMATION;rt_boot_log("Duplicate casting: selection and local confirmation hooks ready");}
}
void rt_formation_configure(uint32_t flags,uint32_t voice1,uint32_t voice2){
    InterlockedExchange(&requested_duplicates,(flags&1)!=0);
#ifdef UMAPRESENCE_PUBLIC_RELEASE
    (void)voice1;(void)voice2;
#else
    rt_mix_configure(flags,voice1,voice2);rt_routing_configure(flags,voice1,voice2);
#endif
}
