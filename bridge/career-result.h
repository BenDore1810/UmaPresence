/* Optional completed-career telemetry. Existing 48-byte career record is unchanged. */
typedef struct {u32 magic;i32 active,card,scenario,score,skills,flags,rank;} CareerResultPacket;
static CareerResultPacket career_result;
/* LEX1 retains its original prefix. LPT1 tags the optional total at the end. */
typedef struct {
    u32 magic;
    i32 active,card,score,id,speed,stamina,power,guts,wisdom,wins,races,factor_count,factors[64];
    u32 points_magic;
    i32 total_skill_points;
} LegacyDetailsPacket;
typedef char legacy_details_packet_size_is_316[(sizeof(LegacyDetailsPacket)==316)?1:-1];
static LegacyDetailsPacket legacy_details;
typedef struct {i32 card,count,factors[64];} LegacyGrandparent;
typedef struct {u32 magic;i32 active,card,score,id,reserved;LegacyGrandparent grandparents[2];} LegacyGrandparentPacket;
static LegacyGrandparentPacket legacy_grandparents;
static Ptr career_result_field(Ptr object,const char*key,i32*type);
static void career_legacy_grandparents(Ptr record,i32 card,i32 score,i32 id){
    LegacyGrandparentPacket value;memset(&value,0,sizeof(value));value.magic=0x4c475031;value.card=card;value.score=score;value.id=id;
    const char*names[]={"get_SuccessionChara1","get_SuccessionChara2"};
    for(i32 group=0;group<2;group++){
        LegacyGrandparent*out=&value.grandparents[group];out->count=-1;Ptr parent=race_ref(record,names[group]);
        if(!race_number(parent,"get_CardId",&out->card)||out->card<100000||out->card>=200000)continue;
        i32 type=0;Ptr array=career_result_field(parent,"factordataarray",&type),items[64];if(!array)continue;
        if(type==0x1d&&array_length&&array_length(array)>64)continue;
        i32 count=race_collection(array,type,items),valid=1;
        for(i32 i=0;i<count;i++)if(!race_number(items[i],"get_FactorId",&out->factors[i])||out->factors[i]<=0){valid=0;break;}
        if(valid)out->count=count;
    }
    value.active=1;legacy_grandparents=value;
}
static void career_legacy_details(Ptr record,i32 card,i32 score){
    LegacyDetailsPacket value;memset(&value,0,sizeof(value));value.magic=0x4c455831;value.card=card;value.score=score;
    if(!race_number(record,"get_Id",&value.id)||value.id<=0)return;
    const char*names[]={"get_Speed","get_Stamina","get_Power","get_Guts","get_Wiz","get_SingleWinNum","get_SingleTotalRaceNum"};
    i32*targets[]={&value.speed,&value.stamina,&value.power,&value.guts,&value.wisdom,&value.wins,&value.races};
    for(i32 i=0;i<7;i++)if(!race_number(record,names[i],targets[i])||*targets[i]<0||(i<5&&*targets[i]>9999))return;
    i32 bare=score==0&&value.speed==0&&value.stamina==0&&value.power==0&&value.guts==0&&value.wisdom==0;
    if(!bare&&(score<=0||value.speed<1||value.stamina<1||value.power<1||value.guts<1||value.wisdom<1))return;
    if(value.wins>value.races||value.races>10000)return;
    i32 type=0;Ptr array=career_result_field(record,"factordataarray",&type),items[64];
    value.factor_count=race_collection(array,type,items);
    for(i32 i=0;i<value.factor_count;i++)if(!race_number(items[i],"get_FactorId",&value.factors[i])||value.factors[i]<=0)return;
    __try {
        i32 points=0;
        if(career_number(record,"get_TotalSkillPt",&points)&&points>0){
            value.points_magic=0x3154504c;
            value.total_skill_points=points;
        }
    }__except(1){value.points_magic=0;}
    value.active=1;legacy_details=value;
    __try {career_legacy_grandparents(record,card,score,value.id);}__except(1){legacy_grandparents.active=0;}
}
static i32 career_result_disabled;
static u64 career_result_last_debug;
static i32 career_result_enum(Ptr object,const char*name){
    if(!object||!race_class_is_enum||!race_static_value||!class_from_type)return 0;
    Ptr method=race_method(object_class(object),name);if(!method)return 0;
    Ptr klass=class_from_type(method_return(method));if(!klass||!race_class_is_enum(klass))return 0;
    Ptr ex=0,boxed=runtime_invoke(method,object,0,&ex);if(ex||!boxed)return 0;Ptr p=object_unbox(boxed);if(!p)return 0;
    i32 value=0;memcpy(&value,p,4);Ptr it=0,field;i32 budget=128;
    while(budget--&&(field=class_fields(klass,&it))){if(!field_flags||(field_flags(field)&0x50)!=0x50)continue;i32 n=0;race_static_value(field,&n);if(n!=value)continue;
        const char*s=field_name(field);if(!s)continue;char key[64];normalized(s,key);
        if(career_describe){career_note(name);career_note("=");career_note(s);career_note("; ");}
        return race_contains(key,"result")||race_contains(key,"finish")||race_contains(key,"complete")||same(key,"singlemodeend")||same(key,"trainingend")||same(key,"trueend");
    }return 0;
}
static i32 career_result_finished(Ptr object){
    const char*flags[]={"get_IsFinished","get_IsFinish","get_IsCompleted","get_IsTrainingEnd","get_IsTrainingFinished","get_IsFinishSingleMode","get_IsFinishedSingleMode","get_IsTrainingCompleted","get_IsSingleModeEnd","IsFinished","IsFinish","IsCompleted","IsTrainingEnd","IsSingleModeEnd"};
    for(i32 i=0;i<14;i++)if(race_bool(object,flags[i])){if(career_describe){career_note(flags[i]);career_note("=true; ");}return 1;}
    const char*states[]={"get_State","get_CurrentState","get_Step","get_CurrentStep","get_SingleModeState","get_SingleModeStep"};
    for(i32 i=0;i<6;i++)if(career_result_enum(object,states[i]))return 1;
    return 0;
}
static Ptr career_result_field(Ptr object,const char*key,i32*type){
    if(!object)return 0;Ptr klass=object_class(object);
    for(i32 level=0;klass&&level<3;level++,klass=class_parent?class_parent(klass):0){Ptr it=0,field;i32 budget=128;while(budget--&&(field=class_fields(klass,&it))){if(field_flags&&(field_flags(field)&0x10))continue;const char*n=field_name(field);if(!n)continue;char k[64];normalized(n,k);if(!same(k,key))continue;
        *type=type_code(field_type(field));if(*type!=0x12&&*type!=0x15&&*type!=0x1d&&*type!=0x1c)return 0;Ptr child=0;field_value(object,field,&child);return child;
    }}return 0;
}
static i32 career_result_skill_count(Ptr character){
    i32 count=-1;if(race_number(character,"get_SkillCount",&count)&&count>=0&&count<=512)return count;
    const char*names[]={"get_AcquiredSkillList","get_AcquiredSkillArray","get_SkillArray","get_SkillDataArray","get_SkillList","get_SkillDataList"};
    const char*fields[]={"acquiredskilllist","acquiredskillarray","skillarray","skilldataarray","skilllist","skilldatalist"};
    for(i32 i=0;i<6;i++){
        Ptr method=character?race_method(object_class(character),names[i]):0,child=0;i32 type=0;
        if(method){type=type_code(method_return(method));if(type==0x1d||type==0x12||type==0x15){Ptr ex=0;child=runtime_invoke(method,character,0,&ex);if(ex)child=0;}}
        if(!child)child=career_result_field(character,fields[i],&type);
        if(!child)continue;
        if(type==0x1d&&array_length){u32 n=array_length(child);if(n<=512)return (i32)n;}
        if(race_number(child,"get_Count",&count)&&count>=0&&count<=512)return count;
    }return -1;
}
static void career_result_schema(Ptr object){
    if(!career_describe||!object)return;Ptr klass=object_class(object);career_note(" [");career_note(class_name?class_name(klass):"object");career_note("] ");
    for(i32 level=0;klass&&level<3;level++,klass=class_parent?class_parent(klass):0){
        if(class_methods&&method_name){Ptr it=0,m;i32 budget=256;while(budget--&&(m=class_methods(klass,&it))){const char*n=method_name(m);if(!n)continue;char key[64];normalized(n,key);
            if((!method_params||method_params(m)==0)&&(race_contains(key,"rank")||race_contains(key,"score")||race_contains(key,"evaluation")||(race_contains(key,"skill")&&!race_contains(key,"hint"))||race_contains(key,"finish")||same(key,"getstate")||same(key,"getcurrentstate")||same(key,"getstep")||same(key,"getcurrentstep"))){career_note(n);career_note("; ");}
        }}
        Ptr it=0,field;i32 budget=128;while(budget--&&(field=class_fields(klass,&it))){const char*n=field_name(field);if(!n)continue;char key[64];normalized(n,key);
            if(race_contains(key,"rank")||race_contains(key,"score")||race_contains(key,"evaluation")||race_contains(key,"skill")||race_contains(key,"finish")||race_contains(key,"state")||race_contains(key,"step")){career_note(n);career_note("; ");}
        }
    }
}
typedef struct {CareerResultPacket packet;i32 text_score,confirmed;} CareerUiResult;
static CareerUiResult career_ui_result,career_ui_legacy;
static Ptr career_ui_seen[128];static i32 career_ui_context[128],career_ui_seen_count;
static void career_result_read_data(Ptr object,CareerResultPacket*out){
    i32 card=0;if(race_number(object,"get_CardId",&card)&&card>=100000&&card<200000){if(out->card&&out->card!=card){out->score=0;out->skills=-1;out->flags=0;}out->card=card;}
    i32 scenario=0;if(race_number(object,"get_ScenarioId",&scenario)&&scenario>=0&&scenario<=1000)out->scenario=scenario;
    const char*getters[]={"get_RankScore","get_EvaluationPoint","get_EvaluationScore","get_RankPoint","get_TotalEvaluationPoint"};
    for(i32 i=0;i<5;i++){i32 n=0;if(race_number(object,getters[i],&n)&&n>0&&n<=999999){out->score=n;out->flags|=1;break;}}
    const char*fields[]={"rankscore","evaluationpoint","evaluationscore","rankpoint"};
    if(out->score<=0)for(i32 i=0;i<4;i++){i32 n=0;if(race_int_field(object,fields[i],&n)&&n>0&&n<=999999){out->score=n;out->flags|=1;break;}}
    i32 skills=career_result_skill_count(object);if(skills>=0){out->skills=skills;out->flags|=2;}
}
typedef struct {Ptr source;u64 last_attempt;i32 program,gate,chara,score,skills,flags;char trainer[96];} CareerRivalLegacyCache;
static CareerRivalLegacyCache career_rival_legacy_cache[18];
/* Race HorseData may expose the exact TrainedCharaData record used by a normal
   Legacy card. Read only that opponent's record; never borrow our own score. */
