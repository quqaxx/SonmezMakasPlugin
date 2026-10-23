using System.Numerics;
using System.Reflection;
using System.Text.Json;
using AssettoServer.Network.ClientMessages;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Ai;
using AssettoServer.Server;
using AssettoServer.Server.Plugin;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace SonmezMakasPlugin;

public class MakasService : BackgroundService
{
    private const bool IsDrift = false;
    private const string ModeName = "Makas";

    private sealed class PlayerData
    {
        public ulong Guid;
        public int Session, Total, LastGain;
        public int Combo;
        public long LastBank;
        public float Pending;
        public float DriftIdle;
        public bool Horn;
        public readonly Queue<long> HornTimes = new();
        public byte Angle;
        public long CrashUntil;
        public bool RaceRequest;
    }

    private sealed class RaceState
    {
        public int Phase;                 // 0 yok, 1 sayım, 2 yarış, 3 sonuç
        public long PhaseStart;
        public readonly List<int> Racers = new();
        public readonly Dictionary<int, float> Dist = new();
        public readonly Dictionary<int, Vector3> Last = new();
        public int Winner = 255;
        public float WinnerTime;
    }

    private readonly MakasConfiguration _cfg;
    private readonly EntryCarManager _entryCarManager;
    private readonly object _lock = new();
    private readonly Dictionary<int, PlayerData> _players = new();
    private readonly Dictionary<ulong, int> _totals = new();
    private readonly Dictionary<(int, int), long> _pairCd = new();
    private readonly Dictionary<int, Vector3> _prevPos = new();
    private readonly RaceState _r = new();
    private readonly string _savePath = Path.Combine("cfg", "sonmez_" + ModeName.ToLowerInvariant() + "_puanlar.json");
    private long _tick;
    private long _lastSave;
    private long _lastDebug;
    private readonly List<AiState> _aiInit = new();
    private readonly List<AiState> _aiUninit = new();

    public MakasService(MakasConfiguration config, EntryCarManager entryCarManager,
        CSPServerScriptProvider scriptProvider, CSPClientMessageTypeManager clientMessages)
    {
        _cfg = config;
        _entryCarManager = entryCarManager;

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("hud.lua")
                           ?? throw new InvalidOperationException("hud.lua bulunamadi");
        scriptProvider.AddScript(stream, "hud.lua");
        clientMessages.RegisterOnlineEvent<InputPacket>(OnInput);
        LoadTotals();
    }

    // ---------- İstemciden gelen veri (10 Hz) ----------
    private void OnInput(ACTcpClient client, InputPacket p)
    {
        long now = Environment.TickCount64;
        lock (_lock)
        {
            if (!_players.TryGetValue(client.SessionId, out var d)) return;
            d.Angle = p.Angle;
            if (p.Crash == 1) d.CrashUntil = now + 1500;

            bool horn = p.Horn == 1;
            if (horn && !d.Horn)
            {
                d.HornTimes.Enqueue(now);
                while (d.HornTimes.Count > 0 && now - d.HornTimes.Peek() > _cfg.HornWindowMs) d.HornTimes.Dequeue();
                if (_cfg.RaceEnabled && d.HornTimes.Count >= _cfg.HornCount)
                {
                    d.HornTimes.Clear();
                    d.RaceRequest = true;
                }
            }
            d.Horn = horn;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { Tick(); }
            catch (Exception ex) { Log.Error(ex, "Sonmez{Mode} tick hatasi", ModeName); }
        }
    }

