/* Race telemetry is optional and independent of concert/career compatibility. */
typedef struct { u32 magic; i32 active,phase,program,course,length; float meters; i32 count; i32 charas[3],ranks[3],gates[3]; i32 total,kind,program_type; u32 track_magic; i32 track_version,season,weather,ground_condition; } RacePacket;
/* RIV3 adds an official gap-to-winner value when directly verified or summed
   from a complete chain of numeric official result margins. */
typedef struct { i32 chara,rank,gate,flags,score,skills; char trainer[96]; i32 margin_kind; float gap_lengths; } RaceRivalRecord;
typedef struct { u32 magic; i32 version,active,count,program,kind,program_type; RaceRivalRecord entries[18]; } RaceRivalsPacket;
typedef char race_rivals_record_size_is_128[(sizeof(RaceRivalRecord)==128)?1:-1];
typedef char race_rivals_packet_size_is_2332[(sizeof(RaceRivalsPacket)==2332)?1:-1];
static RacePacket race_packet;
static RaceRivalsPacket race_rivals;
static Ptr race_rival_objects[18];
static Ptr race_seen[3];
static i32 race_cached_count,race_cached_program,race_cached_program_type,race_cached_charas[3],race_cached_gates[3];
static Ptr race_class;
static Ptr (*race_info_fn)(void);
static char race_debug[3000];
static i32 race_debug_length,race_disabled,race_describe;
static u64 race_last_diagnostic;
typedef struct {Ptr klass,method;const char*name;} RaceMethodCache;
static RaceMethodCache race_method_cache[512];
static i32 race_method_cache_count;
static const char *race_group_getter;
static _Bool (*race_class_is_enum)(Ptr);
static void (*race_static_value)(Ptr,Ptr);
static u32 (*race_condition_method_flags)(Ptr,u32*);
static Ptr race_ground_enum_class,race_ground_text_method;
static Ptr race_type_class;
static void race_note(const char *s) { if(!s||!race_describe)return; while(*s&&race_debug_length<2998)race_debug[race_debug_length++]=*s++;race_debug[race_debug_length]=0; }
static i32 race_contains(const char *s,const char *word) { if(!s)return 0;for(;*s;s++){const char *a=s,*b=word;while(*a&&*b&&*a==*b){a++;b++;}if(!*b)return 1;}return 0; }
static i32 race_scene(Ptr scene,const char *word) { char text[128];i32 n=scene?string_length(scene):0;u16*s=scene?string_chars(scene):0;if(!s||n<1||n>127)return 0;for(i32 i=0;i<n;i++)text[i]=(char)(s[i]<128?s[i]:0);text[n]=0;return race_contains(text,word); }
static Ptr race_method(Ptr klass,const char *name){
    if(!klass||!class_method)return 0;
    for(i32 i=0;i<race_method_cache_count;i++)if(race_method_cache[i].klass==klass&&same(race_method_cache[i].name,name))return race_method_cache[i].method;
    Ptr result=class_method(klass,name,0);
    if(!result&&class_methods&&method_name&&method_params){char wanted[64];normalized(name,wanted);Ptr c=klass;
        for(i32 level=0;c&&level<4&&!result;level++,c=class_parent?class_parent(c):0){Ptr it=0,m;i32 budget=512;while(budget--&&(m=class_methods(c,&it))){if(method_params(m)!=0)continue;const char*n=method_name(m);if(!n)continue;char key[64];normalized(n,key);if(same(wanted,key)){result=m;break;}}}
    }
    if(race_method_cache_count<512){i32 i=race_method_cache_count++;race_method_cache[i].klass=klass;race_method_cache[i].name=name;race_method_cache[i].method=result;}
    return result;
}
static void race_schema(Ptr object){
    if(!race_describe||!object)return;Ptr klass=object_class(object);race_note(" [");race_note(class_name?class_name(klass):"object");race_note("] ");
    if(class_methods&&method_name){Ptr it=0,method;i32 budget=256;while(budget--&&(method=class_methods(klass,&it))){const char*name=method_name(method);if(name&&(race_contains(name,"Viewer")||race_contains(name,"Trainer")||race_contains(name,"Chara")||race_contains(name,"Card")||race_contains(name,"Rank")||race_contains(name,"Order")||race_contains(name,"Rival"))){race_note(name);race_note("; ");}}}
    Ptr it=0,field;i32 budget=24;while(budget--&&(field=class_fields(klass,&it))){race_note(field_name(field));race_note("; ");}
}
static void race_registration_schema(Ptr object){
    if(!race_describe||!object)return;Ptr klass=object_class(object);race_note(" [");race_note(class_name?class_name(klass):"object");race_note("] ");
    for(i32 level=0;klass&&level<3;level++,klass=class_parent?class_parent(klass):0){Ptr it=0,field;i32 budget=128;
        while(budget--&&(field=class_fields(klass,&it))){const char*n=field_name(field);if(!n)continue;char k[64];normalized(n,k);
            if(race_contains(k,"horse")||race_contains(k,"race")||race_contains(k,"champions")||race_contains(k,"room")||race_contains(k,"ultimate")||race_contains(k,"singlemode")||race_contains(k,"player")||race_contains(k,"status")||race_contains(k,"target")||race_contains(k,"team")||race_contains(k,"order")||race_contains(k,"entry")||race_contains(k,"chara")||race_contains(k,"select")){race_note(n);race_note("; ");}
        }
    }
}
static Ptr race_object_field(Ptr object,const char *key) {
    if(!object)return 0;
    Ptr klass=object_class(object);
    for(i32 level=0;klass&&level<4;level++,klass=class_parent?class_parent(klass):0){
        Ptr it=0,field;i32 budget=256;
        while(budget--&&(field=class_fields(klass,&it))){
            if(field_flags&&(field_flags(field)&0x10))continue;
            char normalized_name[64];const char*name=field_name(field);if(!name)continue;normalized(name,normalized_name);
            i32 type=type_code(field_type(field));
            if(same(normalized_name,key)&&(type==0x12||type==0x15||type==0x1c)){Ptr value=0;field_value(object,field,&value);return value;}
        }
    }return 0;
}
static Ptr race_ref(Ptr object,const char *name) {
    if(!object||!class_method||!runtime_invoke||!method_return)return 0;
    Ptr method=race_method(object_class(object),name);if(!method)return 0;
    i32 type=type_code(method_return(method));if(type!=0x12&&type!=0x15&&type!=0x1c&&type!=0x1d)return 0;
    Ptr ex=0,value=runtime_invoke(method,object,0,&ex);return ex?0:value;
}
static i32 race_text(Ptr object,const char *getter,char*out,i32 capacity) {
    if(!object||!out||capacity<2||!method_return||!runtime_invoke||!string_length||!string_chars)return 0;
    out[0]=0;Ptr method=race_method(object_class(object),getter);
    if(!method||type_code(method_return(method))!=14)return 0;
    Ptr ex=0,text=runtime_invoke(method,object,0,&ex);if(ex||!text)return 0;
    i32 length=string_length(text);u16*s=string_chars(text);if(!s||length<1||length>96)return 0;
    i32 used=0;
    for(i32 i=0;i<length;i++){
        u32 c=s[i];if(c>=0xd800&&c<=0xdbff&&i+1<length&&s[i+1]>=0xdc00&&s[i+1]<=0xdfff)c=0x10000+((c-0xd800)<<10)+(s[++i]-0xdc00);
        if(c>=0xd800&&c<=0xdfff)return 0;if(c<32)c=' ';
        i32 need=c<0x80?1:c<0x800?2:c<0x10000?3:4;if(used+need>=capacity)break;
        if(need==1)out[used++]=(char)c;
        else if(need==2){out[used++]=(char)(0xc0|(c>>6));out[used++]=(char)(0x80|(c&63));}
        else if(need==3){out[used++]=(char)(0xe0|(c>>12));out[used++]=(char)(0x80|((c>>6)&63));out[used++]=(char)(0x80|(c&63));}
        else {out[used++]=(char)(0xf0|(c>>18));out[used++]=(char)(0x80|((c>>12)&63));out[used++]=(char)(0x80|((c>>6)&63));out[used++]=(char)(0x80|(c&63));}
    }
    while(used>0&&(out[used-1]==' '||out[used-1]=='\t'))used--;out[used]=0;return used>0;
}
static i32 race_trainer_name(Ptr object,char*out,i32 capacity) {
    return race_text(object,"get_TrainerName",out,capacity);
}
static i32 race_bool_property(Ptr object,const char*name,i32*out) {
    if(!object||!out||!method_return||!runtime_invoke||!object_unbox)return 0;
    Ptr method=race_method(object_class(object),name);if(!method||type_code(method_return(method))!=2)return 0;
    Ptr ex=0,boxed=runtime_invoke(method,object,0,&ex);if(ex||!boxed)return 0;Ptr value=object_unbox(boxed);if(!value)return 0;
    unsigned char b=0;memcpy(&b,value,1);*out=b!=0;return 1;
}
static i32 race_is_marked_rival(Ptr object) {
    /* Only a dedicated career-rival boolean is unambiguous. A generic IsRival
       flag can include scripted/NPC opponents, so it must not create a badge. */
    Ptr method=object?race_method(object_class(object),"get_IsCareerRival"):0;
    if(method&&race_describe){race_note("Career rival marker getter: get_IsCareerRival; ");race_schema(object);}
    i32 value=0;return method&&race_bool_property(object,"get_IsCareerRival",&value)&&value;
}
static i32 race_number(Ptr object,const char*name,i32*out){
    if(!object)return 0;Ptr method=race_method(object_class(object),name);if(!method)return 0;
    i32 type=type_code(method_return(method));if(type!=8&&type!=9&&type!=0x11)return 0;
    Ptr ex=0,boxed=runtime_invoke(method,object,0,&ex);if(ex||!boxed)return 0;
    if(type==0x11)return career_unwrap(boxed,out);
    Ptr value=object_unbox(boxed);if(!value)return 0;memcpy(out,value,4);return 1;
}
static i32 race_int_field(Ptr object,const char*key,i32*out){
    if(!object)return 0;Ptr klass=object_class(object);
    for(i32 level=0;klass&&level<4;level++,klass=class_parent?class_parent(klass):0){Ptr it=0,field;i32 budget=256;
        while(budget--&&(field=class_fields(klass,&it))){
            if(field_flags&&(field_flags(field)&0x10))continue;
            const char*name=field_name(field);if(!name)continue;char key_name[64];normalized(name,key_name);
            i32 type=type_code(field_type(field));if(same(key_name,key)&&(type==8||type==9)){field_value(object,field,out);return 1;}
        }
    }return 0;
}
static u64 race_identity(Ptr object,const char *name) {
    if(!object||!class_method||!runtime_invoke||!method_return||!object_unbox)return 0;
    Ptr method=race_method(object_class(object),name);if(!method)return 0;
    i32 type=type_code(method_return(method));if(type!=8&&type!=9&&type!=10&&type!=11&&type!=0x11&&type!=14)return 0;
    if(race_describe){race_note(name);race_note(" type=");char hex[3]={"0123456789abcdef"[(type>>4)&15],"0123456789abcdef"[type&15],0};race_note(hex);race_note("; ");}
    Ptr ex=0,boxed=runtime_invoke(method,object,0,&ex);if(ex||!boxed)return 0;
    if(type==14){i32 length=string_length(boxed);u16*s=string_chars(boxed);if(!s||length<1||length>20)return 0;u64 value=0;for(i32 i=0;i<length;i++){if(s[i]<'0'||s[i]>'9'||value>1844674407370955161ULL)return 0;u32 digit=s[i]-'0';if(value==1844674407370955161ULL&&digit>5)return 0;value=value*10+digit;}return value;}
    if(type==0x11){
        i32 actual=class_type?type_code(class_type(object_class(boxed))):0;
        if(actual==8||actual==9||actual==10||actual==11)type=actual;
        else {
            Ptr klass=object_class(boxed),wrapper=object_unbox(boxed),getter=race_method(klass,"get_Value");if(!wrapper)return 0;
            if(getter){type=type_code(method_return(getter));if(type!=8&&type!=9&&type!=10&&type!=11)return 0;boxed=runtime_invoke(getter,wrapper,0,&ex);if(ex||!boxed)return 0;}
            else {
                race_note("Identity wrapper: ");race_note(class_name?class_name(klass):"?");race_note("; ");
                if(!class_methods||!method_name||!method_params||!method_param||!class_from_type)return 0;
                Ptr it=0,m;i32 budget=128,found=0;
                while(budget--&&(m=class_methods(klass,&it))){const char*n=method_name(m);i32 t=type_code(method_return(m));
                    if(n&&same(n,"op_Implicit")&&method_params(m)==1&&(t==8||t==9||t==10||t==11)&&class_from_type(method_param(m,0))==klass){
                        Ptr arg=wrapper;boxed=runtime_invoke(m,0,&arg,&ex);if(ex||!boxed)return 0;type=t;found=1;break;
                    }
                }if(!found)return 0;
            }
        }
    }
    Ptr p=object_unbox(boxed);if(!p)return 0;u64 n=0;memcpy(&n,p,type==8||type==9?4:8);return n;
}
static void race_mode(Ptr info) {
    if(!info||!race_class_is_enum||!race_static_value||!class_from_type)return;
    Ptr method=race_method(object_class(info),"get_RaceType");if(!method)return;
    Ptr klass=class_from_type(method_return(method));if(!klass||!race_class_is_enum(klass))klass=race_type_class;if(!klass||!race_class_is_enum(klass))return;
    Ptr ex=0,boxed=runtime_invoke(method,info,0,&ex);if(ex||!boxed)return;Ptr p=object_unbox(boxed);if(!p)return;i32 mode=0;memcpy(&mode,p,4);
    Ptr it=0,field;i32 budget=128;while(budget--&&(field=class_fields(klass,&it))){const char*name=field_name(field);if(!name||!field_flags||(field_flags(field)&0x50)!=0x50)continue;i32 n=0;race_static_value(field,&n);if(n!=mode)continue;char key[64];normalized(name,key);race_note("Race type: ");race_note(name);race_note("; ");
        if(race_contains(key,"champions"))race_packet.kind=1;
        else if(race_contains(key,"room"))race_packet.kind=2;
        else if(race_contains(key,"leagueofheroes")||race_contains(key,"heroes"))race_packet.kind=3;
        else if(race_contains(key,"singlemode")||race_contains(key,"career"))race_packet.kind=4;
        else if(race_contains(key,"masterchallenge")||same(key,"ultimate"))race_packet.kind=5;
    }
}
static i32 race_ground_display(Ptr enum_class,i32 value){
    if(!method_param||!class_from_type||!class_methods||!method_name||!method_params||!race_condition_method_flags)return 0;
    if(race_ground_enum_class!=enum_class){
        race_ground_enum_class=enum_class;race_ground_text_method=0;
        Ptr image=get_image("umamusume"),klass=image?get_class(image,"Gallop","RaceUtil"):0;
        Ptr it=0,m;i32 budget=1024;
        while(klass&&budget--&&(m=class_methods(klass,&it))){
            const char*n=method_name(m);if(!n||!same(n,"GetConditionText")||method_params(m)!=1||type_code(method_return(m))!=14)continue;
            /* There is also an Int32 overload with the same argument count. */
            if(type_code(method_param(m,0))!=0x11||class_from_type(method_param(m,0))!=enum_class)continue;
            u32 impl=0;if(!(race_condition_method_flags(m,&impl)&0x10))continue;
            race_ground_text_method=m;break;
        }
    }
    if(!race_ground_text_method)return 0;
    Ptr arg=&value,ex=0,text=runtime_invoke(race_ground_text_method,0,&arg,&ex);
    if(ex||!text)return 0;i32 count=string_length(text);u16*s=string_chars(text);if(!s||count<1||count>64)return 0;
    while(count>0&&(*s==' '||*s==0xa0)){s++;count--;}while(count>0&&(s[count-1]==' '||s[count-1]==0xa0))count--;
    if(race_describe){
        char utf8[193];i32 used=0;
        for(i32 i=0;i<count;i++){
            u32 c=s[i];if(c>=0xd800&&c<=0xdbff&&i+1<count&&s[i+1]>=0xdc00&&s[i+1]<=0xdfff)c=0x10000+((c-0xd800)<<10)+(s[++i]-0xdc00);
            if(c<32)c=' ';if(c>=0xd800&&c<=0xdfff)c='?';
            if(c<128)utf8[used++]=(char)c;
            else if(c<2048){utf8[used++]=(char)(0xc0|(c>>6));utf8[used++]=(char)(0x80|(c&63));}
            else if(c<65536){utf8[used++]=(char)(0xe0|(c>>12));utf8[used++]=(char)(0x80|((c>>6)&63));utf8[used++]=(char)(0x80|(c&63));}
            else {utf8[used++]=(char)(0xf0|(c>>18));utf8[used++]=(char)(0x80|((c>>12)&63));utf8[used++]=(char)(0x80|((c>>6)&63));utf8[used++]=(char)(0x80|(c&63));}
        }
        utf8[used]=0;race_note("Ground formatter text: ");race_note(utf8);race_note("; ");
    }
    i32 label=0;
    if(count==1&&s[0]==0x826f)label=1; /* 良 */
    else if(count==2&&s[0]==0x7a0d&&s[1]==0x91cd)label=2; /* 稍重 */
    else if(count==1&&s[0]==0x91cd)label=3; /* 重 */
    else if(count==2&&s[0]==0x4e0d&&s[1]==0x826f)label=4; /* 不良 */
    else {
        char english[65];i32 valid=1;for(i32 i=0;i<count;i++){u16 c=s[i];if(c>127){valid=0;break;}english[i]=(char)(c>='A'&&c<='Z'?c+32:c);}english[count]=0;
        if(valid){if(same(english,"firm"))label=1;else if(same(english,"good"))label=2;else if(same(english,"soft"))label=3;else if(same(english,"heavy"))label=4;}
    }
    if(label){const char*names[]={"","Firm","Good","Soft","Heavy"};race_note("Ground source: RaceUtil.GetConditionText(GroundCondition) = ");race_note(names[label]);race_note("; ");}
    return label;
}
/* Resolve named enum constants instead of assuming the game's integer ordering.
   Missing getters, primitive return types and unknown names remain unavailable. */
