/* Execute the real routing adapter against own-process array fixtures. */
#include "../runtime.h"
#include <assert.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
volatile RuntimeStatus *rt_status;
static RuntimeAudioTrace trace;
volatile RuntimeAudioTrace *rt_audio_trace=&trace;
int rt_owned(void){return 1;}int rt_managed_thread(void){return 1;}
int rt_verify_asset(const char*a,const char*b,uint64_t c){(void)a;(void)b;(void)c;return 1;}
int rt_same(const char*a,const char*b){return !strcmp(a,b);}
void rt_detail(uint32_t state,const char*text){(void)state;(void)text;}
RP rt_resolve(const char*n){(void)n;return 0;}RP rt_image(const char*n){(void)n;return 0;}
RP rt_class(RP a,const char*b,const char*c){(void)a;(void)b;(void)c;return 0;}
RP rt_method_info(RP a,const char*b,int c){(void)a;(void)b;(void)c;return 0;}
int rt_install_hook(RP a,RP b,RP*c){(void)a;(void)b;(void)c;return 0;}
#include "../routing.c"
typedef struct {uint32_t count;char reserved[28];int values[256];} Array;
static RP roots[8];static uint32_t root_next;
static uint32_t length(RP a){return ((Array*)a)->count;}static uint32_t header(void){return 32;}
static int stride(RP a){(void)a;return 4;}static RP klass(RP a){return a;}
static RP allocate(RP type,uint32_t n){(void)type;Array*a=calloc(1,sizeof(Array));a->count=n;return a;}
static uint32_t root(RP a,int pinned){(void)pinned;roots[++root_next]=a;return root_next;}
static void unroot(uint32_t index){free(roots[index]);roots[index]=0;}
static Array *input_all,*input_vocals;static int changed;
static RP capture(int song,int part,RP all,RP vocals,RP shuffled,RP method){
    (void)song;(void)part;(void)shuffled;(void)method;Array*a=all,*v=vocals;
    assert(a->count==14&&v->count==3);
    if(changed){assert(a!=input_all&&v!=input_vocals);for(int i=0;i<3;i++)assert(a->values[i]==1004&&v->values[i]==1004);for(int i=3;i<14;i++)assert(a->values[i]==input_all->values[i]);}
    else assert(a==input_all&&v==input_vocals);
    return (RP)123;
}
int main(void){
    array_length=length;array_header=header;array_stride=stride;object_class=klass;new_array=allocate;root_object=root;free_root=unroot;original_singers=capture;
    rt_routing_prepare();Array all={.count=14},vocals={.count=3};for(int i=0;i<14;i++)all.values[i]=i<3?1003:1001+i;for(int i=0;i<3;i++)vocals.values[i]=1003;
    input_all=&all;input_vocals=&vocals;changed=1;rt_routing_configure(8,1004,0);
    assert(singers(1176,1,&all,&vocals,0,0)==(RP)123);assert(trace.routing_replacements==3);assert(all.values[0]==1003&&vocals.values[0]==1003);puts("PASS: real Solo adapter clones a fourteen-slot formation, routes three vocals and preserves all backdancers and inputs");
    changed=0;all.values[0]=1001;assert(singers(1176,1,&all,&vocals,0,0)==(RP)123);puts("PASS: real adapter retains original arrays when vocal-prefix identity is ambiguous");
    all.values[0]=1003;rt_routing_configure(8,1999,0);assert(singers(1176,1,&all,&vocals,0,0)==(RP)123&&trace.routing_fallbacks==3);puts("PASS: unsupported recording reports three fallbacks and preserves existing singers");
    rt_routing_configure(8,1004,0);assert(singers(1176,0,&all,&vocals,0,0)==(RP)123);puts("PASS: unknown part identifier never guesses a recording or replaces a line");
    return 0;
}
