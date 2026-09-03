using System.Windows;
using Velopack;

namespace FHT.Access.App;

/// <summary>
/// Ponto de entrada explícito — necessário para que Velopack possa
/// processar argumentos de instalação/atualização ANTES de o WPF abrir.
/// </summary>
public sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack DEVE ser chamado antes de qualquer coisa.
        // Em caso de --velopack-firstrun / --velopack-install etc. o processo termina aqui.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
