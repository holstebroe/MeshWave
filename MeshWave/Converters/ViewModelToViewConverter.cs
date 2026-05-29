using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using MeshWave.ViewModels;
using MeshWave.Views;

namespace MeshWave.Converters
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
                return new TextBlock { Text = vm3.StatusMessage };
            }
            if (value is SettingsViewModel vm4)
            {
                var view = new SettingsView();
                view.DataContext = vm4;
                return view;
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
