using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;
using Windows.Media;

namespace UmapyonPresence
{
    [ComImport, Guid("DDB0472D-C911-4A1F-86D9-DC3D71A95F5A"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
    internal interface IMediaControlsInterop
    {
        [PreserveSig] int GetForWindow(IntPtr window, ref Guid iid, out IntPtr controls);
    }
    internal sealed class StandaloneMediaControls : IDisposable
    {
        private readonly Control dispatcher;
        private SystemMediaTransportControls controls;
        private Form mediaWindow;
        private int pid, song;
        private DateTime next;
        private bool busy;
        private sealed class Intent { internal int Pid, Song, Command; internal double Position; }
        private readonly LinkedList<Intent> pending = new LinkedList<Intent>();
        internal string Status { get; private set; } = "Inactive (Hachimi owns its own controls)";
        internal StandaloneMediaControls(Control dispatcher) { this.dispatcher = dispatcher; }
        internal static string SelfTest()
        {
            using (var dispatch = new Control()) using (var media = new StandaloneMediaControls(dispatch)) {
                dispatch.CreateControl(); media.Create(); media.controls.IsEnabled = true; media.controls.PlaybackStatus = MediaPlaybackStatus.Paused;
                media.controls.DisplayUpdater.Type = MediaPlaybackType.Music; media.controls.DisplayUpdater.MusicProperties.Title = "UmaPresence verification"; media.controls.DisplayUpdater.Update();
                media.controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties { StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(60), Position = TimeSpan.FromSeconds(2), MinSeekTime = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromSeconds(60) });
                if (media.controls.DisplayUpdater.MusicProperties.Title != "UmaPresence verification") throw new InvalidOperationException("Windows media metadata did not round-trip.");
                return "Owned top-level-window SMTC activation, paused metadata and timeline update passed; game controls still need live acceptance.";
            }
        }
        private void Create()
        {
            var factory = (IMediaControlsInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(SystemMediaTransportControls));
            var iid = new Guid("99FA3FF4-1742-42A6-902E-087D41F965EC"); IntPtr pointer;
            mediaWindow = new Form { ShowInTaskbar = false, Text = "UmaPresence media session" };
            Marshal.ThrowExceptionForHR(factory.GetForWindow(mediaWindow.Handle, ref iid, out pointer));
            try { controls = (SystemMediaTransportControls)Marshal.GetObjectForIUnknown(pointer); } finally { Marshal.Release(pointer); }
            controls.ButtonPressed += OnButton;
            controls.PlaybackPositionChangeRequested += OnSeek;
            controls.IsPlayEnabled = true; controls.IsPauseEnabled = true;
            controls.IsNextEnabled = false; controls.IsPreviousEnabled = false; controls.IsStopEnabled = false;
        }
        private void OnButton(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            if (args.Button == SystemMediaTransportControlsButton.Play) Dispatch(1, 0);
            else if (args.Button == SystemMediaTransportControlsButton.Pause) Dispatch(2, 0);
        }
        private void OnSeek(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args) { Dispatch(3, args.RequestedPlaybackPosition.TotalSeconds); }
        private void Dispatch(int command, double position)
        {
            int expectedPid = pid, expectedSong = song;
            if (expectedPid < 1 || expectedSong < 1 || dispatcher.IsDisposed) return;
            dispatcher.BeginInvoke((Action)(async () => {
                if (pid != expectedPid || song != expectedSong) return;
                var intent = new Intent { Pid = expectedPid, Song = expectedSong, Command = command, Position = position };
                if (command == 3 && pending.Last != null && pending.Last.Value.Command == 3) pending.Last.Value = intent;
                else { if (pending.Count == 8) pending.RemoveFirst(); pending.AddLast(intent); }
                if (busy) return;
                busy = true;
                try { while (pending.Count > 0) {
                    var request = pending.First.Value; pending.RemoveFirst();
                    if (pid != request.Pid || song != request.Song) continue;
                    int result = await StandaloneRuntime.Send(request.Pid, request.Command, request.Song, request.Position); Status = result == 0 ? "Windows media control acknowledged" : "Windows media control rejected: " + result;
                } }
                catch (Exception ex) { Status = "Windows media control: " + ex.Message; }
                finally { busy = false; }
            }));
        }
        internal void Observe(Settings settings, int gamePid, ConcertSnapshot concert)
        {
            var runtime = StandaloneRuntime.Read(gamePid);
            if (!settings.StandaloneWindowsMediaControls || runtime == null || !runtime.Ready || (runtime.Capabilities & RuntimeCapability.Playback) == 0 || concert == null || !concert.Active) { Dispose(); Status = runtime != null && runtime.OwnershipConflict ? "Disabled: competing integration owns concert controls" : "Inactive: no verified standalone playback owner"; return; }
            pid = gamePid; song = concert.MusicId;
            if (DateTime.UtcNow < next) return; next = DateTime.UtcNow.AddSeconds(1);
            try {
                if (controls == null) Create();
                controls.IsEnabled = true; controls.PlaybackStatus = concert.Paused ? MediaPlaybackStatus.Paused : MediaPlaybackStatus.Playing;
                var updater = controls.DisplayUpdater; updater.Type = MediaPlaybackType.Music;
                updater.MusicProperties.Title = concert.Title; updater.MusicProperties.Artist = concert.Singers;
                updater.Update();
                double position = Math.Max(0, Math.Min(concert.Duration, concert.Position));
                bool seek = (runtime.Capabilities & RuntimeCapability.Seek) != 0;
                controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties { StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(concert.Duration), Position = TimeSpan.FromSeconds(position), MinSeekTime = TimeSpan.FromSeconds(seek ? runtime.SeekStart : position), MaxSeekTime = TimeSpan.FromSeconds(seek ? (runtime.SeekEnd > 0 ? Math.Min(runtime.SeekEnd, concert.Duration) : concert.Duration) : position) });
                Status = "UmaPresence Windows media session active";
            } catch (Exception ex) { Status = "Windows media session unavailable: " + ex.Message; Dispose(); }
        }
        public void Dispose()
        {
            pid = song = 0;
            pending.Clear();
            if (controls == null) { if (mediaWindow != null) { mediaWindow.Dispose(); mediaWindow = null; } return; }
            try { controls.IsEnabled = false; controls.PlaybackStatus = MediaPlaybackStatus.Closed; controls.ButtonPressed -= OnButton; controls.PlaybackPositionChangeRequested -= OnSeek; } catch { }
            controls = null; next = DateTime.MinValue;
            if (mediaWindow != null) { mediaWindow.Dispose(); mediaWindow = null; }
        }
    }
}
