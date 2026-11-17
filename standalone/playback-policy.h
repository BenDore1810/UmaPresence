/* Seek within the overlap of the active cues on the Director's timeline.
   The current position must also be covered so a failed seek can restore it. */
#pragma once
#include <math.h>
static int playback_window(double total,double current,const double*offsets,const double*durations,int count,double*minimum,double*maximum){
    if(!isfinite(total)||total<=0||total>7200||!isfinite(current)||count<1||count>65)return 0;
    double start=0,end=total;
    for(int i=0;i<count;i++){
        double finish=offsets[i]+durations[i];
        if(!isfinite(offsets[i])||!isfinite(durations[i])||durations[i]<=0||durations[i]>7200||!isfinite(finish))return 0;
        if(offsets[i]>start)start=offsets[i];if(finish<end)end=finish;
    }
    if(start>=end||current<start||current>=end)return 0;
    *minimum=start;*maximum=end;return 1;
}
