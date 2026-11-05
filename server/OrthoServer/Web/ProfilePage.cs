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
        public static string Render(Profile profile, IReadOnlyList<PlayRecord> recent, string calculatorVersion, string? liveCalculatorVersion, string? owner = null,
                                    bool osuSignIn = true)
        {
            var html = new StringBuilder();

            html.Append(Page.Head("maniaortho scores"));
            html.Append("<h1>maniaortho scores</h1>\n");

            string whose = owner != null ? $"{e(owner)}'s plays. " : string.Empty;
            html.Append(CultureInfo.InvariantCulture,
                $"<p class=\"sub\">{whose}Unofficial pp, rated by osu!'s mania calculators {e(calculatorVersion)}. Not part of osu!: it uses osu! only to sign in.</p>\n");

            if (!osuSignIn)
            {
                html.Append("<p class=\"banner\">Signing in with osu! is not set up: run <code>server/scripts/setup-osu-sign-in</code>, "
                            + "then link the game in its settings to send plays to your account.</p>\n");
            }

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
