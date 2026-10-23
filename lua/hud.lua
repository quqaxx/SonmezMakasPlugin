-- Sönmez Game World | Makas + Yarış HUD  (CSP online script)
local MODE = "Makas"
local KEY = "SonmezMakas"
local IS_DRIFT = false

-- ===== Sunucu -> İstemci =====
local S = { score=0,total=0,combo=0,pending=0,race=0,cd=0,prog=0,place=0,racers=0,winner=255,wtime=0,
            t1=255,t1s=0,t2=255,t2s=0,t3=255,t3s=0,dist=1500 }
local lastRace, goTime = 0, -10
local lastCd, cdTime = 0, -10

ac.OnlineEvent({
  ac.StructItem.key(KEY .. 'State'),
  s01score=ac.StructItem.int32(), s02total=ac.StructItem.int32(), s03combo=ac.StructItem.byte(),
  s04pending=ac.StructItem.int32(), s05race=ac.StructItem.byte(), s06cd=ac.StructItem.byte(),
  s07prog=ac.StructItem.float(), s08place=ac.StructItem.byte(), s09racers=ac.StructItem.byte(),
  s10winner=ac.StructItem.byte(), s11wtime=ac.StructItem.float(),
  s12t1=ac.StructItem.byte(), s13t1s=ac.StructItem.int32(),
  s14t2=ac.StructItem.byte(), s15t2s=ac.StructItem.int32(),
  s16t3=ac.StructItem.byte(), s17t3s=ac.StructItem.int32(),
  s18dist=ac.StructItem.float(),
}, function (sender, d)
  S.score, S.total, S.combo, S.pending = d.s01score, d.s02total, d.s03combo, d.s04pending
  S.race, S.cd, S.prog, S.place, S.racers = d.s05race, d.s06cd, d.s07prog, d.s08place, d.s09racers
  S.winner, S.wtime = d.s10winner, d.s11wtime
  S.t1, S.t1s, S.t2, S.t2s, S.t3, S.t3s = d.s12t1, d.s13t1s, d.s14t2, d.s15t2s, d.s16t3, d.s17t3s
  S.dist = d.s18dist
  if S.race == 2 and lastRace ~= 2 then goTime = os.clock() end
  lastRace = S.race
  if S.cd ~= lastCd then cdTime = os.clock(); lastCd = S.cd end
end)

-- ===== İstemci -> Sunucu (10 Hz) =====
local sendInput = ac.OnlineEvent({
  ac.StructItem.key(KEY .. 'Input'),
  s01horn=ac.StructItem.byte(), s02ang=ac.StructItem.byte(), s03crash=ac.StructItem.byte(),
}, function () end)

local sendTimer, lastSpeed, speedTimer, crashHold = 0, 0, 0, 0

function script.update(dt)
  local sim = ac.getSim()
  local me = ac.getCar(sim.focusedCar)
  if not me then return end

  -- kaza: 0.3 sn içinde ani hız kaybı
  speedTimer = speedTimer + dt
  if speedTimer >= 0.3 then
    if lastSpeed - me.speedKmh > 35 then crashHold = 1.0 end
    lastSpeed = me.speedKmh; speedTimer = 0
  end
  if crashHold > 0 then crashHold = crashHold - dt end

  sendTimer = sendTimer + dt
  if sendTimer >= 0.1 then
    sendTimer = 0
    local v = me.localVelocity
    local ang = 0
    if me.speedKmh > 5 then ang = math.deg(math.atan2(math.abs(v.x), v.z)) end
    if ang < 0 then ang = 0 elseif ang > 180 then ang = 180 end
    local horn = (me.hornActive == true) or ui.keyboardButtonDown(ui.KeyIndex.H)
    sendInput{ s01horn = horn and 1 or 0, s02ang = math.floor(ang), s03crash = crashHold > 0 and 1 or 0 }
  end
end

