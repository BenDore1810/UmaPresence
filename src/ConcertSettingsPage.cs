using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace UmapyonPresence
{
    internal sealed class ConcertAudioChoice
    {
        internal string Key, Label;
        public override string ToString() { return Label; }
        internal static readonly ConcertAudioChoice[] All = {
            new ConcertAudioChoice { Key="Original", Label="Game's original audio" },
            new ConcertAudioChoice { Key="Balanced", Label="Balance duplicate voices" },
            new ConcertAudioChoice { Key="Lead", Label="Highlight selected singers" },
            new ConcertAudioChoice { Key="Solo", Label="Solo arrangement" },
            new ConcertAudioChoice { Key="Duet", Label="Duet arrangement" }
        };
        internal static string Name(string key) { var item=All.FirstOrDefault(value=>value.Key==key);return item==null?key:item.Label; }
    }
    internal sealed class ConcertSettingsPage : UserControl
    {
        private readonly Settings settings;
        private readonly Func<int> pid, song;
        private readonly Func<string> configuration;
        private readonly Label status=SettingsUi.Text(""), audioState=SettingsUi.Text(""), recordings=SettingsUi.Text("");
        private readonly Timer refresh=new Timer {Interval=1500};
        private readonly ComboBox voice1,voice2;
        private HashSet<int> supported;
        private int listedSong=-1;
        private sealed class Voice { internal int Id;internal string Name;public override string ToString(){return Name;} }
        private static int Singer(ComboBox box) {return box.SelectedItem is Voice?((Voice)box.SelectedItem).Id:0;}
        private static void FillVoices(ComboBox box,int selected,HashSet<int> supported,string zero)
        {
            box.BeginUpdate();box.Items.Clear();box.Items.Add(new Voice {Name=zero});
            foreach(var entry in CharacterCatalog.Entries.OrderBy(item=>item.Name)) {int id;if(int.TryParse(entry.Id,out id)&&id>0&&id<2000&&(supported==null||supported.Contains(id)))box.Items.Add(new Voice {Id=id,Name=entry.Name});}
            box.SelectedItem=box.Items.Cast<Voice>().FirstOrDefault(item=>item.Id==selected);
            if(box.SelectedItem==null) {var entry=CharacterCatalog.Entries.FirstOrDefault(item=>item.Id==selected.ToString());var value=new Voice {Id=selected,Name=(entry==null?"Character "+selected:entry.Name)+" (no cached recording for this song)"};box.Items.Add(value);box.SelectedItem=value;}
            box.EndUpdate();
        }
        internal ConcertSettingsPage(Settings settings,Func<int> pid,Action changed,Action<string> saved,Func<int> song=null,Func<string> configuration=null)
        {
            this.settings=settings;this.pid=pid;this.song=song??(()=>0);this.configuration=configuration??(()=>"Waiting for the game.");Dock=DockStyle.Fill;AutoScaleMode=AutoScaleMode.None;
            var page=SettingsUi.Page("Concert","Standalone concert controls. When using Hachimi, use its own concert settings.");page.Controls.Add(status);
            page.Controls.Add(SettingsUi.Section("Playback controls"));
            var media=SettingsUi.Check("Control the concert from Windows",settings.StandaloneWindowsMediaControls);page.Controls.Add(media);
            page.Controls.Add(SettingsUi.Text("Adds play/pause to Windows media controls. In Hachimi mode, use Hachimi's own controls."));
            page.Controls.Add(SettingsUi.Text("Show the concert seek bar"));
            var seek=SettingsUi.Choices(new[]{"Only while paused","Always during a concert","Off"},settings.StandaloneSeekBarMode=="Always"?"Always during a concert":settings.StandaloneSeekBarMode=="Off"?"Off":"Only while paused");page.Controls.Add(seek);
            page.Controls.Add(SettingsUi.Text("The bar shows progress while the concert is in focus. Drag the handle when seeking is available."));
            page.Controls.Add(SettingsUi.Section("Cast"));
            var duplicates=SettingsUi.Check("Use the same Uma in multiple slots",settings.ConcertAllowDuplicates);page.Controls.Add(duplicates);
            page.Controls.Add(SettingsUi.Text("Choose duplicates on the formation screen, then open the concert."));
            if (!ReleasePolicy.ExperimentalAudio) {
                page.Controls.Add(SettingsUi.Section("Concert audio"));
                page.Controls.Add(SettingsUi.Text("This prerelease uses the game's original audio arrangement."));
                voice1 = null; voice2 = null;
                Controls.Add(SettingsUi.StickySave(page, SettingsUi.Button("Save changes", () => {
                    bool previousMedia = settings.StandaloneWindowsMediaControls;
                    bool previousDuplicates = settings.ConcertAllowDuplicates;
                    string previousSeek = settings.StandaloneSeekBarMode;
                    settings.StandaloneWindowsMediaControls = media.Checked;
                    settings.StandaloneSeekBarMode = seek.SelectedIndex == 1 ? "Always" : seek.SelectedIndex == 2 ? "Off" : "Paused";
                    settings.ConcertAllowDuplicates = duplicates.Checked;
                    try { settings.Save(); }
                    catch (Exception error) {
                        settings.StandaloneWindowsMediaControls = previousMedia;
                        settings.ConcertAllowDuplicates = previousDuplicates;
                        settings.StandaloneSeekBarMode = previousSeek;
                        MessageBox.Show(this, error.Message, "Could not save concert settings"); return;
                    }
                    changed();
                    saved(previousDuplicates == settings.ConcertAllowDuplicates
                        ? "Saved. Playback controls update during this session when available."
                        : "Saved. Duplicate casting applies on the formation screen when Standalone supports it.");
                })));
                refresh.Tick += (sender, args) => { if (Visible) RefreshStatus(); };
                refresh.Start(); RefreshStatus(); return;
            }
            page.Controls.Add(SettingsUi.Section("Concert audio"));page.Controls.Add(audioState);
            page.Controls.Add(SettingsUi.Text("These audio features are experimental. Saving a mode does not prove it is working; the next game session will check it."));
            var advanced=new DesktopStack(true) {Visible=false,Tag="hidden"};
            page.Controls.Add(SettingsUi.Button("Advanced audio · Experimental",()=>{advanced.Visible=!advanced.Visible;advanced.Tag=advanced.Visible?null:"hidden";advanced.Parent.PerformLayout();}));page.Controls.Add(advanced);
            advanced.Controls.Add(SettingsUi.Text("Audio mode"));
            var audio=new ComboBox {Font=DesktopDesign.Body(),Name="ConcertAudioMode",DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,Width=300};audio.Items.AddRange(ConcertAudioChoice.All);audio.SelectedItem=ConcertAudioChoice.All.First(item=>item.Key==settings.ConcertAudioMode);advanced.Controls.Add(audio);
            var explanation=SettingsUi.Text("");advanced.Controls.Add(explanation);advanced.Controls.Add(recordings);
            voice1=new ComboBox {Font=DesktopDesign.Body(),DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,Width=300};voice2=new ComboBox {Font=DesktopDesign.Body(),DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,Width=300};
            FillVoices(voice1,settings.ConcertVoice1,null,"Automatic: first singing voice");FillVoices(voice2,settings.ConcertVoice2,null,"No second lead");
            Label firstLabel=SettingsUi.Text("Main singer"),secondLabel=SettingsUi.Text("Second singer");advanced.Controls.Add(firstLabel);advanced.Controls.Add(voice1);advanced.Controls.Add(secondLabel);advanced.Controls.Add(voice2);
            Action describe=()=>{
                string mode=((ConcertAudioChoice)audio.SelectedItem).Key;
                explanation.Text=mode=="Original"?"Keep the game's normal sound. No vocal gain or singer changes are requested.":mode=="Balanced"?"Prevent identical duplicate vocals from overpowering the music. Distinct singing parts are preserved.":mode=="Lead"?"Keep your selected singer(s) at normal volume and lower other vocals. Automatic uses the first active singing voice.":"Assign available singing parts to the selected voice(s). Load the concert again after saving. Parts without compatible recordings keep their original singer.";
                bool one=mode=="Lead"||mode=="Solo"||mode=="Duet",two=mode=="Lead"||mode=="Duet";
                voice1.Visible=firstLabel.Visible=one;voice2.Visible=secondLabel.Visible=two;
                voice1.Tag=firstLabel.Tag=one?null:"hidden";voice2.Tag=secondLabel.Tag=two?null:"hidden";
                firstLabel.Parent?.PerformLayout();
            };audio.SelectedIndexChanged+=(s,e)=>describe();describe();
            var save=SettingsUi.Button("Save changes",()=>{
                try {
                    string mode=((ConcertAudioChoice)audio.SelectedItem).Key;int first=Singer(voice1),second=Singer(voice2);
                    if((mode=="Solo"||mode=="Duet")&&first==0)throw new InvalidOperationException("Choose the main singer for this arrangement.");
                    if(mode=="Duet"&&(second==0||first==second))throw new InvalidOperationException("Choose two different singers for Duet.");
                    if((mode=="Solo"||mode=="Duet")&&supported!=null&&(!supported.Contains(first)||(mode=="Duet"&&!supported.Contains(second))))throw new InvalidOperationException("A selected singer has no cached recording for this song. Choose a listed singer or keep the game's original audio.");
                    var before=Json.Decode<Settings>(Json.Encode(settings));
                    settings.StandaloneWindowsMediaControls=media.Checked;settings.StandaloneSeekBarMode=seek.SelectedIndex==1?"Always":seek.SelectedIndex==2?"Off":"Paused";settings.ConcertAllowDuplicates=duplicates.Checked;settings.ConcertAudioMode=mode;settings.ConcertVoice1=first;settings.ConcertVoice2=second;
                    try{settings.Save();}catch{settings.StandaloneWindowsMediaControls=before.StandaloneWindowsMediaControls;settings.StandaloneSeekBarMode=before.StandaloneSeekBarMode;settings.ConcertAllowDuplicates=before.ConcertAllowDuplicates;settings.ConcertAudioMode=before.ConcertAudioMode;settings.ConcertVoice1=before.ConcertVoice1;settings.ConcertVoice2=before.ConcertVoice2;throw;}
                    changed();saved(SettingsUi.ConcertEffect(before,settings)+" Live verification is still pending.");RefreshStatus();
                }catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not save concert settings",MessageBoxButtons.OK,MessageBoxIcon.Error);}
            });Controls.Add(SettingsUi.StickySave(page,save));
            refresh.Tick+=(s,e)=>{if(Visible&&FindForm()!=null&&FindForm().Visible)RefreshStatus();};refresh.Start();VisibleChanged+=(s,e)=>refresh.Enabled=Visible;RefreshStatus();
        }
        private void RefreshStatus()
        {
            var runtime=StandaloneRuntime.Read(pid());string value;
            if(runtime==null||!runtime.Ready)value="Game controls are not connected. You can save preferences now. Their live behavior will be checked when Standalone is running.";
            else value=(!ReleasePolicy.ExperimentalAudio ? "Standalone connected. Original concert audio. " : "Ready for live checks. ")
                + "Duplicate casting: " + ((runtime.Capabilities & RuntimeCapability.Formation) != 0 ? "available" : "unavailable")
                + ". Seeking: " + (runtime.CanSeek ? "available" : "not ready for this song") + ". " + configuration();
            if(status.Text!=value)status.Text=value;
            if (!ReleasePolicy.ExperimentalAudio) return;
            audioState.Text="Saved audio mode: "+ConcertAudioChoice.Name(settings.ConcertAudioMode)+(settings.ConcertAudioMode=="Original"?"":" · Experimental");
            int current=this.song();
            if(current!=listedSong&&!voice1.DroppedDown&&!voice2.DroppedDown){
                int first=Singer(voice1),second=Singer(voice2);supported=ConcertRecordingCatalog.Available(settings,current);listedSong=current;
                FillVoices(voice1,first,supported,"Automatic: first singing voice");FillVoices(voice2,second,supported,"No second lead");
                recordings.Text=current<=0?"Open a concert to check which singers have cached recordings for that song.":supported==null?"Recording coverage could not be checked. The game will retain unsupported parts.":supported.Count+" singers have cached recordings for this song. Exact part compatibility is checked by the runtime; this is not yet a live audio pass.";
            }
        }
        protected override void Dispose(bool disposing){if(disposing)refresh.Dispose();base.Dispose(disposing);}
    }
}
