// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.ManiaOrtho.Tests.ScoreServer
{
    /// <summary>
    /// A stand-in for the score server on a free port of this computer, over a real socket: it keeps every request and answers
    /// with <see cref="Respond"/> (by default the contract's example answer). Plain TCP, so it needs no rights on any system.
    /// </summary>
    public sealed class FakeScoreServer : IDisposable
    {
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly List<FakeRequest> requests = new List<FakeRequest>();

        public string Address { get; }

        public Func<FakeRequest, (int Status, string Body)> Respond { get; set; } = _ => (200, ExampleResult);

        /// <summary>
        /// A <c>Location</c> header to answer with (for redirects).
        /// </summary>
        public string? Location { get; set; }

        public static string ExampleResult => File.ReadAllText(ScoreServerParityTest.ContractPath("play-result.example.json"));

        public IReadOnlyList<FakeRequest> Requests
        {
            get
            {
                lock (requests)
                    return requests.ToList();
            }
        }

        public FakeScoreServer()
        {
            listener.Start();
            Address = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            Task.Run(acceptLoop);
        }

        /// <summary>
        /// An address on this computer where nothing listens.
        /// </summary>
        public static string UnusedAddress()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return $"http://127.0.0.1:{port}";
        }

        private async Task acceptLoop()
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellation.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(() => handle(client));
            }
        }

        private async Task handle(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var head = new List<byte>();

                while (head.Count < 4 || head[^4] != '\r' || head[^3] != '\n' || head[^2] != '\r' || head[^1] != '\n')
                {
                    int next = stream.ReadByte();

                    if (next < 0)
                        return;

                    head.Add((byte)next);
                }

                string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                var headers = lines.Skip(1).Where(l => l.Contains(':'))
                                   .ToDictionary(l => l[..l.IndexOf(':')].Trim().ToLowerInvariant(), l => l[(l.IndexOf(':') + 1)..].Trim());

                byte[] body = new byte[headers.TryGetValue("content-length", out string? length) ? int.Parse(length, CultureInfo.InvariantCulture) : 0];
                await stream.ReadExactlyAsync(body).ConfigureAwait(false);

                var request = new FakeRequest(lines[0], headers, body);

                lock (requests)
                    requests.Add(request);

                var (status, answer) = Respond(request);
                byte[] answerBytes = Encoding.UTF8.GetBytes(answer);
                string location = Location == null ? string.Empty : $"Location: {Location}\r\n";
                string responseHead = $"HTTP/1.1 {status} Fake\r\n{location}Content-Type: application/json\r\nContent-Length: {answerBytes.Length}\r\nConnection: close\r\n\r\n";

                await stream.WriteAsync(Encoding.ASCII.GetBytes(responseHead)).ConfigureAwait(false);
                await stream.WriteAsync(answerBytes).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            cancellation.Cancel();
            listener.Stop();
            cancellation.Dispose();
        }
    }

    public record FakeRequest(string RequestLine, IReadOnlyDictionary<string, string> Headers, byte[] Body)
    {
        /// <summary>
        /// The parts of a <c>multipart/form-data</c> body, by name.
        /// </summary>
        public IReadOnlyDictionary<string, byte[]> Parts
        {
            get
            {
                string boundary = "--" + Regex.Match(Headers["content-type"], "boundary=\"?([^\";]+)\"?").Groups[1].Value;

                // One char per byte, so positions and contents carry over to the bytes unchanged.
                string body = Encoding.Latin1.GetString(Body);
                var parts = new Dictionary<string, byte[]>();

                foreach (string part in body.Split(boundary).Skip(1))
                {
                    int split = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);

                    if (split < 0)
                        continue;

                    string name = Regex.Match(part[..split], "name=\"?([^\";\r\n]+)\"?").Groups[1].Value;
                    string content = part[(split + 4)..];

                    parts[name] = Encoding.Latin1.GetBytes(content.EndsWith("\r\n", StringComparison.Ordinal) ? content[..^2] : content);
                }

                return parts;
            }
        }

        public string PartText(string name) => Encoding.UTF8.GetString(Parts[name]);
    }
}
