/* Own-process control-flow regression; no game is loaded or modified. */
#include "../runtime.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
static RuntimeStatus status;
volatile RuntimeStatus *rt_status=&status;
volatile RuntimeAudioTrace *rt_audio_trace;
static int owned=1,managed=1,missing,changed,completed,swapped,hooks;
static RP expected_method=(RP)0x1234;
static void original_confirm(RP self,RP method){(void)self;(void)method;changed++;}
static void success(RP self,RP response,RP method){assert(self==(RP)1);assert(!response);assert(method==expected_method);completed++;}
static void original_select(RP self,int a,int b,int c,int d,int e,int f,int g,RP method){(void)self;(void)a;(void)b;(void)c;(void)d;(void)e;(void)f;(void)g;(void)method;swapped++;}
static RP confirm_info[1],success_info[1],select_info[1];
int rt_owned(void){return owned;}
int rt_managed_thread(void){return managed;}
void rt_boot_log(const char*s){(void)s;}
RP rt_image(const char*s){(void)s;return (RP)1;}
RP rt_class(RP i,const char*n,const char*c){(void)i;(void)n;(void)c;return (RP)1;}
RP rt_checked_method(RP k,const char*n,int count,int result,const int*p,int is_static){
    (void)k;assert(result==1&&!is_static);
    if(!strcmp(n,"ChangeLive")){assert(count==0);return confirm_info;}
    if(!strcmp(n,"<ChangeLive>b__48_1")){assert(count==1&&p[0]==18);return missing?0:success_info;}
    assert(!strcmp(n,"CheckSwapChara")&&count==7);for(int i=0;i<7;i++)assert(p[i]==8);return select_info;
}
int rt_install_hook(RP target,RP detour,RP*original){(void)detour;*original=target;hooks++;return 1;}
void rt_mix_configure(uint32_t a,uint32_t b,uint32_t c){(void)a;(void)b;(void)c;}
void rt_routing_configure(uint32_t a,uint32_t b,uint32_t c){(void)a;(void)b;(void)c;}
#include "../formation.c"
int main(void){
    confirm_info[0]=original_confirm;success_info[0]=success;select_info[0]=original_select;
    missing=1;rt_formation_initialize();assert(!hooks&&!(status.capabilities&CAP_FORMATION));puts("PASS: missing confirmation callback installs neither hook nor formation capability");
    missing=0;rt_formation_initialize();assert(hooks==2&&(status.capabilities&CAP_FORMATION));
    expected_method=success_info;puts("PASS: both typed selection and confirmation hooks required for formation support");
    rt_formation_configure(0,0,0);change_live((RP)1,0);assert(changed==1&&!completed);puts("PASS: disabled duplicate casting preserves normal confirmation");
    rt_formation_configure(1,0,0);change_live((RP)1,0);assert(changed==1&&completed==1);puts("PASS: enabled duplicate casting completes locally without submitting duplicate formation");
    managed=0;change_live((RP)1,0);managed=1;owned=0;change_live((RP)1,0);assert(changed==3&&completed==1);owned=1;puts("PASS: foreign ownership and unregistered threads retain original confirmation");
    swap((RP)1,0,0,0,0,0,0,0,0);assert(!swapped);rt_formation_configure(0,0,0);swap((RP)1,0,0,0,0,0,0,0,0);assert(swapped==1);puts("PASS: duplicate selection bypass reverses when disabled");
    return 0;
}
