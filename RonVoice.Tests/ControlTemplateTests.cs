using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RonVoice.App.ViewModels;
using RonVoice.App.Views;
using RonVoice.Core.Speech;

namespace RonVoice.Tests;

/// <summary>
/// Slider, ProgressBar e ScrollBar ganharam template proprio porque o padrao do
/// WPF se pinta com as cores do Windows e ignora o tema.
///
/// Estes testes existem porque os dois medidores vivem dentro de paineis
/// colapsados: o teste de janela monta a aba e nunca chega neles, entao um
/// template sem as partes obrigatorias — PART_Track, PART_Indicator — passaria
/// batido e so' apareceria como uma barra que nao anda, na tela que existe para
/// dizer se o microfone esta vivo.
/// </summary>
[Collection(WpfCollection.Name)]
public class ControlTemplateTests(WpfFixture wpf)
{
    static T Realize<T>(FrameworkElement view) where T : FrameworkElement
    {
        view.Measure(new Size(700, 700));
        view.Arrange(new Rect(0, 0, 700, 700));
        view.UpdateLayout();

        // A raiz conta: quando o teste monta o proprio controle, ele nao e'
        // filho de ninguem e a varredura por filhos nunca o acharia.
        if (view is T root) return root;

        return Find<T>(view) ?? throw new InvalidOperationException(
            $"{typeof(T).Name} não foi montado — o painel continua escondido?");
    }

    static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) return hit;
            if (Find<T>(child) is { } nested) return nested;
        }
        return null;
    }

    static T? Part<T>(Control control, string name) where T : DependencyObject =>
        control.Template.FindName(name, control) as T;

    /// <summary>
    /// A barra do teste de voz, com a gravacao ligada para o painel existir.
    /// </summary>
    [Fact]
    public void TheVoiceTestMeterIsBuiltFromTheThemeTemplate()
    {
        var (indicator, track) = wpf.Run(() =>
        {
            var vm = new TestViewModel();
            vm.BeginRecording();
            vm.Level = 0.5;

            var bar = Realize<ProgressBar>(new TestView { DataContext = vm });
            return (Part<FrameworkElement>(bar, "PART_Indicator"),
                    Part<FrameworkElement>(bar, "PART_Track"));
        });

        Assert.NotNull(track);
        Assert.NotNull(indicator);
    }

    /// <summary>
    /// Sem PART_Indicator com largura, a barra nunca anda — e a tela inteira
    /// existe para responder "o microfone esta captando?".
    /// </summary>
    [Fact]
    public void TheMeterActuallyFillsWithTheLevel()
    {
        var (empty, half) = wpf.Run(() =>
        {
            double Width(double level)
            {
                var vm = new TestViewModel();
                vm.BeginRecording();
                vm.Level = level;

                var bar = Realize<ProgressBar>(new TestView { DataContext = vm });
                bar.UpdateLayout();
                return Part<FrameworkElement>(bar, "PART_Indicator")!.ActualWidth;
            }
            return (Width(0.0), Width(1.0));
        });

        Assert.True(half > empty,
                    $"o indicador nao cresceu com o nivel: {empty} -> {half}");
    }

    [Fact]
    public void TheGuidedCheckMeterIsBuiltToo()
    {
        var indicator = wpf.Run(() =>
        {
            var vm = new ChecksViewModel();
            vm.BeginMicrophoneTest();
            vm.Level = 0.4;

            var bar = Realize<ProgressBar>(new ChecksView { DataContext = vm });
            return Part<FrameworkElement>(bar, "PART_Indicator");
        });

        Assert.NotNull(indicator);
    }

    /// <summary>
    /// O Slider precisa do Track com o Thumb dentro: sem isso ele desenha, mas
    /// nao arrasta, e o limiar vira um controle morto.
    ///
    /// Testado pela tela de verdade, e nao por um Slider solto: controle fora de
    /// arvore nenhuma nao recebe a Style implicita do tema, e o teste mediria
    /// uma coisa que o app nao tem.
    /// </summary>
    [Fact]
    public void TheThresholdSliderHasATrackWithAThumb()
    {
        var (track, thumb) = wpf.Run(() =>
        {
            var view = new SettingsView
            {
                DataContext = new SettingsViewModel(
                    RonVoice.Core.Config.AppSettings.Default,
                    ["Microfone (WIND)"],
                    new Dictionary<string, string>()),
            };

            var slider = Realize<Slider>(view);
            var t = Part<Track>(slider, "PART_Track");
            return (t, t?.Thumb);
        });

        Assert.NotNull(track);
        Assert.NotNull(thumb);
    }

    /// <summary>
    /// A barra de rolagem do catalogo: 70 ordens sempre transbordam, entao ela
    /// esta sempre na tela.
    /// </summary>
    [Fact]
    public void TheCatalogueScrollBarUsesTheThemeTemplate()
    {
        var problem = wpf.Run<string?>(() =>
        {
            var view = new CommandsView
            {
                DataContext = new CommandsViewModel(
                    RonVoice.Core.Commands.CommandMap.Load(CommandMapTests.MapPath),
                    null, null, null, "pt"),
            };
            view.Measure(new Size(700, 400));
            view.Arrange(new Rect(0, 0, 700, 400));
            view.UpdateLayout();

            var bar = Find<ScrollBar>(view);
            if (bar is null) return "não achei ScrollBar nenhuma na tela";

            // A barra so' monta o template quando fica visivel, e o
            // ScrollViewer a esconde ate' o conteudo transbordar.
            bar.ApplyTemplate();

            if (bar.Template is null) return "a ScrollBar ficou sem Template";
            return Part<Track>(bar, "PART_Track")?.Thumb is null
                ? $"PART_Track/Thumb ausente (largura {bar.Width})"
                : null;
        });

        Assert.Null(problem);
    }

    /// <summary>
    /// O resultado do teste de voz tambem so' existe depois de um resultado, e
    /// e' onde o fundo e a tinta mudam juntos.
    /// </summary>
    [Fact]
    public void TheVerdictPanelLaysOutForBothOutcomes()
    {
        var ok = wpf.Run(() =>
        {
            foreach (var outcome in new[] { VoiceTestOutcome.Success, VoiceTestOutcome.NoMatch })
            {
                var vm = new TestViewModel();
                vm.BeginRecording();
                vm.Show(new VoiceTestResult(
                    outcome, "open the door", 1.0, 0.5,
                    outcome == VoiceTestOutcome.Success
                        ? new RonVoice.Core.Matching.Intent(null, "door.toggle", false)
                        : null));

                var view = new TestView { DataContext = vm };
                view.Measure(new Size(700, 700));
                view.Arrange(new Rect(0, 0, 700, 700));
                view.UpdateLayout();
            }
            return true;
        });

        Assert.True(ok);
    }
}
