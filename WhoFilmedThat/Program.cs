using WhoFilmedThat.Hubs;
using WhoFilmedThat.Models;
using WhoFilmedThat.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 512 * 1024; // 512KB for signalr messages
    options.EnableDetailedErrors = true;
});

builder.Services.AddSingleton<RoomManager>();

// Allow large file uploads (up to 200MB total for multiple videos)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 200 * 1024 * 1024;
});

// Listen on all interfaces so phones on the same network can connect
builder.WebHost.UseUrls("http://0.0.0.0:5000");

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<GameHub>("/gamehub");

// Video storage directory
var videoDir = Path.Combine(app.Environment.ContentRootPath, "VideoStorage");
Directory.CreateDirectory(videoDir);

// ── API: Upload Video ──
app.MapPost("/api/video/{roomCode}", async (
    string roomCode,
    HttpRequest request,
    RoomManager rooms) =>
{
    var room = rooms.GetRoom(roomCode.ToUpper());
    if (room == null)
        return Results.NotFound("Room not found");

    var form = await request.ReadFormAsync();
    var playerId = form["playerId"].ToString();
    var file = form.Files.GetFile("video");

    if (string.IsNullOrEmpty(playerId) || file == null)
        return Results.BadRequest("Missing playerId or video file");

    if (!room.Players.Any(p => p.Id == playerId))
        return Results.BadRequest("Player not in room");

    // Check video count limit per player
    var playerVideoCount = room.Videos.Count(v => v.OwnerId == playerId);
    if (playerVideoCount >= 30)
        return Results.BadRequest("Maximum 30 videos per player");

    // Save file
    var videoId = Guid.NewGuid().ToString("N")[..10];
    var ext = Path.GetExtension(file.FileName) ?? ".mp4";
    var fileName = $"{roomCode}_{videoId}{ext}";
    var filePath = Path.Combine(videoDir, fileName);

    await using (var stream = new FileStream(filePath, FileMode.Create))
    {
        await file.CopyToAsync(stream);
    }

    var entry = new VideoEntry
    {
        Id = videoId,
        OwnerId = playerId,
        FileName = fileName,
        DiskPath = filePath
    };

    lock (room.Lock)
    {
        room.Videos.Add(entry);
    }

    return Results.Ok(new { videoId, count = room.Videos.Count(v => v.OwnerId == playerId) });
}).DisableAntiforgery();

// ── API: Delete a video ──
app.MapDelete("/api/video/{roomCode}/{videoId}", (
    string roomCode, string videoId, RoomManager rooms) =>
{
    var room = rooms.GetRoom(roomCode.ToUpper());
    if (room == null) return Results.NotFound();

    lock (room.Lock)
    {
        var vid = room.Videos.FirstOrDefault(v => v.Id == videoId);
        if (vid == null) return Results.NotFound();

        room.Videos.Remove(vid);
        if (File.Exists(vid.DiskPath))
            File.Delete(vid.DiskPath);
    }

    return Results.Ok();
});

// ── API: Serve Video ──
app.MapGet("/api/video/{roomCode}/{videoId}", (
    string roomCode, string videoId, RoomManager rooms) =>
{
    var room = rooms.GetRoom(roomCode.ToUpper());
    var vid = room?.Videos.FirstOrDefault(v => v.Id == videoId);
    if (vid == null || !File.Exists(vid.DiskPath))
        return Results.NotFound();

    var ext = Path.GetExtension(vid.DiskPath).ToLower();
    var contentType = ext switch
    {
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        _ => "video/mp4"
    };

    return Results.File(vid.DiskPath, contentType, enableRangeProcessing: true);
});

// ── API: Get my videos ──
app.MapGet("/api/videos/{roomCode}/{playerId}", (
    string roomCode, string playerId, RoomManager rooms) =>
{
    var room = rooms.GetRoom(roomCode.ToUpper());
    if (room == null) return Results.NotFound();

    var myVideos = room.Videos
        .Where(v => v.OwnerId == playerId)
        .Select(v => new { v.Id, url = $"/api/video/{roomCode}/{v.Id}" })
        .ToList();

    return Results.Ok(myVideos);
});

app.Logger.LogInformation("==============================================");
app.Logger.LogInformation("  WHO REPOSTED THAT? is running on port 5000");
app.Logger.LogInformation("  Players connect to http://<YOUR_IP>:5000");
app.Logger.LogInformation("==============================================");

app.Run();
