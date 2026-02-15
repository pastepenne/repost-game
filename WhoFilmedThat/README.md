# 🎬 Who Reposted That?

A multiplayer repost guessing party game. Players upload their saved reposts, then everyone tries to guess whose repost it is.

## How to Play

1. **Create a Room** — One person creates a room and gets a 4-letter code
2. **Join** — Everyone else joins using the code (same WiFi network)
3. **Upload Reposts** — Each player picks up to 30 reposts from their gallery (max 2 min each)
4. **Guess** — Reposts play one at a time in random order. Everyone votes on whose repost it is
5. **Score** — +1 point for each correct guess. After all videos, the leaderboard crowns the winner!

## Running the Game

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- JetBrains Rider (or any IDE)

### From Rider
1. Open `WhoFilmedThat.sln`
2. Click **Run** (▶️)
3. The server starts at `http://0.0.0.0:5000`

### From Terminal
```bash
cd WhoFilmedThat
dotnet run
```

### Connecting Phones
1. Find your PC's local IP (e.g., `ipconfig` → `192.168.1.x`)
2. On each phone's browser, go to `http://192.168.1.x:5000`
3. Make sure all devices are on the **same WiFi network**
4. Allow camera access (needed for the profile selfie)

## Architecture

- **ASP.NET Core 8** — Web server
- **SignalR** — Real-time WebSocket communication between all players
- **Vanilla JS** — No framework needed for the mobile frontend
- **Videos stored on disk** — `VideoStorage/` folder in the project root

## Game Rules

- Each player can upload up to **30 videos**, max **2 minutes** each
- Videos are shuffled randomly so nobody knows the order
- Everyone votes simultaneously — you have **5 minutes** per video
- You get **1 point** for a correct guess, **0** for wrong (including your own videos)
- The host controls the flow: starting the game, advancing between rounds

## Tech Notes

- Server listens on `0.0.0.0:5000` so it's accessible from other devices on the LAN
- Max upload size is 200MB total (configurable in `Program.cs`)
- Videos are served with range request support for proper seeking on mobile
- Profile photos are taken live (no gallery) and sent as base64 via SignalR
