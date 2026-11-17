using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace UmapyonPresence
{
    [Flags]
    internal enum RuntimeCapability : ulong { Telemetry = 1, Playback = 2, Formation = 4, Routing = 8, Mix = 16, Overlay = 32, Seek = 64 }
    internal sealed class StandaloneRuntimeStatus
    {
        internal int Pid, State, Backend;
        internal ulong Nonce;
        internal RuntimeCapability Capabilities;
        internal double SeekStart, SeekEnd;
        internal int? PlaybackStage, SeekReason;
        internal string Detail;
        internal bool Ready { get { return State == 4 && Backend == 1; } }
        internal bool CanSeek { get { return Ready && (Capabilities & (RuntimeCapability.Playback | RuntimeCapability.Seek)) == (RuntimeCapability.Playback | RuntimeCapability.Seek) && SeekEnd > SeekStart; } }
        internal bool RequiresBasicPresence { get { return Backend == 1 && (State == 2 || State == 5 || State == 6); } }
        internal bool OwnershipConflict { get { return State == 7 || Backend == 2; } }
    }
    internal static class StandaloneRuntime
    {
        private static int sequence;
        internal static StandaloneRuntimeStatus Parse(byte[] bytes, int pid)
        {
            if (bytes == null || bytes.Length < 240 || BitConverter.ToUInt32(bytes, 0) != 0x55505231 || BitConverter.ToInt32(bytes, 4) != 1 || BitConverter.ToInt32(bytes, 8) != pid || pid < 1 || (BitConverter.ToInt32(bytes, 32) & 1) != 0) return null;
            var status = new StandaloneRuntimeStatus { Pid = pid, State = BitConverter.ToInt32(bytes, 12), Nonce = BitConverter.ToUInt64(bytes, 16), Capabilities = (RuntimeCapability)BitConverter.ToUInt64(bytes, 24), Backend = BitConverter.ToInt32(bytes, 44), Detail = Encoding.UTF8.GetString(bytes, 48, 192).TrimEnd('\0') };
            if (bytes.Length >= 304 && BitConverter.ToUInt32(bytes, 256) == 0x55504131 && BitConverter.ToUInt32(bytes, 260) == 2 && (BitConverter.ToUInt32(bytes, 264) & 1) == 0) {
                float start = BitConverter.ToSingle(bytes, 296), end = BitConverter.ToSingle(bytes, 300);
                if (!float.IsNaN(start) && !float.IsInfinity(start) && !float.IsNaN(end) && !float.IsInfinity(end) && start >= 0 && end > start && end <= 7200) { status.SeekStart = start; status.SeekEnd = end; }
                if (bytes.Length >= 312) { status.PlaybackStage = BitConverter.ToInt32(bytes, 304); status.SeekReason = BitConverter.ToInt32(bytes, 308); }
            }
            return status;
        }
        internal static StandaloneRuntimeStatus Read(int pid)
        {
            if (pid < 1) return null;
            try { using (var mapping = MemoryMappedFile.OpenExisting("Local\\UmaPresenceRuntime-" + pid, MemoryMappedFileRights.Read)) using (var view = mapping.CreateViewAccessor(0, 320, MemoryMappedFileAccess.Read)) {
                for (int i = 0; i < 3; i++) { int before = view.ReadInt32(32), audio = view.ReadInt32(264); var bytes = new byte[320]; view.ReadArray(0, bytes, 0, bytes.Length); if (before == view.ReadInt32(32) && audio == view.ReadInt32(264)) return Parse(bytes, pid); }
            } } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
        internal static byte[] Command(StandaloneRuntimeStatus status, int command, int song, double position, uint flags = 0, uint voice1 = 0, uint voice2 = 0)
        {
            if (status == null || !status.Ready || status.Nonce == 0) throw new InvalidOperationException("Standalone runtime is not ready.");
            if (command < 1 || command > 7 || double.IsNaN(position) || double.IsInfinity(position) || position < 0 || position > 7200) throw new ArgumentException("Invalid concert command.");
            var bytes = new byte[56];
            Action<int, uint> put = (offset, value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
            put(0, 0x55504331); put(4, 1); put(8, (uint)status.Pid); put(12, unchecked((uint)System.Threading.Interlocked.Increment(ref sequence)));
            Buffer.BlockCopy(BitConverter.GetBytes(status.Nonce), 0, bytes, 16, 8); put(24, (uint)command); put(28, (uint)song);
            Buffer.BlockCopy(BitConverter.GetBytes(position), 0, bytes, 32, 8); put(40, flags); put(44, voice1); put(48, voice2); return bytes;
        }
        internal static async Task<int> Send(int pid, int command, int song, double position = 0, uint flags = 0, uint voice1 = 0, uint voice2 = 0)
        {
            var request = Command(Read(pid), command, song, position, flags, voice1, voice2);
            using (var pipe = new NamedPipeClientStream(".", "UmaPresence-Control-" + pid, PipeDirection.InOut, PipeOptions.Asynchronous)) {
                var connect = Task.Run(() => pipe.Connect(1500)); if (await Task.WhenAny(connect, Task.Delay(2000)) != connect) throw new TimeoutException("Runtime control connection timed out."); await connect;
                await pipe.WriteAsync(request, 0, request.Length); var response = new byte[4]; int read = 0;
                while (read < response.Length) { var operation = pipe.ReadAsync(response, read, response.Length - read); if (await Task.WhenAny(operation, Task.Delay(2500)) != operation) throw new TimeoutException("Runtime control acknowledgement timed out."); int count = await operation; if (count == 0) throw new IOException("Runtime control channel closed."); read += count; }
                return BitConverter.ToInt32(response, 0);
            }
        }
    }
}