    private static float Horizontal(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    // Iki aracin bir tick icindeki en yakin gecis mesafesi (hizli gecislerde ornekleme kacirmasin diye)
    private float PairDist(int ka, Vector3 pa, int kb, Vector3 pb)
    {
        if (_prevPos.TryGetValue(ka, out var a0) && _prevPos.TryGetValue(kb, out var b0)
            && Horizontal(a0, pa) < 40f && Horizontal(b0, pb) < 40f)
        {
            float rx0 = a0.X - b0.X, rz0 = a0.Z - b0.Z;
            float rx1 = pa.X - pb.X, rz1 = pa.Z - pb.Z;
            float dx = rx1 - rx0, dz = rz1 - rz0;
            float len2 = dx * dx + dz * dz;
            float t = len2 > 1e-6f ? Math.Clamp(-(rx0 * dx + rz0 * dz) / len2, 0f, 1f) : 0f;
            float cx = rx0 + dx * t, cz = rz0 + dz * t;
            return MathF.Sqrt(cx * cx + cz * cz);
        }
        return Horizontal(pa, pb);
    }

    private void Bank(PlayerData d, int pts, long now)
    {
        if (now - d.LastBank <= _cfg.ComboWindowMs) d.Combo = Math.Min(d.Combo + 1, _cfg.MaxCombo);
        else d.Combo = 1;
        d.LastBank = now;
        int gain = pts * d.Combo;
        d.Session += gain;
        d.Total += gain;
        d.LastGain = gain;
    }

    private void Tick()
    {
        long now = Environment.TickCount64;
        _tick++;

        lock (_lock)
        {
            var active = new List<(int id, ACTcpClient client, PlayerData d, Vector3 pos, Vector3 vel, float kmh)>();
            var present = new HashSet<int>();
            var ai = new List<(int id, Vector3 pos, Vector3 vel)>();

            foreach (var car in _entryCarManager.EntryCars)
            {
                var client = car.Client;
                if (!IsDrift && client == null && car.AiControlled)
                {
                    // AI konumu EntryCar.Status'ta degil, AiState.Status icinde tutulur
                    int idx = 0;
                    _aiInit.Clear(); _aiUninit.Clear();
                    car.GetInitializedStates(_aiInit, _aiUninit);
                    foreach (var state in _aiInit)
                    {
                        ai.Add((car.SessionId * 16 + idx, state.Status.Position, state.Status.Velocity));
                        idx++;
                    }
                    continue;
                }
                if (client == null || !client.HasSentFirstUpdate) continue;
                int id = car.SessionId;
                present.Add(id);

                if (!_players.TryGetValue(id, out var d) || d.Guid != client.Guid)
                {
                    if (d != null) _totals[d.Guid] = d.Total;
                    d = new PlayerData { Guid = client.Guid };
                    if (_totals.TryGetValue(client.Guid, out int t)) d.Total = t;
                    _players[id] = d;
                }

                var pos = car.Status.Position;
                var vel = car.Status.Velocity;
                active.Add((id, client, d, pos, vel, vel.Length() * 3.6f));
            }

            foreach (int id in _players.Keys.Where(k => !present.Contains(k)).ToList())
            {
                _totals[_players[id].Guid] = _players[id].Total;
                _players.Remove(id);
            }

            // ---------- PUANLAMA ----------
            if (IsDrift)
            {
                foreach (var a in active)
                {
                    var d = a.d;
                    if (now < d.CrashUntil) { d.Pending = 0; d.Combo = 0; d.DriftIdle = 0; continue; }

                    bool drifting = a.kmh >= _cfg.DriftMinSpeedKmh && d.Angle >= _cfg.DriftMinAngle && d.Angle <= _cfg.DriftMaxAngle;
                    if (drifting)
                    {
                        d.Pending += d.Angle * a.kmh * _cfg.DriftPointFactor * 0.1f;
                        d.DriftIdle = 0;
                    }
                    else if (d.Pending > 0)
                    {
                        d.DriftIdle += 0.1f;
                        if (d.DriftIdle >= 1.0f)
                        {
                            if (d.Pending >= 20) Bank(d, (int)d.Pending, now);
                            d.Pending = 0; d.DriftIdle = 0;
                        }
                    }
                }
            }
            else
            {
                // Oyuncu - oyuncu
                for (int i = 0; i < active.Count; i++)
                {
                    var a = active[i];
                    if (a.kmh < _cfg.MakasMinSpeedKmh || now < a.d.CrashUntil) continue;
                    for (int j = i + 1; j < active.Count; j++)
                    {
                        var b = active[j];
                        if (b.kmh < _cfg.MakasMinSpeedKmh || now < b.d.CrashUntil) continue;
                        if (Horizontal(a.pos, b.pos) > 60f) continue;
                        float dist = PairDist(a.id, a.pos, b.id, b.pos);
                        if (dist > _cfg.MakasCloseDistance) continue;

                        var key = (Math.Min(a.id, b.id), Math.Max(a.id, b.id));
                        if (_pairCd.TryGetValue(key, out long until) && now < until) continue;

                        float dot = Vector3.Dot(Vector3.Normalize(a.vel), Vector3.Normalize(b.vel));
                        int pts = (int)(50 + (_cfg.MakasCloseDistance - dist) / _cfg.MakasCloseDistance * 150);

                        if (dot > 0.5f)
                        {
                            if (MathF.Abs(a.kmh - b.kmh) < _cfg.MakasMinRelSpeedKmh) continue;
                            Bank((a.kmh > b.kmh ? a : b).d, pts, now);   // geçen (hızlı) araç alır
                        }
                        else if (dot < -0.5f)
                        {
                            Bank(a.d, pts, now);                          // karşılıklı geçişte ikisi de alır
                            Bank(b.d, pts, now);
                        }
                        else continue;

                        _pairCd[key] = now + _cfg.MakasPairCooldownMs;
                    }
                }

                // Oyuncu - trafik (AI) araçları
                foreach (var a in active)
                {
                    if (a.kmh < _cfg.MakasMinSpeedKmh || now < a.d.CrashUntil) continue;
                    foreach (var b in ai)
                    {
                        if (Horizontal(a.pos, b.pos) > 60f) continue;
                        float rel = (a.vel - b.vel).Length() * 3.6f;
                        if (rel < _cfg.MakasMinRelSpeedKmh) continue;
                        float dist = PairDist(a.id, a.pos, 1000 + b.id, b.pos);
                        if (dist > _cfg.MakasCloseDistance) continue;

                        var key = (a.id, 1000 + b.id);
                        if (_pairCd.TryGetValue(key, out long until) && now < until) continue;

                        int pts = (int)(50 + (_cfg.MakasCloseDistance - dist) / _cfg.MakasCloseDistance * 150);
                        Bank(a.d, pts, now);
                        _pairCd[key] = now + _cfg.MakasPairCooldownMs;
                    }
                }

                // ---------- TEŞHİS (cfg: DebugLog) ----------
                if (_cfg.DebugLog && now - _lastDebug > 3000)
                {
                    _lastDebug = now;
                    int moving = ai.Count(b => b.vel.Length() > 1f);
                    int zeroPos = ai.Count(b => b.pos == Vector3.Zero);
                    foreach (var a in active)
                    {
                        float nearest = -1f, nearestRel = 0f;
                        foreach (var b in ai)
                        {
                            float h = Horizontal(a.pos, b.pos);
                            if (nearest < 0f || h < nearest) { nearest = h; nearestRel = (a.vel - b.vel).Length() * 3.6f; }
                        }
                        Log.Information("[Sonmez{Mode}] DEBUG oyuncu={Id} hiz={Kmh:0}kmh ai_sayisi={Ai} hareketli_ai={Mov} sifir_konum_ai={Zero} en_yakin_ai={Near:0.0}m goreli_hiz={Rel:0}kmh (esikler: minhiz={MinSpd}, mesafe={Close}, gorelihiz={MinRel})",
                            ModeName, a.id, a.kmh, ai.Count, moving, zeroPos, nearest, nearestRel,
                            _cfg.MakasMinSpeedKmh, _cfg.MakasCloseDistance, _cfg.MakasMinRelSpeedKmh);
                    }
                }

                foreach (var a in active) _prevPos[a.id] = a.pos;
                foreach (var b in ai) _prevPos[1000 + b.id] = b.pos;

                foreach (var k in _pairCd.Where(kv => kv.Value < now - 10000).Select(kv => kv.Key).ToList()) _pairCd.Remove(k);
            }

            // ---------- YARIŞ ----------
            UpdateRace(active, now);

            // ---------- HUD ----------
            var top = active.OrderByDescending(x => x.d.Total).Take(3).ToList();
            bool fast = _r.Phase != 0;
            if (_tick % (fast ? 2 : 5) == 0)
            {
                var ranking = _r.Racers.Where(_r.Dist.ContainsKey).OrderByDescending(x => _r.Dist[x]).ToList();
                foreach (var a in active)
                {
                    bool inRace = _r.Racers.Contains(a.id);
                    byte race = 0;
                    if (_r.Phase == 3) race = 3;
                    else if (inRace && _r.Phase != 0) race = (byte)_r.Phase;

                    int cd = 0;
                    if (_r.Phase == 1)
                        cd = Math.Max(0, _cfg.CountdownSeconds - (int)((now - _r.PhaseStart) / 1000));

                    var pk = new StatePacket
                    {
                        Score = a.d.Session,
                        Total = a.d.Total,
                        Combo = (byte)(now - a.d.LastBank <= _cfg.ComboWindowMs ? a.d.Combo : 0),
                        Pending = (int)a.d.Pending,
                        Race = race,
                        Countdown = (byte)cd,
                        Progress = _r.Dist.TryGetValue(a.id, out var pr) ? pr : 0f,
                        Place = (byte)(inRace ? ranking.IndexOf(a.id) + 1 : 0),
                        Racers = (byte)_r.Racers.Count,
                        Winner = (byte)_r.Winner,
                        WinnerTime = _r.WinnerTime,
                        RaceDistance = _cfg.RaceDistanceMeters,
                        Top1 = top.Count > 0 ? (byte)top[0].id : (byte)255, Top1Score = top.Count > 0 ? top[0].d.Total : 0,
                        Top2 = top.Count > 1 ? (byte)top[1].id : (byte)255, Top2Score = top.Count > 1 ? top[1].d.Total : 0,
                        Top3 = top.Count > 2 ? (byte)top[2].id : (byte)255, Top3Score = top.Count > 2 ? top[2].d.Total : 0,
                    };
                    a.client.SendPacket(pk);
                }
            }

            if (now - _lastSave > 30000) { SaveTotals(); _lastSave = now; }
        }
    }

    private void UpdateRace(List<(int id, ACTcpClient client, PlayerData d, Vector3 pos, Vector3 vel, float kmh)> active, long now)
    {
        // Başlatma
        if (_r.Phase == 0 && _cfg.RaceEnabled)
        {
            foreach (var a in active.Where(x => x.d.RaceRequest))
            {
                var near = active.Where(o => _cfg.RaceRadiusMeters <= 0f || Horizontal(o.pos, a.pos) <= _cfg.RaceRadiusMeters).ToList();   // 0 = sunucudaki herkes
                if (near.Count < 2)
                {
                    a.client.SendChatMessage("Yakında rakip yok! Yarış için en az 2 oyuncu 20 metre içinde olmalı.");
                    continue;
                }
                _r.Racers.Clear(); _r.Dist.Clear(); _r.Last.Clear();
                foreach (var n in near) { _r.Racers.Add(n.id); _r.Dist[n.id] = 0f; }
                _r.Winner = 255; _r.WinnerTime = 0;
                _r.Phase = 1; _r.PhaseStart = now;
                foreach (var n in near)
                    n.client.SendChatMessage($"YARIŞ BAŞLIYOR! {_cfg.RaceDistanceMeters / 1000f:0.0} km - hazır ol!");
                break;
            }
        }
        foreach (var a in active) a.d.RaceRequest = false;

        if (_r.Phase == 1)
        {
            if (now - _r.PhaseStart >= _cfg.CountdownSeconds * 1000L)
            {
                _r.Phase = 2; _r.PhaseStart = now;
                foreach (var a in active.Where(x => _r.Racers.Contains(x.id))) _r.Last[a.id] = a.pos;
            }
        }
        else if (_r.Phase == 2)
        {
            _r.Racers.RemoveAll(id => active.All(a => a.id != id));
            if (_r.Racers.Count < 2 || now - _r.PhaseStart > _cfg.RaceTimeoutSeconds * 1000L)
            {
                _r.Phase = 0; _r.Racers.Clear();
                return;
            }

            int best = -1; float bestDist = 0;
            foreach (var a in active.Where(x => _r.Racers.Contains(x.id)))
            {
                if (_r.Last.TryGetValue(a.id, out var last))
                {
                    float step = Vector3.Distance(last, a.pos);
                    if (step < 80f) _r.Dist[a.id] += step;     // ışınlanma/boşluk sayma
                }
                _r.Last[a.id] = a.pos;
                if (_r.Dist[a.id] >= _cfg.RaceDistanceMeters && _r.Dist[a.id] > bestDist)
                { best = a.id; bestDist = _r.Dist[a.id]; }
            }

            if (best >= 0)
            {
                _r.Winner = best;
                _r.WinnerTime = (now - _r.PhaseStart) / 1000f;
                _r.Phase = 3; _r.PhaseStart = now;
                var w = active.First(x => x.id == best);
                w.d.Session += _cfg.RaceWinBonus; w.d.Total += _cfg.RaceWinBonus;
                _entryCarManager.BroadcastChat($"YARIŞI {w.client.Name} KAZANDI! Süre: {_r.WinnerTime:0.00} sn");
            }
        }
        else if (_r.Phase == 3 && now - _r.PhaseStart > _cfg.FinishBannerSeconds * 1000L)
        {
            _r.Phase = 0; _r.Racers.Clear(); _r.Dist.Clear(); _r.Winner = 255;
        }
    }

    // ---------- Kalıcı toplam puan ----------
    private void LoadTotals()
    {
        try
        {
            if (File.Exists(_savePath))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_savePath));
                if (data != null)
                    foreach (var kv in data) if (ulong.TryParse(kv.Key, out var g)) _totals[g] = kv.Value;
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Puan dosyasi okunamadi"); }
    }

    private void SaveTotals()
    {
        try
        {
            foreach (var p in _players.Values) _totals[p.Guid] = p.Total;
            Directory.CreateDirectory("cfg");
            File.WriteAllText(_savePath, JsonSerializer.Serialize(_totals.ToDictionary(k => k.Key.ToString(), v => v.Value)));
        }
        catch (Exception ex) { Log.Warning(ex, "Puan dosyasi yazilamadi"); }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_lock) SaveTotals();
        return base.StopAsync(cancellationToken);
    }
}
