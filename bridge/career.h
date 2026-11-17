/* Optional career snapshot. IL2CPP metadata supplies every field address/type. */
typedef struct { u32 magic; i32 active,card,turn,scenario,speed,stamina,power,guts,wiz,skill,race; } CareerPacket;
static CareerPacket career;
static Ptr work_class;
static Ptr career_work,career_character;
static const char *(*class_name)(Ptr);
static char career_debug[1500];
static i32 career_debug_len, career_failed, career_describe;
static i32 previous_career_scene=-1;
static Ptr (*class_method)(Ptr,const char*,i32);
static Ptr (*method_return)(Ptr);
static Ptr (*runtime_invoke)(Ptr,Ptr,Ptr*,Ptr*);
static Ptr (*object_unbox)(Ptr);
static Ptr (*class_methods)(Ptr,Ptr*);
static const char *(*method_name)(Ptr);
static u32 (*method_params)(Ptr);
static Ptr (*method_param)(Ptr,u32);
static Ptr (*class_from_type)(Ptr);
static Ptr (*class_type)(Ptr);
static i32 career_have_turn,career_have_skill;
static void career_note(const char *s) {
    if(!s) return;
    while(*s && career_debug_len<1498) career_debug[career_debug_len++]=*s++;
    career_debug[career_debug_len]=0;
}
static i32 career_path(const char *key,i32 depth) {
    return same(key,"singlemodedata")||same(key,"singlemode")||same(key,"singlemodeinfo")||
        same(key,"singlemodeworkdata")||same(key,"singlemodecharadata")||
        (depth>0&&(same(key,"charainfo")||same(key,"charadata")||same(key,"chara")||same(key,"character")||
        same(key,"currentchara")||same(key,"trainingchara")||same(key,"data")||same(key,"status")||same(key,"parameter")));
}
static i32 career_unwrap(Ptr boxed,i32 *result) {
    if(!boxed) return 0;
    Ptr klass=object_class(boxed),value=object_unbox(boxed);
    if(!value) return 0;
    /* Boxing Nullable<T> produces a boxed T, rather than a Nullable<T> wrapper. */
    i32 boxed_type=class_type?type_code(class_type(klass)):0;
    if(boxed_type==8||boxed_type==9){memcpy(result,value,4);return 1;}
    if(career_describe) { career_note("wrapper="); career_note(class_name?class_name(klass):"?"); career_note("; "); }
    Ptr getter=class_method(klass,"get_Value",0);
    if(getter && (type_code(method_return(getter))==8||type_code(method_return(getter))==9)) {
        Ptr ex=0,b=runtime_invoke(getter,value,0,&ex);
        if(!ex&&b) { Ptr n=object_unbox(b); if(n) {memcpy(result,n,4);return 1;} }
    }
    if(!class_methods||!method_name||!method_params||!method_param||!class_from_type) return 0;
    Ptr iterator=0,method; i32 budget=128;
    while(budget-- && (method=class_methods(klass,&iterator))) {
        const char *name=method_name(method);
        i32 type=type_code(method_return(method));
        if(name && same(name,"op_Implicit") && method_params(method)==1 && (type==8||type==9) && class_from_type(method_param(method,0))==klass) {
            Ptr args[1]={value},ex=0,b=runtime_invoke(method,0,args,&ex);
            if(!ex&&b) { Ptr n=object_unbox(b); if(n) {memcpy(result,n,4);return 1;} }
        }
    }
    return 0;
}
static i32 career_number(Ptr object,const char *property,i32 *result) {
    if(!class_method||!method_return||!runtime_invoke||!object_unbox) return 0;
    Ptr klass=object_class(object),method=class_method(klass,property,0);
    if(!method) return 0;
    i32 type=type_code(method_return(method));
    if(career_describe) { career_note(property); career_note(type==8||type==9?":int; ":":wrapped; "); }
    if(type!=8&&type!=9&&type!=0x11) return 0;
    Ptr exception=0,boxed=runtime_invoke(method,object,0,&exception);
    if(exception||!boxed) return 0;
    if(type==0x11) return career_unwrap(boxed,result);
    Ptr value=object_unbox(boxed); if(!value) return 0;
    memcpy(result,value,4); return 1;
}
static void career_getters(Ptr object) {
    if(career_number(object,"get_CardId",&career.card)&&career.card>=100000&&career.card<200000)career_character=object;
    const char *turns[]={"get_Turn","get_CurrentTurn","get_TurnNum","get_TurnNumber","get_CurrentTurnNum","get_CurrentTurnNumber","get_TrainingTurn","GetCurrentTurn"};
    for(i32 i=0;i<8&&!career.turn;i++) if(career_number(object,turns[i],&career.turn)) career_have_turn=1;
    career_number(object,"get_ScenarioId",&career.scenario);
    career_number(object,"get_Speed",&career.speed);
    career_number(object,"get_Stamina",&career.stamina);
    career_number(object,"get_Power",&career.power);
    career_number(object,"get_Guts",&career.guts);
    if(!career_number(object,"get_Wiz",&career.wiz)) career_number(object,"get_Wisdom",&career.wiz);
    if(career_number(object,"get_SkillPoint",&career.skill)) career_have_skill=1;
    if(career_describe&&class_methods&&method_name) {
        Ptr iterator=0,method; i32 budget=512;
        while(budget-- && (method=class_methods(object_class(object),&iterator))) {
            const char *name=method_name(method); if(!name) continue;
            for(const char *s=name;*s;s++) if(s[0]=='T'&&s[1]=='u'&&s[2]=='r'&&s[3]=='n') { career_note("turn-method="); career_note(name); career_note("; ");break; }
        }
    }
}
static void career_fields(Ptr object,i32 depth,i32 *budget) {
    if(!object || depth>4 || *budget<=0) return;
    Ptr klass=object_class(object);
    if(depth==1)career_work=object;
    if(career_describe) { career_note(" ["); career_note(class_name?class_name(klass):"object"); career_note("] "); }
    if(depth>0) career_getters(object);
    for(i32 level=0;klass&&level<4;level++,klass=class_parent?class_parent(klass):0) {
        Ptr iterator=0,field;
        while(*budget>0 && (field=class_fields(klass,&iterator))) {
            (*budget)--;
            if(field_flags && (field_flags(field)&0x10)) continue;
            const char *name=field_name(field); if(!name) continue;
            char key[64]; normalized(name,key);
            i32 type=type_code(field_type(field));
            if(career_describe && (depth>=2 || career_path(key,depth))) { career_note(name); career_note(type==8||type==9?":int; ":type==0x11?":value-type; ":":object; "); }
            if(depth>0&&(type==8||type==9)) {
                i32 n=0; field_value(object,field,&n);
                if(same(key,"cardid")) {career.card=n;career_character=object;}
                else if(same(key,"turn")) {career.turn=n;career_have_turn=1;}
                else if(same(key,"scenarioid")) career.scenario=n;
                else if(same(key,"speed")) career.speed=n;
                else if(same(key,"stamina")) career.stamina=n;
                else if(same(key,"power")) career.power=n;
                else if(same(key,"guts")) career.guts=n;
                else if(same(key,"wiz")||same(key,"wisdom")) career.wiz=n;
                else if(same(key,"skillpoint")) {career.skill=n;career_have_skill=1;}
            }
            if((type==0x12||type==0x1c||type==0x15)&&career_path(key,depth)) {
                Ptr child=0; field_value(object,field,&child);
                if(child!=object) career_fields(child,depth+1,budget);
            }
        }
    }
}
static void sample_career(Ptr scene_name) {
    memset(&career,0,sizeof(career)); career.magic=0x43415231;
    career_work=career_character=0;
    career_have_turn=career_have_skill=0;
    if(career_failed||!work_class||!class_fields||!field_name||!field_type||!type_code||!field_value) return;
    __try {
        /* Cached work data survives scene changes; only publish within career scenes. */
        i32 in_career=0;
        if(scene_name) {
            i32 length=string_length(scene_name); u16 *s=string_chars(scene_name);
            const char *token="SingleMode";
            if(s && length>0&&length<128) for(i32 i=0;i+10<=length;i++) {
                i32 j=0; while(j<10&&s[i+j]==(u16)token[j]) j++;
                if(j==10) in_career=1;
            }
            if(career_describe && s) { career_note("Scene: "); for(i32 i=0;i<length&&i<80;i++) { char c[2]={(char)(s[i]<128?s[i]:'?'),0}; career_note(c); } }
        }
        if(in_career!=previous_career_scene) {
            career_describe=1; career_debug_len=0; career_debug[0]=0; previous_career_scene=in_career;
            if(scene_name) { i32 length=string_length(scene_name); u16 *s=string_chars(scene_name); career_note("Scene: "); if(s) for(i32 i=0;i<length&&i<80;i++) { char c[2]={(char)(s[i]<128?s[i]:'?'),0}; career_note(c); } }
        }
        Ptr work=singleton(work_class); i32 budget=512;
        if(work) career_fields(work,0,&budget);
        if(in_career && career_have_turn && career_have_skill && career.card>=100000 && career.card<200000 && career.turn>=1&&career.turn<=200 &&
           career.speed>0&&career.speed<=9999 && career.stamina>0&&career.stamina<=9999 &&
           career.power>0&&career.power<=9999 && career.guts>0&&career.guts<=9999 &&
           career.wiz>0&&career.wiz<=9999 && career.skill>=0&&career.skill<=99999) career.active=1;
        if(work) career_describe=0;
    } __except(1) { career_failed=1; career.active=0; career_note(" [career read disabled after incompatible metadata]"); }
}
