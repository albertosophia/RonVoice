using System.Windows;
using System.Windows.Controls;
using RonVoice.App.ViewModels;
using RonVoice.App.Views;
using RonVoice.Core.Commands;

namespace RonVoice.Tests;

/// <summary>
/// Um erro de XAML — chave de recurso trocada, propriedade que nao existe —
/// so' aparece quando a tela abre. Este teste abre a tela.
/// </summary>
public class CommandsViewSmokeTests
{
    /// <summary>WPF exige STA; o xUnit roda em MTA.</summary>
    static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
        return result;
    }

    [Fact]
    public void TheCatalogueScreenLoadsAndBindsToTheViewModel()
    {
        var loaded = OnStaThread(() =>
        {
            var view = new CommandsView
            {
                DataContext = new CommandsViewModel(CommandMap.Load(CommandMapTests.MapPath)),
            };

            // Sem o Measure o WPF nao aplica os templates, e um erro dentro do
            // DataTemplate das linhas passaria despercebido.
            view.Measure(new Size(1000, 4000));
            view.Arrange(new Rect(0, 0, 1000, 4000));
            view.UpdateLayout();
            return view;
        });

        Assert.NotNull(loaded);
    }

    /// <summary>
    /// O x de remover pega o comando pelo ItemsControl ancestral. Se a busca
    /// de ancestral errar o alvo, o botao nasce desabilitado e nao remove nada.
    /// </summary>
    [Fact]
    public void TheRemoveButtonOnAUserPhraseFindsItsCommand()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ronvoice-view-{Guid.NewGuid():N}.json");
        try
        {
            var enabled = OnStaThread(() =>
            {
                var vm = new CommandsViewModel(
                    CommandMap.Load(CommandMapTests.MapPath), null, null, path, "pt");

                var row = vm.Groups.SelectMany(g => g.Orders).First(o => o.Id == "hold");
                row.Draft = "fica quieto ai";
                row.AddCommand.Execute(null);

                var view = new CommandsView { DataContext = vm };
                view.Measure(new Size(1000, 8000));
                view.Arrange(new Rect(0, 0, 1000, 8000));
                view.UpdateLayout();

                return Buttons(view).Any(b => (b.Content as string) == "✕" && b.IsEnabled);
            });

            Assert.True(enabled, "o x da frase do usuario nao achou o RemoveCommand");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    static IEnumerable<Button> Buttons(DependencyObject root)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Button button) yield return button;
            foreach (var nested in Buttons(child)) yield return nested;
        }
    }
}
