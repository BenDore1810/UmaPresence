using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UmapyonPresence
{
    internal sealed class DiscordIpc : IDisposable
    {
        private NamedPipeClientStream pipe;
        private readonly SemaphoreSlim writer = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, TaskCompletionSource<Dictionary<string, object>>> pending = new Dictionary<string, TaskCompletionSource<Dictionary<string, object>>>();
        private TaskCompletionSource<bool> ready;
        private Task receiver;
        public Dictionary<string, object> LastActivity { get; private set; }
        public bool IsConnected { get { return pipe != null && pipe.IsConnected && ready != null && ready.Task.Status == TaskStatus.RanToCompletion && !receiver.IsCompleted; } }

        public async Task Connect(string appId, string testPipe = null)
        {
            DisposeConnection();
            Exception last = null;
            for (int i = 0; i < (testPipe == null ? 10 : 1); i++)
            {
                var candidate = new NamedPipeClientStream(".", testPipe ?? "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await candidate.ConnectAsync(80).ConfigureAwait(false);
                    pipe = candidate;
                    ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    receiver = ReceiveLoop(candidate, ready);
                    await WriteFrame(candidate, 0, Encoding.UTF8.GetBytes(Json.Encode(new { v = 1, client_id = appId }))).ConfigureAwait(false);
                    if (await Task.WhenAny(ready.Task, Task.Delay(3000)).ConfigureAwait(false) != ready.Task)
                        throw new TimeoutException("Discord handshake timed out.");
                    await ready.Task.ConfigureAwait(false);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    candidate.Dispose();
                    if (pipe == candidate) DisposeConnection();
                }
            }
            throw new IOException("Discord desktop is not available. It may be closed or running as another user.", last);
        }

        public async Task SetActivity(object activity, int processId)
        {
            if (!IsConnected) throw new IOException("Discord is disconnected.");
            string nonce = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<Dictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (pending) pending.Add(nonce, completion);
            try
            {
                await WriteFrame(pipe, 1, Encoding.UTF8.GetBytes(Json.Encode(new
                {
                    cmd = "SET_ACTIVITY", args = new { pid = processId, activity = activity }, nonce = nonce
                }))).ConfigureAwait(false);
                if (await Task.WhenAny(completion.Task, Task.Delay(3000)).ConfigureAwait(false) != completion.Task)
                    throw new TimeoutException("Discord did not acknowledge the activity.");
                var response = await completion.Task.ConfigureAwait(false);
                object evt;
                if (response.TryGetValue("evt", out evt) && (string)evt == "ERROR")
                {
                    object data;
                    var detail = response.TryGetValue("data", out data) ? data as Dictionary<string, object> : null;
                    object message;
                    throw new IOException("Discord rejected the activity: " + (detail != null && detail.TryGetValue("message", out message) ? Convert.ToString(message) : "Unknown error"));
                }
                object activityData;
                var normalized = response.TryGetValue("data", out activityData) ? activityData as Dictionary<string, object> : null;
                LastActivity = null;
                if (normalized != null)
                {
                    LastActivity = new Dictionary<string, object>();
                    foreach (string field in new[] { "name", "type", "details", "state", "timestamps", "assets" })
                    {
                        object value; if (normalized.TryGetValue(field, out value)) LastActivity[field] = value;
                    }
                }
            }
            finally { lock (pending) pending.Remove(nonce); }
        }

        private async Task ReceiveLoop(NamedPipeClientStream connection, TaskCompletionSource<bool> handshake)
        {
            try
            {
                while (true)
                {
                    var frame = await ReadFrame(connection).ConfigureAwait(false);
                    if (frame.Opcode == 3) { await WriteFrame(connection, 4, frame.Data).ConfigureAwait(false); continue; }
                    if (frame.Opcode == 2) throw new IOException("Discord closed the connection.");
                    if (frame.Opcode != 1) continue;
                    var json = Json.Decode<Dictionary<string, object>>(Encoding.UTF8.GetString(frame.Data));
                    object evt;
                    if (json.TryGetValue("evt", out evt) && (string)evt == "READY") handshake.TrySetResult(true);
                    object nonce;
                    if (json.TryGetValue("nonce", out nonce) && nonce is string)
                    {
                        TaskCompletionSource<Dictionary<string, object>> completion;
                        lock (pending) pending.TryGetValue((string)nonce, out completion);
                        if (completion != null) completion.TrySetResult(json);
                    }
                    if (!handshake.Task.IsCompleted && (string)evt == "ERROR")
                        throw new IOException("Discord rejected the application handshake.");
                }
            }
            catch (Exception ex)
            {
                handshake.TrySetException(ex);
                if (ReferenceEquals(pipe, connection))
                    lock (pending) foreach (var request in pending.Values) request.TrySetException(new IOException("Discord disconnected.", ex));
            }
        }

        private async Task WriteFrame(Stream connection, int opcode, byte[] payload)
        {
            await writer.WaitAsync().ConfigureAwait(false);
            try
            {
                var bytes = new byte[8 + payload.Length];
                Buffer.BlockCopy(BitConverter.GetBytes(opcode), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, bytes, 4, 4);
                Buffer.BlockCopy(payload, 0, bytes, 8, payload.Length);
                await connection.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await connection.FlushAsync().ConfigureAwait(false);
            }
            finally { writer.Release(); }
        }

        internal sealed class Frame { public int Opcode; public byte[] Data; }
        internal static async Task<Frame> ReadFrame(Stream connection)
        {
            byte[] header = new byte[8];
            await ReadExact(connection, header).ConfigureAwait(false);
            int length = BitConverter.ToInt32(header, 4);
            if (length < 0 || length > 1024 * 1024) throw new InvalidDataException("Invalid Discord IPC frame length.");
            var data = new byte[length];
            await ReadExact(connection, data).ConfigureAwait(false);
            return new Frame { Opcode = BitConverter.ToInt32(header, 0), Data = data };
        }

        private static async Task ReadExact(Stream stream, byte[] bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = await stream.ReadAsync(bytes, offset, bytes.Length - offset).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("Discord IPC ended.");
                offset += count;
            }
        }
        private void DisposeConnection()
        {
            var old = pipe;
            pipe = null;
            if (old != null) old.Dispose();
        }
        public void Dispose() { DisposeConnection(); }
    }
}
