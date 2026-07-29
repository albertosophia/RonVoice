using System.Drawing;
using RonVoice.Core.Pipeline;

namespace RonVoice.Tray;

/// <summary>
/// Quatro estados visíveis. Com o microfone sempre ligado, saber se ele está
/// ativo não é conforto: é a única forma de o jogador perceber que está sendo
/// ouvido.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    /// <summary>NotifyIcon.Text lança acima disto.</summary>
    const int MaxTooltip = 63;

    readonly NotifyIcon _icon;
    readonly Dictionary<ListenState, Icon> _icons = [];
    readonly Icon _faultIcon;

    public event Action? MuteRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _icons[ListenState.Listening] = Dot(Color.LimeGreen);
        _icons[ListenState.Idle] = Dot(Color.Gray);
        _icons[ListenState.Muted] = Dot(Color.OrangeRed);
        _faultIcon = Dot(Color.Red);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Mutar / desmutar", null, (_, _) => MuteRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = _icons[ListenState.Idle],
            Text = "RonVoice",
            Visible = true,
            ContextMenuStrip = menu,
        };
    }

    public void Show(ListenState state)
    {
        _icon.Icon = _icons[state];
        _icon.Text = Clip(state switch
        {
            ListenState.Listening => "RonVoice — escutando",
            ListenState.Idle => "RonVoice — jogo fora de foco",
            ListenState.Muted => "RonVoice — mudo",
            _ => "RonVoice",
        });
    }

    public void ShowFault(string message)
    {
        _icon.Icon = _faultIcon;
        _icon.Text = Clip("RonVoice — falha: " + message);
        _icon.ShowBalloonTip(5000, "RonVoice", message, ToolTipIcon.Error);
    }

    internal static string Clip(string text) =>
        text.Length > MaxTooltip ? text[..MaxTooltip] : text;

    static Icon Dot(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var i in _icons.Values) i.Dispose();
        _faultIcon.Dispose();
    }
}
