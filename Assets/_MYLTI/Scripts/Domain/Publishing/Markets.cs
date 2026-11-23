using System;
using System.Collections.Generic;
using System.Linq;

namespace Mylti.Domain
{
    /// <summary>Time zones needed for the US + European markets, with their daylight-saving rules built in
    /// (no OS time-zone database needed, identical on every Windows install).</summary>
    public enum MarketZone { UsEastern, UsCentral, UsMountain, UsPacific, UkIreland, CentralEurope, EasternEurope }

    public static class MarketClock
    {
        /// <summary>UTC offset (hours) of a zone at a given UTC instant.</summary>
        public static int Offset(MarketZone z, DateTime utc)
        {
            switch (z)
            {
                case MarketZone.UsEastern: return UsDst(utc, -5) ? -4 : -5;
                case MarketZone.UsCentral: return UsDst(utc, -6) ? -5 : -6;
                case MarketZone.UsMountain: return UsDst(utc, -7) ? -6 : -7;
                case MarketZone.UsPacific: return UsDst(utc, -8) ? -7 : -8;
                case MarketZone.UkIreland: return EuDst(utc) ? 1 : 0;
                case MarketZone.CentralEurope: return EuDst(utc) ? 2 : 1;
                default: return EuDst(utc) ? 3 : 2;
            }
        }

        public static DateTime ToLocal(MarketZone z, DateTime utc) => utc.AddHours(Offset(z, utc));

        /// <summary>US: from the 2nd Sunday of March 02:00 local standard time to the 1st Sunday of November 02:00 local daylight time.</summary>
        static bool UsDst(DateTime utc, int standardOffset)
        {
            int y = utc.Year;
            var start = NthSunday(y, 3, 2).AddHours(2 - standardOffset);          // UTC
            var end = NthSunday(y, 11, 1).AddHours(2 - (standardOffset + 1));    // UTC
            return utc >= start && utc < end;
        }

        /// <summary>EU/UK: from the last Sunday of March 01:00 UTC to the last Sunday of October 01:00 UTC.</summary>
        static bool EuDst(DateTime utc)
        {
            int y = utc.Year;
            return utc >= LastSunday(y, 3).AddHours(1) && utc < LastSunday(y, 10).AddHours(1);
        }

        static DateTime NthSunday(int y, int month, int n)
        {
            var d = new DateTime(y, month, 1, 0, 0, 0, DateTimeKind.Utc);
            while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1);
            return d.AddDays(7 * (n - 1));
        }

        static DateTime LastSunday(int y, int month)
        {
            var d = new DateTime(y, month, DateTime.DaysInMonth(y, month), 0, 0, 0, DateTimeKind.Utc);
            while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(-1);
            return d;
        }

