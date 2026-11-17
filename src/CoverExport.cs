using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace UmapyonPresence
{
    internal static class CoverExport
    {
        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
        public static void Run(string input, string output)
        {
            input = Path.GetFullPath(input); output = Path.GetFullPath(output);
            var songs = Json.Decode<List<SongEntry>>(File.ReadAllText(Path.Combine(input, "songs.json"), Encoding.UTF8));
            Directory.CreateDirectory(output);
            var map = new Dictionary<string, string>();
            var index = new StringBuilder("SongID,SongTitle,OriginalTitle,DiscordAssetName,FileName\r\n");
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("special-week.png"))
            using (var special = Image.FromStream(stream))
            {
                special.Save(Path.Combine(output, "umaicon.png"), ImageFormat.Png);
                foreach (var song in songs)
                {
                    string key = "concert_" + song.Id;
                    using (var jacket = Image.FromFile(Path.Combine(input, "song_" + song.Id + ".png")))
                    using (var cover = ConcertArtworkBuilder.Compose(special, jacket))
                        cover.Save(Path.Combine(output, key + ".png"), ImageFormat.Png);
                    map[song.Id.ToString()] = key;
                    index.AppendLine(song.Id + "," + Csv(song.Title) + "," + Csv(song.OriginalTitle) + "," + key + "," + key + ".png");
                }
            }
            File.WriteAllText(Path.Combine(output, "Song-IDs.csv"), index.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(output, "ConcertArtwork.json"), Json.Encode(new { ConcertArtwork = map }), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "UPLOAD-INSTRUCTIONS.txt"),
                "UmaPresence song covers\r\nApplication ID: 1563519446503522344\r\n\r\n" +
                "Upload concert_*.png under Rich Presence > Art Assets. Keep each filename as the asset name (without .png).\r\n" +
                "Example: concert_1176.png = concert_1176 = VOLTAGE.\r\n" +
                "umaicon.png is the default Special Week icon; keep asset name umaicon.\r\n" +
                "Song-IDs.csv lists all IDs and titles. ConcertArtwork.json contains the matching settings fragment.\r\n" +
                "Do not upload the CSV, JSON, text instructions, or contact sheet as Discord assets.\r\n" +
                "Tell me when the uploads are saved; I can activate only the assets confirmed by Discord.\r\n\r\n" +
                "All " + songs.Count + " covers were extracted from installed concert jacket assets.\r\n", Encoding.UTF8);
            const int cols = 6, cell = 156;
            using (var sheet = new Bitmap(cols * cell, ((songs.Count + cols - 1) / cols) * 184))
            using (var graphics = Graphics.FromImage(sheet))
            using (var font = new Font("Segoe UI", 9))
            {
                graphics.Clear(Color.FromArgb(26, 28, 34));
                for (int i = 0; i < songs.Count; i++)
                {
                    int x = i % cols * cell, y = i / cols * 184;
                    using (var image = Image.FromFile(Path.Combine(output, "concert_" + songs[i].Id + ".png"))) graphics.DrawImage(image, x + 6, y + 4, 144, 144);
                    graphics.DrawString(songs[i].Id + " • " + songs[i].Title, font, Brushes.White, new RectangleF(x + 6, y + 150, 144, 32));
                }
                sheet.Save(Path.Combine(output, "Cover-Preview.jpg"), ImageFormat.Jpeg);
            }
            Console.WriteLine("Exported " + songs.Count + " diagonal covers: " + output);
        }
    }
}
