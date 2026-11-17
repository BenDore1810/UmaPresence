/* GPL-3.0. Typed, build-profile-gated hooks; MinHook retains its own license. */
#include "runtime.h"
#include "upstream/minhook/include/MinHook.h"
RP rt_checked_method(RP klass,const char*name,int count,int result,const int*parameters,int is_static){
    RP(*returns)(RP)=rt_resolve("il2cpp_method_get_return_type"),(*parameter)(RP,uint32_t)=rt_resolve("il2cpp_method_get_param");
    int(*type)(RP)=rt_resolve("il2cpp_type_get_type");uint32_t(*flags)(RP,uint32_t*)=rt_resolve("il2cpp_method_get_flags");
    RP(*methods)(RP,RP*)=rt_resolve("il2cpp_class_get_methods"),(*parent)(RP)=rt_resolve("il2cpp_class_get_parent");
    const char*(*method_name)(RP)=rt_resolve("il2cpp_method_get_name");uint32_t(*arity)(RP)=rt_resolve("il2cpp_method_get_param_count");
    _Bool(*is_generic)(RP)=rt_resolve("il2cpp_method_is_generic");
    if(!returns||!parameter||!type||!flags||!methods||!parent||!method_name||!arity)return 0;
    for(int depth=0;klass&&depth<8;klass=parent(klass),depth++){
        RP iterator=0,method,found=0;
        while((method=methods(klass,&iterator))){
            if(is_generic&&is_generic(method))continue;
            if(!rt_same(method_name(method),name)||arity(method)!=(uint32_t)count||type(returns(method))!=result)continue;
            uint32_t implementation=0;if(((flags(method,&implementation)&0x10)!=0)!=is_static)continue;
            int match=1;for(int i=0;i<count;i++)if(type(parameter(method,i))!=parameters[i]){match=0;break;}
            if(match){if(found)return 0;found=method;}
        }
        if(found)return found;
    }
    return 0;
}
int rt_install_hook(RP target,RP detour,RP*original){
    if(!target||!detour||!original||!rt_owned())return 0;
    MEMORY_BASIC_INFORMATION region;if(!VirtualQuery(target,&region,sizeof(region))||region.State!=MEM_COMMIT||!(region.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY)))return 0;
    MH_STATUS initialized=MH_Initialize();if(initialized!=MH_OK&&initialized!=MH_ERROR_ALREADY_INITIALIZED)return 0;
    if(MH_CreateHook(target,detour,original)!=MH_OK)return 0;
    if(MH_EnableHook(target)!=MH_OK){MH_RemoveHook(target);*original=0;return 0;}return 1;
}
