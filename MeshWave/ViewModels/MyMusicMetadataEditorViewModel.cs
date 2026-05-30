using System.Windows.Input;
using MeshWave.Models;
using MeshWave.Mvvm;
using MeshWave.Services;

namespace MeshWave.ViewModels
{
    public class MyMusicMetadataEditorViewModel : ViewModelBase
    {
        private readonly MyMusicMetadataService _metadataService = new();
        private string _trackFilePath = string.Empty;
        private string _title = string.Empty;
        private string _artist = string.Empty;
        private string _album = string.Empty;
        private string _description = string.Empty;
        private string _genre = string.Empty;
        private int _year;

        public MyMusicMetadataEditorViewModel()
        {
            SaveCommand = new RelayCommand(_ => Save(), _ => !string.IsNullOrWhiteSpace(TrackFilePath));
        }

        public ICommand SaveCommand { get; }

        public string TrackFilePath
        {
            get => _trackFilePath;
            set => SetProperty(ref _trackFilePath, value);
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Artist
        {
            get => _artist;
            set => SetProperty(ref _artist, value);
        }

        public string Album
        {
            get => _album;
            set => SetProperty(ref _album, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public string Genre
        {
            get => _genre;
            set => SetProperty(ref _genre, value);
        }

        public int Year
        {
            get => _year;
            set => SetProperty(ref _year, value);
        }

        public void LoadTrack(string trackFilePath)
        {
            TrackFilePath = trackFilePath;
            var metadata = _metadataService.LoadForTrack(trackFilePath);
            Title = metadata.Title;
            Artist = metadata.Artist;
            Album = metadata.Album;
            Description = metadata.Description;
            Genre = metadata.Genre;
            Year = metadata.Year;
        }

        private void Save()
        {
            var metadata = new MyMusicMetadata
            {
                Title = Title,
                Artist = Artist,
                Album = Album,
                Description = Description,
                Genre = Genre,
                Year = Year
            };

            _metadataService.SaveForTrack(TrackFilePath, metadata);
        }
    }
}
