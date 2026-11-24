using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace GallerySaver;

/// <summary>The dialog behind "Settings…" in Windows' Screen Saver Settings.</summary>
internal sealed class ConfigForm : Form
{
    private sealed record Option(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ComboBox dwell;
    private readonly ComboBox transition;
    private readonly ComboBox variation;
    private readonly ComboBox quality;
    private readonly ComboBox language;
    private readonly CheckBox captions;
    private readonly ComboBox displays;
    private readonly TextBox works;
    private readonly TextBox site;
    public Settings? PreviewSettings { get; private set; }

    public ConfigForm(Settings settings)
    {
        Text = Loc.L("GALLERY 화면 보호기 설정", "GALLERY Screen Saver Settings");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(16);

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));

        void Row(string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 16, 6) });
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 3, 0, 3);
            grid.Controls.Add(control);
        }
        ComboBox Choice(string current, params Option[] options)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            box.Items.AddRange(options);
            box.SelectedItem = options.FirstOrDefault(o => o.Value == current) ?? options[0];
            return box;
        }

        dwell = Choice(settings.Dwell.ToString(), Settings.Dwells.Append(settings.Dwell).Distinct().OrderBy(s => s)
            .Select(s => new Option(s.ToString(), s < 60 || s % 60 != 0
                ? Loc.L($"{s}초", $"{s} seconds") : Loc.L($"{s / 60}분", $"{s / 60} min")))
            .ToArray());
        transition = Choice(settings.Transition,
            new Option("fade", Loc.L("암전", "Through black")),
            new Option("cross", Loc.L("교차", "Crossfade")),
            new Option("cut", Loc.L("즉시", "Cut")));
        variation = Choice(settings.Variation,
            new Option("curated", Loc.L("작가의 레퍼런스", "Curated references")),
            new Option("seed", Loc.L("매번 새 시드", "New seed each time")),
            new Option("fixed", Loc.L("기본 상태만", "Default only")));
        quality = Choice(settings.Quality,
            new Option("auto", Loc.L("자동", "Automatic")),
            new Option("high", Loc.L("높음", "High")),
            new Option("medium", Loc.L("보통", "Medium")),
            new Option("low", Loc.L("낮음 (전력 절약)", "Low (saves power)")));
        language = Choice(settings.Language,
            new Option("auto", Loc.L("Windows 언어를 따름", "Follow Windows")),
            new Option("ko", "한국어"),
            new Option("en", "English"));
        captions = new CheckBox { Text = Loc.L("작품이 바뀔 때 캡션을 잠시 표시", "Show the caption briefly for each work"), Checked = settings.Captions, AutoSize = true };
        displays = Choice(settings.SpanDisplays ? "span" : "primary",
            new Option("primary", Loc.L("주 모니터만", "Primary display only")),
            new Option("span", Loc.L("여러 모니터에 이어서 표시", "Span all displays")));
        works = new TextBox { Text = settings.Works.Replace(",", ", ") };
        site = new TextBox { Text = settings.Site };

        Row(Loc.L("작품당 전시 시간", "Each work stays"), dwell);
        Row(Loc.L("전환", "Transition"), transition);
        Row(Loc.L("변형", "Variations"), variation);
        Row(Loc.L("화질", "Quality"), quality);
        Row(Loc.L("언어", "Language"), language);
        Row(Loc.L("캡션", "Captions"), captions);
        Row(Loc.L("모니터", "Displays"), displays);
        Row(Loc.L("작품 번호", "Works"), works);
        grid.Controls.Add(new Label());
        grid.Controls.Add(new Label
        {
            Text = Loc.L("비우면 전체. 예: 001, 005, 013", "Empty for all. For example: 001, 005, 013"),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 8),
        });
        Row(Loc.L("사이트 주소", "Site"), site);

        var preview = new Button { Text = Loc.L("전체 화면 미리 보기", "Full-screen preview"), AutoSize = true };
        preview.Click += (_, _) =>
        {
            if (Collect() is not { } chosen) return;
            PreviewSettings = chosen;
            DialogResult = DialogResult.Retry;
        };
        var browser = new Button { Text = Loc.L("브라우저에서 보기", "Open in browser"), AutoSize = true };
        browser.Click += (_, _) =>
        {
            if (Collect() is not { } chosen) return;
            Process.Start(new ProcessStartInfo(chosen.ExhibitionUrl().AbsoluteUri) { UseShellExecute = true });
        };
        var previews = new FlowLayoutPanel { AutoSize = true };
        previews.Controls.AddRange(new Control[] { preview, browser });
        Row(Loc.L("미리 보기", "Preview"), previews);
        var ok = new Button { Text = Loc.L("확인", "OK"), AutoSize = true, DialogResult = DialogResult.None };
        ok.Click += (_, _) =>
        {
            if (Collect() is not { } chosen) return;
            try
            {
                chosen.Save();
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        var cancel = new Button { Text = Loc.L("취소", "Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        AcceptButton = ok;
        CancelButton = cancel;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var about = new Label
        {
            Text = $"GALLERY — The Art of Computation · v{version.Major}.{version.Minor}.{version.Build}",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 10, 24, 0),
        };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });

        var footer = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.Controls.Add(about);
        footer.Controls.Add(buttons);

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        layout.Controls.Add(grid);
        layout.Controls.Add(footer);
        Controls.Add(layout);
    }

    /// <summary>The dialog's values as settings, or null (with a message) if the site is not a web address.</summary>
    private Settings? Collect()
    {
        var origin = Settings.NormaliseSite(site.Text);
        if (origin == null)
        {
            MessageBox.Show(this, Loc.L("사이트 주소는 http:// 또는 https:// 로 시작해야 합니다.", "The site must be an http:// or https:// address."),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            site.Focus();
            return null;
        }
        string Value(ComboBox box) => ((Option)box.SelectedItem).Value;
        return new Settings
        {
            Site = origin,
            Works = Settings.NormaliseWorks(works.Text),
            Dwell = int.Parse(Value(dwell)),
            Transition = Value(transition),
            Variation = Value(variation),
            Captions = captions.Checked,
            Quality = Value(quality),
            Language = Value(language),
            SpanDisplays = Value(displays) == "span",
        };
    }
}