static void sample_rival_legacy_data(void){
    if(!race_rivals.active||race_rivals.version!=3)return;
    u64 now=GetTickCount64();
    for(i32 i=0;i<race_rivals.count&&i<18;i++){
        RaceRivalRecord*entry=&race_rivals.entries[i];Ptr source=race_rival_objects[i];
        if(!source)continue;CareerRivalLegacyCache*cache=&career_rival_legacy_cache[i];
        i32 trainer_same=1;for(i32 j=0;j<96;j++)if(cache->trainer[j]!=entry->trainer[j]){trainer_same=0;break;}
        if(cache->source!=source||cache->program!=race_rivals.program||cache->gate!=entry->gate||cache->chara!=entry->chara||!trainer_same){
            memset(cache,0,sizeof(*cache));cache->source=source;cache->program=race_rivals.program;cache->gate=entry->gate;cache->chara=entry->chara;cache->skills=-1;memcpy(cache->trainer,entry->trainer,sizeof(cache->trainer));
        }
        if(cache->flags!=3&&now-cache->last_attempt>=1000){
            cache->last_attempt=now;Ptr record=race_ref(source,"get_TrainedCharaData");
            if(!record)record=source;
            CareerResultPacket profile;memset(&profile,0,sizeof(profile));profile.skills=-1;career_result_read_data(record,&profile);
            if(profile.card>=100000&&profile.card<200000&&profile.card/100==entry->chara){
                if((profile.flags&1)&&profile.score>0&&profile.score<=999999){cache->score=profile.score;cache->flags|=1;}
                if((profile.flags&2)&&profile.skills>=0&&profile.skills<=512){cache->skills=profile.skills;cache->flags|=2;}
            }
        }
        if(cache->flags&1){entry->score=cache->score;entry->flags|=2;}
        if(cache->flags&2){entry->skills=cache->skills;entry->flags|=4;}
    }
}
/* Verified in the live rank panel before trait selection. At this stage the
   result-data container is still null; the sequence already owns the score. */