-- ===== Çizim =====
local WHITE  = rgbm(1, 1, 1, 1)
local SOFT   = rgbm(1, 1, 1, 0.62)
local RED    = rgbm(0.90, 0.07, 0.11, 1)
local RED2   = rgbm(0.62, 0.03, 0.07, 1)
local BLACK  = rgbm(0.035, 0.035, 0.045, 0.90)
local BLACK2 = rgbm(0.12, 0.12, 0.14, 0.95)
local ROW    = rgbm(1, 1, 1, 0.05)
local ROWME  = rgbm(0.90, 0.07, 0.11, 0.30)
local LINE   = rgbm(0.90, 0.07, 0.11, 0.85)
local FADE   = rgbm(0.62, 0.03, 0.07, 0)

local TOGGLE_KEY = ui.KeyIndex.Insert  -- HUD gizle/göster tuşu (AC varsayılanında kullanılmayan tuş)
local visible = true

local function name(i)
  if i == nil or i >= 255 then return '-' end
  local n = ac.getDriverName(i) or ('#' .. i)
  if #n > 14 then n = n:sub(1, 14) end
  return n
end
local function fmtTime(t) return string.format('%d:%05.2f', math.floor(t / 60), t % 60) end
local function txt(s, size, x, y, col) ui.dwriteDrawText(s, size, vec2(x, y), col or WHITE) end
local function txtC(s, size, cx, y, col)
  local w = ui.measureDWriteText(s, size).x
  ui.dwriteDrawText(s, size, vec2(cx - w / 2, y), col or WHITE)
end
local function txtCC(s, size, cx, cy, col)       -- yatay + dikey ortalı
  local m = ui.measureDWriteText(s, size)
  ui.dwriteDrawText(s, size, vec2(cx - m.x / 2, cy - m.y / 2), col or WHITE)
end
local function txtLC(s, size, x, cy, col)        -- soldan, dikey ortalı
  local m = ui.measureDWriteText(s, size)
  ui.dwriteDrawText(s, size, vec2(x, cy - m.y / 2), col or WHITE)
end
local function txtRC(s, size, rx, cy, col)       -- sağdan, dikey ortalı
  local m = ui.measureDWriteText(s, size)
  ui.dwriteDrawText(s, size, vec2(rx - m.x, cy - m.y / 2), col or WHITE)
end
local function hline(sc, pw, y)
  ui.drawRectFilled(vec2(14 * sc, y), vec2(pw - 14 * sc, y + 1.5 * sc), LINE)
end

