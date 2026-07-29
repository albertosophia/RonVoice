using System.IO;
using System.Windows;
using RonVoice.App.ViewModels;

namespace RonVoice.App.Views;

public partial class FirstRunView : Window
{
    readonly FirstRunViewModel _vm = new();
    readonly string _language;
    readonly string _modelsDir;

    public bool Succeeded { get; private set; }

    public FirstRunView(string language, string modelsDir)
    {
        InitializeComponent();
        _language = language;
        _modelsDir = modelsDir;
        DataContext = _vm;
        RetryButton.Click += async (_, _) => await RunAsync();
        Loaded += async (_, _) => await RunAsync();
    }

    async Task RunAsync()
    {
        Directory.CreateDirectory(_modelsDir);
        Succeeded = await _vm.DownloadAsync(_language, _modelsDir);
        if (Succeeded) Close();
    }
}
