/* Native menu identity. Current controller names are independent of text,
   translations, window focus and resolution. English labels live in C#. */
typedef struct {u32 magic;i32 active;char root[96],view[96];} MenuPacket;
typedef char menu_size_is_200[(sizeof(MenuPacket)==200)?1:-1];
static MenuPacket menu_packet;
static Ptr menu_manager_class;
static i32 menu_disabled;
static void menu_name(char*out,Ptr object){const char*name=object&&class_name?class_name(object_class(object)):0;i32 n=0;if(name)while(name[n]&&n<95){out[n]=name[n];n++;}out[n]=0;}
static void sample_menu(void){
    memset(&menu_packet,0,sizeof(menu_packet));menu_packet.magic=0x4d454e31;
    if(menu_disabled||!menu_manager_class)return;
    __try {
        Ptr manager=singleton(menu_manager_class);
        if(!manager||career_legacy_flag(manager,"isrunchangeview"))return;
        Ptr controller=career_legacy_member(manager,"currentviewcontroller");if(!controller)return;
        menu_name(menu_packet.root,controller);
        for(i32 n=0;n<3;n++){Ptr child=career_legacy_member(controller,"childcurrentcontroller");if(!child||child==controller)break;controller=child;}
        menu_name(menu_packet.view,controller);
        if(same(menu_packet.view,"HomeHubViewController"))return;
        menu_packet.active=menu_packet.root[0]&&menu_packet.view[0];
    }__except(1){menu_disabled=1;menu_packet.active=0;report("Native menu reader disabled after incompatible metadata.");}
}
