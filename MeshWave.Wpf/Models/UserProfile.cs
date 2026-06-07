namespace MeshWave.Wpf.Models;

public class UserProfile
{
    public string DisplayName { get; set; } = "You";
    public string AvatarImagePath { get; set; } = string.Empty;
    public string AvatarIconPath { get; set; } = string.Empty;

    // Role
    public bool IsArtist { get; set; } = false;

    // Extended artist fields
    public string Bio { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string BannerImagePath { get; set; } = string.Empty;
}