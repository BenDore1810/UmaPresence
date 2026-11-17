/* Official result margins. Call the same read-only conversion/formatter used by
   the game's result rows. No DefeatType, frame-distance conversion or thresholds. */
typedef struct { i32 kind; float lengths; } OfficialMargin; /* 1 nose,2 head,3 neck,4 distance,5 numeric */
static u32 (*official_method_flags)(Ptr,u32*);
static i32 official_disabled;
static i32 official_digits(const char**p,i32*out){i32 n=0,count=0;while(**p>='0'&&**p<='9'){if(++count>4)return 0;n=n*10+*(*p)++-'0';}*out=n;return count>0;}
static void official_space(const char**p){while(**p==' '||**p=='\t')(*p)++;}
static i32 official_label(Ptr value,OfficialMargin*out){
    out->kind=0;out->lengths=0;if(!value)return 0;i32 count=string_length(value);u16*s=string_chars(value);if(!s||count<1||count>128)return 0;
    u16 clean[129];i32 used=0,skip_tag=0;
    for(i32 i=0;i<count;i++){
        u16 c=s[i];if(c=='<'){skip_tag=1;continue;}if(skip_tag){if(c=='>')skip_tag=0;continue;}
        if(i+4<count&&c=='$'&&s[i+1]=='('&&s[i+2]=='n'&&s[i+3]=='b'&&s[i+4]==')'){clean[used++]=' ';i+=4;continue;}
        clean[used++]=(c==0xa0||c==0x202f)?' ':c;
    }
    count=used;s=clean;while(count>0&&*s==' '){s++;count--;}while(count>0&&s[count-1]==' ')count--;
    if(count>=2&&s[0]==0x5927&&s[1]==0x5dee){out->kind=4;return 1;}
    if(count>=2&&s[0]==0x30cf&&s[1]==0x30ca){out->kind=1;return 1;}
    if(count>=3&&s[0]==0x30a2&&s[1]==0x30bf&&s[2]==0x30de){out->kind=2;return 1;}
    if(count>=2&&s[0]==0x30af&&s[1]==0x30d3){out->kind=3;return 1;}
    char text[256];i32 n=0,tag=0;
    for(i32 i=0;i<count;i++){
        u16 c=s[i];if(c=='<'){tag=1;continue;}if(tag){if(c=='>')tag=0;continue;}
        const char*fraction=c==0xbd?" 1/2":c==0xbc?" 1/4":c==0xbe?" 3/4":0;
        if(fraction){while(*fraction&&n<250)text[n++]=*fraction++;continue;}
        if(c==0x30d0&&i+1<count&&s[i+1]==0x8eab){const char*unit=" lengths";while(*unit&&n<250)text[n++]=*unit++;i++;continue;}
        if(n>=250)return 0;if(c==0xa0||c==0x202f)c=' ';if(c==0x2044)c='/';if(c>127)return 0;
        text[n++]=(char)(c>='A'&&c<='Z'?c+32:c);
    }
    while(n>0&&(text[n-1]==' '||text[n-1]=='\t'))n--;text[n]=0;const char*p=text;official_space(&p);
    if(same(p,"nose")||same(p,"a nose")||same(p,"ns")){out->kind=1;return 1;}
    if(same(p,"head")||same(p,"a head")||same(p,"hd")){out->kind=2;return 1;}
    if(same(p,"neck")||same(p,"a neck")||same(p,"nk")){out->kind=3;return 1;}
    if(same(p,"distance")||same(p,"dst")||same(p,"large margin")){out->kind=4;return 1;}
    i32 whole=0,num=0,den=0;if(!official_digits(&p,&whole))return 0;float result=(float)whole;
    if(*p=='.'){p++;const char*start=p;if(!official_digits(&p,&num))return 0;den=1;for(const char*q=start;q<p;q++)den*=10;result+=(float)num/(float)den;}
    else if(*p=='/'){p++;if(!official_digits(&p,&den)||den<=0||den>64)return 0;result=(float)whole/(float)den;}
    else {official_space(&p);if(*p>='0'&&*p<='9'){if(!official_digits(&p,&num)||*p++!='/')return 0;if(!official_digits(&p,&den)||den<=0||den>64)return 0;result+=(float)num/(float)den;}}
    official_space(&p);if(*p&& !same(p,"lengths")&&!same(p,"length")&&!same(p,"l"))return 0;
    if(!(result>0&&result<=1000))return 0;out->kind=5;out->lengths=result;return 1;
}
static i32 official_get(Ptr getter,Ptr formatter,i32 order,OfficialMargin*out){
    Ptr args[1]={&order},ex=0,value=runtime_invoke(getter,0,args,&ex);if(ex||!value)return -1;
    Ptr raw=object_unbox(value);if(!raw)return -1;i32 code=0;memcpy(&code,raw,4);args[0]=&code;ex=0;
    Ptr text=runtime_invoke(formatter,0,args,&ex);return ex?-1:official_label(text,out);
}
static i32 official_row_items(Ptr list,Ptr*out){
    /* Closed generic get_Item can return null while the visible list's
       reference-array backing is populated (also seen in Legacy dialogs). */
    Ptr array=race_object_field(list,"items");i32 count=0;
    if(!array&&list){Ptr it=0,field;while((field=class_fields(object_class(list),&it))){char key[64];normalized(field_name(field),key);if(same(key,"items")&&type_code(field_type(field))==0x1d){field_value(list,field,&array);break;}}}
    if(array&&race_int_field(list,"size",&count)&&count>0&&count<=64&&array_length&&array_element_size&&array_header_size){
        u32 capacity=array_length(array),header=array_header_size();
        if((u32)count<=capacity&&capacity<=262144&&header>=24&&header<=128&&array_element_size(object_class(array))==8){
            for(i32 i=0;i<count;i++){memcpy(&out[i],(unsigned char*)array+header+(usize)i*8,8);if(!out[i])return 0;}
            return count;
        }
    }
    count=race_collection(list,0x12,out);for(i32 i=0;i<count;i++)if(!out[i])return 0;return count;
}
static i32 official_rows(Ptr horses,i32 total,i32*by_rank,OfficialMargin*margins){
    Ptr image=get_image("umamusume"),klass=image?get_class(image,"Gallop","SceneManager"):0;
    Ptr controller=race_object_field(klass?singleton(klass):0,"currentviewcontroller");
    const char*name=controller&&class_name?class_name(object_class(controller)):0;
    if(!name||!same(name,"RaceMainViewController"))return 0;
    Ptr panel=race_object_field(controller,"resultlist");if(!race_bool(race_ref(panel,"get_gameObject"),"get_activeInHierarchy"))return 0;
    Ptr rows=race_object_field(panel,"containerlist"),items[64];i32 count=official_row_items(rows,items),seen[64]={0};
    if(count<2||count>total)return 0;
    for(i32 i=0;i<count;i++){
        Ptr horse=race_object_field(items[i],"horsedata");i32 chara=0,gate=0,rank=-1;
        if(!race_int_field(horse,"charaid",&chara)&&!race_number(horse,"get_CharaId",&chara))return 0;
        if(!race_number(horse,"get_GateNo",&gate))return 0;
        for(i32 r=0;r<total;r++){
            MetricNode h=metric_item(horses,(u32)by_rank[r]);i32 c=0,g=0;
            if(metric_read(h,"charaid",8,&c,4)&&race_number(h.value,"get_GateNo",&g)&&c==chara&&g==gate){rank=r;break;}
        }
        if(rank<0||seen[rank])return 0;seen[rank]=1;if(rank==0)continue;
        Ptr label=race_object_field(items[i],"recordtext"),method=label?race_method(object_class(label),"get_text"):0;
        if(!method||type_code(method_return(method))!=14)continue;
        Ptr ex=0,text=runtime_invoke(method,label,0,&ex);if(!ex)official_label(text,&margins[rank]);
    }
    /* The runner-up row contains the winner's actual winning margin. */
    return seen[0]&&seen[1]&&margins[1].kind;
}
static void metric_official(Ptr horses,i32 total){
    /* Never publish reconstructed result lengths as official labels. */
    for(i32 k=0;k<race_packet.count;k++){race_metrics.entries[k].flags&=~(16|32|64);race_metrics.entries[k].reserved=0;}
    for(i32 i=0;i<race_rivals.count&&i<18;i++){race_rivals.entries[i].flags&=~8;race_rivals.entries[i].margin_kind=0;race_rivals.entries[i].gap_lengths=0;}
    race_metrics.reserved[1]=1;if(official_disabled||total<2||!official_method_flags||!method_param||!class_from_type||!race_class_is_enum)return;
    __try {
        i32 ranks[64],by_rank[64];for(i32 i=0;i<total;i++){
            MetricNode h=metric_item(horses,(u32)i);by_rank[i]=-1;
            if(!race_int_field(h.value,"finishorder",&ranks[i])&&!race_number(h.value,"get_FinishOrder",&ranks[i]))return;
        }
        i32 rank_base=race_rank_base(ranks,total);if(rank_base<0)return;
        for(i32 i=0;i<total;i++){i32 r=ranks[i]+rank_base-1;if(r<0||r>=total||by_rank[r]>=0)return;by_rank[r]=i;}
        OfficialMargin margins[64];memset(margins,0,sizeof(margins));
        i32 rows=official_rows(horses,total,by_rank,margins);
        if(!rows){
        memset(margins,0,sizeof(margins));
        Ptr image=get_image("umamusume"),klass=image?get_class(image,"Gallop","RaceUtil"):0;
        Ptr getter=klass?class_method(klass,"GetHorseLengthByOrder",1):0,formatter=klass?class_method(klass,"GetHorseLengthAmericaString",1):0;
        race_metrics.reserved[1]=2;if(!getter||!formatter)return;
        u32 impl=0;if(!(official_method_flags(getter,&impl)&0x10)||!(official_method_flags(formatter,&impl)&0x10))return;
        Ptr margin_class=class_from_type(method_return(getter));
        race_metrics.reserved[1]=3;
        if(type_code(method_param(getter,0))!=8||!margin_class||!race_class_is_enum(margin_class)||class_from_type(method_param(formatter,0))!=margin_class||type_code(method_return(formatter))!=14)return;
        /* Discover whether the API indexes orders from zero or one. A winner has
           no previous-finisher margin; a runner-up must have a recognized label. */
        OfficialMargin first={0},second={0};i32 a=official_get(getter,formatter,0,&first),b=official_get(getter,formatter,1,&second),api_base=-1;
        if(a==0&&b==1)api_base=0;else if(a<0&&b==0&&official_get(getter,formatter,2,&second)==1)api_base=1;
        race_metrics.reserved[1]=4;if(api_base<0)return;
        margins[1]=second;
        for(i32 r=2;r<total;r++)official_get(getter,formatter,r+api_base,&margins[r]);
        }
        race_metrics.reserved[1]=rows?6:5;
        if(race_rivals.active&&race_rivals.program==race_packet.program&&race_rivals.kind==race_packet.kind&&race_rivals.program_type==race_packet.program_type){
            for(i32 i=0;i<race_rivals.count&&i<18;i++){
                RaceRivalRecord*entry=&race_rivals.entries[i];i32 rank=entry->rank;
                if(rank<1||rank>total)continue;
                if(rank<=2){OfficialMargin m=margins[1];if(!m.kind)continue;entry->flags|=8;entry->margin_kind=m.kind;entry->gap_lengths=m.lengths;}
                else{
                    float sum=0;i32 numeric=1;for(i32 j=1;j<rank;j++){if(margins[j].kind!=5){numeric=0;break;}sum+=margins[j].lengths;}
                    if(numeric&&sum>0&&sum<=10000){entry->flags|=8;entry->margin_kind=5;entry->gap_lengths=sum;}
                }
            }
        }
        for(i32 k=0;k<race_packet.count;k++){
            RaceMetricEntry*e=&race_metrics.entries[k];i32 r=race_packet.ranks[k]-1;
            if(r<0||r>=total)continue;MetricNode h=metric_item(horses,(u32)by_rank[r]);i32 chara=0,gate=0;
            if(!metric_read(h,"charaid",8,&chara,4)||!race_number(h.value,"get_GateNo",&gate)||chara!=e->chara||gate!=e->gate)continue;
            if(r<=1){OfficialMargin m=margins[1];if(!m.kind)continue;
                e->flags|=64|(r==0?32:16);e->reserved=m.kind==5?0:m.kind;e->gap_lengths=m.lengths;
            }else{
                /* Only sum official numeric margins. Never invent lengths for a
                   nose/head/neck/distance; retain the exact time gap instead. */
                float sum=0;i32 numeric=1;for(i32 j=1;j<=r;j++){if(margins[j].kind!=5){numeric=0;break;}sum+=margins[j].lengths;}
                if(numeric&&sum>0&&sum<=10000){e->flags|=64|16;e->gap_lengths=sum;}
            }
        }
    }__except(1){official_disabled=1;race_metrics.reserved[1]=-1;report("Official margin metadata unavailable; result lengths withheld.");}
}
