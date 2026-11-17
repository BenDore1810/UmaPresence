using System;
using System.Collections.Generic;
using System.Linq;

namespace UmapyonPresence
{
    internal sealed class ConcertPortraitRotation
    {
        private int music, slot = -1;
        private string current, published;
        private DateTime next = DateTime.MaxValue;
        private readonly HashSet<string> visited = new HashSet<string>();
        private static string Key(ConcertSnapshot c, int slot, Settings settings) { return c.CastIds[slot] + ":" + c.DressFor(slot); }
        internal void Observe(ConcertSnapshot c, Settings settings, DateTime now)
        {
            if (music != c.MusicId) { Reset(); music = c.MusicId; }
            int[] slots = c.PortraitSlots(settings); if (slots.Length == 0) { c.PortraitIndex = 0; return; }
            string[] keys = slots.Select(s => Key(c, s, settings)).ToArray();
            int index = Array.IndexOf(keys, current);
            if (!settings.ConcertRotatePortraits) index = 0;
            else if (index < 0) { index = Array.IndexOf(slots, slot); if (index < 0) index = 0; next = DateTime.MaxValue; }
            else if (now >= next && slots.Length > 1) {
                var candidates = Enumerable.Range(1, slots.Length).Select(n => (index + n) % slots.Length).Where(i => !visited.Contains(keys[i])).ToArray();
                if (candidates.Length == 0) { visited.Clear(); candidates = Enumerable.Range(1, slots.Length - 1).Select(n => (index + n) % slots.Length).ToArray(); }
                int character = c.CastIds[slots[index]];
                index = candidates.Where(i => c.CastIds[slots[i]] != character).DefaultIfEmpty(candidates[0]).First();
                next = DateTime.MaxValue;
            }
            slot = slots[index]; current = keys[index]; c.PortraitIndex = index;
        }
        internal void Published(ConcertSnapshot c, Settings settings, DateTime now)
        {
            int[] slots = c.PortraitSlots(settings); if (slots.Length == 0) return;
            string key = Key(c, slots[Math.Abs(c.PortraitIndex % slots.Length)], settings);
            if (key == published) return; // Timing/metadata refreshes cannot skip a portrait.
            published = key; visited.Add(key); next = now.AddSeconds(settings.ConcertPortraitIntervalSeconds);
        }
        internal void Reset() { music = 0; slot = -1; current = published = null; next = DateTime.MaxValue; visited.Clear(); }
    }
}
