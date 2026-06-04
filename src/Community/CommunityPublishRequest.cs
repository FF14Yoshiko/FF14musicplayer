namespace AllTimeSoundTrigger.Community;

public sealed class CommunityPublishRequest
{
    public string RepositoryPath { get; set; } = string.Empty;

    public string PackagePath { get; set; } = string.Empty;

    public string CoverPath { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string PackageVersion { get; set; } = "1.0.0";

    public string TagsText { get; set; } = string.Empty;

    public string Readme { get; set; } = string.Empty;

    public bool AllowOverwrite { get; set; }

    public bool PushToRemote { get; set; } = true;
}
