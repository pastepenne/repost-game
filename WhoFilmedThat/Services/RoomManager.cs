using System.Collections.Concurrent;
using WhoFilmedThat.Models;

namespace WhoFilmedThat.Services;

public class RoomManager
{
    private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();
    private static readonly Random Rng = new();

    public string GenerateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string code;
        do
        {
            code = new string(Enumerable.Range(0, 4).Select(_ => chars[Rng.Next(chars.Length)]).ToArray());
        } while (_rooms.ContainsKey(code));
        return code;
    }

    public GameRoom CreateRoom(string code, Player host)
    {
        var room = new GameRoom
        {
            Code = code,
            HostId = host.Id,
            Phase = GamePhase.Lobby,
            Players = [host]
        };
        _rooms[code] = room;
        return room;
    }

    public GameRoom? GetRoom(string code) => _rooms.GetValueOrDefault(code);

    public bool RemoveRoom(string code) => _rooms.TryRemove(code, out _);

    public Player? FindPlayerByConnection(string connectionId)
    {
        foreach (var room in _rooms.Values)
        {
            var p = room.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
            if (p != null) return p;
        }
        return null;
    }

    public GameRoom? FindRoomByConnection(string connectionId)
    {
        foreach (var room in _rooms.Values)
        {
            if (room.Players.Any(p => p.ConnectionId == connectionId))
                return room;
        }
        return null;
    }

    public void ShuffleVideos(GameRoom room)
    {
        var ids = room.Videos.Select(v => v.Id).ToList();
        // Fisher-Yates
        for (int i = ids.Count - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (ids[i], ids[j]) = (ids[j], ids[i]);
        }
        room.ShuffledVideoIds = ids;
        room.CurrentVideoIndex = 0;
    }

    public RoomStateDto ToStateDto(GameRoom room, string playerId)
    {
        var voteTimeSec = 300;
        if (room.VoteStartedAt.HasValue)
        {
            var elapsed = (int)(DateTime.UtcNow - room.VoteStartedAt.Value).TotalSeconds;
            voteTimeSec = Math.Max(0, 300 - elapsed);
        }

        var myVideoCount = room.Videos.Count(v => v.OwnerId == playerId);

        return new RoomStateDto(
            room.Code,
            room.HostId,
            room.Phase.ToString(),
            room.Players.Select(p => new PlayerDto(p.Id, p.Name, p.PhotoBase64, p.Score)).ToList(),
            room.Videos.Count,
            room.CurrentVideoIndex,
            voteTimeSec,
            myVideoCount
        );
    }
}
