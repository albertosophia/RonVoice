using System.Windows;
using System.Windows.Threading;

namespace RonVoice.Tests;

/// <summary>
/// Uma única thread STA com dispatcher próprio, compartilhada por todos os
/// testes de tela.
///
/// A primeira tentativa criava uma <see cref="Application"/> por thread de
/// teste, e isso passava isolado e falhava na suíte inteira: Application.Current
/// é global do processo, então o segundo teste a encontrava já preenchida por
/// OUTRA thread e ia mexer nos recursos dela. Um teste instável é pior que
/// nenhum — ele ensina a ignorar vermelho.
/// </summary>
public sealed class WpfFixture : IDisposable
{
    readonly Thread _thread;
    Dispatcher _dispatcher = null!;
    Exception? _falhaAoMontar;

    /// <summary>
    /// O nome do assembly do app, perguntado a ele. Escrito à mão aqui, uma
    /// renomeação do projeto quebra este URI — e quebra CALADA: a exceção sobe
    /// numa thread STA, derruba o processo do teste, e a suíte termina cedo
    /// reportando só o que já tinha passado. Foi assim que 28 testes deixaram de
    /// rodar sem uma linha de vermelho.
    /// </summary>
    static string AssemblyDoApp =>
        typeof(RonVoice.App.RonVoiceSession).Assembly.GetName().Name!;

    public WpfFixture()
    {
        using var ready = new ManualResetEventSlim();

        _thread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;

            try
            {
                // O tema vive em App.xaml, que não é carregado nos testes: sem
                // montar o dicionário à mão, todo StaticResource estoura e o
                // teste não prova nada.
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        $"pack://application:,,,/{AssemblyDoApp};component/Theme.xaml"),
                });
            }
            catch (Exception ex)
            {
                // Guardar e seguir. Deixar subir aqui mata o processo inteiro e
                // leva junto os testes que ainda nem começaram — o resultado é
                // uma suíte que diz "tudo passou" com trinta testes a menos.
                _falhaAoMontar = ex;
            }

            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
    }

    /// <summary>
    /// Roda na thread da interface e devolve o resultado. Exceção de dentro
    /// aparece aqui com a pilha original.
    /// </summary>
    public T Run<T>(Func<T> work)
    {
        // Um teste vermelho por vez, dizendo o que houve. É o oposto de sumir.
        if (_falhaAoMontar is { } erro)
            throw new InvalidOperationException(
                "o tema não montou, então nenhum teste de tela vale nada: "
                + erro.Message, erro);

        return _dispatcher.Invoke(work);
    }

    public void Dispose() => _dispatcher.InvokeShutdown();
}

/// <summary>
/// Serializa os testes de tela: eles compartilham a thread e a Application, que
/// não aceitam ser usadas de duas em paralelo.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfCollection : ICollectionFixture<WpfFixture>
{
    public const string Name = "wpf";
}
