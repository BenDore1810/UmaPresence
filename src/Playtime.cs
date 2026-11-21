using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace UmapyonPresence
{
    internal sealed class Playtime
    {
        private readonly string path;
        private long lastTick;
        private int processId;
        private double totalSeconds;
        private double savedSeconds;
        private bool writable = true;

        internal Playtime(string path)
        {
            this.path = path;
            try
            {
                if (!File.Exists(path)) return;
                if (!double.TryParse(File.ReadAllText(path), NumberStyles.Float, CultureInfo.InvariantCulture, out totalSeconds)
                    || double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds) || totalSeconds < 0 || totalSeconds > TimeSpan.MaxValue.TotalSeconds)
                {
                    totalSeconds = 0;
                    writable = false;
                    return;
                }
                savedSeconds = totalSeconds;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { writable = false; }
        }

        internal string Label
        {
            get
            {
                long minutes = (long)(totalSeconds / 60);
                return minutes < 60 ? minutes + " min played" : (minutes / 60) + " h " + (minutes % 60).ToString("00") + " min played";
            }
        }

        internal void Observe(int pid)
        {
            long now = Stopwatch.GetTimestamp();
            if (pid != 0 && pid == processId)
            {
                // Skip long gaps when Windows sleeps or the companion stops polling.
                double elapsed = (double)(now - lastTick) / Stopwatch.Frequency;
                if (elapsed <= 10) totalSeconds += elapsed;
                if (totalSeconds - savedSeconds >= 60) Save();
            }
            else if (processId != 0) Save();
            lastTick = now;
            processId = pid;
        }

        internal void Save()
        {
            if (!writable || totalSeconds <= savedSeconds) return;
            try
            {
                AtomicText.Write(path, totalSeconds.ToString("R", CultureInfo.InvariantCulture));
                savedSeconds = totalSeconds;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Log.Write("Could not save tracked playtime.");
                writable = false;
            }
        }

        internal object AddToActivity(object activity)
        {
            var payload = activity as Dictionary<string, object> ?? Json.Decode<Dictionary<string, object>>(Json.Encode(activity));
            if (!payload.TryGetValue("assets", out var value) || !(value is Dictionary<string, object> assets))
                payload["assets"] = assets = new Dictionary<string, object>();
            if (!assets.TryGetValue("small_image", out var image) || string.IsNullOrEmpty(image as string))
                assets["small_image"] = "umaicon";
            assets["small_text"] = Label + " (tracked by UmaPresence)";
            return payload;
        }
    }
}
