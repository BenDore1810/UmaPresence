/* Pure gain policy. Identity means a certified single-wave cue, never just a
   character. Zero identity leaves the track untouched by balancing. */
#pragma once
#include <stdint.h>
#include <math.h>
typedef struct { uintptr_t source;int cue,voice;float offset,native;uint64_t routing; } PolicyVoice;
static uint64_t audio_selector_identity(const uint64_t*input_names,const uint64_t*input_labels,int count){
    if(count<0||count>8)return 0;uint64_t hash=1469598103934665603ULL,names[8],labels[8];
    for(int i=0;i<count;i++){names[i]=input_names[i];labels[i]=input_labels[i];}
    for(int i=0;i<count;i++)for(int j=i+1;j<count;j++)if(names[j]<names[i]){uint64_t n=names[i],l=labels[i];names[i]=names[j];labels[i]=labels[j];names[j]=n;labels[j]=l;}
    for(int i=0;i<count;i++)for(int j=0;j<8;j++){hash^=(names[i]>>(j*8))&255;hash*=1099511628211ULL;hash^=(labels[i]>>(j*8))&255;hash*=1099511628211ULL;}return hash;
}
static float audio_factor(const PolicyVoice*v,int count,int index,int balanced,int lead,int first,int second){
    if(index<0||index>=count||!isfinite(v[index].native)||v[index].native<0)return 1;
    float factor=1;
    if(lead&&first>0&&v[index].voice>0&&v[index].voice!=first&&v[index].voice!=second)factor=0.5011872336f;
    if(balanced&&v[index].source){
        float sum=0,strongest=0;
        if(!isfinite(v[index].offset)||fabsf(v[index].offset)>7200)return factor;
        // Quantized timeline identity is transitive: pairwise tolerance would
        // produce inconsistent groups for three staggered copies.
        int64_t alignment=(int64_t)llroundf(v[index].offset*500);
        for(int i=0;i<count;i++)if(v[i].source==v[index].source&&v[i].cue==v[index].cue&&v[i].routing==v[index].routing&&isfinite(v[i].offset)&&fabsf(v[i].offset)<=7200&&(int64_t)llroundf(v[i].offset*500)==alignment){
            float gain=v[i].native;if(!isfinite(gain)||gain<0)return factor;
            sum+=gain;if(gain>strongest)strongest=gain;
        }
        if(sum>0)factor*=strongest/sum;
    }
    return factor;
}