static void career_result_sequence(CareerResultPacket*out,i32 finished){
    if(!finished||out->card<100000)return;Ptr image=get_image("umamusume");Ptr klass=image?get_class(image,"Gallop","SceneManager"):0;
    Ptr manager=klass?singleton(klass):0;i32 type=0;
    Ptr controller=career_result_field(manager,"currentviewcontroller",&type);
    const char*name=controller&&class_name?class_name(object_class(controller)):0;
    if(!name||!same(name,"SingleModeResultViewController"))return;
    Ptr sequence=career_result_field(controller,"sequence",&type);
    Ptr model=career_result_field(sequence,"rankscoremodel",&type);i32 character=0,score=0;
    if(!race_number(model,"get_CharacterId",&character)||character!=out->card/100)return;
    if(race_number(model,"get_RankScore",&score)&&score>0&&score<=999999){out->score=score;out->flags|=1;if(career_describe)career_note("Score source: active SingleModeResultSequence rank model; ");}
}
static Ptr career_legacy_member(Ptr object,const char*name){i32 type=0;return career_result_field(object,name,&type);}
static i32 career_legacy_list(Ptr list,Ptr*out){
    i32 count=race_collection(list,0x12,out),valid=count>0;
    for(i32 i=0;i<count;i++)if(!out[i])valid=0;if(valid)return count;
    /* Some closed generic List<DialogCommon> accessors return null even while
       their verified reference-array backing contains the open dialog. */
    Ptr array=career_legacy_member(list,"items");count=0;
    if(!race_int_field(list,"size",&count)||count<1||count>64||!array||!array_length||!array_element_size||!array_header_size)return 0;
    u32 capacity=array_length(array),header=array_header_size();
    if((u32)count>capacity||capacity>262144||header<24||header>128||array_element_size(object_class(array))!=8)return 0;
    for(i32 i=0;i<count;i++)memcpy(&out[i],(unsigned char*)array+header+(usize)i*8,8);return count;
}
static i32 career_legacy_flag(Ptr object,const char*key){if(!object)return 0;Ptr klass=object_class(object);for(i32 level=0;klass&&level<4;level++,klass=class_parent?class_parent(klass):0){Ptr it=0,f;while((f=class_fields(klass,&it))){char name[64];normalized(field_name(f),name);if(same(name,key)&&type_code(field_type(f))==2){unsigned char flag=0;field_value(object,f,&flag);return flag!=0;}}}return 0;}
static i32 career_legacy_visible(Ptr object){Ptr go=race_ref(object,"get_gameObject");return go&&race_bool(go,"get_activeInHierarchy");}
static Ptr career_legacy_item(Ptr dictionary,i32 id){
    if(!dictionary)return 0;Ptr m=class_method(object_class(dictionary),"get_Item",1);if(!m||!method_param||type_code(method_param(m,0))!=8)return 0;
    i32 type=type_code(method_return(m));if(type!=0x12&&type!=0x15&&type!=0x1c)return 0;Ptr arg=&id,ex=0;Ptr value=runtime_invoke(m,dictionary,&arg,&ex);return ex?0:value;
}
static Ptr career_dialog_record(Ptr dialog){
    Ptr target=career_legacy_member(career_legacy_member(dialog,"onclosedialogwithcharaswitchbutton"),"mtarget");
    target=career_legacy_member(career_legacy_member(target,"getindexedsetupparam"),"mtarget");
    return career_legacy_member(career_legacy_member(target,"buttoninfo"),"trainedchara");
}
/* DialogCommon owns a live detail component through its close-button delegate.
   Read the target only; never invoke callbacks. Select the roster record by the
   displayed trained ID, so arrow navigation cannot retain a previous Uma. */
