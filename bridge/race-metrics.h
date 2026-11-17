/* Optional typed replay telemetry. Metadata reads only; no hooks or game writes. */
typedef struct { i32 chara,gate,flags,reserved; float current,peak,finish,seconds,gap_seconds,gap_lengths; } RaceMetricEntry;
typedef struct { u32 magic; i32 active,program,program_type,count,phase,reserved[2]; RaceMetricEntry entries[3]; } RaceMetricsPacket;
typedef char metric_packet_is_152[(sizeof(RaceMetricsPacket)==152)?1:-1];
static RaceMetricsPacket race_metrics;
static Ptr (*metric_element_class)(Ptr);
static _Bool (*metric_value_type)(Ptr);
static i32 (*metric_field_offset)(Ptr);
typedef struct { Ptr value,klass; i32 inline_size; } MetricNode;
static MetricNode metric_node(Ptr value){MetricNode n={value,value?object_class(value):0,0};return n;}
static i32 metric_read(MetricNode n,const char*key,i32 code,Ptr out,i32 size){
    if(!n.value||!n.klass)return 0;Ptr c=n.klass;
    for(i32 level=0;c&&level<6;level++,c=class_parent?class_parent(c):0){Ptr it=0,f;i32 budget=256;while(budget--&&(f=class_fields(c,&it))){
        if(field_flags&&(field_flags(f)&0x10))continue;char name[64];normalized(field_name(f),name);if(!same(name,key))continue;
        i32 actual=type_code(field_type(f));if(code==-1){if(actual!=0x12&&actual!=0x1d&&actual!=0x15&&actual!=0x1c)return 0;}else if(actual!=code)return 0;
        if(n.inline_size){if(!metric_field_offset)return 0;i32 offset=metric_field_offset(f)-16;if(offset<0||offset+size>n.inline_size)return 0;memcpy(out,(unsigned char*)n.value+offset,(usize)size);}
        else field_value(n.value,f,out);return 1;
    }}return 0;
}
static Ptr metric_ref(MetricNode n,const char*key){Ptr value=0;metric_read(n,key,-1,&value,8);return value;}
static i32 metric_float(MetricNode n,const char*key,float*out){return metric_read(n,key,12,out,4)&&*out>=0&&*out<1000000;}
static i32 metric_array(Ptr array,u32*count){if(!array||!metric_element_class||!metric_value_type||!array_length||!array_header_size||!array_element_size)return 0;u32 h=array_header_size();i32 stride=array_element_size(object_class(array));*count=array_length(array);return *count>0&&*count<=32768&&h>=24&&h<=128&&stride>=1&&stride<=512;}
static MetricNode metric_item(Ptr array,u32 index){
    MetricNode empty={0};u32 count=0;if(!metric_array(array,&count)||index>=count)return empty;
    Ptr element=metric_element_class(object_class(array));if(!element)return empty;i32 stride=array_element_size(object_class(array));unsigned char*p=(unsigned char*)array+array_header_size()+(usize)index*(usize)stride;
    if(metric_value_type(element)){MetricNode n={p,element,stride};return n;}
    if(stride!=8)return empty;Ptr child=0;memcpy(&child,p,8);return metric_node(child);
}
#include "official-margins.h"
#define METRIC_FRAMES 8192
static struct {
    Ptr info,sim,frames; i32 valid,count,team_count,program,total,gates[3],charas[3],indexes[3],flags[3],margins[3];
    float raw[3],scaled[3],all_raw[64],all_scaled[64],finish[3],gap_seconds[3],gap_lengths[3],times[METRIC_FRAMES],peaks[3][METRIC_FRAMES];
} metric_cache;
static i32 metric_disabled;
static u64 metric_retry;
/* DefeatType is a loss-reason enum, not an official finishing-margin label. */
static float metric_lerp(float a,float b,float t){return a+(b-a)*t;}
static void metric_build(Ptr info,Ptr sim,Ptr frames,i32 count,Ptr racehorses,i32 team_indexes[3],i32 total){
    memset(&metric_cache,0,sizeof(metric_cache));metric_cache.info=info;metric_cache.sim=sim;metric_cache.frames=frames;metric_cache.program=race_packet.program;metric_cache.count=count;metric_cache.team_count=race_packet.count;
    float winner_raw=1000000,winner_scaled=0,target=(float)race_packet.length; i32 winner=-1,runup=0;
    race_number(info,"get_RunUpDistance",&runup);if(runup<0||runup>1000)return;target+=(float)runup;
    for(i32 i=0;i<total;i++){MetricNode h=metric_item(racehorses,(u32)i);float raw=0,scaled=0;if(!metric_float(h,"finishtimeraw",&raw)||raw<=0||!metric_float(h,"finishtimescaled",&scaled)||scaled<=0)return;metric_cache.all_raw[i]=raw;metric_cache.all_scaled[i]=scaled;if(raw<winner_raw){winner_raw=raw;winner_scaled=scaled;winner=i;}}
    if(winner<0)return;metric_cache.total=total;
    i32 second=-1;for(i32 i=0;i<total;i++)if(i!=winner&&(second<0||metric_cache.all_raw[i]<metric_cache.all_raw[second]))second=i;
    for(i32 k=0;k<race_packet.count;k++){
        metric_cache.indexes[k]=team_indexes[k];metric_cache.gates[k]=race_packet.gates[k];metric_cache.charas[k]=race_packet.charas[k];MetricNode h=metric_item(racehorses,(u32)team_indexes[k]);
        metric_float(h,"finishtimeraw",&metric_cache.raw[k]);metric_float(h,"finishtimescaled",&metric_cache.scaled[k]);metric_cache.gap_seconds[k]=metric_cache.scaled[k]-winner_scaled;if(metric_cache.gap_seconds[k]<0)metric_cache.gap_seconds[k]=0;
        metric_cache.flags[k]=8;
    }
    float previous_time=-1,previous_distance[3]={0},previous_speed[3]={0},previous_winner_distance=0,previous_second_distance=0;
    i32 previous_valid=0,gap_done=0,finish_done[3]={0};
    for(i32 f=0;f<count;f++){
        MetricNode frame=metric_item(frames,(u32)f);float time=0;if(!metric_float(frame,"time",&time)||time<=previous_time)return;
        Ptr horses=metric_ref(frame,"horsedataarray");u32 horse_count=0;if(!metric_array(horses,&horse_count)||horse_count!=(u32)total)return;
        float distance[3],speed[3],winner_distance=0,second_distance=0;if(!metric_float(metric_item(horses,(u32)winner),"distance",&winner_distance))return;
        if(second>=0&&!metric_float(metric_item(horses,(u32)second),"distance",&second_distance))return;
        for(i32 k=0;k<race_packet.count;k++){
            MetricNode h=metric_item(horses,(u32)team_indexes[k]);if(!metric_float(h,"distance",&distance[k])||!metric_float(h,"speed",&speed[k])||speed[k]>100)return;
            float peak=f?metric_cache.peaks[k][f-1]:0;if(time<=metric_cache.raw[k]&&speed[k]>peak)peak=speed[k];metric_cache.peaks[k][f]=peak;
            if(previous_valid&&!finish_done[k]&&previous_time<=metric_cache.raw[k]&&time>=metric_cache.raw[k]){
                float t=(metric_cache.raw[k]-previous_time)/(time-previous_time),d=metric_lerp(previous_distance[k],distance[k],t);
                /* Validate the clock and run-up against the finish crossing before labelling speed. */
                if(d>=target-2&&d<=target+2){metric_cache.finish[k]=metric_lerp(previous_speed[k],speed[k],t);metric_cache.flags[k]|=4;if(metric_cache.finish[k]>metric_cache.peaks[k][f])metric_cache.peaks[k][f]=metric_cache.finish[k];}
                finish_done[k]=1;
            }
        }
        if(previous_valid&&!gap_done&&previous_time<=winner_raw&&time>=winner_raw){
            float t=(winner_raw-previous_time)/(time-previous_time),leader=metric_lerp(previous_winner_distance,winner_distance,t);
            if(leader>=target-2&&leader<=target+2)for(i32 k=0;k<race_packet.count;k++){
                if(team_indexes[k]==winner){
                    if(second>=0&&metric_cache.all_raw[second]>winner_raw){float gap=leader-metric_lerp(previous_second_distance,second_distance,t);if(gap>0){metric_cache.gap_lengths[k]=gap/2.5f;metric_cache.flags[k]|=32;}}
                }else {float d=metric_lerp(previous_distance[k],distance[k],t),gap=leader-d;metric_cache.gap_lengths[k]=(gap>0?gap:0)/2.5f;metric_cache.flags[k]|=16;}
            }
            gap_done=1;
        }
        metric_cache.times[f]=time;previous_time=time;previous_winner_distance=winner_distance;previous_second_distance=second_distance;previous_valid=1;
        for(i32 k=0;k<race_packet.count;k++){previous_distance[k]=distance[k];previous_speed[k]=speed[k];}
    }
    /* All frames are validated before any full-replay peak is published. */
    for(i32 k=0;k<race_packet.count;k++)if(previous_time>=metric_cache.raw[k])metric_cache.flags[k]|=2;
    metric_cache.valid=1;
}
static float room_rival_seconds[18];
static void sample_room_rival_times(Ptr horses,i32 total){
    if(race_packet.kind!=2||race_packet.phase!=2||!race_rivals.active||total<2)return;
    i32 ranks[64],by_rank[64];
    for(i32 i=0;i<total;i++){
        MetricNode horse=metric_item(horses,(u32)i);
        if(!race_int_field(horse.value,"finishorder",&ranks[i])&&!race_number(horse.value,"get_FinishOrder",&ranks[i]))return;
        by_rank[i]=-1;
    }
    i32 base=race_rank_base(ranks,total);
    if(base<0)return;
    for(i32 i=0;i<total;i++){
        i32 rank=ranks[i]+base-1;
        if(rank<0||rank>=total||by_rank[rank]>=0)return;
        by_rank[rank]=i;
    }
    float first=0,second=0;
    if(!metric_float(metric_item(horses,(u32)by_rank[0]),"finishtimescaled",&first)||!(first>0&&first<3600))return;
    metric_float(metric_item(horses,(u32)by_rank[1]),"finishtimescaled",&second);
    for(i32 i=0;i<race_rivals.count&&i<18;i++){
        RaceRivalRecord* rival=&race_rivals.entries[i];
        if(rival->rank<1||rival->rank>total)continue;
        MetricNode horse=metric_item(horses,(u32)by_rank[rival->rank-1]);
        i32 chara=0,gate=0;
        if(!metric_read(horse,"charaid",8,&chara,4)||!race_number(horse.value,"get_GateNo",&gate)||chara!=rival->chara||gate!=rival->gate)continue;
        float finish=second;
        if(rival->rank!=1&&!metric_float(horse,"finishtimescaled",&finish))continue;
        if(finish>first&&finish<3600)room_rival_seconds[i]=finish-first;
    }
}

