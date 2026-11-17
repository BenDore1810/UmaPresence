using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UmapyonPresence
{
    internal static class ArtworkDownload
    {
        internal static async Task<string> WikiText(string url)
        {
            const int limit = 1024 * 1024;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "UmaPresence/" + ReleasePolicy.Version + " (game artwork)";
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            using (deadline.Token.Register(request.Abort))
            {
                try
                {
                    using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                    {
                        if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > limit)
                            throw new IOException("Artwork service returned an unexpected response.");
                        using (var input = response.GetResponseStream())
                            return await ReadText(input, limit, deadline.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception error) when (deadline.IsCancellationRequested &&
                    (error is WebException || error is IOException || error is OperationCanceledException))
                {
                    throw new IOException("Artwork lookup timed out.", error);
                }
            }
        }

        internal static async Task<string> ReadText(Stream input, int limit, CancellationToken cancellationToken)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int count;
                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    if (output.Length + count > limit) throw new IOException("Artwork response exceeds the size limit.");
                    output.Write(buffer, 0, count);
                }
                return new UTF8Encoding(false, true).GetString(output.ToArray());
            }
        }
    }
}
