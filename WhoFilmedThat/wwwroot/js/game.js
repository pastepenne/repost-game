// ═══════════════════════════════════════════
//  WHO REPOSTED THAT? — Client-side game logic
// ═══════════════════════════════════════════

let connection = null;
let setupMode = "create"; // 'create' | 'join'
let myPlayerId = null;
let roomCode = null;
let isHost = false;
let currentState = null;
let myVote = null;
let selfieStream = null;
let selfieDataUrl = null;
let voteTimerInterval = null;
let myUploadedVideos = []; // [{id, name}]

// ── Screen management ──

function showScreen(id) {
    document.querySelectorAll(".screen").forEach(s => s.classList.remove("active"));
    document.getElementById(id).classList.add("active");

    // Screen-specific init
    if (id === "setupScreen") initSetup();
}

function showError(msg) {
    const t = document.getElementById("errorToast");
    t.textContent = msg;
    t.style.display = "block";
    setTimeout(() => t.style.display = "none", 4000);
}

// ── SignalR Connection ──

async function connectSignalR() {
    connection = new signalR.HubConnectionBuilder()
        .withUrl("/gamehub")
        .withAutomaticReconnect()
        .build();

    connection.on("RoomCreated", code => {
        roomCode = code;
        isHost = true;
    });

    connection.on("Error", msg => showError(msg));

    connection.on("RoomState", state => {
        currentState = state;
        myPlayerId = connection.connectionId;
        isHost = state.hostId === myPlayerId;
        roomCode = state.code;

        updateLobby(state);
        updateUploadScreen(state);

        // Phase transitions
        if (state.phase === "Lobby") showScreen("lobbyScreen");
        else if (state.phase === "Upload") showScreen("uploadScreen");
    });

    connection.on("PlayVideo", data => {
        showScreen("playingScreen");
        playVideo(data);
    });

    connection.on("VoteUpdate", (count, total) => {
        const el = document.getElementById("voteStatus");
        el.style.display = "block";
        el.textContent = `Votes: ${count}/${total}`;
    });

    connection.on("Reveal", data => {
        showScreen("revealScreen");
        showReveal(data);
    });

    connection.on("Leaderboard", entries => {
        showScreen("leaderboardScreen");
        showLeaderboard(entries);
    });

    connection.on("UploadProgress", progress => {
        renderUploadProgress(progress);
    });

    await connection.start();
    console.log("SignalR connected:", connection.connectionId);
}

connectSignalR();

// ── SETUP SCREEN ──

async function initSetup() {
    const title = document.getElementById("setupTitle");
    const sub = document.getElementById("setupSub");
    const codeInput = document.getElementById("joinCode");
    const goBtn = document.getElementById("goBtn");

    if (setupMode === "create") {
        title.textContent = "CREATE ROOM";
        sub.textContent = "Take a selfie and pick a name";
        codeInput.style.display = "none";
        goBtn.textContent = "CREATE & JOIN";
    } else {
        title.textContent = "JOIN ROOM";
        sub.textContent = "Enter the room code & take a selfie";
        codeInput.style.display = "";
        goBtn.textContent = "JOIN GAME";
    }

    // Start camera for selfie
    selfieDataUrl = null;
    document.getElementById("selfieImg").style.display = "none";
    document.getElementById("selfieVideo").style.display = "";
    document.getElementById("takePhotoBtn").style.display = "";
    document.getElementById("retakeBtn").style.display = "none";

    try {
        selfieStream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "user", width: 320, height: 320 }, audio: false });
        document.getElementById("selfieVideo").srcObject = selfieStream;
    } catch (e) {
        showError("Camera access needed for your profile photo.");
    }

    checkSetupReady();
}

function takePhoto() {
    const video = document.getElementById("selfieVideo");
    const canvas = document.createElement("canvas");
    canvas.width = 240; canvas.height = 240;
    const ctx = canvas.getContext("2d");
    const s = Math.min(video.videoWidth, video.videoHeight);
    const sx = (video.videoWidth - s) / 2;
    const sy = (video.videoHeight - s) / 2;
    ctx.drawImage(video, sx, sy, s, s, 0, 0, 240, 240);
    selfieDataUrl = canvas.toDataURL("image/jpeg", 0.7);

    document.getElementById("selfieVideo").style.display = "none";
    const img = document.getElementById("selfieImg");
    img.src = selfieDataUrl;
    img.style.display = "";

    document.getElementById("takePhotoBtn").style.display = "none";
    document.getElementById("retakeBtn").style.display = "";

    if (selfieStream) { selfieStream.getTracks().forEach(t => t.stop()); selfieStream = null; }
    checkSetupReady();
}

