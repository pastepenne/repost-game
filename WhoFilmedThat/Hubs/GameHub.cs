using Microsoft.AspNetCore.SignalR;
using WhoFilmedThat.Models;
using WhoFilmedThat.Services;

namespace WhoFilmedThat.Hubs;

public class GameHub : Hub
{
    private readonly RoomManager _rooms;
    private readonly ILogger<GameHub> _log;

    public GameHub(RoomManager rooms, ILogger<GameHub> log)
    {
        _rooms = rooms;
        _log = log;
    }

    // ── Create Room ──
    public async Task CreateRoom(string playerName, string? photoBase64)
    {
        var code = _rooms.GenerateCode();
        var player = new Player
        {
            Id = Context.ConnectionId,
            Name = playerName,
            PhotoBase64 = photoBase64,
            ConnectionId = Context.ConnectionId
        };

        var room = _rooms.CreateRoom(code, player);
        await Groups.AddToGroupAsync(Context.ConnectionId, code);

        _log.LogInformation("Room {Code} created by {Name}", code, playerName);
        await Clients.Caller.SendAsync("RoomCreated", code);
        await SendRoomState(room);
    }

    // ── Join Room ──
    public async Task JoinRoom(string code, string playerName, string? photoBase64)
    {
        code = code.ToUpper().Trim();
        var room = _rooms.GetRoom(code);
        if (room == null)
        {
            await Clients.Caller.SendAsync("Error", "Room not found!");
            return;
        }

        lock (room.Lock)
        {
            if (room.Phase != GamePhase.Lobby)
            {
                Clients.Caller.SendAsync("Error", "Game already in progress!").Wait();
                return;
            }

            if (room.Players.Any(p => p.ConnectionId == Context.ConnectionId))
            {
                Clients.Caller.SendAsync("Error", "Already in this room.").Wait();
                return;
            }

            var player = new Player
            {
                Id = Context.ConnectionId,
                Name = playerName,
                PhotoBase64 = photoBase64,
                ConnectionId = Context.ConnectionId
            };
            room.Players.Add(player);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, code);
        _log.LogInformation("{Name} joined room {Code}", playerName, code);
        await SendRoomState(room);
    }

    // ── Start Game (host only) → Upload phase ──
    public async Task StartGame()
    {
        var room = _rooms.FindRoomByConnection(Context.ConnectionId);
        if (room == null || room.HostId != Context.ConnectionId) return;
        if (room.Players.Count < 2)
        {
            await Clients.Caller.SendAsync("Error", "Need at least 2 players!");
            return;
        }

        lock (room.Lock)
        {
            room.Phase = GamePhase.Upload;
        }

        _log.LogInformation("Game started in room {Code}", room.Code);
        await SendRoomState(room);
    }

    // ── Video Uploaded notification ──
    public async Task VideoUploaded(string videoId)
    {
        var room = _rooms.FindRoomByConnection(Context.ConnectionId);
        if (room == null) return;
        await SendRoomState(room);
        await Clients.Group(room.Code).SendAsync("UploadProgress",
            room.Players.Select(p => new
            {
                p.Id,
                p.Name,
                Count = room.Videos.Count(v => v.OwnerId == p.Id)
            }));
    }

    // ── Done Uploading (player signals they're finished) ──
    private readonly HashSet<string> _doneUploading = new();

    // ── Start Playing (host only) ──
    public async Task StartPlaying()
    {
        var room = _rooms.FindRoomByConnection(Context.ConnectionId);
        if (room == null || room.HostId != Context.ConnectionId) return;
        if (room.Videos.Count == 0)
        {
            await Clients.Caller.SendAsync("Error", "No videos uploaded yet!");
            return;
        }

        lock (room.Lock)
        {
            _rooms.ShuffleVideos(room);
            room.Phase = GamePhase.Playing;
            room.VoteStartedAt = DateTime.UtcNow;
        }

        await SendCurrentVideo(room);
        await SendRoomState(room);
    }

