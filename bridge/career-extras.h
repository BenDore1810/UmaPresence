/* CEX1: optional ongoing-career values. Never derive missing values from pixels. */
typedef struct {u32 magic;i32 active,card,turn,flags,hp,max_hp,motivation,fans,remaining;char goal[192];} CareerExtrasPacket;
typedef char career_extras_is_232[(sizeof(CareerExtrasPacket)==232)?1:-1];
static CareerExtrasPacket career_extras;
static i32 career_extras_disabled;
static void career_goal_text(Ptr text,char*out,i32 capacity){
    if(!text)return;i32 count=string_length(text);u16*s=string_chars(text);if(!s||count<1||count>2048)return;
    i32 n=0,tag=0;for(i32 i=0;i<count;i++){
        u32 c=s[i];if(c=='<'){tag=1;continue;}if(tag){if(c=='>')tag=0;continue;}
        if(c=='\\'&&i+1<count&&s[i+1]=='n'){c=' ';i++;}if(c<32||c==0x2028||c==0x2029)c=' ';
        if(c>=0xd800&&c<=0xdbff){if(i+1>=count||s[i+1]<0xdc00||s[i+1]>0xdfff)return;c=0x10000+((c-0xd800)<<10)+(s[++i]-0xdc00);}else if(c>=0xdc00&&c<=0xdfff)return;
        i32 bytes=c<128?1:c<2048?2:c<65536?3:4;if(n+bytes>=capacity)break;
        if(bytes==1)out[n++]=(char)c;
        else if(bytes==2){out[n++]=(char)(0xc0|(c>>6));out[n++]=(char)(0x80|(c&63));}
        else if(bytes==3){out[n++]=(char)(0xe0|(c>>12));out[n++]=(char)(0x80|((c>>6)&63));out[n++]=(char)(0x80|(c&63));}
        else {out[n++]=(char)(0xf0|(c>>18));out[n++]=(char)(0x80|((c>>12)&63));out[n++]=(char)(0x80|((c>>6)&63));out[n++]=(char)(0x80|(c&63));}
    }out[n]=0;
}
static void sample_career_extras(void){
    memset(&career_extras,0,sizeof(career_extras));career_extras.magic=0x43455831;
    if(career_extras_disabled||!career.active||career_result.active||!career_character||!career_work)return;
    __try{
        CareerExtrasPacket value;memset(&value,0,sizeof(value));value.magic=0x43455831;value.card=career.card;value.turn=career.turn;
        if(career_number(career_character,"get_Hp",&value.hp)&&career_number(career_character,"get_MaxHp",&value.max_hp)&&value.hp>=0&&value.max_hp>0&&value.hp<=value.max_hp&&value.max_hp<=1000)value.flags|=1;
        /* Motivation is a boxed Int32-backed enum, not an ObscuredInt. */
        Ptr method=race_method(object_class(career_character),"get_Motivation");
        if(method&&class_from_type&&race_class_is_enum&&race_class_is_enum(class_from_type(method_return(method)))){
            Ptr ex=0,b=runtime_invoke(method,career_character,0,&ex),p=!ex&&b?object_unbox(b):0;
            if(p){memcpy(&value.motivation,p,4);if(value.motivation>=1&&value.motivation<=5)value.flags|=2;}
        }
        if(career_number(career_character,"get_FanCount",&value.fans)&&value.fans>=0&&value.fans<=1000000000)value.flags|=4;
        if(career_number(career_work,"GetDisplayRemainTurnNum",&value.remaining)&&value.remaining>=0&&value.remaining<=200)value.flags|=8;
        /* Only invoke the known read-only text accessor when its parameter is
           actually Boolean. Other overload signatures are left for diagnostics. */
        method=class_method(object_class(career_work),"GetNextTargetText",1);
        if(method&&method_param&&type_code(method_return(method))==14&&type_code(method_param(method,0))==2){
            unsigned char option=0;Ptr args[1]={&option},ex=0,text=runtime_invoke(method,career_work,args,&ex);
            if(!ex)career_goal_text(text,value.goal,sizeof(value.goal));if(value.goal[0])value.flags|=16;
        }
        value.active=1;career_extras=value;
    }__except(1){career_extras_disabled=1;career_extras.active=0;report("Optional career extras disabled after incompatible metadata.");}
}