local function drawSide(sc, pw, me)
  local hh = 40 * sc
  local showRace = (S.race == 1 or S.race == 2)
  local ph = (40 + 98 + 98 + 28 + (showRace and 88 or 0)) * sc

  -- gövde + ince kırmızı çerçeve
  ui.drawRectFilled(vec2(0, 0), vec2(pw, ph), BLACK, 12 * sc)
  ui.drawRect(vec2(0, 0), vec2(pw, ph), rgbm(0.90, 0.07, 0.11, 0.55), 12 * sc, ui.CornerFlags.All, 1.5)

  -- başlık (kırmızıdan koyuya geçiş)
  ui.drawRectFilled(vec2(0, 0), vec2(pw, hh), RED, 12 * sc, ui.CornerFlags.Top)
  ui.drawRectFilledMultiColor(vec2(pw * 0.40, 0), vec2(pw - 12 * sc, hh), FADE, RED2, RED2, FADE)
  ui.drawRectFilled(vec2(0, hh), vec2(pw, hh + 2 * sc), RED2)
  txtLC('SÖNMEZ GAME WORLD', 15 * sc, 14 * sc, hh / 2, WHITE)
  ui.drawRectFilled(vec2(pw - 78 * sc, 9 * sc), vec2(pw - 12 * sc, 31 * sc), BLACK, 11 * sc)
  txtCC(MODE:upper(), 12 * sc, pw - 45 * sc, 20 * sc, WHITE)

  -- puan
  local y0 = hh + 10 * sc
  txt('PUAN', 11 * sc, 16 * sc, y0, SOFT)
  txt(string.format('%d', S.score), 40 * sc, 16 * sc, y0 + 10 * sc, WHITE)
  if S.combo > 1 then
    ui.drawRectFilled(vec2(pw - 86 * sc, y0 + 8 * sc), vec2(pw - 14 * sc, y0 + 52 * sc), RED, 9 * sc)
    txtCC('x' .. S.combo, 28 * sc, pw - 50 * sc, y0 + 30 * sc, WHITE)
  end
  txtLC('TOPLAM  ' .. S.total, 12 * sc, 16 * sc, y0 + 68 * sc, SOFT)
  if IS_DRIFT and S.pending > 0 then
    txtRC('+' .. S.pending, 18 * sc, pw - 16 * sc, y0 + 68 * sc, RED)
  end

  -- en çok puan
  local y1 = hh + 98 * sc
  hline(sc, pw, y1 - 3 * sc)
  txt('EN ÇOK PUAN', 11 * sc, 16 * sc, y1 + 3 * sc, SOFT)
  local rows = { { S.t1, S.t1s }, { S.t2, S.t2s }, { S.t3, S.t3s } }
  for i, r in ipairs(rows) do
    local ry = y1 + (22 + (i - 1) * 24) * sc
    local mine = (r[1] == me and r[1] < 255)
    ui.drawRectFilled(vec2(10 * sc, ry), vec2(pw - 10 * sc, ry + 21 * sc), mine and ROWME or ROW, 6 * sc)
    ui.drawCircleFilled(vec2(25 * sc, ry + 10.5 * sc), 8.5 * sc, i == 1 and RED or BLACK2, 24)
    txtCC(tostring(i), 11 * sc, 25 * sc, ry + 10.5 * sc, WHITE)
    txtLC(name(r[1]), 13 * sc, 42 * sc, ry + 10.5 * sc, WHITE)
    txtRC(tostring(r[2]), 13 * sc, pw - 18 * sc, ry + 10.5 * sc, WHITE)
  end

  -- yarış
  local y2 = hh + 196 * sc
  if showRace then
    hline(sc, pw, y2 - 3 * sc)
    txtLC('YARIŞ ' .. string.format('%.1f', S.dist / 1000) .. ' KM', 11 * sc, 16 * sc, y2 + 14 * sc, SOFT)
    ui.drawRectFilled(vec2(pw - 74 * sc, y2 + 3 * sc), vec2(pw - 14 * sc, y2 + 27 * sc), RED, 8 * sc)
    txtCC(S.place .. '/' .. S.racers, 15 * sc, pw - 44 * sc, y2 + 15 * sc, WHITE)

    local frac = math.saturate(S.prog / math.max(S.dist, 1))
    local bx1, bx2, by = 16 * sc, pw - 16 * sc, y2 + 38 * sc
    ui.drawRectFilled(vec2(bx1, by), vec2(bx2, by + 16 * sc), BLACK2, 8 * sc)
    local fx = bx1 + (bx2 - bx1) * frac
    if frac > 0.01 then
      ui.drawRectFilled(vec2(bx1, by), vec2(fx, by + 16 * sc), RED, 8 * sc)
      ui.drawRectFilled(vec2(bx1 + 2 * sc, by + 2 * sc), vec2(fx, by + 7 * sc), rgbm(1, 1, 1, 0.16), 4 * sc)
      ui.drawCircleFilled(vec2(fx, by + 8 * sc), 9 * sc, WHITE, 24)
      ui.drawCircleFilled(vec2(fx, by + 8 * sc), 5 * sc, RED, 24)
    end
    txtCC(string.format('%d / %d m', math.floor(S.prog), math.floor(S.dist)), 12 * sc, pw / 2, y2 + 68 * sc, WHITE)
  end

  txtCC('3x KORNA = YARIŞ', 10 * sc, pw / 2, ph - 14 * sc, SOFT)
