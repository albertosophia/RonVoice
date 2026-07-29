using System.Windows;
using RonVoice.App.ViewModels;

namespace RonVoice.App;

/// <summary>
/// Só faz wiring: a §9 do brief proíbe lógica de negócio neste projeto, e é o
/// que mantém tudo o que importa dentro dos view models, onde há teste.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
