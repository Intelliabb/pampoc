namespace PamPocClient;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("mainpage", typeof(MainPage));
    }
}