#include "../playback-policy.h"
#include <assert.h>
#include <stdio.h>
int main(void){
    double offsets[]={0,0,0},lengths[]={154,153.846,153.846},start=0,end=0;
    assert(playback_window(154,12,offsets,lengths,3,&start,&end)&&start==0&&fabs(end-153.846)<.00001);
    puts("PASS: VOLTAGE video tail padding does not disable seeking");
    offsets[1]=.154;offsets[2]=.154;
    assert(playback_window(154,12,offsets,lengths,3,&start,&end)&&fabs(start-.154)<.00001&&fabs(end-154)<.00001);
    puts("PASS: native cue offsets are preserved in the seek interval");
    lengths[1]=20;
    assert(playback_window(154,12,offsets,lengths,3,&start,&end)&&fabs(start-.154)<.00001&&fabs(end-20.154)<.00001);
    assert(!playback_window(154,30,offsets,lengths,3,&start,&end));
    assert(!playback_window(154,0,offsets,lengths,3,&start,&end));
    puts("PASS: shorter cues allow seeking within their overlap only while the current position is recoverable");
    offsets[2]=30;
    assert(!playback_window(154,12,offsets,lengths,3,&start,&end));
    offsets[1]=NAN;
    assert(!playback_window(154,12,offsets,lengths,3,&start,&end));
    assert(!playback_window(154,12,offsets,lengths,0,&start,&end));
    puts("PASS: disjoint, unknown and empty cue ranges reject seeking");
    return 0;
}
