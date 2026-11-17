#include "../playback-origin.h"
#include "../playback-policy.h"
#include "../routing-policy.h"
#include "../start-parameters.h"
#include <assert.h>
#include <stdio.h>
int main(void){
    StartParameters parameters={0};
    assert(!start_parameter_find(&parameters,1));puts("PASS: an unobserved start parameter is unknown rather than assumed zero");
    StartParameter* setting=start_parameter_empty(&parameters);*setting=(StartParameter){1,7,150000,1};
    assert(start_parameter_find(&parameters,1)->value==150000&&start_parameter_find(&parameters,1)->micro);puts("PASS: original start parameter preserves its native microsecond units and value");
    for(int i=1;i<128;i++)parameters.rows[i]=(StartParameter){(uintptr_t)i+1,7,0,0};
    assert(!start_parameter_empty(&parameters)&&!start_parameter_find(&parameters,129));puts("PASS: bounded start-parameter cache does not evict a live player's setting");
    PlaybackOrigins cache={0};
    assert(fabs(playback_origin(&cache,1,100,1176,10,9.846)-.154)<.00001);puts("PASS: initial cue origin includes its native offset");
    playback_origin_store(&cache,1,101,1176,.154);
    assert(playback_origin(&cache,1,101,1176,60,0)==.154);puts("PASS: restart sample counter cannot shift the cue origin to the seek target");
    double offsets[]={0,playback_origin(&cache,1,101,1176,60,0)},durations[]={154,153.846},start,end;
    assert(playback_window(154,60,offsets,durations,2,&start,&end)&&fabs(start-.154)<.00001);puts("PASS: forward seek keeps the original backward-seek range available");
    for(uint32_t id=102;id<202;id++){playback_origin_store(&cache,1,id,1176,.154);assert(playback_origin(&cache,1,id,1176,5,0)==.154);}puts("PASS: one hundred repeated seeks do not accumulate timing offset");
    assert(playback_origin(&cache,1,999,1176,4,4)==0);puts("PASS: a game-created replacement playback does not inherit an old ID's origin");
    assert(playback_origin(&cache,1,201,1193,4,4)==0);puts("PASS: origin identity is scoped to the current song");
    playback_origins_clear(&cache);assert(playback_origin(&cache,1,201,1176,4,4)==0);puts("PASS: scene cleanup clears cached origins");
    int all[14]={1001,1002,1003,1004,1005,1006},vocal[]={1001,1002,1003};
    assert(routing_prefix(all,14,vocal,3));puts("PASS: three main vocal slots can map to a larger fourteen-person formation");
    all[1]=1001;vocal[1]=1001;assert(routing_prefix(all,14,vocal,3));puts("PASS: duplicate characters retain their ordered vocal slot identity");
    all[0]=1002;assert(!routing_prefix(all,14,vocal,3));puts("PASS: reordered or ambiguous vocal correspondence is left unchanged");
    assert(!routing_prefix(all,2,vocal,3)&&!routing_prefix(all,14,vocal,0)&&!routing_prefix(all,257,vocal,3));puts("PASS: mismatched and out-of-bounds formation sizes are rejected");
    return 0;
}
