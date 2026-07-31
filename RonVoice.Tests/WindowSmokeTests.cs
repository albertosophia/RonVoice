using System.Windows;
using RonVoice.App;
using RonVoice.App.ViewModels;
using RonVoice.Core.Commands;
using RonVoice.Core.Config;
using RonVoice.Core.Pipeline;

namespace RonVoice.Tests;

/// <summary>
/// Abre a janela inteira com o tema aplicado. O redesenho moveu tudo para
/// recursos e templates, e um erro ali — chave trocada, propriedade que não
/// existe, DataTrigger num tipo errado — só apareceria quando alguém abrisse o
/// programa. Nenhum teste de view model pegaria.
/// </summary>
[Collection(WpfCollection.Name)]
public class WindowSmokeTests(WpfFixture wpf)
{

    static MainViewModel Build(bool elevated, bool viaMod)
    {
        var map = CommandMap.Load(CommandMapTests.MapPath);
        var main = new MainViewModel
        {
            Commands = new CommandsViewModel(map, null, null, null, "pt", viaMod),
            Test = new TestViewModel(),
            Checks = new ChecksViewModel(),
            Settings = new SettingsViewModel(
                AppSettings.Default, ["Microfone (WIND)"], new Dictionary<string, string>()),
        };
        main.StatusBar.Elevated = elevated;
        main.StatusBar.ListenState = ListenState.Listening;
        main.StatusBar.MicrophoneName = "Microfone (WIND)";
        return main;
    }

    static Window Show(MainViewModel vm)
    {
        var window = new MainWindow(vm);
        // Sem Measure/Arrange o WPF não aplica os templates, e um erro dentro
        // deles passaria despercebido.
        window.Measure(new Size(940, 680));
        window.Arrange(new Rect(0, 0, 940, 680));
        window.UpdateLayout();
        return window;
    }

    [Fact]
    public void TheWholeWindowLoadsAndLaysOut() =>
        Assert.NotNull(wpf.Run(() => Show(Build(elevated: true, viaMod: true))));

    /// <summary>
    /// A ficha de falha tem template próprio, com DataTrigger no nível. O caminho
    /// "sem elevação" é justamente o que mais importa aparecer.
    /// </summary>
    [Fact]
    public void TheWindowLoadsWithAFailureChipShowing() =>
        Assert.NotNull(wpf.Run(() => Show(Build(elevated: false, viaMod: true))));

    /// <summary>Cada aba tem XAML próprio, e só a primeira é montada de saída.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryTabLoads(int index)
    {
        var loaded = wpf.Run(() =>
        {
            var vm = Build(elevated: true, viaMod: true);
            vm.SelectedTabIndex = index;
            return Show(vm);
        });

        Assert.NotNull(loaded);
    }

    /// <summary>
    /// As fichas são o que responde "por que não funciona". Um erro no template
    /// delas deixaria a barra vazia, que é o pior resultado possível aqui.
    /// </summary>
    [Fact]
    public void TheStatusChipsSayWhatIsWrongFirst()
    {
        var vm = Build(elevated: false, viaMod: true);
        vm.StatusBar.MicrophoneProblem = "MICROFONE TROCADO — gravando de outro";

        Assert.Equal(ChipLevel.Bad, vm.StatusBar.Chips[0].Level);
        Assert.Contains("elevação", vm.StatusBar.Chips[0].Label);
        Assert.Equal(ChipLevel.Bad, vm.StatusBar.Chips[1].Level);
    }

    [Fact]
    public void WhenNothingIsWrongTheFirstChipIsTheListeningState()
    {
        var vm = Build(elevated: true, viaMod: true);

        Assert.Equal(ChipLevel.Good, vm.StatusBar.Chips[0].Level);
        Assert.Equal("escutando", vm.StatusBar.Chips[0].Label);
    }

    /// <summary>
    /// Nenhuma janela pode abrir clara. Nao havia Style para Window, entao cada
    /// uma precisava lembrar de pintar o proprio fundo — e a da primeira
    /// execucao esqueceu, aparecendo branca antes do app existir.
    ///
    /// Isto mede o valor RESOLVIDO em tempo de execucao, que e' a unica forma
    /// de pegar "esqueceu de definir": no XAML nao ha nada escrito para
    /// procurar.
    /// </summary>
    [Theory]
    [InlineData(typeof(MainWindow))]
    [InlineData(typeof(RonVoice.App.Views.FirstRunView))]
    public void EveryWindowOpensDark(Type window)
    {
        var luminance = wpf.Run(() =>
        {
            var w = window == typeof(MainWindow)
                ? new MainWindow(Build(elevated: true, viaMod: true))
                : (Window)new RonVoice.App.Views.FirstRunView("en", Path.GetTempPath());

            w.ApplyTemplate();

            var brush = w.Background as System.Windows.Media.SolidColorBrush;
            if (brush is null) return -1.0;

            var c = brush.Color;
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        });

        Assert.True(luminance >= 0,
                    $"{window.Name} abriu sem fundo definido — vira branco do Windows");
        Assert.True(luminance < 0.25,
                    $"{window.Name} abriu com luminancia {luminance:0.00}: claro demais");
    }
}