static i32 career_legacy_dialog(CareerResultPacket*out){
    Ptr image=get_image("umamusume"),klass=image?get_class(image,"Gallop","DialogManager"):0;
    Ptr manager=klass?singleton(klass):0,list=career_legacy_member(manager,"dialoglist"),items[64];i32 count=career_legacy_list(list,items);
    for(i32 n=count-1;n>=0;n--){Ptr common=items[n];if(career_legacy_flag(common,"isdispclosing")||career_legacy_flag(common,"isdispclosed"))continue;
        Ptr data=career_legacy_member(common,"data"),callback=career_legacy_member(data,"centerbuttoncallback");
        Ptr dialog=career_legacy_member(callback,"mtarget");const char*name=dialog&&class_name?class_name(object_class(dialog)):0;
        if(!name||(!same(name,"DialogTrainedCharacterDetail")&&!same(name,"DialogTrainedCharacterDetailLandscape"))||!career_legacy_visible(dialog))continue;
        Ptr header=career_legacy_member(dialog,"headertitle"),own=career_legacy_member(header,"innerown");i32 id=0;
        if(!career_legacy_visible(own)||!race_int_field(own,"trainedcharaid",&id)||id<=0)continue;
        Ptr work=work_class?singleton(work_class):0,roster=race_ref(work,"get_TrainedCharaData");
        Ptr record=career_legacy_item(career_legacy_member(roster,"datadic"),id);
        if(!record)record=career_legacy_item(career_legacy_member(roster,"alldatadic"),id);
        if(!record){
            Ptr candidate=career_dialog_record(dialog);
            i32 candidate_score=-1;
            if(race_bool(candidate,"get_IsSuccessionOnly")&&race_number(candidate,"get_RankScore",&candidate_score)&&candidate_score==0)record=candidate;
        }
        i32 selected=0;if(!race_number(record,"get_Id",&selected)||selected!=id)continue;
        CareerResultPacket value;memset(&value,0,sizeof(value));value.magic=0x43525331;value.skills=-1;career_result_read_data(record,&value);
        if(value.card>=100000&&value.card<200000){
            __try {career_legacy_details(record,value.card,value.score);}__except(1){legacy_details.active=0;}
            /* A bare record must successfully expose ALL five absent parameters,
               zero evaluation, learned skills and wins, under the selected ID.
               Failed getters or a half-loaded normal veteran cannot qualify. */
            i32 bare=!(value.flags&1)&&(value.flags&2)&&race_bool(record,"get_IsSuccessionOnly")&&legacy_details.active&&legacy_details.id==id&&legacy_details.score==0;
            if(!(value.flags&1)&&!bare)continue;
            value.active=1;value.flags|=4;if(bare)value.flags|=8;value.rank=id;*out=value;
            return 1;}
    }return 0;
}
static i32 career_result_label(Ptr object){
    Ptr method=object?race_method(object_class(object),"get_text"):0;if(!method||type_code(method_return(method))!=14)return 0;
    Ptr ex=0,s=runtime_invoke(method,object,0,&ex);if(ex||!s)return 0;i32 length=string_length(s);u16*chars=string_chars(s);if(!chars||length<1||length>24)return 0;
    i32 value=0,digits=0;for(i32 i=0;i<length;i++){u16 c=chars[i];if(c>='0'&&c<='9'){if(++digits>6)return 0;value=value*10+c-'0';}else if(c!=','&&c!=' '&&c!='\t'&&c!='\n'&&c!='\r')return 0;}
    return digits&&value>0&&value<=999999?value:0;
}
typedef struct {u32 magic; i32 active,card,placement; char trainer[96]; i32 margin_kind; float gap_lengths; u32 time_magic; float gap_seconds;} RivalDetailPacket;
typedef char rival_detail_size_is_128[(sizeof(RivalDetailPacket)==128)?1:-1];
static RivalDetailPacket rival_detail;

