using System.Windows;
using MeshWave.ViewModels;

namespace MeshWave
{
    public partial class App : Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            if (MainWindow?.DataContext is ApplicationViewModel vm)
            {
                vm.ShutdownAsync().GetAwaiter().GetResult();
            }
            base.OnExit(e);
        }
    }
}

