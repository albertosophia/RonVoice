using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using RonVoice.Core.Startup;

namespace RonVoice.App.Views;

/// <summary>Só wiring; a lógica está em StartupChecks e ChecksViewModel.</summary>
public partial class ChecksView : UserControl
{
    public ChecksView() => InitializeComponent();
}

/// <summary>Verde, âmbar, vermelho — a leitura mais rápida possível do estado.</summary>
public sealed class CheckStatusBrushConverter : IValueConverter
{
    // Qualificado: System.Drawing.Color tambem esta no escopo por causa do
    // NotifyIcon, e sem isto o nome fica ambiguo.
    static readonly SolidColorBrush Ok =
        new(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32));
    static readonly SolidColorBrush Warning =
        new(System.Windows.Media.Color.FromRgb(0xB8, 0x86, 0x0B));
    static readonly SolidColorBrush Failed =
        new(System.Windows.Media.Color.FromRgb(0xC6, 0x28, 0x28));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CheckStatus status
            ? status switch
            {
                CheckStatus.Ok => Ok,
                CheckStatus.Warning => Warning,
                _ => Failed,
            }
            : Failed;

    public object ConvertBack(
        object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
