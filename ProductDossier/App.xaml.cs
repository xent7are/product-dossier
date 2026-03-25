using ProductDossier.Data.Services;
using System.Windows;

namespace ProductDossier
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            FileEditTrackingService.StopAll();
            base.OnExit(e);
        }
    }

}
