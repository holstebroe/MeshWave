using System.Globalization;
using System.Windows.Data;
using MeshWave.Wpf.ViewModels;
using MeshWave.Wpf.Views;

namespace MeshWave.Wpf.Converters
{
    public class ViewModelToViewConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LibraryViewModel vm)
            {
                var view = new LibraryView();
                view.DataContext = vm;
                return view;
            }
            if (value is PlaybackViewModel vm2)
            {
                var view = new PlaybackView();
                view.DataContext = vm2;
                return view;
            }
            if (value is HomeViewModel vm3)
            {
                var view = new HomeView();
                view.DataContext = vm3;
                return view;
            }
            if (value is SettingsViewModel vm4)
            {
                var view = new SettingsView();
                view.DataContext = vm4;
                return view;
            }
            if (value is BrowseViewModel vm5)
            {
                var view = new BrowseView();
                view.DataContext = vm5;
                return view;
            }
            if (value is CommunityViewModel vm6)
            {
                var view = new CommunityView();
                view.DataContext = vm6;
                return view;
            }
            return null!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
