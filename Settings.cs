using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GallerySaver;

/// <summary>The screen saver's options, kept per user in the registry.</summary>
internal sealed class Settings
{
    private const string Key = @"Software\Euiyun\Gallery Screen Saver";
    public const string DefaultSite = "https://gallery.euiyun.com";

    public static readonly int[] Dwells = { 30, 60, 120, 300, 600, 1800 };
    public static readonly string[] Transitions = { "fade", "cross", "cut" };
    public static readonly string[] Variations = { "curated", "seed", "fixed" };
    public static readonly string[] Qualities = { "auto", "high", "medium", "low" };
    public static readonly string[] Languages = { "auto", "ko", "en" };

    public string Site { get; set; } = DefaultSite;
    /// <summary>Catalogue numbers ("001", "013"); empty means every work.</summary>
    public string Works { get; set; } = "";
    public int Dwell { get; set; } = 120;
    public string Transition { get; set; } = "fade";
    public string Variation { get; set; } = "curated";
    public bool Captions { get; set; } = true;
    public string Quality { get; set; } = "auto";
    public string Language { get; set; } = "auto";
    public bool SpanDisplays { get; set; }

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            if (key == null) return s;
            string Str(string name, string fallback) => key.GetValue(name) as string ?? fallback;
            string Pick(string name, string[] allowed, string fallback)
            {
                var value = Str(name, fallback);
                return allowed.Contains(value) ? value : fallback;
            }
            s.Site = NormaliseSite(Str(nameof(Site), DefaultSite)) ?? DefaultSite;
            s.Works = NormaliseWorks(Str(nameof(Works), ""));
            s.Dwell = key.GetValue(nameof(Dwell)) is int dwell && dwell >= 5 && dwell <= 86400 ? dwell : s.Dwell;
            s.Transition = Pick(nameof(Transition), Transitions, s.Transition);
            s.Variation = Pick(nameof(Variation), Variations, s.Variation);
            s.Captions = key.GetValue(nameof(Captions)) is not int captions || captions != 0;
            s.Quality = Pick(nameof(Quality), Qualities, s.Quality);
            s.Language = Pick(nameof(Language), Languages, s.Language);
            s.SpanDisplays = key.GetValue(nameof(SpanDisplays)) is 1;
        }
        catch
        {
            // Unreadable settings are not worth failing over: run with the defaults.
        }
        return s;
    }

    public void Save()
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        key.SetValue(nameof(Site), Site);
        key.SetValue(nameof(Works), Works);
        key.SetValue(nameof(Dwell), Dwell, RegistryValueKind.DWord);
        key.SetValue(nameof(Transition), Transition);
        key.SetValue(nameof(Variation), Variation);
        key.SetValue(nameof(Captions), Captions ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(nameof(Quality), Quality);
        key.SetValue(nameof(Language), Language);
        key.SetValue(nameof(SpanDisplays), SpanDisplays ? 1 : 0, RegistryValueKind.DWord);
    }

    /// <summary>An http(s) origin without a trailing slash, or null if it is not one.</summary>
    public static string? NormaliseSite(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    /// <summary>"1, 5 13" → "001,005,013".</summary>
    public static string NormaliseWorks(string text)
    {
        var ids = new List<string>();
        foreach (Match m in Regex.Matches(text, @"(?<![0-9])[0-9]{1,3}(?![0-9])"))
        {
            var id = int.Parse(m.Value).ToString("000");
            if (id != "000" && !ids.Contains(id)) ids.Add(id);
        }
        return string.Join(",", ids);
    }

    /// <summary>The exhibition page, in the web app's screen-saver mode (src/pages/Exhibition.tsx).</summary>
    public Uri ExhibitionUrl(bool spanning = false)
    {
        var q = new List<string> { "screensaver=1", "order=shuffle" };
        if (Works.Length > 0) q.Add("works=" + Works);
        if (Dwell != 120) q.Add("dwell=" + Dwell);
        if (Transition != "fade") q.Add("transition=" + Transition);
        if (Variation != "curated") q.Add("vary=" + Variation);
        if (!Captions) q.Add("captions=0");
        var quality = spanning && Quality == "auto" ? "low" : Quality;
        if (quality != "auto") q.Add("q=" + quality);
        return new Uri(Site + "/exhibition?" + string.Join("&", q));
    }
}
