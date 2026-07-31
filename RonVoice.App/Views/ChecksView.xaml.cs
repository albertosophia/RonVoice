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
    // Os mesmos tons do tema. Cor em C# escapa da varredura do XAML, e estes
    // eram os valores claros de antes do tema escuro — apagados sobre o fundo.
    static readonly SolidColorBrush Ok = new(Color.FromRgb(0x5F, 0xB3, 0x7A));
    static readonly SolidColorBrush Warning = new(Color.FromRgb(0xE8, 0xA3, 0x3D));
    static readonly SolidColorBrush Failed = new(Color.FromRgb(0xE0, 0x52, 0x52));

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
