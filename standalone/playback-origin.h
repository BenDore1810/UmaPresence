/* Preserve a cue's timeline origin across our own CRI playback restarts.
   Sample counters describe the current playback, not a seek-independent cue origin. */
#pragma once
#include <stdint.h>
#include <math.h>
#include <string.h>
typedef struct {uintptr_t player;uint32_t id;int song;double offset;} PlaybackOrigin;
typedef struct {PlaybackOrigin rows[65];unsigned next;} PlaybackOrigins;
static void playback_origins_clear(PlaybackOrigins*cache){memset(cache,0,sizeof(*cache));}
static void playback_origin_store(PlaybackOrigins*cache,uintptr_t player,uint32_t id,int song,double offset){
    if(!player||!id||song<=0||!isfinite(offset))return;
    unsigned slot=65;for(unsigned i=0;i<65;i++)if(cache->rows[i].player==player){slot=i;break;}
    if(slot==65){slot=cache->next++%65;}cache->rows[slot]=(PlaybackOrigin){player,id,song,offset};
}
static double playback_origin(PlaybackOrigins*cache,uintptr_t player,uint32_t id,int song,double current,double samples_seconds){
    for(unsigned i=0;i<65;i++){PlaybackOrigin*row=&cache->rows[i];if(row->player==player&&row->id==id&&row->song==song)return row->offset;}
    return current-samples_seconds;
}