async function retakePhoto() {
    selfieDataUrl = null;
    document.getElementById("selfieImg").style.display = "none";
    document.getElementById("selfieVideo").style.display = "";
    document.getElementById("takePhotoBtn").style.display = "";
    document.getElementById("retakeBtn").style.display = "none";

    try {
        selfieStream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "user", width: 320, height: 320 }, audio: false });
        document.getElementById("selfieVideo").srcObject = selfieStream;
    } catch (e) {}
    checkSetupReady();
}

function checkSetupReady() {
    const name = document.getElementById("playerName").value.trim();
    const code = document.getElementById("joinCode").value.trim();
    const ready = name && selfieDataUrl && (setupMode === "create" || code.length === 4);
    document.getElementById("goBtn").disabled = !ready;
}
document.getElementById("playerName").addEventListener("input", checkSetupReady);
document.getElementById("joinCode").addEventListener("input", checkSetupReady);

async function submitSetup() {
    const name = document.getElementById("playerName").value.trim();
    if (!name || !selfieDataUrl) return;

    if (setupMode === "create") {
        await connection.invoke("CreateRoom", name, selfieDataUrl);
    } else {
        const code = document.getElementById("joinCode").value.trim().toUpperCase();
        await connection.invoke("JoinRoom", code, name, selfieDataUrl);
    }
}

// ── LOBBY ──

function updateLobby(state) {
    document.getElementById("lobbyCode").textContent = state.code;
    document.getElementById("playerCount").textContent = state.players.length;

    const grid = document.getElementById("lobbyPlayers");
    grid.innerHTML = state.players.map(p => `
        <div class="player-bubble">
            <div class="player-avatar ${p.id === state.hostId ? 'host' : ''}">
                ${p.photoBase64 ? `<img src="${p.photoBase64}">` : `<div style="display:flex;align-items:center;justify-content:center;height:100%;font-size:1.5rem;color:#fff">${p.name[0].toUpperCase()}</div>`}
            </div>
            <div class="player-name">${esc(p.name)}${p.id === state.hostId ? ' 👑' : ''}</div>
        </div>
    `).join("");

    const startBtn = document.getElementById("startGameBtn");
    const spinner = document.getElementById("lobbySpinner");
    const hint = document.getElementById("lobbyHint");

    if (isHost) {
        startBtn.style.display = "";
        startBtn.disabled = state.players.length < 2;
        startBtn.textContent = `🚀 Start Game (${state.players.length} players)`;
        spinner.style.display = "none";
        hint.textContent = "Press start when everyone's in!";
    } else {
        startBtn.style.display = "none";
        spinner.style.display = "flex";
        hint.textContent = "Waiting for the host to start...";
    }
}

async function startGame() {
    await connection.invoke("StartGame");
}

// ── UPLOAD ──

const videoFileInput = document.getElementById("videoFileInput");
videoFileInput.addEventListener("change", handleFileSelect);

async function handleFileSelect() {
    const files = Array.from(videoFileInput.files);
    if (files.length === 0) return;

    const remaining = 30 - myUploadedVideos.length;
    const toUpload = files.slice(0, remaining);

    if (toUpload.length < files.length) {
        showError(`Only uploading ${toUpload.length} of ${files.length} (max 30 total)`);
    }

    document.getElementById("uploadingIndicator").style.display = "flex";

    for (const file of toUpload) {
        // Check duration
        const duration = await getVideoDuration(file);
        if (duration > 120) {
            showError(`"${file.name}" is longer than 2 minutes — skipped.`);
            continue;
        }

        const formData = new FormData();
        formData.append("playerId", myPlayerId);
        formData.append("video", file);

        try {
            const resp = await fetch(`/api/video/${roomCode}`, { method: "POST", body: formData });
            if (!resp.ok) {
                const text = await resp.text();
                showError(`Upload failed: ${text}`);
                continue;
            }
            const result = await resp.json();
            myUploadedVideos.push({ id: result.videoId, name: file.name });
            await connection.invoke("VideoUploaded", result.videoId);
        } catch (e) {
            showError(`Upload error: ${e.message}`);
        }
    }

    document.getElementById("uploadingIndicator").style.display = "none";
    videoFileInput.value = "";
    renderMyVideos();
}