static void sample_practice_partner(Ptr dialog,i32 card,i32 score,const char*trainer){
    Ptr record=career_dialog_record(dialog);
    if(!record)return;
    CareerResultPacket value={0};
    value.magic=0x43525331;value.skills=-1;
    career_result_read_data(record,&value);
    if(value.card!=card||!race_number(record,"get_Id",&value.rank)||value.rank<=0)return;
    i32 bare=0,record_score=-1;
    if(!score&&!(value.flags&1)&&(value.flags&2)&&race_bool(record,"get_IsSuccessionOnly")&&
        race_number(record,"get_RankScore",&record_score)&&record_score==0)bare=1;
    if(!bare&&(!score||!(value.flags&1)||value.score!=score))return;

    LegacyDetailsPacket previous_details=legacy_details;
    LegacyGrandparentPacket previous_grandparents=legacy_grandparents;
    legacy_details.active=0;legacy_grandparents.active=0;
    __try {career_legacy_details(record,card,value.score);} __except(1){legacy_details.active=0;}
    if(!legacy_details.active){
        legacy_details=previous_details;legacy_grandparents=previous_grandparents;
        return;
    }
    value.active=1;value.flags|=4|32;
    if(bare)value.flags|=8;
    career_result=value;
    rival_detail.active=1;rival_detail.card=card;
    memcpy(rival_detail.trainer,trainer,sizeof(rival_detail.trainer));
}

