#pragma once
#include <stdint.h>
/* Hachimi changes matching front slots in both arrays. Allow a larger full
   formation only when the vocal prefix matches exactly; never guess a mapping. */
static int routing_prefix(const int*all,uint32_t full_count,const int*vocals,uint32_t vocal_count){
    if(!all||!vocals||!vocal_count||vocal_count>256||full_count<vocal_count||full_count>256)return 0;
    for(uint32_t i=0;i<vocal_count;i++)if(all[i]!=vocals[i])return 0;return 1;
}
