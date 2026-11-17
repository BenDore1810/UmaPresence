/* Isolated own-process tests. No game modules are opened or modified. */
#include "../runtime.h"
#include "../upstream/minhook/include/MinHook.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
typedef struct {const char*name;int count,result,params[2];uint32_t flags;} Method;
static RP methods(RP klass,RP*iterator){Method*m=klass;uintptr_t i=(uintptr_t)*iterator;if(!m[i].name)return 0;*iterator=(RP)(i+1);return &m[i];}
static RP parent(RP klass){(void)klass;return 0;}
static const char*name(RP m){return ((Method*)m)->name;}
static RP result(RP m){return (RP)(uintptr_t)((Method*)m)->result;}
static RP parameter(RP m,uint32_t i){return (RP)(uintptr_t)((Method*)m)->params[i];}
static int type(RP t){return (int)(uintptr_t)t;}
static uint32_t count(RP m){return ((Method*)m)->count;}
static uint32_t flags(RP m,uint32_t*impl){*impl=0;return ((Method*)m)->flags;}
int rt_same(const char*a,const char*b){return a&&b&&!strcmp(a,b);}
int rt_owned(void){return 1;}
RP rt_resolve(const char*symbol){
    if(rt_same(symbol,"il2cpp_class_get_methods"))return methods;
    if(rt_same(symbol,"il2cpp_class_get_parent"))return parent;
    if(rt_same(symbol,"il2cpp_method_get_name"))return name;
    if(rt_same(symbol,"il2cpp_method_get_return_type"))return result;
    if(rt_same(symbol,"il2cpp_method_get_param"))return parameter;
    if(rt_same(symbol,"il2cpp_type_get_type"))return type;
    if(rt_same(symbol,"il2cpp_method_get_param_count"))return count;
    if(rt_same(symbol,"il2cpp_method_get_flags"))return flags;
    return 0;
}
static volatile int total;
static __attribute__((noinline)) int target(int value){total+=value;MemoryBarrier();return total;}
static int(*original)(int);
static int replacement(int value){return original(value)+100;}
int main(void){
    Method overloads[]={{"GetCueInfo",2,2,{14,17},0},{"GetCueInfo",2,2,{8,17},0},{0}};int args[]={8,17};
    assert(rt_checked_method(overloads,"GetCueInfo",2,2,args,0)==&overloads[1]);puts("PASS: typed lookup finds integer cue overload after string overload");
    overloads[0].params[0]=8;assert(!rt_checked_method(overloads,"GetCueInfo",2,2,args,0));puts("PASS: ambiguous typed overloads are rejected");
    overloads[0].params[0]=14;assert(!rt_checked_method(overloads,"GetCueInfo",2,2,args,1));puts("PASS: static/instance mismatch is rejected");
    int(*volatile call_target)(int)=target;assert(call_target(2)==2);
    assert(rt_install_hook(target,replacement,(RP*)&original));assert(call_target(3)==105);puts("PASS: real own-process MinHook detour preserves original call");
    assert(MH_DisableHook(target)==MH_OK);assert(call_target(1)==6);MH_Uninitialize();puts("PASS: own-process detour removal restores original function");
    return 0;
}
