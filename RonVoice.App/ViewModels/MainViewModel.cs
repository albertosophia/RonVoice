namespace RonVoice.App.ViewModels;

/// <summary>
/// Dona das abas e da barra de estado. Não contém regra: só junta as peças,
/// porque a §9 do brief proíbe lógica de negócio neste projeto.
/// </summary>
public sealed class MainViewModel : ObservableBase
{
    int _selectedTabIndex;

    public StatusBarViewModel StatusBar { get; } = new();

    /// <summary>0 = Comandos, 1 = Teste, 2 = Configuração. Abre em Comandos.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => Set(ref _selectedTabIndex, value);
    }
}
