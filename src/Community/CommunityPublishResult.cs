namespace AllTimeSoundTrigger.Community;

public sealed record CommunityPublishResult(
    bool Success,
    string Message,
    string PackDirectory,
    CommunityPackInfo? Pack,
    string GitOutput);
