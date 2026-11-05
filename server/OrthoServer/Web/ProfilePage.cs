using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using OrthoServer.Scoring;

namespace OrthoServer.Web
{
    /// <summary>
    /// The server's one page: the profile, its best plays and the latest plays. Plain HTML, nothing loaded from elsewhere.
    /// </summary>
    public static class ProfilePage
    {
        public static string Render(Profile profile, IReadOnlyList<PlayRecord> recent, string calculatorVersion, string? liveCalculatorVersion)
        {
            var html = new StringBuilder();

            html.Append("""
                        <!doctype html>
                        <html lang="en">
                        <head>
                        <meta charset="utf-8">
                        <meta name="viewport" content="width=device-width, initial-scale=1">
                        <title>maniaortho scores</title>
                        <style>
                        :root { --bg: #f6f6f8; --fg: #1c1c22; --muted: #6b6b78; --line: #dcdce3; --card: #ffffff; --accent: #5b4bd6; --warn: #8a5a00; --warn-bg: #fff3d6; }
                        @media (prefers-color-scheme: dark) { :root { --bg: #15151a; --fg: #ececf1; --muted: #9a9aa8; --line: #2c2c35; --card: #1d1d24; --accent: #a99cff; --warn: #ffd37a; --warn-bg: #3a2e12; } }
                        * { box-sizing: border-box; }
                        body { margin: 0; background: var(--bg); color: var(--fg); font: 15px/1.45 system-ui, -apple-system, "Segoe UI", sans-serif; }
                        main { max-width: 1040px; margin: 0 auto; padding: 24px 16px 48px; }
                        h1 { font-size: 20px; margin: 0 0 4px; }
                        h2 { font-size: 16px; margin: 32px 0 8px; }
                        .sub { color: var(--muted); margin: 0 0 20px; }
                        .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; }
                        .stat { background: var(--card); border: 1px solid var(--line); border-radius: 8px; padding: 12px 14px; }
                        .stat b { display: block; font-size: 22px; font-variant-numeric: tabular-nums; }
                        .stat span { color: var(--muted); font-size: 13px; }
                        .banner { background: var(--warn-bg); color: var(--warn); border-radius: 8px; padding: 10px 14px; margin: 0 0 16px; }
                        .table { overflow-x: auto; background: var(--card); border: 1px solid var(--line); border-radius: 8px; }
                        table { width: 100%; border-collapse: collapse; }
                        th, td { text-align: left; padding: 8px 10px; border-bottom: 1px solid var(--line); white-space: nowrap; }
                        tr:last-child td { border-bottom: 0; }
                        th { color: var(--muted); font-weight: 600; font-size: 13px; }
                        td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
                        td.map { white-space: normal; min-width: 240px; }
                        .muted { color: var(--muted); }
                        .pp { color: var(--accent); font-weight: 600; }
                        </style>
                        </head>
                        <body>
                        <main>
                        <h1>maniaortho scores</h1>
                        """);

            html.Append(CultureInfo.InvariantCulture, $"<p class=\"sub\">Unofficial pp, rated by osu!'s mania calculators {e(calculatorVersion)}. Not connected to osu!.</p>\n");

            if (liveCalculatorVersion != null)
            {
                html.Append(CultureInfo.InvariantCulture,
                    $"<p class=\"banner\">osu! now rates scores with {e(liveCalculatorVersion)}. Run <code>server/scripts/server-update</code> and restart the server to rate every play again with it.</p>\n");
            }

            html.Append("<div class=\"stats\">\n");
            stat(html, profile.TotalPp.ToString("#,0.##", CultureInfo.InvariantCulture) + " pp", "Total (unofficial)");
            stat(html, profile.Accuracy.ToString("0.00", CultureInfo.InvariantCulture) + "%", "Accuracy (weighted)");
            stat(html, profile.Top.Count.ToString("#,0", CultureInfo.InvariantCulture), "Maps with pp");
            stat(html, profile.BonusPp.ToString("0.0", CultureInfo.InvariantCulture) + " pp", $"Bonus ({profile.CountedScores:#,0} scores)");
            html.Append("</div>\n");

            html.Append("<h2>Best plays</h2>\n");

            if (profile.Top.Count == 0)
                html.Append("<p class=\"muted\">No pp yet: pass a ranked or approved map without unranked mods.</p>\n");
            else
            {
                html.Append("<div class=\"table\"><table><thead><tr><th class=\"num\">#</th><th>Map</th><th>Mods</th><th class=\"num\">Accuracy</th>"
                            + "<th class=\"num\">Stars</th><th class=\"num\">pp</th><th class=\"num\">Weighted</th><th>Date</th></tr></thead><tbody>\n");

                for (int i = 0; i < profile.Top.Count && i < 100; i++)
                {
                    var top = profile.Top[i];
                    var play = top.Play;

                    html.Append(CultureInfo.InvariantCulture,
                        $"<tr><td class=\"num\">{i + 1}</td><td class=\"map\">{mapName(play)}</td><td>{mods(play)}</td>"
                        + $"<td class=\"num\">{play.Accuracy * 100:0.00}%</td><td class=\"num\">{play.Stars:0.00}</td>"
                        + $"<td class=\"num pp\">{play.Pp:0.00}</td><td class=\"num\">{top.WeightedPp:0.0} <span class=\"muted\">({top.Weight * 100:0}%)</span></td>"
                        + $"<td>{play.PlayedAt.ToLocalTime():yyyy-MM-dd}</td></tr>\n");
                }

                html.Append("</tbody></table></div>\n");
            }

            html.Append("<h2>Latest plays</h2>\n");

            if (recent.Count == 0)
                html.Append("<p class=\"muted\">No plays yet. Turn on \"Send plays to my score server\" in maniaortho's settings and play.</p>\n");
            else
            {
                html.Append("<div class=\"table\"><table><thead><tr><th>Date</th><th>Map</th><th>Mods</th><th>Result</th><th class=\"num\">Accuracy</th>"
                            + "<th class=\"num\">pp</th><th>Awards pp</th></tr></thead><tbody>\n");

                foreach (var play in recent)
                {
                    string pp = play.Pp == null ? "<span class=\"muted\">-</span>" : play.AwardsPp ? $"<span class=\"pp\">{play.Pp:0.00}</span>" : $"<span class=\"muted\">{play.Pp:0.00}</span>";

                    html.Append(CultureInfo.InvariantCulture,
                        $"<tr><td>{play.PlayedAt.ToLocalTime():yyyy-MM-dd HH:mm}</td><td class=\"map\">{mapName(play)}</td><td>{mods(play)}</td>"
                        + $"<td>{(play.Passed ? e(play.Rank ?? "pass") : "fail")}</td><td class=\"num\">{play.Accuracy * 100:0.00}%</td>"
                        + $"<td class=\"num\">{pp}</td><td>{(play.AwardsPp ? "yes" : $"<span class=\"muted\">{e(play.Reason ?? "no")}</span>")}</td></tr>\n");
                }

                html.Append("</tbody></table></div>\n");
            }

            html.Append("</main>\n</body>\n</html>\n");
            return html.ToString();
        }

        private static void stat(StringBuilder html, string value, string label)
            => html.Append(CultureInfo.InvariantCulture, $"<div class=\"stat\"><b>{e(value)}</b><span>{e(label)}</span></div>\n");

        private static string mapName(PlayRecord play)
        {
            string name = string.IsNullOrEmpty(play.Artist) ? e(play.Title) : $"{e(play.Artist)} - {e(play.Title)}";
            string version = string.IsNullOrEmpty(play.Version) ? string.Empty : $"[{e(play.Version)}] ";

            return $"{name} <span class=\"muted\">{version}{play.Keys}K</span>";
        }

        private static string mods(PlayRecord play)
            => play.Mods.Count == 0 ? "<span class=\"muted\">-</span>" : e(string.Join(" ", play.Mods.Select(m => m.Acronym)));

        private static string e(string text) => WebUtility.HtmlEncode(text);
    }
}
