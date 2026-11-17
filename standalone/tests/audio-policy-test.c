#include "../audio-policy.h"
#include <assert.h>
#include <stdio.h>
int main(void){
    uint64_t names[]={10,20},labels[]={30,40},reversed_names[]={20,10},reversed_labels[]={40,30},swapped_labels[]={40,30};
    assert(audio_selector_identity(names,labels,2)==audio_selector_identity(reversed_names,reversed_labels,2));puts("PASS: selector identity is independent of native setter call order");
    assert(audio_selector_identity(names,labels,2)!=audio_selector_identity(names,swapped_labels,2));puts("PASS: swapping labels between selectors changes vocal part identity");
    PolicyVoice v[12];for(int i=0;i<12;i++)v[i]=(PolicyVoice){1,0,1001,0,1};
    float sum=0;for(int i=0;i<12;i++)sum+=v[i].native*audio_factor(v,12,i,1,0,0,0);
    assert(fabsf(sum-1)<.00001f);puts("PASS: twelve aligned identical vocals sum to one native copy");
    v[1].native=.5f;assert(fabsf(audio_factor(v,2,0,1,0,0,0)-2.f/3)<.00001f);puts("PASS: unequal native gains retain their relative balance");
    v[1].cue=1;assert(audio_factor(v,2,0,1,0,0,0)==1);puts("PASS: different vocal parts sharing a character remain distinct");
    v[1].cue=0;v[1].routing=1;assert(audio_factor(v,2,0,1,0,0,0)==1);v[1].routing=0;puts("PASS: different selector labels keep multitrack cue parts distinct");
    v[1].cue=0;v[1].offset=.2f;assert(audio_factor(v,2,0,1,0,0,0)==1);puts("PASS: different cue timing remains distinct");
    v[0].source=0;assert(audio_factor(v,2,0,1,0,0,0)==1);puts("PASS: uncertified cue identity is never normalized");
    v[0].voice=1002;assert(fabsf(audio_factor(v,2,0,0,1,1001,0)-.50118723f)<.00001f);puts("PASS: nonlead attenuation is minus six decibels");
    assert(audio_factor(v,2,0,0,1,1002,0)==1);assert(audio_factor(v,2,0,0,1,1001,1002)==1);puts("PASS: selected one or two leads retain native gain");
    assert(audio_factor(v,2,0,0,1,0,0)==1);puts("PASS: missing lead selection never attenuates every singer");
    v[0].native=0;assert(audio_factor(v,1,0,1,0,0,0)==1);puts("PASS: native silence and fades are preserved");
    v[0].source=1;v[0].native=1;v[1].native=1;v[1].offset=0;float factor=audio_factor(v,2,0,1,0,0,0);
    for(int i=0;i<10000;i++)assert(audio_factor(v,2,0,1,0,0,0)==factor);puts("PASS: repeated policy evaluation cannot accumulate gain changes");
    return 0;
}