static i32 race_condition(Ptr info,const char*getter,i32 kind) {
    if(!info||!race_class_is_enum||!class_from_type)return 0;
    Ptr method=race_method(object_class(info),getter);if(!method||type_code(method_return(method))!=0x11)return 0;
    Ptr klass=class_from_type(method_return(method));if(!klass||!race_class_is_enum(klass))return 0;
    Ptr ex=0,boxed=runtime_invoke(method,info,0,&ex);if(ex||!boxed)return 0;
    Ptr unboxed=object_unbox(boxed);if(!unboxed)return 0;i32 value=0;memcpy(&value,unboxed,4);
    if(kind==3){i32 displayed=race_ground_display(klass,value);if(displayed)return displayed;}
    if(!race_static_value||!field_flags)return 0;
    i32 ground_scheme=0;
    if(kind==3){
        Ptr scan=0,f;i32 left=64,mask=0;
        while(left--&&(f=class_fields(klass,&scan))){
            if((field_flags(f)&0x50)!=0x50)continue;const char*n=field_name(f);if(!n)continue;char k[64];normalized(n,k);
            if(same(k,"good"))mask|=1;if(same(k,"littlebad"))mask|=2;if(same(k,"bad"))mask|=4;if(same(k,"verybad"))mask|=8;
            if(same(k,"slightlyheavy"))mask|=16;if(same(k,"heavy"))mask|=32;if(same(k,"firm"))mask|=64;if(same(k,"soft"))mask|=128;
        }
        if((mask&15)==15)ground_scheme=1;
        else if((mask&53)==53)ground_scheme=2;
        else if((mask&225)==225)ground_scheme=3;
    }
    Ptr it=0,field;i32 budget=64;
    while(budget--&&(field=class_fields(klass,&it))){
        if((field_flags(field)&0x50)!=0x50)continue;
        i32 constant=0;race_static_value(field,&constant);if(constant!=value)continue;
        const char*name=field_name(field);if(!name)continue;char key[64];normalized(name,key);
        race_note("Track condition: ");race_note(getter);race_note("=");race_note(name);race_note("; ");
        if(kind==1){if(same(key,"spring"))return 1;if(same(key,"summer"))return 2;if(same(key,"autumn")||same(key,"fall"))return 3;if(same(key,"winter"))return 4;}
        if(kind==2){if(same(key,"sunny")||same(key,"fine"))return 1;if(same(key,"cloudy")||same(key,"cloud"))return 2;if(same(key,"rain")||same(key,"rainy"))return 3;if(same(key,"snow")||same(key,"snowy"))return 4;}
        /* Good/Heavy have different meanings in different enum naming schemes.
           Require the complete named scheme before mapping either one. */
        if(kind==3&&ground_scheme==1){if(same(key,"good"))return 1;if(same(key,"littlebad"))return 2;if(same(key,"bad"))return 3;if(same(key,"verybad"))return 4;}
        if(kind==3&&ground_scheme==2){if(same(key,"good"))return 1;if(same(key,"slightlyheavy"))return 2;if(same(key,"heavy"))return 3;if(same(key,"bad"))return 4;}
        if(kind==3&&ground_scheme==3){if(same(key,"firm"))return 1;if(same(key,"good"))return 2;if(same(key,"soft"))return 3;if(same(key,"heavy"))return 4;}
    }return 0;
}
static i32 race_bool(Ptr object,const char *name) {
    if(!object||!class_method)return 0;Ptr method=race_method(object_class(object),name);
    if(!method||type_code(method_return(method))!=2)return 0;
    Ptr ex=0,boxed=runtime_invoke(method,object,0,&ex);if(ex||!boxed)return 0;unsigned char n=0;Ptr p=object_unbox(boxed);if(p)memcpy(&n,p,1);return n==1;
}
static i32 race_result_ui(void){
    Ptr image=get_image("umamusume"),c=image?get_class(image,"Gallop","SceneManager"):0,manager=c?singleton(c):0;
    Ptr controller=race_object_field(manager,"currentviewcontroller");if(!controller||!same(class_name(object_class(controller)),"RaceMainViewController"))return 0;
    const char*panels[]={"raceresultchampionsview","resultlist"};
    for(i32 i=0;i<2;i++){Ptr panel=race_object_field(controller,panels[i]),go=race_ref(panel,"get_gameObject");if(race_bool(go,"get_activeInHierarchy"))return 1;}return 0;
}
static i32 race_float_field(Ptr object,const char*key,float*out) {
    if(!object)return 0;Ptr klass=object_class(object);
    for(i32 level=0;klass&&level<4;level++,klass=class_parent?class_parent(klass):0){Ptr it=0,field;i32 budget=256;while(budget--&&(field=class_fields(klass,&it))){
        if(field_flags&&(field_flags(field)&0x10))continue;
        const char *name=field_name(field);if(!name)continue;char normalized_name[64];normalized(name,normalized_name);
        if(same(normalized_name,key)&&type_code(field_type(field))==12){field_value(object,field,out);return *out>=0&&*out<100000;}
    }}return 0;
}
static u64 race_owner(Ptr object){
    if(!object)return 0;
    if(race_group_getter){i32 group=-1;return race_number(object,race_group_getter,&group)&&group>=0&&group<=64?(u64)group+1:0;}
    u64 owner=race_identity(object,"get_ViewerId");if(!owner)owner=race_identity(object,"get_TrainerId");if(!owner)owner=race_identity(object,"get_OwnerViewerId");
    if(!owner)owner=race_identity(object,"get_ViewerIdString");if(!owner)owner=race_identity(object,"get_TrainerIdString");
    if(!owner){Ptr trainer=race_ref(object,"get_TrainerInfo");if(!trainer)trainer=race_ref(object,"get_TrainerData");if(!trainer)trainer=race_object_field(object,"trainerinfo");if(!trainer)trainer=race_object_field(object,"trainerdata");owner=race_identity(trainer,"get_ViewerId");}
    return owner;
}
static void race_add(Ptr object,u64 viewer,i32 fallback_rank,i32 gate) {
    if(fallback_rank<0)return;
    u64 owner=race_owner(object);
    if(!viewer||owner!=viewer||race_packet.count>=3)return;
    for(i32 i=0;i<race_packet.count;i++)if(race_seen[i]==object)return;
    i32 chara=0,card=0,rank=fallback_rank;
    if(!race_number(object,"get_CharaId",&chara))race_number(object,"get_charaId",&chara);
    if(!chara)race_int_field(object,"charaid",&chara);
    if(!chara&&race_number(object,"get_CardId",&card))chara=card/100;
    if(!chara){Ptr carddata=race_ref(object,"get_CardData");if(!carddata)carddata=race_object_field(object,"carddata");race_number(carddata,"get_CharaId",&chara);}
    if(!rank&&race_packet.phase!=3&&!race_number(object,"get_FinishOrder",&rank))if(!race_number(object,"get_Order",&rank))race_number(object,"get_Rank",&rank);
    if(!gate)race_number(object,"get_GateNo",&gate);
    if(chara<1000||chara>=2000||rank<(race_packet.phase==3?0:1)||rank>64)return;
    for(i32 i=0;i<race_packet.count;i++)if(gate>0&&race_packet.gates[i]==gate)return;
    i32 slot=race_packet.count++;race_seen[slot]=object;race_packet.charas[slot]=chara;race_packet.ranks[slot]=rank;race_packet.gates[slot]=gate;
}
static i32 race_rank_base(i32*ranks,i32 n){
    i32 min=65,max=-1;for(i32 i=0;i<n;i++){if(ranks[i]<min)min=ranks[i];if(ranks[i]>max)max=ranks[i];}
    if(!((min==0&&max==n-1)||(min==1&&max==n)))return -1;
    u64 mask=0;for(i32 i=0;i<n;i++){i32 bit=ranks[i]-min;if(bit<0||bit>=64)return -1;mask|=1ULL<<bit;}
    if(mask!=(n==64?~0ULL:((1ULL<<n)-1)))return -1;
    return min==0?1:0;
}
static i32 race_result_path(const char *key) {
    if(same(key,"entry")||same(key,"entrydata")||same(key,"entryinfo")||same(key,"entrylist")||same(key,"playerdata")||same(key,"playerentry")||same(key,"selectedentry")||same(key,"selectedentrydata")||same(key,"selectedcharadata")||same(key,"selectedtrainedcharadata")||same(key,"trainedcharadata")||same(key,"charadata"))return 1;
    if(same(key,"racehorse")||same(key,"racestatedata")||same(key,"roommatchdata")||same(key,"practiceracedata")||same(key,"ultimateracedata")||same(key,"leagueofheroesdata"))return 1;
    if(same(key,"horsedatas")||same(key,"horsedataarray")||same(key,"horsedatalist")||same(key,"racehorsedata")||same(key,"racehorsedatas")||same(key,"racehorsedataarray")||same(key,"racehorsedatalist")||same(key,"entryhorsedata")||same(key,"entryhorsedatas")||same(key,"raceentrydata")||same(key,"raceentrydataarray")||same(key,"raceentrylist")||same(key,"ultimate")||same(key,"ultimatedata")||same(key,"ultimaterace")||same(key,"masterchallenge")||same(key,"leagueofheroes")||same(key,"singlemode"))return 1;
    return same(key,"race")||same(key,"racedata")||same(key,"result")||same(key,"raceresult")||same(key,"resultdata")||same(key,"raceresultdata")||same(key,"raceresultdataarray")||same(key,"raceresultarray")||same(key,"horseresultarray")||same(key,"horseinfoarray")||same(key,"racehorsearray")||same(key,"entryinfoarray")||same(key,"entryarray")||same(key,"champions")||same(key,"championsdata")||same(key,"championsmeeting")||same(key,"roommatch")||same(key,"raceinfo")||same(key,"horsedata")||same(key,"resultarray");
}
static i32 race_collection(Ptr child,i32 type,Ptr*out){
    if(!child)return 0;
    if(type==0x1d&&array_length&&array_element_size&&array_header_size&&array_element_size(object_class(child))==8){
        u32 n=array_length(child),header=array_header_size();if(n>64||header<24||header>128)return 0;
        for(u32 i=0;i<n;i++)memcpy(&out[i],(unsigned char*)child+header+i*8,8);return (i32)n;
    }
    i32 count=0;Ptr item=class_method(object_class(child),"get_Item",1);
    if(!item||!race_number(child,"get_Count",&count)||count<1||count>64)return 0;
    i32 item_type=type_code(method_return(item));if(item_type!=0x12&&item_type!=0x1c&&item_type!=0x15)return 0;
    for(i32 i=0;i<count;i++){Ptr arg=&i,ex=0;out[i]=runtime_invoke(item,child,&arg,&ex);if(ex)return 0;}return count;
}
static i32 race_packet_slot(Ptr data){
    if(!data)return -1;for(i32 i=0;i<race_packet.count;i++)if(data==race_seen[i])return i;
    i32 gate=0;if(!race_number(data,"get_GateNo",&gate)||gate<1)return -1;
    i32 match=-1;for(i32 i=0;i<race_packet.count;i++)if(race_packet.gates[i]==gate){if(match>=0)return -1;match=i;}return match;
}
static void race_add_trainer_rival(Ptr data,Ptr race_horse,u64 viewer,i32 rank,i32 gate){
    /* Trainer-owned opponents are useful in any completed race result, not
       only career races. Trainer-less NPCs are still excluded below. */
    if(!data||race_packet.phase!=2||race_rivals.count>=18)return;
    Ptr outer=data;
    if(!race_method(object_class(data),"get_TrainerName")){
        Ptr nested=race_ref(data,"get_HorseData");if(!nested)nested=race_object_field(data,"horsedata");
        if(nested){data=nested;if(!race_horse)race_horse=outer;}
    }
    if(race_packet_slot(data)>=0||race_packet_slot(outer)>=0)return;
    u64 owner=race_owner(data);if(!owner&&data!=outer)owner=race_owner(outer);if(!owner||(viewer&&owner==viewer))return;
    i32 chara=0,card=0;
    if(!race_number(data,"get_CharaId",&chara))race_number(data,"get_charaId",&chara);
    if(!chara&&data!=outer){if(!race_number(outer,"get_CharaId",&chara))race_number(outer,"get_charaId",&chara);}
    if(!chara)race_int_field(data,"charaid",&chara);
    if(!chara&&race_number(data,"get_CardId",&card))chara=card/100;
    if(!chara){Ptr cd=race_ref(data,"get_CardData");if(!cd)cd=race_object_field(data,"carddata");race_number(cd,"get_CharaId",&chara);}
    if(!gate)race_number(data,"get_GateNo",&gate);
    if(!gate&&data!=outer)race_number(outer,"get_GateNo",&gate);
    if(!rank)race_int_field(data,"finishorder",&rank);
    if(!rank)race_number(race_horse,"get_FinishOrder",&rank);
    if(!rank)race_number(data,"get_FinishOrder",&rank);
    if(!rank&&data!=outer)race_number(outer,"get_FinishOrder",&rank);
    if(chara<1000||chara>=2000||rank<1||rank>64||gate<1||gate>64)return;
    for(i32 i=0;i<race_rivals.count;i++)if(race_rivals.entries[i].gate==gate&&race_rivals.entries[i].chara==chara)return;
    RaceRivalRecord*entry=&race_rivals.entries[race_rivals.count];memset(entry,0,sizeof(*entry));
    if(!race_trainer_name(data,entry->trainer,sizeof(entry->trainer)))return;
    entry->chara=chara;entry->rank=rank;entry->gate=gate;
    entry->flags=(race_is_marked_rival(data)||(race_horse&&race_is_marked_rival(race_horse))||race_is_marked_rival(outer))?1:0;
    race_rival_objects[race_rivals.count]=data;
    race_rivals.count++;race_rivals.active=1;race_rivals.program=race_packet.program;race_rivals.kind=race_packet.kind;race_rivals.program_type=race_packet.program_type;
}
static i32 race_hud_slot(Ptr object,Ptr*horses,i32 total,i32 depth){
    if(!object||depth>2)return -1;i32 slot=race_packet_slot(object);if(slot>=0)return slot;
    i32 index=-1;
    if((race_int_field(object,"horseindex",&index)||race_int_field(object,"targethorseindex",&index)||race_number(object,"get_HorseIndex",&index)||race_number(object,"get_TargetHorseIndex",&index))&&index>=0&&index<total){slot=race_packet_slot(race_ref(horses[index],"get_HorseData"));if(slot>=0)return slot;}
    const char*getters[]={"get_HorseData","get_HorseRaceInfo","get_TargetHorseRaceInfo"};
    for(i32 i=0;i<3;i++){Ptr child=race_ref(object,getters[i]);if(child&&child!=object){slot=race_hud_slot(child,horses,total,depth+1);if(slot>=0)return slot;}}
    const char*fields[]={"horsedata","horseraceinfo","targethorseraceinfo","targethorse"};
    for(i32 i=0;i<4;i++){Ptr child=race_object_field(object,fields[i]);if(child&&child!=object){slot=race_hud_slot(child,horses,total,depth+1);if(slot>=0)return slot;}}
    return -1;
}
static i32 race_order_valid(i32*order,i32 count){i32 mask=0;for(i32 i=0;i<count;i++){if(order[i]<0||order[i]>=count||(mask&(1<<order[i])))return 0;mask|=1<<order[i];}return count>0;}
static void race_reorder(i32*order){
    i32 charas[3],ranks[3],gates[3];Ptr seen[3];for(i32 i=0;i<race_packet.count;i++){charas[i]=race_packet.charas[order[i]];ranks[i]=race_packet.ranks[order[i]];gates[i]=race_packet.gates[order[i]];seen[i]=race_seen[order[i]];}
    for(i32 i=0;i<race_packet.count;i++){race_packet.charas[i]=charas[i];race_packet.ranks[i]=ranks[i];race_packet.gates[i]=gates[i];race_seen[i]=seen[i];}
}
static i32 race_hud_find(Ptr object,Ptr*horses,i32 total,i32 depth,i32*budget,i32*out){
    if(!object||depth>4||*budget<=0)return 0;Ptr klass=object_class(object);
    for(i32 level=0;klass&&level<3;level++,klass=class_parent?class_parent(klass):0){Ptr it=0,field;
        while(*budget>0&&(field=class_fields(klass,&it))){(*budget)--;if(field_flags&&(field_flags(field)&0x10))continue;
            const char*n=field_name(field);if(!n)continue;char key[64];normalized(n,key);
            if(!(race_contains(key,"player")||race_contains(key,"target")||race_contains(key,"horse")||race_contains(key,"status")||race_contains(key,"team")||race_contains(key,"race")))continue;
            i32 type=type_code(field_type(field));if(type!=0x12&&type!=0x15&&type!=0x1c&&type!=0x1d)continue;
            Ptr child=0;field_value(object,field,&child);if(!child||child==object)continue;
            Ptr widgets[64];i32 count=race_collection(child,type,widgets);
            if(count==race_packet.count){for(i32 i=0;i<count;i++)out[i]=race_hud_slot(widgets[i],horses,total,0);if(race_order_valid(out,count)){race_note("HUD slots: ");race_note(n);race_note("; ");return 1;}}
            if(count>0&&count<=8){for(i32 i=0;i<count;i++)if(race_hud_find(widgets[i],horses,total,depth+1,budget,out))return 1;}
            else if(type!=0x1d&&race_hud_find(child,horses,total,depth+1,budget,out))return 1;
        }
    }return 0;
}
static void race_team_order(Ptr manager,Ptr info,Ptr entries){
    if(race_packet.phase==3)race_cached_count=0;
    if(race_packet.count<2)return;i32 order[3],found=0;
    if(race_packet.phase==1){
        Ptr horses[64];i32 total=race_collection(entries,0x1d,horses);
        Ptr hud=race_ref(manager,"get_RaceMainView");if(!hud){Ptr view=race_ref(manager,"get_RaceView");hud=race_ref(view,"get_RaceMainView");}
        if(hud&&total>0){i32 budget=128;found=race_hud_find(hud,horses,total,0,&budget,order);if(!found)race_registration_schema(hud);}
        if(found){race_reorder(order);race_cached_count=race_packet.count;race_cached_program=race_packet.program;race_cached_program_type=race_packet.program_type;for(i32 i=0;i<race_packet.count;i++){race_cached_charas[i]=race_packet.charas[i];race_cached_gates[i]=race_packet.gates[i];}return;}
    }
    if(race_cached_count==race_packet.count&&race_cached_program==race_packet.program&&race_cached_program_type==race_packet.program_type){
        for(i32 i=0;i<race_packet.count;i++){order[i]=-1;for(i32 j=0;j<race_packet.count;j++)if(race_packet.charas[j]==race_cached_charas[i]&&race_packet.gates[j]==race_cached_gates[i])order[i]=j;}
        if(race_order_valid(order,race_packet.count)){race_reorder(order);race_note("Order: retained live HUD slots; ");return;}
    }
    /* Registration order is a fallback, never a gate/rank sort. */
    Ptr klass=info?object_class(info):0;
    if(klass){Ptr it=0,field;i32 budget=128;while(budget--&&(field=class_fields(klass,&it))){const char*n=field_name(field);if(!n)continue;char key[64];normalized(n,key);if(!same(key,"racehorse"))continue;
        i32 type=type_code(field_type(field));if(type!=0x12&&type!=0x15&&type!=0x1d)break;Ptr child=0;field_value(info,field,&child);Ptr roster[64];i32 count=race_collection(child,type,roster),selected=0;
        for(i32 i=0;i<count&&selected<3;i++){i32 slot=race_packet_slot(roster[i]);if(slot>=0)order[selected++]=slot;}
        if(selected==race_packet.count&&race_order_valid(order,selected)){
            i32 anchor=-1;for(i32 i=0;i<selected;i++)if(order[i]==0)anchor=i;
            if(anchor>0){for(i32 i=anchor;i>0;i--)order[i]=order[i-1];order[0]=0;}
            race_reorder(order);race_note("Order: RaceHorse registration slots (HUD unavailable); ");}break;
    }}
}
static void race_results(Ptr object,u64 viewer,i32 depth,i32*budget) {
    if(!object||depth>5||*budget<=0)return;
    Ptr klass=object_class(object);
    race_note(" [");race_note(class_name?class_name(klass):"object");race_note("] ");
    if(race_packet.phase==3&&depth==1&&race_packet.count==0)race_schema(object);
    i32 previous_count=race_packet.count;
    race_add(object,viewer,0,0);
    if(race_packet.phase==2)race_add_trainer_rival(object,0,viewer,0,0);
    /* An accepted entrant is a leaf. Its nested TrainedCharaData describes
       that same entrant; traversing it again consumes a phantom team slot. */
    if(race_packet.count>previous_count||(class_name&&same(class_name(klass),"HorseData")))return;
    if(!race_packet.program&&race_number(object,"get_ProgramId",&race_packet.program))race_packet.program_type=1;
    if(class_name&&race_contains(class_name(klass),"Champions"))race_packet.kind=1;
    for(i32 level=0;klass&&level<3;level++,klass=class_parent?class_parent(klass):0){Ptr it=0,field;while(*budget>0&&(field=class_fields(klass,&it))){
        (*budget)--;if(field_flags&&(field_flags(field)&0x10))continue;
        const char*name=field_name(field);if(!name)continue;char key[64];normalized(name,key);
        if(race_packet.phase==3&&race_contains(key,"result"))continue;
        if(race_packet.phase==3&&work_class&&object==singleton(work_class)){
            i32 selected=(race_packet.kind==1&&(same(key,"champions")||same(key,"championsdata")))||(race_packet.kind==2&&(same(key,"roommatch")||same(key,"roommatchdata")||same(key,"practiceracedata")))||(race_packet.kind==3&&same(key,"leagueofheroesdata"))||(race_packet.kind==4&&same(key,"singlemode"))||(race_packet.kind==5&&(same(key,"ultimateracedata")||same(key,"ultimate")||same(key,"masterchallenge")));
            if(!selected&&!same(key,"racestatedata")&&!same(key,"raceinfo")&&!same(key,"racedata"))continue;
        }
        if(!race_result_path(key))continue;race_note(name);race_note("; ");
        i32 type=type_code(field_type(field));if(type!=0x12&&type!=0x15&&type!=0x1c&&type!=0x1d)continue;
        char hex[3]={"0123456789abcdef"[(type>>4)&15],"0123456789abcdef"[type&15],0};race_note("type=");race_note(hex);race_note("; ");
        Ptr child=0;field_value(object,field,&child);if(!child||child==object)continue;
        Ptr collection[64];i32 n=race_collection(child,type,collection);
        if(n>0){
            i32 player=-1;u64 collection_viewer=viewer;
            if((race_int_field(object,"playerhorseindex",&player)||race_number(object,"get_PlayerHorseIndex",&player))&&player>=0&&player<n){
                u64 recorded=race_owner(collection[player]);if(recorded)collection_viewer=recorded;
            }else player=-1;
            if(race_packet.phase==2&&n>0){
                i32 ranks[64],have_final=0;for(i32 i=0;i<n;i++){ranks[i]=-100;if(race_int_field(collection[i],"finishorder",&ranks[i])||race_number(collection[i],"get_FinishOrder",&ranks[i]))have_final++;}
                if(have_final){i32 base=race_rank_base(ranks,n);if(base>=0)for(i32 step=0;step<n;step++){i32 i=step;if(player>=0){if(step==0)i=player;else{i=step-1;if(i>=player)i++;}}race_add(collection[i],collection_viewer,ranks[i]+base,0);race_add_trainer_rival(collection[i],0,collection_viewer,ranks[i]+base,0);}continue;}
            }
            for(i32 step=0;step<n&&*budget>0;step++){i32 i=step;if(player>=0){if(step==0)i=player;else{i=step-1;if(i>=player)i++;}}race_results(collection[i],collection_viewer,depth+1,budget);}
        }else if(type!=0x1d)race_results(child,viewer,depth+1,budget);
    }}
}
static void sample_race(Ptr scene) {
    memset(&race_packet,0,sizeof(race_packet));race_packet.magic=0x52414331;race_packet.track_magic=0x54524b31;race_packet.track_version=1;
    memset(&race_rivals,0,sizeof(race_rivals));race_rivals.magic=0x33564952;race_rivals.version=3;
    memset(race_rival_objects,0,sizeof(race_rival_objects));
    memset(race_seen,0,sizeof(race_seen));
    race_group_getter=0;
    if(race_disabled||!race_class||!class_fields||!field_type||!type_code||!field_value||!class_method||!method_return||!runtime_invoke||!object_unbox)return;
    __try {
        i32 relevant=race_scene(scene,"Race")||race_scene(scene,"Champions");
        u64 now=GetTickCount64();race_describe=race_debug_length==0||now-race_last_diagnostic>=10000;
        Ptr info=race_info_fn?race_info_fn():0;
        if(!relevant&&(!info||!race_scene(scene,"_Boot"))){race_debug_length=0;race_debug[0]=0;race_cached_count=0;return;}
        if(race_describe){race_debug_length=0;race_debug[0]=0;race_last_diagnostic=now;}
        i32 result_ui=race_result_ui();race_packet.active=1;race_packet.phase=(race_scene(scene,"Result")||result_ui)?2:1;
        if(race_scene(scene,"Champions"))race_packet.kind=1;
        else if(race_scene(scene,"Room"))race_packet.kind=2;
        else if(race_scene(scene,"Heroes"))race_packet.kind=3;
        else if(race_scene(scene,"MasterChallenge"))race_packet.kind=5;
        Ptr work=work_class?singleton(work_class):0;
        Ptr user=race_object_field(work,"userdata");
        u64 viewer=race_identity(user,"get_ViewerId");
        if(!viewer)viewer=race_identity(user,"get_ViewerIdString");
        if(!viewer){Ptr userinfo=race_ref(user,"get_UserInfo");if(!userinfo)userinfo=race_object_field(user,"userinfo");viewer=race_identity(userinfo,"get_ViewerId");}
        race_note(viewer?"Own identity available; ":"Own identity unavailable; ");
        if(info){
            race_mode(info);
            race_packet.season=race_condition(info,"get_Season",1);
            if(!race_packet.season)race_packet.season=race_condition(info,"get_SeasonType",1);
            race_packet.weather=race_condition(info,"get_Weather",2);
            if(!race_packet.weather)race_packet.weather=race_condition(info,"get_WeatherType",2);
            race_packet.ground_condition=race_condition(info,"get_GroundCondition",3);
            race_registration_schema(info);
            race_number(info,"get_CourseOnlyDistance",&race_packet.length);
            race_number(info,"get_CourseId",&race_packet.course);
            if(!race_packet.course)race_number(info,"get_CourseSetId",&race_packet.course);
            if(!race_packet.course){
                /* Live replay schema: RaceInfo.RaceCourseSet -> RaceCourseSet.Id
                   is a real integer field; CourseId/CourseSetId getters are absent. */
                Ptr course_set=race_ref(info,"get_RaceCourseSet");
                if(!race_int_field(course_set,"id",&race_packet.course))race_number(course_set,"get_Id",&race_packet.course);
            }
            if(race_number(info,"get_ProgramId",&race_packet.program))race_packet.program_type=1;
            if(!race_packet.program&&race_number(info,"get_RaceInstanceId",&race_packet.program))race_packet.program_type=2;
            if(!race_packet.program&&race_number(info,"get_RaceId",&race_packet.program))race_packet.program_type=3;
        }
        Ptr manager=singleton(race_class),horses=race_object_field(manager,"horsemanager");
        Ptr entries=race_ref(horses,"GetHorseRaceInfos");
        i32 finished=0;
        if(entries&&array_length&&array_element_size&&array_header_size&&array_element_size(object_class(entries))==8){
            u32 n=array_length(entries),header=array_header_size();
            if(n>0&&n<=64&&header>=24&&header<=128){
                race_packet.total=(i32)n;
                i32 raw_orders[64],final_orders[64];
                for(u32 i=0;i<n;i++){
                    Ptr horse=0;memcpy(&horse,(unsigned char*)entries+header+i*8,8);
                    raw_orders[i]=final_orders[i]=-100;race_number(horse,"get_CurOrder",&raw_orders[i]);
                    Ptr data=race_ref(horse,"get_HorseData");
                    if(!race_int_field(data,"finishorder",&final_orders[i]))race_number(horse,"get_FinishOrder",&final_orders[i]);
                    if(race_bool(horse,"IsFinished"))finished++;
                }
                if(finished==(i32)n)race_packet.phase=2;
                if(finished==0&&!result_ui){
                    float farthest=0;for(u32 i=0;i<n;i++){Ptr h=0;memcpy(&h,(unsigned char*)entries+header+i*8,8);float d=0;if(race_float_field(h,"distance",&d)&&d>farthest)farthest=d;}
                    if(farthest<=0)race_packet.phase=3;
                }
                i32 rank_base=race_rank_base(raw_orders,(i32)n),final_base=race_rank_base(final_orders,(i32)n);
                /* Replay perspective is supplied by the game, never by camera focus. */
                i32 player_index=-1;
                if(race_number(horses,"GetPlayerHorseIndex",&player_index)&&player_index>=0&&player_index<(i32)n){
                    Ptr player=0;memcpy(&player,(unsigned char*)entries+header+player_index*8,8);
                    Ptr data=race_ref(player,"get_HorseData");u64 recorded=race_owner(data);
                    if(recorded){viewer=recorded;race_note("Recorded player-team identity available; ");}
                    else{
                        const char*groups[]={"get_TeamId","get_TrainerIndex","get_TrainerId"};
                        for(i32 g=0;g<3&&!race_group_getter;g++){
                            i32 selected=-1;if(!race_number(data,groups[g],&selected)||selected<0||selected>64)continue;
                            i32 matches=0,others=0,valid=1;
                            for(u32 i=0;i<n;i++){Ptr h=0;memcpy(&h,(unsigned char*)entries+header+i*8,8);Ptr d=race_ref(h,"get_HorseData");i32 value=-1;if(!race_number(d,groups[g],&value)||value<0||value>64){valid=0;break;}if(value==selected)matches++;else others++;}
                            if(valid&&matches>=1&&matches<=3&&others>0){race_group_getter=groups[g];viewer=(u64)selected+1;race_note("Recorded player-team group available; ");}
                        }
                    }
                }
                /* Keep the game's player anchor first, followed by its other entrants.
                   Gate sorting changes the order of the HUD's three positions. */
                for(u32 step=0;step<n;step++){
                    u32 i=step;
                    if(player_index>=0&&player_index<(i32)n){if(step==0)i=(u32)player_index;else{i=step-1;if(i>=(u32)player_index)i++;}}
                    Ptr horse=0;memcpy(&horse,(unsigned char*)entries+header+i*8,8);if(!horse)continue;
                    float meters=0;if(race_float_field(horse,"distance",&meters)&&meters>race_packet.meters)race_packet.meters=meters;
                    i32 rank=rank_base<0?-1:raw_orders[i]+rank_base;
                    if(race_packet.phase==3)rank=0;
                    if(race_packet.phase==2)rank=final_base>=0?final_orders[i]+final_base:-1;
                    Ptr data=race_ref(horse,"get_HorseData");race_add(data,viewer,rank,0);if(race_packet.phase==2)race_add_trainer_rival(data,horse,viewer,rank,0);
                }
            }
        }
        if(info&&race_packet.meters>0){i32 runup=0;if(race_number(info,"get_RunUpDistance",&runup)&&runup>0&&runup<1000)race_packet.meters=race_packet.meters>runup?race_packet.meters-runup:0;}
        if(race_packet.total==0&&info&&race_packet.phase!=2){
            race_packet.phase=3;i32 budget=512;race_results(info,viewer,0,&budget);
            if(race_packet.count==0){race_registration_schema(work);budget=512;race_results(work,viewer,0,&budget);}
        }
        if(race_packet.phase==2&&race_packet.count==0&&info){i32 budget=512;race_results(info,viewer,0,&budget);}
        if(race_packet.phase==2&&race_packet.count==0&&work){i32 budget=768;race_results(work,viewer,0,&budget);}
        race_team_order(manager,info,entries);
        race_packet.active=race_packet.phase==2||race_packet.total>0||(race_packet.phase==3&&race_packet.count>0);
        if(!relevant&&race_packet.phase!=3)race_packet.active=0;
        if(!race_rivals.active){race_rivals.program=race_packet.program;race_rivals.kind=race_packet.kind;race_rivals.program_type=race_packet.program_type;}
        race_describe=0;
    } __except(1){race_disabled=1;race_packet.active=0;race_rivals.active=0;report("Optional race telemetry disabled after incompatible runtime metadata.");}
}
