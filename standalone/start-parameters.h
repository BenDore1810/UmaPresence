/* Only restore settings we actually observed from the game. Unknown is not 0. */
#pragma once
#include <stdint.h>
typedef struct { uintptr_t player; uint32_t weak; int64_t value; int micro,known; } StartParameter;
typedef struct { StartParameter rows[128]; } StartParameters;
static StartParameter* start_parameter_find(StartParameters* state,uintptr_t player){
    for(int i=0;i<128;i++)if(state->rows[i].player==player)return &state->rows[i];return 0;
}
static StartParameter* start_parameter_empty(StartParameters* state){
    for(int i=0;i<128;i++)if(!state->rows[i].player)return &state->rows[i];return 0;
}