static void sample_rival_profile(void) {
    memset(&rival_detail,0,sizeof(rival_detail));rival_detail.magic=0x32444c52;
    Ptr image=get_image("umamusume"),klass=image?get_class(image,"Gallop","DialogManager"):0;
    Ptr manager=klass?singleton(klass):0,items[64];
    i32 count=career_legacy_list(career_legacy_member(manager,"dialoglist"),items);
    if(count<1) return;
    Ptr common=items[count-1];
    if(!common || career_legacy_flag(common,"isdispclosing") || career_legacy_flag(common,"isdispclosed")) return;
    Ptr data=career_legacy_member(common,"data");
    Ptr dialog=career_legacy_member(career_legacy_member(data,"centerbuttoncallback"),"mtarget");
    const char* name=dialog?class_name(object_class(dialog)):0;
    if(!name || (!same(name,"DialogTrainedCharacterDetail") && !same(name,"DialogTrainedCharacterDetailLandscape")) ||
        !career_legacy_visible(dialog)) return;
    Ptr header=career_legacy_member(dialog,"headertitle");
    Ptr panel=career_legacy_member(header,"innerpartner");
    if(!career_legacy_visible(panel)) return;
    Ptr button=career_legacy_member(career_legacy_member(header,"headerbase"),"characterbutton");
    Ptr info=career_legacy_member(button,"info");
    i32 card=0;
    if(!race_int_field(info,"id",&card) || card<100000 || card>=200000) return;
    i32 score=career_result_label(career_legacy_member(panel,"textpoint"));
    char trainer[96]={0};
    if(!race_text(career_legacy_member(panel,"texttrainername"),"get_text",trainer,sizeof(trainer))) return;
    if(!race_packet.active){sample_practice_partner(dialog,card,score,trainer);return;}
    if(race_packet.phase!=2||!race_rivals.active||!score)return;

    i32 selected=-1;
    for(i32 i=0;i<race_rivals.count;i++) {
        RaceRivalRecord* entry=&race_rivals.entries[i];
        if(entry->chara!=card/100 || entry->score!=score || !same(entry->trainer,trainer)) continue;
        i32 entrant_card=0;
        if(!race_number(race_rival_objects[i],"get_CardId",&entrant_card) || entrant_card!=card) continue;
        if(selected>=0) return;
        selected=i;
    }
    if(selected<0) return;
    RaceRivalRecord* entry=&race_rivals.entries[selected];
    CareerResultPacket value={0};
    value.magic=0x43525331;value.active=1;value.card=card;value.score=score;
    value.flags=21;value.skills=-1;
    if(entry->flags&4) {value.skills=entry->skills;value.flags|=2;}
    Ptr record=race_ref(race_rival_objects[selected],"get_TrainedCharaData");
    legacy_details.active=0;legacy_grandparents.active=0;
    if(record && race_number(record,"get_Id",&value.rank) && value.rank>0) {
        race_number(record,"get_ScenarioId",&value.scenario);
        __try {career_legacy_details(record,card,score);} __except(1) {legacy_details.active=0;legacy_grandparents.active=0;}
    }
    career_result=value;
    rival_detail.active=1;rival_detail.card=card;rival_detail.placement=entry->rank;
    memcpy(rival_detail.trainer,trainer,sizeof(trainer));
    if(entry->flags&8){rival_detail.margin_kind=entry->margin_kind;rival_detail.gap_lengths=entry->gap_lengths;}
    if(room_rival_seconds[selected]>0){rival_detail.time_magic=0x31544c52;rival_detail.gap_seconds=room_rival_seconds[selected];}
}
static void career_result_ui(Ptr object,i32 context,i32 visible,i32 depth,i32*budget){
    if(!object||depth>6||*budget<=0||career_ui_seen_count>=128)return;
    for(i32 i=0;i<career_ui_seen_count;i++)if(career_ui_seen[i]==object&&career_ui_context[i]==context)return;
    career_ui_seen[career_ui_seen_count]=object;career_ui_context[career_ui_seen_count++]=context;(*budget)--;
    Ptr gameobject=race_ref(object,"get_gameObject");
    if(gameobject){Ptr active=race_method(object_class(gameobject),"get_activeInHierarchy");if(active&&type_code(method_return(active))==2){if(!race_bool(gameobject,"get_activeInHierarchy"))return;visible=1;}}
    Ptr klass=object_class(object);const char*name=class_name?class_name(klass):0;char classkey[64];normalized(name?name:"",classkey);
    if(gameobject&&visible&&((race_contains(classkey,"trainedchara")&&race_contains(classkey,"detail"))||(race_contains(classkey,"succession")&&race_contains(classkey,"detail"))))context=2;
    else if(gameobject&&visible&&race_contains(classkey,"singlemode")&&(race_contains(classkey,"result")||race_contains(classkey,"finish"))){context=1;career_ui_result.confirmed=1;}
    CareerUiResult*target=context==2?&career_ui_legacy:context==1?&career_ui_result:0;
    if(target&&visible)career_result_read_data(object,&target->packet);
    if(career_describe&&depth<2){career_note("UI [");career_note(name?name:"?");career_note("] ");}
    const char*getters[]={"get_CurrentView","get_MainView","get_CurrentDialog","get_TopDialog","get_CurrentViewController","get_TrainedCharaData","get_ResultData","get_ResultInfo","get_Data","get_Parameter"};
    for(i32 i=0;i<10&&*budget>0;i++){Ptr child=race_ref(object,getters[i]);if(child&&child!=object)career_result_ui(child,context,visible,depth+1,budget);}
    for(i32 level=0;klass&&level<3;level++,klass=class_parent?class_parent(klass):0){Ptr it=0,field;
        while(*budget>0&&(field=class_fields(klass,&it))){(*budget)--;if(field_flags&&(field_flags(field)&0x10))continue;const char*n=field_name(field);if(!n)continue;char key[64];normalized(n,key);
            i32 type=type_code(field_type(field));if(type!=0x12&&type!=0x15&&type!=0x1c&&type!=0x1d)continue;
            if(career_describe&&depth==0){career_note(n);career_note("; ");}
            if(race_contains(key,"cache")||race_contains(key,"pool")||race_contains(key,"prefab")||race_contains(key,"template"))continue;
            i32 score_label=race_contains(key,"rankscore")||race_contains(key,"evaluationpoint")||race_contains(key,"totalscore");
            if(!(score_label||race_contains(key,"view")||race_contains(key,"dialog")||race_contains(key,"trained")||race_contains(key,"chara")||race_contains(key,"result")||race_contains(key,"finish")||race_contains(key,"data")||race_contains(key,"model")||race_contains(key,"param")||race_contains(key,"current")||race_contains(key,"controller")||race_contains(key,"stack")||same(key,"items")||same(key,"array")))continue;
            Ptr child=0;field_value(object,field,&child);if(!child||child==object)continue;
            if(target&&visible&&score_label){i32 score=career_result_label(child);if(score>0)target->text_score=score;}
            Ptr items[64];i32 count=race_collection(child,type,items);
            if(count>0){for(i32 i=count-1;i>=0&&*budget>0;i--)career_result_ui(items[i],context,visible,depth+1,budget);}
            else if(type!=0x1d)career_result_ui(child,context,visible,depth+1,budget);
        }
    }
}
static void sample_career_result(Ptr scene){
    memset(&legacy_details,0,sizeof(legacy_details));legacy_details.magic=0x4c455831;
    memset(&legacy_grandparents,0,sizeof(legacy_grandparents));legacy_grandparents.magic=0x4c475031;
    memset(&career_result,0,sizeof(career_result));career_result.magic=0x43525331;career_result.skills=-1;
    if(career_result_disabled)return;
    __try {
        u64 now=GetTickCount64();i32 describe=now-career_result_last_debug>=10000||career_result_last_debug==0;
        i32 in_career=race_scene(scene,"SingleMode");
        if(describe){career_result_last_debug=now;career_debug_len=0;career_debug[0]=0;career_describe=1;career_note(in_career?"Scene: SingleMode [career result inspection] ":"Scene: UI [legacy inspection] ");}
        if(in_career&&career_character){career_result.card=career.card;career_result.scenario=career.scenario;career_result_read_data(career_character,&career_result);}
        i32 finished=in_career&&(career_result_finished(career_work)||career_result_finished(career_character)||race_scene(scene,"SingleModeResult")||race_scene(scene,"SingleModeFinish"));
        career_result_sequence(&career_result,finished);
        memset(&career_ui_result,0,sizeof(career_ui_result));memset(&career_ui_legacy,0,sizeof(career_ui_legacy));career_ui_result.packet=career_result;career_ui_legacy.packet.skills=-1;career_ui_seen_count=0;
        Ptr image=get_image("umamusume");const char*roots[]={"UIManager","DialogManager","ViewManager"};i32 budget=256;
        for(i32 i=0;i<3&&budget>0;i++){Ptr klass=image?get_class(image,"Gallop",roots[i]):0;Ptr root=klass?singleton(klass):0;career_result_ui(root,finished?1:0,0,0,&budget);}
        if(career_ui_result.packet.score<=0&&career_ui_result.text_score>0){career_ui_result.packet.score=career_ui_result.text_score;career_ui_result.packet.flags|=1;}
        if(career_ui_legacy.packet.score<=0&&career_ui_legacy.text_score>0){career_ui_legacy.packet.score=career_ui_legacy.text_score;career_ui_legacy.packet.flags|=1;}
        if(career_ui_legacy.packet.card>=100000&&(career_ui_legacy.packet.flags&1)){career_result=career_ui_legacy.packet;career_result.magic=0x43525331;career_result.flags|=4;finished=1;}
        else {career_result=career_ui_result.packet;if(career_ui_result.confirmed)finished=1;}
        if(career_legacy_dialog(&career_result))finished=1;
        career_result.active=finished&&((career_result.flags&1)||(career_result.flags&14)==14)&&career_result.card>=100000&&career_result.card<200000;
        if(describe){career_note(career_result.score>0?"Evaluation available; ":"Evaluation unavailable; ");career_note(career_result.skills>=0?"Learned-skill count available; ":"Learned-skill count unavailable; ");career_note(finished?"Result stage confirmed; ":"Result stage unconfirmed; ");career_result_schema(career_character);career_result_schema(career_work);}
        career_describe=0;
    }__except(1){career_result_disabled=1;career_result.active=0;legacy_details.active=0;report("Optional career result telemetry disabled after incompatible metadata.");}
}
