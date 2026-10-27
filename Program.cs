using Velopack;

namespace HukukErp;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Kurulum/güncelleme/kaldırma kancalarını işler; normal açılışta hemen döner.
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