    // ── Vote ──
    public async Task CastVote(string votedForPlayerId)
    {
        var room = _rooms.FindRoomByConnection(Context.ConnectionId);
        if (room == null || room.Phase != GamePhase.Playing) return;

        var videoId = room.ShuffledVideoIds[room.CurrentVideoIndex];

        lock (room.Lock)
        {
            if (!room.Votes.ContainsKey(videoId))
                room.Votes[videoId] = new Dictionary<string, string>();

            room.Votes[videoId][Context.ConnectionId] = votedForPlayerId;
        }

        // Broadcast vote count
        var voteCount = room.Votes.GetValueOrDefault(videoId)?.Count ?? 0;
        await Clients.Group(room.Code).SendAsync("VoteUpdate", voteCount, room.Players.Count);

        // Check if all voted
        if (voteCount >= room.Players.Count)
        {
            await DoReveal(room);
        }
    }

    // ── Force Reveal (host, for timeout) ──
    public async Task ForceReveal()
    {
        var room = _rooms.FindRoomByConnection(Context.ConnectionId);
        if (room == null || room.HostId != Context.ConnectionId) return;
        if (room.Phase != GamePhase.Playing) return;
        await DoReveal(room);
    }

    private async Task DoReveal(GameRoom room)
    {
        var videoId = room.ShuffledVideoIds[room.CurrentVideoIndex];
        var video = room.Videos.First(v => v.Id == videoId);
        var correctPlayer = room.Players.First(p => p.Id == video.OwnerId);
        var votes = room.Votes.GetValueOrDefault(videoId) ?? new Dictionary<string, string>();

        lock (room.Lock)
        {
            // Award points
            foreach (var (voterId, votedFor) in votes)
            {
                if (votedFor == video.OwnerId)
                {
                    var voter = room.Players.First(p => p.Id == voterId);
                    voter.Score++;
                }
            }
            room.Phase = GamePhase.Reveal;
        }

        var reveal = new RevealDto(
            videoId,
            correctPlayer.Id,
            correctPlayer.Name,
            votes,
            room.Players.ToDictionary(p => p.Id, p => p.Score)
        );

        await Clients.Group(room.Code).SendAsync("Reveal", reveal);
        await SendRoomState(room);
    }

    // ── Next Video (host) ──
    public async Task NextVideo()
    {
        var room = _rooms.FindRoomByConnection(Context.ConnectionId);
        if (room == null || room.HostId != Context.ConnectionId) return;

        lock (room.Lock)
        {
            room.CurrentVideoIndex++;
            if (room.CurrentVideoIndex >= room.ShuffledVideoIds.Count)
            {
                room.Phase = GamePhase.Leaderboard;
            }
            else
            {
                room.Phase = GamePhase.Playing;
                room.VoteStartedAt = DateTime.UtcNow;
            }
        }

        if (room.Phase == GamePhase.Leaderboard)
        {
            var leaderboard = room.Players
                .OrderByDescending(p => p.Score)
                .Select((p, i) => new LeaderboardEntryDto(p.Id, p.Name, p.PhotoBase64, p.Score, i + 1))
                .ToList();
            await Clients.Group(room.Code).SendAsync("Leaderboard", leaderboard);
        }
        else
        {
            await SendCurrentVideo(room);
        }

        await SendRoomState(room);
    }

    // ── Helpers ──
    private async Task SendRoomState(GameRoom room)
    {
        foreach (var player in room.Players)
        {
            var dto = _rooms.ToStateDto(room, player.Id);
            await Clients.Client(player.ConnectionId).SendAsync("RoomState", dto);
        }
    }

    private async Task SendCurrentVideo(GameRoom room)
    {
        if (room.CurrentVideoIndex >= room.ShuffledVideoIds.Count) return;
        var videoId = room.ShuffledVideoIds[room.CurrentVideoIndex];
        var dto = new VideoPlayDto(
            videoId,
            $"/api/video/{room.Code}/{videoId}",
            room.CurrentVideoIndex,
            room.ShuffledVideoIds.Count
        );
        await Clients.Group(room.Code).SendAsync("PlayVideo", dto);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var room = _rooms.FindRoomByConnection(Context.ConnectionId);
        if (room != null)
        {
            var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
            _log.LogInformation("{Name} disconnected from {Code}", player?.Name ?? "?", room.Code);
            // Don't remove during game, just log. Could add reconnect logic later.
        }
        await base.OnDisconnectedAsync(exception);
    }
}
