namespace WhoFilmedThat.Models;

public class Player
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? PhotoBase64 { get; set; }
    public string ConnectionId { get; set; } = "";
    public int Score { get; set; }
}

public class VideoEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string OwnerId { get; set; } = "";
    public string FileName { get; set; } = "";
    // Stored on disk, served via /api/video/{roomCode}/{id}
    public string DiskPath { get; set; } = "";
}

public enum GamePhase
{
    Lobby,
    Upload,
    Playing,
    Reveal,
    Leaderboard
}

public class GameRoom
{
    public string Code { get; set; } = "";
    public string HostId { get; set; } = "";
    public GamePhase Phase { get; set; } = GamePhase.Lobby;
    public List<Player> Players { get; set; } = new();
    public List<VideoEntry> Videos { get; set; } = new();
    public List<string> ShuffledVideoIds { get; set; } = new();
    public int CurrentVideoIndex { get; set; }
    public DateTime? VoteStartedAt { get; set; }

    // VideoId -> dict of PlayerId -> VotedForPlayerId
    public Dictionary<string, Dictionary<string, string>> Votes { get; set; } = new();

    public readonly Lock Lock = new();
}

// DTOs sent to clients
public record PlayerDto(string Id, string Name, string? PhotoBase64, int Score);
public record RoomStateDto(
    string Code,
    string HostId,
    string Phase,
    List<PlayerDto> Players,
    int TotalVideos,
    int CurrentVideoIndex,
    int VoteTimeLeftSec,
    int VideosUploadedByMe
);

public record VideoPlayDto(string VideoId, string VideoUrl, int Index, int Total);

public record RevealDto(
    string VideoId,
    string CorrectPlayerId,
    string CorrectPlayerName,
    Dictionary<string, string> AllVotes, // voterId -> votedForId
    Dictionary<string, int> Scores
);

public record LeaderboardEntryDto(string Id, string Name, string? PhotoBase64, int Score, int Rank);
