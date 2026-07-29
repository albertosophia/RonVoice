using System.Windows.Input;
using RonVoice.App.Views;

namespace RonVoice.Tests;

/// <summary>
/// O nome tem que bater com o vocabulario do Input.ini, senao o aviso de
/// colisao de tecla compara coisas diferentes e nunca dispara.
/// </summary>
public class UeKeyNamesTests
{
    /// <summary>
    /// Tab foi a tecla que o autor tentou e nao funcionava: o WPF a consome
    /// para navegar entre controles antes de qualquer KeyDown comum.
    /// </summary>
    [Fact]
    public void TabHasAName() => Assert.Equal("Tab", UeKeyNames.From(Key.Tab));

    [Theory]
    [InlineData(Key.F8, "F8")]
    [InlineData(Key.F1, "F1")]
    [InlineData(Key.F12, "F12")]
    [InlineData(Key.A, "A")]
    [InlineData(Key.Z, "Z")]
    public void LettersAndFunctionKeysMatchTheGameVocabulary(Key key, string expected) =>
        Assert.Equal(expected, UeKeyNames.From(key));

    [Theory]
    [InlineData(Key.D0, "Zero")]
    [InlineData(Key.D1, "One")]
    [InlineData(Key.D9, "Nine")]
    public void DigitsUseTheWordFormLikeTheGameDoes(Key key, string expected) =>
        Assert.Equal(expected, UeKeyNames.From(key));

    [Theory]
    [InlineData(Key.NumPad0, "NumPadZero")]
    [InlineData(Key.NumPad7, "NumPadSeven")]
    public void NumpadDigitsToo(Key key, string expected) =>
        Assert.Equal(expected, UeKeyNames.From(key));

    [Theory]
    [InlineData(Key.Space, "SpaceBar")]
    [InlineData(Key.Back, "BackSpace")]
    [InlineData(Key.Capital, "CapsLock")]
    [InlineData(Key.Prior, "PageUp")]
    [InlineData(Key.Next, "PageDown")]
    [InlineData(Key.Escape, "Escape")]
    public void NamedKeysUseTheUnrealSpellingNotTheWpfOne(Key key, string expected) =>
        Assert.Equal(expected, UeKeyNames.From(key));

    /// <summary>
    /// Sao os nomes que aparecem no Input.ini real desta maquina, entao o aviso
    /// de colisao consegue compara-los.
    /// </summary>
    [Theory]
    [InlineData(Key.LeftCtrl, "LeftCtrl")]
    [InlineData(Key.Left, "Left")]
    [InlineData(Key.Delete, "Delete")]
    public void ArrowsAndEditingKeysKeepTheirNames(Key key, string expected) =>
        Assert.Equal(expected, UeKeyNames.From(key));
}
