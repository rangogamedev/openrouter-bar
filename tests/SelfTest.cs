// Self-test for the UI-free logic in src/Core.cs. Built and run by test.cmd; exits non-zero on
// any failure so CI fails.

using System;
using System.Globalization;
using OpenRouterBar;

static class SelfTest
{
    static int failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok ? "" : " -- " + detail));
        if (!ok) failures++;
    }

    static void Eq(string name, object actual, object expected)
    {
        Check(name, Equals(actual, expected), "expected <" + expected + "> got <" + actual + ">");
    }

    static void Near(string name, double actual, double expected)
    {
        Check(name, Math.Abs(actual - expected) < 1e-6, "expected <" + expected.ToString(CultureInfo.InvariantCulture) + "> got <" + actual.ToString(CultureInfo.InvariantCulture) + ">");
    }

    static DateTime Utc(int y, int mo, int d, int h, int mi) { return new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc); }

    static int Main()
    {
        DateTime now = Utc(2026, 9, 18, 8, 0);

        // Parsing /api/v1/key.
        Snapshot s = Api.Parse("{\"data\":{\"label\":\"x\",\"limit\":10,\"limit_remaining\":9.433169835,\"limit_reset\":\"weekly\",\"usage\":12.5,\"usage_daily\":0.459020263}}", now);
        Eq("parse: ok", s.Ok, true);
        Eq("parse: has limit", s.HasLimit, true);
        Near("parse: percent used from limit, not lifetime usage", s.PercentUsed, 5.6683016500);
        Near("parse: used dollars", s.Used, 0.566830165);
        Eq("parse: reset kind", s.LimitReset, "weekly");

        Snapshot nested = Api.Parse("{\"data\":{\"free_model_daily_requests\":{\"used\":3,\"limit\":50,\"remaining\":47},\"limit\":20,\"limit_remaining\":15,\"limit_reset\":\"monthly\"}}", now);
        Near("parse: a nested 'limit' before the top-level one is ignored", nested.Limit, 20);
        Near("parse: percent with the new quota", nested.PercentUsed, 25);

        Snapshot noCap = Api.Parse("{\"data\":{\"limit\":null,\"limit_remaining\":null,\"limit_reset\":null,\"usage_daily\":1.5}}", now);
        Eq("parse: no cap is ok", noCap.Ok, true);
        Eq("parse: no cap has no limit", noCap.HasLimit, false);
        Near("parse: no cap percent is 0", noCap.PercentUsed, 0);

        Snapshot over = Api.Parse("{\"data\":{\"limit\":10,\"limit_remaining\":-0.25,\"limit_reset\":\"daily\"}}", now);
        Near("parse: overspend clamps to 100", over.PercentUsed, 100);

        Eq("parse: garbage is an error", Api.Parse("<html>", now).Ok, false);
        Eq("parse: an out-of-range number is an error", Api.Parse("{\"data\":{\"limit\":1e400,\"limit_remaining\":1}}", now).Ok, false);
        Eq("parse: an unknown reset kind is dropped", Api.Parse("{\"data\":{\"limit\":10,\"limit_remaining\":5,\"limit_reset\":\"weekly\\nforged=1\"}}", now).LimitReset, "");
        Eq("parse: missing data is an error", Api.Parse("{\"error\":{\"code\":401}}", now).Ok, false);

        // Reset windows (00:00 UTC; weeks start Monday).
        Eq("reset: daily", Reset.NextUtc("daily", now), (DateTime?)Utc(2026, 9, 19, 0, 0));
        Eq("reset: weekly from Friday", Reset.NextUtc("weekly", now), (DateTime?)Utc(2026, 9, 21, 0, 0));
        Eq("reset: weekly at Monday 00:00 is next Monday", Reset.NextUtc("weekly", Utc(2026, 9, 21, 0, 0)), (DateTime?)Utc(2026, 9, 28, 0, 0));
        Eq("reset: weekly on Sunday night", Reset.NextUtc("weekly", Utc(2026, 9, 20, 23, 59)), (DateTime?)Utc(2026, 9, 21, 0, 0));
        Eq("reset: monthly across a year", Reset.NextUtc("monthly", Utc(2026, 12, 31, 12, 0)), (DateTime?)Utc(2027, 1, 1, 0, 0));
        Eq("reset: none", Reset.NextUtc("", now), (DateTime?)null);

        // Countdown text.
        Eq("countdown: days", Reset.Countdown(Utc(2026, 9, 21, 0, 0), now), "2d");
        Eq("countdown: hours", Reset.Countdown(Utc(2026, 9, 18, 13, 30), now), "5h");
        Eq("countdown: minutes round up", Reset.Countdown(Utc(2026, 9, 18, 8, 50), Utc(2026, 9, 18, 7, 59)), "51m");
        Eq("countdown: never 0m", Reset.Countdown(now, now), "1m");

        // Settings.
        Settings st = Settings.Parse(new string[] { "# comment", "IntervalSec=5", "Label=OPENROUTER-LONG", "FillColor=#8B5CF6", "bogus", "GapPx=abc" });
        Eq("settings: interval clamps to the minimum", st.IntervalSec, Settings.MinIntervalSec);
        Eq("settings: label is capped", st.Label, "OPENROUT");
        Eq("settings: color parses", st.Fill.ToArgb(), System.Drawing.ColorTranslator.FromHtml("#8B5CF6").ToArgb());
        Eq("settings: bad value keeps the default", st.GapPx, 6);

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures == 0 ? 0 : 1;
    }
}
