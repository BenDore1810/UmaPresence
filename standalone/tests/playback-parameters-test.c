/* Exercise the real playback adapter's parameter hooks without loading a game. */
#include "../playback.c"
#include <assert.h>
#include <stdio.h>
volatile RuntimeStatus *rt_status;volatile RuntimeAudioTrace *rt_audio_trace;
RP rt_resolve(const char*n){return 0;}RP rt_image(const char*n){return 0;}
RP rt_class(RP p,const char*a,const char*b){return 0;}RP rt_singleton(RP p){return 0;}
RP rt_object_field(RP p,const char*n){return 0;}RP rt_field(RP p,const char*n){return 0;}
int rt_int_field(RP p,const char*n,int*v){return 0;}RP rt_method_info(RP p,const char*n,int c){return 0;}
RP rt_checked_method(RP p,const char*n,int c,int r,const int*a,int s){return 0;}
int rt_cue_query(RP a,RP b,RP c,int d,RuntimeCue*e){return 0;}int rt_cue_available(void){return 0;}
int rt_install_hook(RP a,RP b,RP*c){return 0;}void rt_boot_log(const char*s){}
void rt_detail(uint32_t s,const char*t){}
static uint32_t formation_flags;
void rt_formation_configure(uint32_t flags,uint32_t voice1,uint32_t voice2){formation_flags=flags;}
static int registered=1;int rt_managed_thread(void){return registered;}
static uint32_t fake_weak(RP p,int b){return (uint32_t)(uintptr_t)p;}
static RP fake_target(uint32_t h){return (RP)(uintptr_t)h;}static void fake_free(uint32_t h){}
static int64_t observed;static int observed_micro;
static void milliseconds(RP p,int64_t v,RP m){observed=v;observed_micro=0;}
static void microseconds(RP p,int64_t v,RP m){observed=v;observed_micro=1;}
int main(void){
#ifdef UMAPRESENCE_PUBLIC_RELEASE
    RuntimeCommand rejected={0};rejected.command=CMD_CONFIGURE;
    for(uint32_t bit=2;bit<=16;bit<<=1){
        rejected.flags=bit;assert(rt_playback_command(&rejected)==RESULT_UNAVAILABLE);
        rejected.flags=bit|1;assert(rt_playback_command(&rejected)==RESULT_UNAVAILABLE);
    }
    rejected.flags=0;rejected.voice1=1001;assert(rt_playback_command(&rejected)==RESULT_UNAVAILABLE);
    rejected.voice1=0;rejected.voice2=1002;assert(rt_playback_command(&rejected)==RESULT_UNAVAILABLE);
    puts("PASS: public native command channel rejects every experimental flag and voice selection");
    RuntimeStatus status={0};rt_status=&status;
    RuntimeCommand formation={0};formation.command=CMD_CONFIGURE;formation.flags=1;
    assert(rt_playback_command(&formation)==RESULT_UNAVAILABLE);
    status.capabilities=CAP_FORMATION;
    assert(rt_playback_command(&formation)==RESULT_OK&&formation_flags==1);
    formation.flags=0;
    assert(rt_playback_command(&formation)==RESULT_OK&&formation_flags==0);
    rt_status=0;
    puts("PASS: public native duplicate casting can be enabled and disabled when formation is available");
#endif
    start_weak=fake_weak;start_target=fake_target;start_free=fake_free;
    native_start_time=milliseconds;native_start_micro=microseconds;start_hooks=1;
    RP player=(RP)1;int64_t value;int micro;
    assert(!original_start(player,&value,&micro));puts("PASS: real adapter rejects an unobserved player parameter");
    rt_playback_reset_hook_ready();rt_playback_parameters_reset(player);
    assert(original_start(player,&value,&micro)&&value==0&&!micro);puts("PASS: observed game ResetParameters establishes its documented zero start");
    start_micro_hook(player,123456,0);assert(original_start(player,&value,&micro)&&value==123456&&micro);
    SeekStem stem={.player=player,.original_start=value,.original_micro=micro};
    native_start_time(player,60000,0);restore_start(&stem);
    assert(observed==123456&&observed_micro&&original_start(player,&value,&micro)&&value==123456&&micro);
    puts("PASS: real seek restoration preserves the game's microsecond parameter after a temporary seek");
    start_time_hook(player,150,0);assert(original_start(player,&value,&micro)&&value==150&&!micro);
    stem.original_start=value;stem.original_micro=micro;
    for(int i=0;i<100;i++){native_start_time(player,i*1000,0);restore_start(&stem);assert(observed==150&&!observed_micro);}
    puts("PASS: one hundred real restoration calls retain the original millisecond setting");
    registered=0;start_time_hook(player,200,0);registered=1;
    assert(!original_start(player,&value,&micro));puts("PASS: a foreign-thread parameter update invalidates the cached setting without managed access");
    rt_playback_parameters_reset(player);assert(original_start(player,&value,&micro)&&value==0&&!micro);
    puts("PASS: a later observed game reset restores seek eligibility");
    return 0;
}
