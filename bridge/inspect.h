/* Development-only metadata inspection. No process writes, hooks or network.
   A changed request file selects read-only field paths; response stays in workspace.
   Compiled only with UMAPRESENCE_INSPECT; absent in a normal build. */
#ifdef UMAPRESENCE_INSPECT
IMPORT Ptr __stdcall CreateFileW(const u16*,u32,u32,Ptr,u32,u32,Ptr);
IMPORT i32 __stdcall ReadFile(Ptr,Ptr,u32,u32*,Ptr);
IMPORT i32 __stdcall WriteFile(Ptr,const void*,u32,u32*,Ptr);
IMPORT u32 __stdcall GetEnvironmentVariableW(const u16*,u16*,u32);
static usize (*inspect_class_count)(Ptr);
static Ptr (*inspect_image_class)(Ptr,usize);
static const char*(*inspect_namespace)(Ptr);
static _Bool (*inspect_valuetype)(Ptr);
static Ptr (*inspect_element_class)(Ptr);
static i32 inspect_reference(Ptr type){i32 code=type_code(type);if(code==0x12||code==0x1c||code==0x1d)return 1;if(code!=0x15)return 0;Ptr c=class_from_type?class_from_type(type):0;return c&&inspect_valuetype&&!inspect_valuetype(c);}
static char inspect_request[8192],inspect_previous[8192],inspect_output[262144];
static i32 inspect_length,inspect_disabled;static u64 inspect_last;
static i32 inspect_value,inspect_have_value;
static char inspect_score_path[1024],inspect_skills_path[1024];
static void inspect_copy(char*out,const char*in){i32 n=0;while(in[n]&&n<1022){out[n]=in[n];n++;}out[n]=0;}
static Ptr inspect_getter(Ptr klass,const char*name){for(i32 n=0;klass&&n<6;n++,klass=class_parent?class_parent(klass):0){Ptr m=class_method(klass,name,0);if(m)return m;}return 0;}
static void inspect_put(const char*s){if(s)while(*s&&inspect_length<262140)inspect_output[inspect_length++]=*s++;}
static void inspect_num(i32 n){char b[16];i32 k=0;if(n<0){inspect_put("-");n=-n;}do{b[k++]=(char)('0'+n%10);n/=10;}while(n&&k<15);while(k){char c[2]={b[--k],0};inspect_put(c);}}
static void inspect_string(Ptr value){
    i32 count=value?string_length(value):0;u16*s=value?string_chars(value):0;if(!s||count<0||count>256){inspect_put("[unavailable]");return;}
    for(i32 i=0;i<count;i++){u32 c=s[i];if(c>=0xd800&&c<=0xdbff&&i+1<count&&s[i+1]>=0xdc00&&s[i+1]<=0xdfff)c=0x10000+((c-0xd800)<<10)+(s[++i]-0xdc00);
        char b[5]={0};if(c<32)c=' ';if(c<128)b[0]=(char)c;else if(c<2048){b[0]=(char)(0xc0|(c>>6));b[1]=(char)(0x80|(c&63));}else if(c<65536){b[0]=(char)(0xe0|(c>>12));b[1]=(char)(0x80|((c>>6)&63));b[2]=(char)(0x80|(c&63));}else{b[0]=(char)(0xf0|(c>>18));b[1]=(char)(0x80|((c>>12)&63));b[2]=(char)(0x80|((c>>6)&63));b[3]=(char)(0x80|(c&63));}inspect_put(b);
    }
}
/* Fixed six-decimal diagnostic output; no CRT dependency and no non-finite values. */
static void inspect_decimal(double value){
    if(!(value>=-1000000000.0&&value<=1000000000.0)){inspect_put("INVALID NUMBER");return;}
    if(value<0){inspect_put("-");value=-value;}
    u64 scaled=(u64)(value*1000000.0+0.5);inspect_num((i32)(scaled/1000000));inspect_put(".");
    u32 fraction=(u32)(scaled%1000000);for(u32 divisor=100000;divisor;divisor/=10){char digit[2]={(char)('0'+fraction/divisor),0};inspect_put(digit);fraction%=divisor;}
}
static void inspect_type(Ptr type){Ptr c=class_from_type?class_from_type(type):0;inspect_put(c&&class_name?class_name(c):"?");}
static Ptr inspect_field(Ptr object,const char*name){
    char wanted[64];normalized(name,wanted);Ptr c=object_class(object);
    for(i32 level=0;c&&level<6;level++,c=class_parent?class_parent(c):0){Ptr it=0,f;while((f=class_fields(c,&it))){char key[64];normalized(field_name(f),key);if(same(key,wanted)&&(!field_flags||!(field_flags(f)&0x10)))return f;}}return 0;
}
static Ptr inspect_index(Ptr object,const char*part){
    u32 index=0;const char*p=part+1;if(*p<'0'||*p>'9')return 0;
    for(;*p>='0'&&*p<='9';p++){u32 digit=(u32)(*p-'0');if(index>(262143-digit)/10)return 0;index=index*10+digit;}
    if(*p!=']'||p[1])return 0;
    Ptr klass=object_class(object),element=inspect_element_class?inspect_element_class(klass):0;
    if(element){
        /* Direct, bounded reference-array access; never interpret primitive data as pointers. */
        if(!inspect_valuetype||inspect_valuetype(element)||!array_length||!array_header_size||!array_element_size||array_element_size(klass)!=8)return 0;
        u32 count=array_length(object),header=array_header_size();if(count>262144||index>=count||header<24||header>128)return 0;
        Ptr child=0;memcpy(&child,(unsigned char*)object+header+(usize)index*8,8);return child;
    }
    Ptr entries[64];const char*name=class_name(klass);i32 count=race_collection(object,race_contains(name,"[]")?0x1d:0x12,entries);return index<(u32)count?entries[index]:0;
}
static void inspect_schema(Ptr object,Ptr klass){
    if(!klass){inspect_put("NULL\n");return;}
    inspect_put("CLASS ");inspect_put(inspect_namespace?inspect_namespace(klass):"");inspect_put(".");inspect_put(class_name(klass));inspect_put("\n");
    Ptr c=klass;for(i32 level=0;c&&level<6;level++,c=class_parent?class_parent(c):0){Ptr it=0,f;i32 budget=512;
        while(budget--&&(f=class_fields(c,&it))){Ptr t=field_type(f);i32 code=type_code(t);inspect_put("FIELD ");inspect_put(field_name(f));inspect_put(" : ");inspect_type(t);
            if(field_flags&&(field_flags(f)&0x10)){inspect_put(" [static]");if(race_class_is_enum&&race_class_is_enum(c)&&race_static_value&&(field_flags(f)&0x50)==0x50){i32 value=0;race_static_value(f,&value);inspect_put(" = ");inspect_num(value);}}
            else if(object){if(code==8||code==9){i32 n=0;field_value(object,f,&n);inspect_put(" = ");inspect_num(n);}
                else if(code==2){unsigned char b=0;field_value(object,f,&b);inspect_put(b?" = true":" = false");}
                else if(code==12||code==13){double value=0;if(code==12){float single=0;field_value(object,f,&single);value=single;}else field_value(object,f,&value);inspect_put(" = ");inspect_decimal(value);}
                else if(inspect_reference(t)){Ptr child=0;field_value(object,f,&child);inspect_put(child?" -> ":" = null");if(child)inspect_put(class_name(object_class(child)));}
            }inspect_put("\n");
        }
        it=0;Ptr m;budget=1024;while(class_methods&&budget--&&(m=class_methods(c,&it))){inspect_put("METHOD ");inspect_put(method_name(m));inspect_put(" (");inspect_num((i32)method_params(m));inspect_put(") : ");inspect_type(method_return(m));if(method_param&&method_params(m)>0&&method_params(m)<=8){inspect_put(" PARAMS ");for(u32 j=0;j<method_params(m);j++){if(j)inspect_put(", ");inspect_type(method_param(m,j));}}inspect_put("\n");}
    }
}
/* Read-only locator for a fixed set of finish-display functions. No method is
   invoked, no machine code or object memory is exported, and no hooks are used.
   An RVA lets offline tools inspect the matching on-disk GameAssembly image. */
