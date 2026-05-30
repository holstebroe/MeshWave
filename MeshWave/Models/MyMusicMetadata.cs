namespace MeshWave.Models
{
    public class MyMusicMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public int Year { get; set; }
        public bool IsReleased { get; set; }
        public int Version { get; set; } = 1;
    }
}