end

local function drawCenter(sc, w, me)
  local cx, cy = w / 2, 62 * sc
  if S.race == 1 and S.cd > 0 then
    local pulse = 1 + 0.30 * math.max(0, 1 - (os.clock() - cdTime) / 0.45)
    local R = 48 * sc
    ui.drawCircleFilled(vec2(cx, cy), R * 1.16, rgbm(0.90, 0.07, 0.11, 0.22), 48)   -- parıltı
    ui.drawCircleFilled(vec2(cx, cy), R, BLACK, 48)
    ui.drawCircle(vec2(cx, cy), R, RED, 48, 5 * sc)
    txtCC(tostring(S.cd), 70 * sc * pulse, cx, cy, WHITE)
  elseif S.race == 2 and os.clock() - goTime < 1.25 then
    local a = math.min(1, (1.25 - (os.clock() - goTime)) / 0.25)
    ui.drawRectFilled(vec2(cx - 96 * sc, cy - 34 * sc), vec2(cx + 96 * sc, cy + 34 * sc), rgbm(0.90, 0.07, 0.11, a), 18 * sc)
    ui.drawRect(vec2(cx - 96 * sc, cy - 34 * sc), vec2(cx + 96 * sc, cy + 34 * sc), rgbm(1, 1, 1, 0.5 * a), 18 * sc, ui.CornerFlags.All, 2)
    txtCC('GO!', 52 * sc, cx, cy, rgbm(1, 1, 1, a))
  elseif S.race == 3 then
    local bw, bh, top = 380 * sc, 116 * sc, 4 * sc
    ui.drawRectFilled(vec2(cx - bw / 2, top), vec2(cx + bw / 2, top + bh), BLACK, 14 * sc)
    ui.drawRect(vec2(cx - bw / 2, top), vec2(cx + bw / 2, top + bh), rgbm(0.90, 0.07, 0.11, 0.6), 14 * sc, ui.CornerFlags.All, 1.5)
    ui.drawRectFilled(vec2(cx - bw / 2, top), vec2(cx + bw / 2, top + 28 * sc), RED, 14 * sc, ui.CornerFlags.Top)
    ui.drawRectFilledMultiColor(vec2(cx - bw * 0.1, top), vec2(cx + bw / 2 - 14 * sc, top + 28 * sc), FADE, RED2, RED2, FADE)
    txtCC('SÖNMEZ GAME WORLD  •  YARIŞ BİTTİ', 13 * sc, cx, top + 14 * sc, WHITE)
    local mine = (S.winner == me)
    txtCC(mine and 'TEBRİKLER, KAZANDIN!' or 'KAZANAN', 12 * sc, cx, top + 44 * sc, mine and RED or SOFT)
    txtCC(name(S.winner), 34 * sc, cx, top + 70 * sc, WHITE)
    txtCC(fmtTime(S.wtime), 18 * sc, cx, top + 101 * sc, RED)
  end
end

function script.drawUI()
  if ui.keyboardButtonPressed(TOGGLE_KEY, false) then visible = not visible end
  local sim = ac.getSim()
  local uis = ac.getUI()
  local w, h = uis.windowSize.x, uis.windowSize.y
  local sc = math.max(0.75, math.min(1.4, h / 1080))
  local me = sim.focusedCar

  if visible then
    local pw = 270 * sc
    ui.transparentWindow('sonmez_hud_' .. MODE, vec2(w - pw - 24 * sc, 70 * sc), vec2(pw + 4, 440 * sc), function()
      drawSide(sc, pw, me)
    end)
  end
  if S.race ~= 0 then
    -- ekranın en üstünde, yan taraftaki aracı kapatmasın diye küçük
    ui.transparentWindow('sonmez_center_' .. MODE, vec2(w / 2 - 260 * sc, h * 0.012), vec2(520 * sc, 140 * sc), function()
      drawCenter(sc, 520 * sc, me)
    end)
  end
end
