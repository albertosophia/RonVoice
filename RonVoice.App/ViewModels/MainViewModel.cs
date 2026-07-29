namespace RonVoice.App.ViewModels;

/// <summary>
/// Dona das abas e da barra de estado. Não contém regra: só junta as peças,
/// porque a §9 do brief proíbe lógica de negócio neste projeto.
/// </summary>
public sealed class MainViewModel : ObservableBase
{
    int _selectedTabIndex;

    public StatusBarViewModel StatusBar { get; } = new();

    /// <summary>
    /// Atribuídos pela sessão logo após a construção, que é quem tem o mapa, os
    /// binds e a lista de microfones. Não nulos depois disso.
    /// </summary>
    public CommandsViewModel Commands { get; set; } = null!;
    public TestViewModel Test { get; set; } = null!;
    public SettingsViewModel Settings { get; set; } = null!;
    public ChecksViewModel Checks { get; set; } = null!;

    /// <summary>Chamado quando o Recarregar troca o catálogo inteiro.</summary>
    public void RaiseCommandsChanged() => Raise(nameof(Commands));

    /// <summary>0 = Comandos, 1 = Teste, 2 = Configuração. Abre em Comandos.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => Set(ref _selectedTabIndex, value);
    }
}