        public static string ZoneLabel(MarketZone z, DateTime utc)
        {
            int o = Offset(z, utc);
            switch (z)
            {
                case MarketZone.UsEastern: return o == -4 ? "EDT" : "EST";
                case MarketZone.UsCentral: return o == -5 ? "CDT" : "CST";
                case MarketZone.UsMountain: return o == -6 ? "MDT" : "MST";
                case MarketZone.UsPacific: return o == -7 ? "PDT" : "PST";
                case MarketZone.UkIreland: return o == 1 ? "BST" : "GMT";
                case MarketZone.CentralEurope: return o == 2 ? "CEST" : "CET";
                default: return o == 3 ? "EEST" : "EET";
            }
        }
    }

    /// <summary>One target country. Weight = share of the audience we plan for (editable in the app).</summary>
    public sealed class Market
    {
        public string Code, Name;
        public double Weight;
        public List<KeyValuePair<MarketZone, double>> Zones = new List<KeyValuePair<MarketZone, double>>();
        public double? RpmUsd;          // creator RPM, all niches (Dynamoi dataset, 20 Nov 2025 – 19 Nov 2026)
        public string English;          // English proficiency note (EF EPI)
        public string MetadataLanguage; // YouTube language code for translated title/description, null = English market
        public bool AutoDub;            // YouTube auto dubbing from English is offered for this language

        Market Z(MarketZone z, double share) { Zones.Add(new KeyValuePair<MarketZone, double>(z, share)); return this; }

        /// <summary>
        /// Default plan for an English-language channel aimed at the US first, then Europe. The weights are a planning
        /// judgement based on the research notes in docs/MARKET_RESEARCH.md (market size, RPM, English proficiency,
        /// auto-dubbing availability), not measured audience data: replace them with your YouTube Analytics
        /// “Top geographies” once the channel has views.
        /// </summary>
        public static List<Market> Defaults() => new List<Market>
        {
            new Market { Code = "US", Name = "United States", Weight = 0.60, RpmUsd = 4.91, English = "native" }
                .Z(MarketZone.UsEastern, 0.47).Z(MarketZone.UsCentral, 0.29).Z(MarketZone.UsMountain, 0.07).Z(MarketZone.UsPacific, 0.17),
            new Market { Code = "CA", Name = "Canada", Weight = 0.05, RpmUsd = 4.72, English = "native" }
                .Z(MarketZone.UsEastern, 0.62).Z(MarketZone.UsCentral, 0.12).Z(MarketZone.UsMountain, 0.12).Z(MarketZone.UsPacific, 0.14),
            new Market { Code = "GB", Name = "United Kingdom", Weight = 0.10, RpmUsd = 3.81, English = "native" }.Z(MarketZone.UkIreland, 1),
            new Market { Code = "IE", Name = "Ireland", Weight = 0.01, RpmUsd = 3.49, English = "native" }.Z(MarketZone.UkIreland, 1),
            new Market { Code = "DE", Name = "Germany", Weight = 0.05, RpmUsd = 1.30, English = "EF EPI 2026: 4th (611)", MetadataLanguage = "de", AutoDub = true }.Z(MarketZone.CentralEurope, 1),
            new Market { Code = "AT", Name = "Austria", Weight = 0.01, RpmUsd = 2.27, English = "EF EPI 2026: 2nd (613)", MetadataLanguage = "de", AutoDub = true }.Z(MarketZone.CentralEurope, 1),
            new Market { Code = "CH", Name = "Switzerland", Weight = 0.01, RpmUsd = 4.07, English = "high", MetadataLanguage = "de", AutoDub = true }.Z(MarketZone.CentralEurope, 1),
            new Market { Code = "NL", Name = "Netherlands", Weight = 0.03, RpmUsd = 2.92, English = "EF EPI 2026: 5th (609)", MetadataLanguage = "nl", AutoDub = true }.Z(MarketZone.CentralEurope, 1),
            new Market { Code = "BE", Name = "Belgium", Weight = 0.01, RpmUsd = 3.07, English = "very high", MetadataLanguage = "nl", AutoDub = true }.Z(MarketZone.CentralEurope, 1),
            new Market { Code = "SE", Name = "Sweden", Weight = 0.02, RpmUsd = 2.64, English = "EF EPI 2026: 1st (615)", MetadataLanguage = "sv" }.Z(MarketZone.CentralEurope, 1),
            new Market { Code = "DK", Name = "Denmark", Weight = 0.01, RpmUsd = 5.80, English = "EF EPI 2026: 3rd (612)", MetadataLanguage = "da" }.Z(MarketZone.CentralEurope, 1),
            new Market { Code = "NO", Name = "Norway", Weight = 0.01, RpmUsd = 5.83, English = "very high" }.Z(MarketZone.CentralEurope, 1),
            new Market { Code = "FI", Name = "Finland", Weight = 0.01, RpmUsd = 3.75, English = "very high", MetadataLanguage = "fi" }.Z(MarketZone.EasternEurope, 1),
            new Market { Code = "FR", Name = "France", Weight = 0.03, RpmUsd = 1.19, English = "moderate", MetadataLanguage = "fr", AutoDub = true }.Z(MarketZone.CentralEurope, 1),
            new Market { Code = "ES", Name = "Spain", Weight = 0.02, RpmUsd = 1.41, English = "moderate", MetadataLanguage = "es", AutoDub = true }.Z(MarketZone.CentralEurope, 1),
            new Market { Code = "IT", Name = "Italy", Weight = 0.02, RpmUsd = 1.28, English = "moderate", MetadataLanguage = "it", AutoDub = true }.Z(MarketZone.CentralEurope, 1),
            new Market { Code = "PL", Name = "Poland", Weight = 0.01, RpmUsd = 0.34, English = "high", MetadataLanguage = "pl", AutoDub = true }.Z(MarketZone.CentralEurope, 1),
        };

        /// <summary>Distinct metadata languages of the markets with weight &gt; 0.</summary>
        public static List<string> LanguagesFor(IEnumerable<Market> markets)
            => markets.Where(m => m.Weight > 0 && !string.IsNullOrEmpty(m.MetadataLanguage)).Select(m => m.MetadataLanguage).Distinct().ToList();
    }
}
