// Core logic with no UI: API call and parsing, settings, reset-window math.
// Compiled into both the widget (build.cmd) and the self-test (test.cmd).

using System;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace OpenRouterBar
{
    public class Snapshot
    {
        public bool Ok;
        public string Error = "";
        public DateTime CheckedUtc;
        public bool HasLimit;
        public double Limit, Remaining, UsageDaily;
        public string LimitReset = "";

        // Share of the key's credit limit already spent, 0-100.
        public double PercentUsed
        {
            get
            {
                if (!HasLimit) return 0;
                double p = (Limit - Remaining) / Limit * 100.0;
                return Math.Max(0, Math.Min(100, p));
            }
        }

        public double Used { get { return HasLimit ? Math.Max(0, Limit - Remaining) : 0; } }
    }

    public class Settings
    {
        public const int MinIntervalSec = 60, MaxIntervalSec = 3600, MaxLabelLength = 8;

        public int IntervalSec = 600;
        public string Label = "OR";
        public Color Fill = ColorTranslator.FromHtml("#14B8A6");
        public Color Text = ColorTranslator.FromHtml("#2DD4BF");
        public int GapPx = 6;

        // Unknown keys and unparsable values are ignored; the defaults stay.
        public static Settings Parse(string[] lines)
        {
            Settings s = new Settings();
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || eq < 1) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                try
                {
                    if (k == "intervalsec") s.IntervalSec = Math.Max(MinIntervalSec, Math.Min(MaxIntervalSec, int.Parse(v, CultureInfo.InvariantCulture)));
                    else if (k == "label") s.Label = v.Length > MaxLabelLength ? v.Substring(0, MaxLabelLength) : v;
                    else if (k == "fillcolor") s.Fill = ColorTranslator.FromHtml(v);
                    else if (k == "textcolor") s.Text = ColorTranslator.FromHtml(v);
                    else if (k == "gappx") s.GapPx = Math.Max(0, Math.Min(200, int.Parse(v, CultureInfo.InvariantCulture)));
                }
                catch (Exception) { }
            }
            return s;
        }

        public static Settings Load(string path)
        {
            try { return File.Exists(path) ? Parse(File.ReadAllLines(path)) : new Settings(); }
            catch (Exception) { return new Settings(); }
        }
    }

    [DataContract]
    public class KeyEnvelope
    {
        [DataMember(Name = "data")] public KeyData Data;
    }

    // Only the top-level fields of GET /api/v1/key that the widget uses. A typed parser (not a
    // regex) matters here: the response also carries nested objects with their own "limit" field.
    [DataContract]
    public class KeyData
    {
        [DataMember(Name = "limit")] public double? Limit;
        [DataMember(Name = "limit_remaining")] public double? LimitRemaining;
        [DataMember(Name = "limit_reset")] public string LimitReset;
        [DataMember(Name = "usage_daily")] public double? UsageDaily;
    }

    public static class Api
    {
        public const string KeyUrl = "https://openrouter.ai/api/v1/key";
        const int MaxBodyBytes = 64 * 1024;
        const int TimeoutMs = 15000, TotalDeadlineMs = 30000;

        public static Snapshot Fetch(string key)
        {
            DateTime now = DateTime.UtcNow;
            string body;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(KeyUrl);
                req.Method = "GET";
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = TimeoutMs;
                req.AllowAutoRedirect = false;
                req.UserAgent = "OpenRouterBar/" + typeof(Api).Assembly.GetName().Version.ToString(3);
                req.Accept = "application/json";
                req.Headers["Authorization"] = "Bearer " + key;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    if ((int)resp.StatusCode != 200) return Failed(now, "HTTP " + (int)resp.StatusCode);
                    using (Stream st = resp.GetResponseStream()) body = ReadCapped(st, MaxBodyBytes);
                }
            }
            catch (WebException ex)
            {
                using (HttpWebResponse r = ex.Response as HttpWebResponse)
                {
                    if (r == null) return Failed(now, ex.Status.ToString());
                    int code = (int)r.StatusCode;
                    return Failed(now, "HTTP " + code + (code == 401 || code == 403 ? " (key rejected)" : ""));
                }
            }
            catch (Exception ex)
            {
                return Failed(now, ex.GetType().Name);
            }
            return Parse(body, now);
        }

        public static Snapshot Parse(string body, DateTime checkedUtc)
        {
            KeyEnvelope env;
            try
            {
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                    env = (KeyEnvelope)new DataContractJsonSerializer(typeof(KeyEnvelope)).ReadObject(ms);
            }
            catch (Exception) { return Failed(checkedUtc, "unreadable response"); }
            if (env == null || env.Data == null) return Failed(checkedUtc, "unexpected response");

            KeyData d = env.Data;
            if (!Finite(d.Limit) || !Finite(d.LimitRemaining) || !Finite(d.UsageDaily)) return Failed(checkedUtc, "invalid numbers in response");
            Snapshot s = new Snapshot();
            s.CheckedUtc = checkedUtc;
            s.Ok = true;
            s.HasLimit = d.Limit.HasValue && d.Limit.Value > 0 && d.LimitRemaining.HasValue;
            s.Limit = d.Limit.HasValue ? d.Limit.Value : 0;
            s.Remaining = d.LimitRemaining.HasValue ? d.LimitRemaining.Value : 0;
            s.UsageDaily = d.UsageDaily.HasValue ? d.UsageDaily.Value : 0;
            s.LimitReset = d.LimitReset == "daily" || d.LimitReset == "weekly" || d.LimitReset == "monthly" ? d.LimitReset : "";
            return s;
        }

        static bool Finite(double? v) { return !v.HasValue || !(double.IsNaN(v.Value) || double.IsInfinity(v.Value)); }

        static Snapshot Failed(DateTime checkedUtc, string error)
        {
            Snapshot s = new Snapshot();
            s.CheckedUtc = checkedUtc;
            s.Error = error;
            return s;
        }

        static string ReadCapped(Stream s, int max)
        {
            Stopwatch clock = Stopwatch.StartNew();
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    if (ms.Length + n > max) throw new InvalidDataException("response too large");
                    if (clock.ElapsedMilliseconds > TotalDeadlineMs) throw new TimeoutException("response too slow");
                    ms.Write(buf, 0, n);
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
    }

    // OpenRouter resets key limits at 00:00 UTC; weekly windows run Monday to Sunday.
    public static class Reset
    {
        public static DateTime? NextUtc(string kind, DateTime nowUtc)
        {
            DateTime today = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, DateTimeKind.Utc);
            switch (kind)
            {
                case "daily": return today.AddDays(1);
                case "weekly":
                    int days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
                    return today.AddDays(days == 0 ? 7 : days);
                case "monthly": return new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
                default: return null;
            }
        }

        // The largest whole unit, as the Claude Code Usage Monitor shows it: "2d", "5h", "51m".
        public static string Countdown(DateTime nextUtc, DateTime nowUtc)
        {
            TimeSpan t = nextUtc - nowUtc;
            if (t.TotalDays >= 1) return ((int)Math.Floor(t.TotalDays)).ToString(CultureInfo.InvariantCulture) + "d";
            if (t.TotalHours >= 1) return ((int)Math.Floor(t.TotalHours)).ToString(CultureInfo.InvariantCulture) + "h";
            return Math.Max(1, (int)Math.Ceiling(t.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + "m";
        }
    }
}