static void inspect_hex32(u32 value){
    char text[11]={'0','x'};for(i32 i=0;i<8;i++)text[i+2]="0123456789ABCDEF"[(value>>(28-i*4))&15];text[10]=0;inspect_put(text);
}
EXPORT u32 umapyon_inspect_code_rva(usize base,u32 image_size,usize address){
    if(!base||image_size<4096||address<base||address-base<4096||address-base>=(usize)image_size)return 0;
    return (u32)(address-base);
}
static void inspect_finish_code(void){
    unsigned char*module=(unsigned char*)GetModuleHandleW(u"GameAssembly.dll");
    if(!module){inspect_put("Finish code locator unavailable: GameAssembly.dll is not loaded\n");return;}
    u16 dos=0;memcpy(&dos,module,2);u32 pe=0;memcpy(&pe,module+0x3c,4);
    if(dos!=0x5a4d||pe<64||pe>1048576){inspect_put("Unsupported module header\n");return;}
    u32 signature=0,timestamp=0,size=0;u16 magic=0,sections=0,optional_size=0;
    memcpy(&signature,module+pe,4);memcpy(&timestamp,module+pe+8,4);memcpy(&sections,module+pe+6,2);memcpy(&optional_size,module+pe+20,2);
    memcpy(&magic,module+pe+24,2);memcpy(&size,module+pe+24+56,4);
    if(signature!=0x4550||magic!=0x20b||size<4096||sections<1||sections>96||optional_size<112||optional_size>4096||(u64)pe+24+optional_size+(u64)sections*40>size){inspect_put("Unsupported module image\n");return;}
    inspect_put("MODULE GameAssembly.dll\nTIMESTAMP ");inspect_hex32(timestamp);inspect_put("\nIMAGE_SIZE ");inspect_hex32(size);inspect_put("\n");
    Ptr image=get_image("umamusume"),klass=image?get_class(image,"Gallop","RaceUtil"):0;
    const char*names[]={"GetHorseLengthByOrder","GetHorseLengthString","GetHorseLengthAmericaString","GetHorseLengthFrenchString","InitFinishInfo"};
    i32 counts[]={1,2,1,1,5};
    if(!klass||!get_method){inspect_put("Finish methods unavailable\n");return;}
    for(i32 i=0;i<5;i++){
        Ptr metadata=class_method?class_method(klass,names[i],counts[i]):0;
        Ptr code=metadata?get_method(klass,names[i],counts[i]):0;
        u32 rva=umapyon_inspect_code_rva((usize)module,size,(usize)code);i32 executable=0;
        for(i32 s=0;rva&&s<sections;s++){
            unsigned char*section=module+pe+24+optional_size+s*40;u32 virtual_size=0,start=0,flags=0;
            memcpy(&virtual_size,section+8,4);memcpy(&start,section+12,4);memcpy(&flags,section+36,4);
            if((flags&0x20000000)&&rva>=start&&(u64)rva<(u64)start+virtual_size){executable=1;break;}
        }
        inspect_put("FINISH_METHOD ");inspect_put(names[i]);inspect_put(" (");inspect_num(counts[i]);inspect_put(") ");
        if(rva&&executable){inspect_put("RVA ");inspect_hex32(rva);}else inspect_put("UNAVAILABLE");
        inspect_put("\n");
    }
}
static void inspect_query(char*query){
    inspect_have_value=0;
    inspect_put("\nQUERY ");inspect_put(query);inspect_put("\n");Ptr image=get_image("umamusume");
    if(same(query,"!finish-code")){inspect_finish_code();return;}
    if(query[0]=='!'&&inspect_class_count&&inspect_image_class){usize n=inspect_class_count(image);for(usize j=0;j<n;j++){Ptr c=inspect_image_class(image,j);const char*name=c?class_name(c):0;if(name&&race_contains(name,query+1)){inspect_put("TYPE ");inspect_put(inspect_namespace?inspect_namespace(c):"");inspect_put(".");inspect_put(name);inspect_put("\n");}}return;}
    char*part=query;while(*part&&*part!='/')part++;char sep=*part;*part=0;
    Ptr object=0,klass=0;if(same(query,"career"))object=career_character;else if(same(query,"work"))object=career_work;
    else if(query[0]=='#'){klass=image?get_class(image,"Gallop",query+1):0;if(!klass&&image)klass=get_class(image,"",query+1);
        if(!klass&&image&&inspect_class_count&&inspect_image_class){Ptr found=0;usize count=inspect_class_count(image);for(usize i=0;i<count;i++){Ptr c=inspect_image_class(image,i);if(c&&same(class_name(c),query+1)){if(found){found=0;break;}found=c;}}klass=found;}}
    else {klass=image?get_class(image,"Gallop",query):0;object=klass?singleton(klass):0;}
    if(sep)*part++=sep;else part=0;
    while(object&&part&&*part){char*end=part;while(*end&&*end!='/')end++;sep=*end;*end=0;
        klass=0; /* An absent child must not fall back to the root singleton's schema. */
        if(part[0]=='@'){
            if(!(part[1]=='g'&&part[2]=='e'&&part[3]=='t'&&part[4]=='_')){inspect_put("Only get_ property reads allowed\n");return;}
            Ptr m=inspect_getter(object_class(object),part+1);if(!m){inspect_put("Getter missing\n");return;}i32 type=type_code(method_return(m));
            Ptr ex=0,value=runtime_invoke(m,object,0,&ex);if(ex){inspect_put("Getter returned exception\n");return;}
            if(type==14){inspect_put("TEXT ");inspect_string(value);inspect_put("\n");return;}
            if(type==12||type==13){Ptr raw=value?object_unbox(value):0;if(!raw){inspect_put("No numeric value\n");return;}double number=0;if(type==12){float single=0;memcpy(&single,raw,4);number=single;}else memcpy(&number,raw,8);inspect_put("DECIMAL ");inspect_decimal(number);inspect_put("\n");return;}
            if(type==2||type==8||type==9||type==0x11){i32 n=0,ok=0;if(value){Ptr c=object_class(value);if(type==0x11&&!(race_class_is_enum&&race_class_is_enum(c)))ok=career_unwrap(value,&n);else{Ptr raw=object_unbox(value);if(raw){memcpy(&n,raw,type==2?1:4);ok=1;}}}if(ok){inspect_value=n;inspect_have_value=1;inspect_put("VALUE ");inspect_num(n);inspect_put("\n");}else inspect_put("Not a supported integer\n");return;}
            if(!inspect_reference(method_return(m))){inspect_put("Not a reference getter\n");return;}object=value;
        }else if(part[0]=='['){object=inspect_index(object,part);
        }else {Ptr f=inspect_field(object,part);if(!f){inspect_put("Field missing: ");inspect_put(part);inspect_put("\n");return;}i32 code=type_code(field_type(f));
            if(code==8||code==9){i32 n=0;field_value(object,f,&n);inspect_value=n;inspect_have_value=1;inspect_put("VALUE ");inspect_num(n);inspect_put("\n");return;}
            if(code==12||code==13){double number=0;if(code==12){float single=0;field_value(object,f,&single);number=single;}else field_value(object,f,&number);inspect_put("DECIMAL ");inspect_decimal(number);inspect_put("\n");return;}
            if(!inspect_reference(field_type(f))){inspect_put("Not a reference: ");inspect_type(field_type(f));inspect_put("\n");return;}Ptr next=0;field_value(object,f,&next);object=next;
        }part=sep?end+1:0;
    }if(object){i32 n=career_result_label(object);if(n>0){inspect_value=n;inspect_have_value=1;inspect_put("NUMERIC LABEL ");inspect_num(n);inspect_put("\n");}}inspect_schema(object,object?object_class(object):klass);
}
static void inspect_bindings(void){
    if(!career.active||!career_result_finished(career_work))return;
    i32 length=inspect_length;
    if(inspect_score_path[0]){char path[1024];inspect_copy(path,inspect_score_path);inspect_query(path);if(inspect_have_value&&inspect_value>0&&inspect_value<=999999){career_result.score=inspect_value;career_result.flags|=1;career_result.active=1;}}
    if(inspect_skills_path[0]){char path[1024];inspect_copy(path,inspect_skills_path);inspect_query(path);if(inspect_have_value&&inspect_value>=0&&inspect_value<=512){career_result.skills=inspect_value;career_result.flags|=2;}}
    inspect_length=length;
}
static void sample_inspect(void){
    if(inspect_disabled)return;
    __try {inspect_bindings();}__except(1){inspect_score_path[0]=inspect_skills_path[0]=0;}
    if(GetTickCount64()-inspect_last<1000)return;inspect_last=GetTickCount64();
    static const u16 request[]=u"bridge-inspect.request";
    static const u16 response[]=u"bridge-inspect.response";
    static const u16 test_request[]=u"bridge-inspect-test.request";
    static const u16 test_response[]=u"bridge-inspect-test.response";
    u16 test[8];i32 testing=GetEnvironmentVariableW(u"UMAPRESENCE_TEST_INSPECT",test,8)>0;
    Ptr f=CreateFileW(testing?test_request:request,0x80000000,7,0,3,0x80,0);if(f==(Ptr)(usize)-1)return;u32 size=0;ReadFile(f,inspect_request,8190,&size,0);CloseHandle(f);inspect_request[size]=0;
    if(same(inspect_request,inspect_previous))return;memcpy(inspect_previous,inspect_request,size+1);inspect_length=0;
    __try {char*p=inspect_request;while(*p){char*end=p;while(*end&&*end!='\n'&&*end!='\r')end++;char sep=*end;*end=0;
        if(p[0]=='s'&&p[1]=='c'&&p[2]=='o'&&p[3]=='r'&&p[4]=='e'&&p[5]=='='){inspect_copy(inspect_score_path,p+6);inspect_put("Score source configured\n");}
        else if(p[0]=='s'&&p[1]=='k'&&p[2]=='i'&&p[3]=='l'&&p[4]=='l'&&p[5]=='s'&&p[6]=='='){inspect_copy(inspect_skills_path,p+7);inspect_put("Skill count source configured\n");}
        else if(*p&&*p!=';')inspect_query(p);if(!sep)break;p=end+1;while(*p=='\n'||*p=='\r')p++;}}
    __except(1){inspect_put("\nInspection stopped on incompatible metadata; normal telemetry unchanged.\n");}
    f=CreateFileW(testing?test_response:response,0x40000000,7,0,2,0x80,0);if(f!=(Ptr)(usize)-1){u32 written=0;WriteFile(f,inspect_output,(u32)inspect_length,&written,0);CloseHandle(f);}
}
#else
static void sample_inspect(void){}
#endif