function getVideoDuration(file) {
    return new Promise(resolve => {
        const video = document.createElement("video");
        video.preload = "metadata";
        video.onloadedmetadata = () => {
            URL.revokeObjectURL(video.src);
            resolve(video.duration || 0);
        };
        video.onerror = () => resolve(0);
        video.src = URL.createObjectURL(file);
    });
}

function renderMyVideos() {
    const list = document.getElementById("myVideosList");
    document.getElementById("uploadCount").textContent = myUploadedVideos.length;

    list.innerHTML = myUploadedVideos.map((v, i) => `
        <div class="upload-item">
            <span style="color:var(--purple); font-weight:800;">${i + 1}.</span>
            <span class="name">${esc(v.name)}</span>
            <button class="remove" onclick="removeVideo('${v.id}', ${i})">✕</button>
        </div>
    `).join("");
}

async function removeVideo(videoId, index) {
    try {
        await fetch(`/api/video/${roomCode}/${videoId}`, { method: "DELETE" });
        myUploadedVideos.splice(index, 1);
        renderMyVideos();
        await connection.invoke("VideoUploaded", "removed");
    } catch (e) {
        showError("Failed to remove video");
    }
}

function updateUploadScreen(state) {
    if (state.phase !== "Upload") return;
    const btn = document.getElementById("doneUploadingBtn");
    if (isHost && state.totalVideos > 0) {
        btn.style.display = "";
        btn.textContent = `✅ Start Playing (${state.totalVideos} videos)`;
    } else {
        btn.style.display = "none";
    }
}

function renderUploadProgress(progress) {
    const list = document.getElementById("uploadProgressList");
    list.innerHTML = progress.map(p => `
        <div class="progress-row">
            <div class="player-avatar" style="width:36px;height:36px;">
                ${getPlayerAvatar(p.id)}
            </div>
            <span style="font-weight:700; font-size:0.9rem;">${esc(p.name)}</span>
            <span style="color:var(--blue); font-weight:800; font-size:0.9rem;">${p.count} video${p.count !== 1 ? 's' : ''}</span>
            <span class="status">${p.count > 0 ? '✅' : '⏳'}</span>
        </div>
    `).join("");
}

async function startPlaying() {
    await connection.invoke("StartPlaying");
}

// ── PLAYING ──

function playVideo(data) {
    myVote = null;
    document.getElementById("voteStatus").style.display = "none";

    // Counter
    document.getElementById("videoCounter").textContent = `VIDEO ${data.index + 1}/${data.total}`;

    // Progress dots
    const dots = document.getElementById("progressDots");
    dots.innerHTML = Array.from({ length: data.total }, (_, i) =>
        `<div class="dot ${i < data.index ? 'done' : i === data.index ? 'current' : ''}"></div>`
    ).join("");

    // Video
    const video = document.getElementById("gameVideo");
    video.src = data.videoUrl;
    video.play().catch(() => {});

    // Vote grid
    const grid = document.getElementById("voteGrid");
    grid.innerHTML = currentState.players.map(p => `
        <button class="vote-btn" id="vote-${p.id}" onclick="castVote('${p.id}')">
            <div class="mini-avatar">
                ${p.photoBase64 ? `<img src="${p.photoBase64}">` : ''}
            </div>
            <span>${esc(p.name)}</span>
        </button>
    `).join("");

    // Timer
    startVoteTimer(currentState.voteTimeLeftSec);
}

function startVoteTimer(seconds) {
    if (voteTimerInterval) clearInterval(voteTimerInterval);
    let remaining = seconds;
    const el = document.getElementById("voteTimer");

    const tick = () => {
        const m = Math.floor(remaining / 60);
        const s = (remaining % 60).toString().padStart(2, "0");
        el.textContent = `⏱ ${m}:${s}`;
        el.className = remaining < 30 ? "timer-display urgent" : "timer-display";

        if (remaining <= 0) {
            clearInterval(voteTimerInterval);
            if (isHost) connection.invoke("ForceReveal");
        }
        remaining--;
    };
    tick();
    voteTimerInterval = setInterval(tick, 1000);
}

async function castVote(playerId) {
    if (myVote) return;
    myVote = playerId;

    // Highlight
    document.querySelectorAll(".vote-btn").forEach(b => b.style.pointerEvents = "none");
    document.getElementById(`vote-${playerId}`).classList.add("selected");

    await connection.invoke("CastVote", playerId);

    const el = document.getElementById("voteStatus");
    el.style.display = "block";
    el.textContent = "Vote locked! Waiting for others...";
}

// ── REVEAL ──

