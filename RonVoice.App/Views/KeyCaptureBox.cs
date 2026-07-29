using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RonVoice.App.Views;

/// <summary>
/// Captura a tecla que a pessoa apertar, em vez de pedir que ela digite o nome.
/// Existe porque teclas como Tab, Shift e as setas nunca chegariam a uma caixa
/// de texto comum: o WPF as consome para navegar entre controles.
/// </summary>
public sealed class KeyCaptureBox : Button
{
    public static readonly DependencyProperty KeyNameProperty =
        DependencyProperty.Register(
            nameof(KeyName), typeof(string), typeof(KeyCaptureBox),
            new FrameworkPropertyMetadata(
                null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnKeyNameChanged));

    bool _capturing;

    public string? KeyName
    {
        get => (string?)GetValue(KeyNameProperty);
        set => SetValue(KeyNameProperty, value);
    }

    public KeyCaptureBox()
    {
        Focusable = true;
        UpdateLabel();
        Click += (_, _) => BeginCapture();
        LostFocus += (_, _) => EndCapture();
    }

    void BeginCapture()
    {
        _capturing = true;
        Content = "aperte a tecla…";
        Keyboard.Focus(this);
    }

    void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        UpdateLabel();
    }

    /// <summary>
    /// PreviewKeyDown, não KeyDown: Tab, setas e Enter são consumidos pela
    /// navegação do WPF antes de chegarem ao KeyDown, e eram exatamente as
    /// teclas que não funcionavam.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturing) { base.OnPreviewKeyDown(e); return; }

        e.Handled = true;

        if (e.Key == Key.Escape) { EndCapture(); return; }

        // Uma tecla morta significa que veio um modificador junto; usamos a real.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Modificador sozinho não serve como PTT: ele nunca "solta" sozinho.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return;

        KeyName = UeKeyNames.From(key);
        EndCapture();
    }

    protected override void OnMouseDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        // Botões laterais do mouse são os PTT mais comuns; o polegar já descansa
        // neles, então capturá-los importa tanto quanto as teclas.
        if (_capturing && e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
        {
            KeyName = e.ChangedButton == MouseButton.XButton1
                ? "ThumbMouseButton" : "ThumbMouseButton2";
            EndCapture();
            e.Handled = true;
            return;
        }
        base.OnMouseDown(e);
    }

    static void OnKeyNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((KeyCaptureBox)d).UpdateLabel();

    void UpdateLabel() =>
        Content = string.IsNullOrWhiteSpace(KeyName) ? "clique e aperte uma tecla" : KeyName;
}