static void sample_race_metrics(void){
    memset(&race_metrics,0,sizeof(race_metrics));race_metrics.magic=0x524d5431;
    memset(room_rival_seconds,0,sizeof(room_rival_seconds));
    if(metric_disabled||!race_packet.active||race_packet.phase==3||race_packet.count>3||!race_info_fn)return;
    __try {
        race_metrics.reserved[0]=1;
        Ptr info=race_info_fn(),manager=singleton(race_class),horsemanager=race_object_field(manager,"horsemanager");
        Ptr live=race_ref(horsemanager,"GetHorseRaceInfos"),all=race_ref(info,"get_RaceHorse"),sim=race_ref(info,"get_SimData");
        if(!sim)sim=race_object_field(info,"simdata");
        if(!all)all=metric_ref(metric_node(info),"racehorse");u32 total=0;if(!metric_array(all,&total)||total>64)return;
        if(!race_packet.count&&!race_rivals.active)return;
        sample_room_rival_times(all,(i32)total);
        if(!race_packet.count){if(race_packet.phase==2)metric_official(all,(i32)total);return;}
        race_metrics.reserved[0]=2;
        i32 indexes[3]={-1,-1,-1};
        for(i32 k=0;k<race_packet.count;k++)for(u32 i=0;i<total;i++){MetricNode h=metric_item(all,i);i32 chara=0,gate=0,horse_index=-1;if(!metric_read(h,"horseindex",8,&horse_index,4)||horse_index!=(i32)i)return;if(!metric_read(h,"charaid",8,&chara,4)||!race_number(h.value,"get_GateNo",&gate))continue;if(chara==race_packet.charas[k]&&gate==race_packet.gates[k]){if(indexes[k]>=0)return;indexes[k]=(i32)i;}}
        for(i32 k=0;k<race_packet.count;k++)if(indexes[k]<0)return;
        race_metrics.reserved[0]=3;
        Ptr list=metric_ref(metric_node(sim),"framedatalist"),frames=metric_ref(metric_node(list),"items");i32 count=0;u32 capacity=0;
        if(!race_int_field(list,"size",&count)||!metric_array(frames,&capacity)||count<2||count>METRIC_FRAMES||(u32)count>capacity)frames=0;
        i32 matches=metric_cache.valid&&metric_cache.total==(i32)total&&metric_cache.info==info&&metric_cache.sim==sim&&metric_cache.frames==frames&&metric_cache.count==count&&metric_cache.program==race_packet.program&&metric_cache.team_count==race_packet.count;
        for(i32 k=0;k<race_packet.count&&matches;k++)if(metric_cache.charas[k]!=race_packet.charas[k]||metric_cache.gates[k]!=race_packet.gates[k])matches=0;
        for(u32 i=0;i<total&&matches;i++){float raw=0,scaled=0;MetricNode h=metric_item(all,i);if(!metric_float(h,"finishtimeraw",&raw)||!metric_float(h,"finishtimescaled",&scaled)||raw!=metric_cache.all_raw[i]||scaled!=metric_cache.all_scaled[i])matches=0;}
        if(frames&&!matches&&GetTickCount64()>=metric_retry){metric_build(info,sim,frames,count,all,indexes,(i32)total);metric_retry=metric_cache.valid?0:GetTickCount64()+5000;}
        if(!frames)metric_cache.valid=0;
        float now=0;Ptr reader=race_ref(info,"get_SimReader");if(!reader)reader=race_object_field(info,"simreader");i32 have_time=race_float_field(reader,"curtime",&now);
        i32 frame_index=-1;if(metric_cache.valid&&have_time)for(i32 f=0;f<count;f++){if(metric_cache.times[f]>now)break;frame_index=f;}
        float first_finish=1000000;if(metric_cache.valid)for(u32 i=0;i<total;i++)if(metric_cache.all_raw[i]<first_finish)first_finish=metric_cache.all_raw[i];
        float leader=0,live_distances[64]={0};u32 live_count=0;i32 have_live=metric_array(live,&live_count)&&live_count==total,have_distances=have_live;
        if(have_live)for(u32 i=0;i<total;i++){float d=0;if(!race_float_field(metric_item(live,i).value,"distance",&d)||!(d>=0&&d<1000000)){have_distances=0;continue;}live_distances[i]=d;if(d>leader)leader=d;}
        for(i32 k=0;k<race_packet.count;k++){
            RaceMetricEntry*e=&race_metrics.entries[k];e->chara=race_packet.charas[k];e->gate=race_packet.gates[k];
            if(have_live&&race_packet.phase==1){MetricNode h=metric_item(live,(u32)indexes[k]);if(race_float_field(h.value,"lastspeed",&e->current)&&e->current<=100)e->flags|=1;
                if(have_distances){float d=live_distances[indexes[k]];if(race_packet.ranks[k]==1){float next=-1;for(u32 i=0;i<total;i++)if((i32)i!=indexes[k]&&live_distances[i]>next)next=live_distances[i];if(next>=0&&d>next){e->gap_lengths=(d-next)/2.5f;e->flags|=32;}}
                    else {e->gap_lengths=leader>d?(leader-d)/2.5f:0;e->flags|=16;}}
            }
            if(metric_cache.valid){
                if(frame_index>=0&&(metric_cache.flags[k]&2)){e->peak=metric_cache.peaks[k][frame_index];e->flags|=2;}
                if(race_packet.phase==1&&(e->flags&3)==3&&e->current>e->peak)e->peak=e->current;
                if(race_packet.phase==1&&have_time){
                    if(now>=first_finish){e->flags=(e->flags&~(16|32))|(metric_cache.flags[k]&(16|32));e->gap_lengths=metric_cache.gap_lengths[k];}
                    if(now>=metric_cache.raw[k]&&(metric_cache.flags[k]&4)){e->flags=(e->flags&~1)|4;e->finish=metric_cache.finish[k];}
                }
                if(race_packet.phase==2){e->flags=metric_cache.flags[k];e->reserved=metric_cache.margins[k];e->peak=metric_cache.peaks[k][count-1];e->finish=metric_cache.finish[k];e->seconds=metric_cache.scaled[k];e->gap_seconds=metric_cache.gap_seconds[k];e->gap_lengths=metric_cache.gap_lengths[k];}
            }
        }
        if(race_packet.phase==2)metric_official(all,(i32)total);
        race_metrics.active=1;race_metrics.program=race_packet.program;race_metrics.program_type=race_packet.program_type;race_metrics.count=race_packet.count;race_metrics.phase=race_packet.phase;
        race_metrics.reserved[0]=metric_cache.valid?5:4;
    }__except(1){metric_disabled=1;race_metrics.active=0;report("Optional race speed telemetry disabled after incompatible replay metadata.");}
}
