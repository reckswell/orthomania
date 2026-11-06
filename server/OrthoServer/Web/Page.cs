using System.Net;
using System.Text;

namespace OrthoServer.Web
{
    /// <summary>
    /// What the server's pages share: the head with their style, and short message pages (osu! sign-in). Plain HTML, nothing
    /// loaded from elsewhere.
    /// </summary>
    public static class Page
    {
        public static string Head(string title) => $$"""
                            <!doctype html>
                            <html lang="en">
                            <head>
                            <meta charset="utf-8">
                            <meta name="viewport" content="width=device-width, initial-scale=1">
                            <meta name="referrer" content="no-referrer">
                            <title>{{E(title)}}</title>
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
                            .code { font: 600 22px/1.2 ui-monospace, "Cascadia Mono", Consolas, monospace; letter-spacing: 2px; }
                            a.button { display: inline-block; background: var(--accent); color: var(--bg); border-radius: 8px; padding: 8px 16px; text-decoration: none; font-weight: 600; }
                            </style>
                            </head>
                            <body>
                            <main>

                            """;

        public const string FOOT = "</main>\n</body>\n</html>\n";

        /// <summary>
        /// A page with a heading, optionally a code shown large, paragraphs of text (encoded here), and optionally a link to follow.
        /// </summary>
        public static string Message(string title, string[] paragraphs, string? code = null, (string Text, string Url)? link = null)
        {
            var html = new StringBuilder(Head(title));
            html.Append("<h1>").Append(E(title)).Append("</h1>\n");

            if (code != null)
                html.Append("<p class=\"code\">").Append(E(code)).Append("</p>\n");

            foreach (string paragraph in paragraphs)
                html.Append("<p>").Append(E(paragraph)).Append("</p>\n");

            if (link is var (text, url))
                html.Append("<p><a class=\"button\" href=\"").Append(E(url)).Append("\">").Append(E(text)).Append("</a></p>\n");

            return html.Append(FOOT).ToString();
        }

        public static string E(string text) => WebUtility.HtmlEncode(text);
    }
}