function showReveal(data) {
    if (voteTimerInterval) clearInterval(voteTimerInterval);

    const correct = myVote === data.correctPlayerId;
    document.getElementById("revealEmoji").textContent = correct ? "🎉" : "😅";
    const titleEl = document.getElementById("revealTitle");
    titleEl.textContent = correct ? "CORRECT!" : "WRONG!";
    titleEl.className = "title " + (correct ? "correct-banner" : "wrong-banner");
    document.getElementById("revealSubtitle").textContent = correct ? "You guessed it!" : "Better luck next time!";

    // Points
    const myScore = data.scores[myPlayerId] || 0;
    document.getElementById("revealPoints").textContent = correct ? "+1 point" : "+0 points";

    // Correct player
    const cp = currentState.players.find(p => p.id === data.correctPlayerId);
    document.getElementById("revealPlayer").innerHTML = cp ? `
        <div class="player-avatar" style="border-color:var(--green); width:70px; height:70px;">
            ${cp.photoBase64 ? `<img src="${cp.photoBase64}">` : `<div style="display:flex;align-items:center;justify-content:center;height:100%;font-size:2rem;color:#fff">${cp.name[0]}</div>`}
        </div>
        <span style="font-weight:800; font-size:1.1rem;">${esc(cp.name)}</span>
    ` : "";

    // Progress dots
    const idx = currentState.currentVideoIndex;
    const total = currentState.totalVideos;
    document.getElementById("revealDots").innerHTML = Array.from({ length: total }, (_, i) =>
        `<div class="dot ${i <= idx ? 'done' : ''}"></div>`
    ).join("");

    // Scores grid
    const scores = document.getElementById("revealScores");
    const sorted = [...currentState.players].sort((a, b) => (data.scores[b.id] || 0) - (data.scores[a.id] || 0));
    scores.innerHTML = sorted.map(p => `
        <div class="player-bubble">
            <div class="player-avatar" style="width:48px;height:48px;border-color:var(--yellow);">
                ${p.photoBase64 ? `<img src="${p.photoBase64}">` : `<div style="display:flex;align-items:center;justify-content:center;height:100%;font-size:1.2rem">${p.name[0]}</div>`}
            </div>
            <div class="player-name">${esc(p.name)}</div>
            <div style="color:var(--yellow);font-weight:800;font-size:0.9rem;">${data.scores[p.id] || 0}</div>
        </div>
    `).join("");

    // Host controls
    const nextBtn = document.getElementById("nextVideoBtn");
    const wait = document.getElementById("revealWait");
    if (isHost) {
        nextBtn.style.display = "";
        nextBtn.textContent = (idx + 1 < total) ? "Next Video →" : "🏆 See Leaderboard";
        wait.style.display = "none";
    } else {
        nextBtn.style.display = "none";
        wait.style.display = "";
    }
}

async function nextVideo() {
    await connection.invoke("NextVideo");
}

// ── LEADERBOARD ──

function showLeaderboard(entries) {
    const medals = ["🥇", "🥈", "🥉"];
    const colors = ["var(--yellow)", "#c0c0c0", "#cd7f32"];

    const list = document.getElementById("leaderboardList");
    list.innerHTML = entries.map((e, i) => `
        <div class="card lb-entry ${i === 0 ? 'first' : ''}">
            <span class="lb-rank">${medals[i] || '#' + e.rank}</span>
            <div class="player-bubble" style="flex-direction:row;gap:10px;">
                <div class="player-avatar" style="width:50px;height:50px;border-color:${colors[i] || 'var(--blue)'};">
                    ${e.photoBase64 ? `<img src="${e.photoBase64}">` : `<div style="display:flex;align-items:center;justify-content:center;height:100%;font-size:1.5rem">${e.name[0]}</div>`}
                </div>
                <span style="font-weight:700; font-size:1rem;">${esc(e.name)}</span>
            </div>
            <span class="lb-score" style="color:${colors[i] || '#fff'};">${e.score}</span>
        </div>
    `).join("");
}

// ── Utilities ──

function esc(text) {
    const d = document.createElement("div");
    d.textContent = text;
    return d.innerHTML;
}

function getPlayerAvatar(playerId) {
    const p = currentState?.players?.find(pl => pl.id === playerId);
    if (p?.photoBase64) return `<img src="${p.photoBase64}" style="width:100%;height:100%;object-fit:cover;">`;
    return `<div style="display:flex;align-items:center;justify-content:center;height:100%;font-size:1rem;color:#fff">${p?.name?.[0] || '?'}</div>`;
}
