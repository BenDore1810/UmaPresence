/* GPL-3.0. Metadata/scheduling adaptations from pinned Hachimi Edge; see README. */
#pragma once
#define WIN32_LEAN_AND_MEAN
#define COBJMACROS
#include <windows.h>
#include <stdint.h>
typedef void *RP;
typedef RP (*RuntimeLookup)(const char*);
typedef struct { uint32_t size,version; RuntimeLookup lookup; uint64_t capabilities; } RuntimeServices;
typedef struct {
    uint32_t magic,version,pid,state;
    uint64_t nonce,capabilities;
    uint32_t sequence,command_sequence,command_result,backend;
    char detail[192];
} RuntimeStatus;
enum { RT_STARTING=1,RT_PROFILE_FAILED=2,RT_WAITING=3,RT_READY=4,RT_INCOMPATIBLE=5,RT_STOPPED=6,RT_CONFLICT=7 };
enum { CAP_TELEMETRY=1,CAP_PLAYBACK=2,CAP_FORMATION=4,CAP_ROUTING=8,CAP_MIX=16,CAP_OVERLAY=32,CAP_SEEK=64 };
enum { CMD_PLAY=1,CMD_PAUSE=2,CMD_SEEK=3,CMD_BEGIN_DRAG=4,CMD_END_DRAG=5,CMD_CONFIGURE=6,CMD_CANCEL_DRAG=7 };
enum { RESULT_OK=0,RESULT_UNAVAILABLE=1,RESULT_STALE=2,RESULT_BAD_ARGUMENT=3,RESULT_FAILED=4 };
typedef struct {
    uint32_t magic,version,pid,sequence;
    uint64_t nonce;
    uint32_t command,song;
    double position;
    uint32_t flags,voice1,voice2,reserved;
} RuntimeCommand;
extern volatile RuntimeStatus *rt_status;
typedef struct {uint32_t id;int32_t voice,cue;uint32_t flags;float offset,native_gain,factor,effective;char sheet[96];} RuntimeAudioRow;
typedef struct {uint32_t magic,version,sequence,count;int32_t routing_song,routing_part;uint32_t routing_fallbacks,routing_replacements;uint64_t tick;float seek_min,seek_max;uint32_t playback_stage,seek_reason,formation_checks,formation_bypasses;RuntimeAudioRow rows[64];} RuntimeAudioTrace;
extern volatile RuntimeAudioTrace *rt_audio_trace;
_Static_assert(sizeof(RuntimeStatus)==240,"Runtime status ABI size");
_Static_assert(sizeof(RuntimeAudioRow)==128,"Runtime audio row ABI size");
_Static_assert(sizeof(RuntimeAudioTrace)==8256,"Runtime audio trace ABI size");
RP rt_resolve(const char*);
RP rt_image(const char*);
RP rt_class(RP,const char*,const char*);
RP rt_method(RP,const char*,int);
RP rt_method_info(RP,const char*,int);
RP rt_singleton(RP);
RP rt_field(RP,const char*);
RP rt_object_field(RP,const char*);
int rt_int_field(RP,const char*,int*);
void rt_detail(uint32_t,const char*);
int rt_same(const char*,const char*);
int rt_owned(void);
void rt_boot_log(const char*);
int rt_managed_thread(void);
int rt_vm_state(void);
int rt_vm_enter(void);
void rt_vm_leave(void);
int rt_init_gate_install(HMODULE,const char*,const char*,const char*);
int rt_verify_asset(const char*,const char*,uint64_t);
RP rt_checked_method(RP,const char*,int,int,const int*,int);
int rt_install_hook(RP,RP,RP*);
void rt_queue_command(const RuntimeCommand*);
void rt_playback_initialize(void);
typedef struct {RP acb;int id,type;int64_t length_ms;uint16_t tracks,waves;} RuntimeCue;
void rt_cue_initialize(void);
int rt_cue_available(void);
int rt_cue_query(RP,RP,RP,int,RuntimeCue*);
void rt_playback_tick(void);
void rt_playback_parameters_reset(RP);
void rt_playback_reset_hook_ready(void);
uint32_t rt_playback_command(const RuntimeCommand*);
int rt_playback_snapshot(int*,double*,double*,int*);
void rt_formation_initialize(void);
void rt_mix_tick(void);
void rt_mix_initialize(void);
void rt_mix_configure(uint32_t,uint32_t,uint32_t);
void rt_routing_prepare(void);
void rt_routing_initialize(void);
void rt_routing_configure(uint32_t,uint32_t,uint32_t);
typedef struct { RP player,sheet,name;uint32_t id;int cue;float offset,duration; } RuntimeStem;
int rt_audio_graph(RuntimeStem*,int);
void rt_formation_configure(uint32_t,uint32_t,uint32_t);
