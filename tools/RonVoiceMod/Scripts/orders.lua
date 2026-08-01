-- orders.lua — id da ordem -> como pedir ao jogo.
--
-- O RonVoice manda um id pela caixa de correio ("door.breach.kick.gas"). Esta
-- tabela e' onde o id vira chamada de verdade. Vive separada do main.lua para
-- poder ser conferida FORA do jogo: ModOrdersTests, do lado C#, le' este arquivo
-- e prende cada valor. As duas linguagens nao compilam juntas — sem isso, uma
-- letra trocada num id vira uma ordem que nunca acontece, calada.
--
-- De onde vem cada numero:
--   EDoorBreachType   None=0 Open=1 Move=2 Kick=3 Shotgun=4 Ram=5 C2=6 Leader=7
--   EDoorScanMethod   None=0 Slide=1 Slice=2 Snap=3 CenterCheck=4
--   ESubDoorPosition  None=0 Left=1 Right=2
-- colhidos do proprio jogo pela sonda (docs/reference/eswatcommand.txt). Os
-- valores 1, 2, 4 e 6 de EDoorBreachType estao provados em jogo: sao os que o
-- RoNSpeech passa, e ele funciona.
--
-- ATENCAO ao campo `verify`. Marca o que ainda e' hipotese, nao o que falta
-- fazer. Uma ordem errada nao da erro: o esquadrao obedece a ordem errada, no
-- meio da missao. Enquanto ninguem confirmar em jogo, fica marcada.

local M = {}

-- A granada NAO e' um numero. O jogo quer a classe, achada por caminho; caminho
-- errado devolve nil e a ordem sai sem granada nenhuma — arromba sem o gas que
-- voce pediu, sem avisar.
local FLASHBANG = "/Game/Blueprints/Items/WeaponsRevised/Grenade_Flashbang_V2.Grenade_Flashbang_V2_C"
local STINGER   = "/Game/Blueprints/Items/WeaponsRevised/Grenade_Stinger_V2.Grenade_Stinger_V2_C"
local GAS       = "/Game/Blueprints/Items/WeaponsRevised/Grenade_CSGas_V2.Grenade_CSGas_V2_C"

M.grenades = { FLASHBANG = FLASHBANG, STINGER = STINGER, GAS = GAS }

-- `launcher` e `leader` viram os argumentos 7 e 8 de GiveBreachAndClearCommand.
-- Sao hipotese: o RoNSpeech os liga por tecla modificadora e nunca diz o que
-- significam. Por isso toda entrada que usa os dois esta marcada.
M.orders = {

    -- ---- porta: abrir e limpar (EDoorBreachType.Open) ----
    ["door.open.clear"]     = { call = "breach", breach = 1 },
    ["door.open.flashbang"] = { call = "breach", breach = 1, grenade = FLASHBANG },
    ["door.open.stinger"]   = { call = "breach", breach = 1, grenade = STINGER },
    ["door.open.gas"]       = { call = "breach", breach = 1, grenade = GAS },
    ["door.open.launcher"]  = { call = "breach", breach = 1, launcher = true, verify = true },
    ["door.open.leader"]    = { call = "breach", breach = 1, leader = true, verify = true },

    -- ---- porta: chute (Kick) ----
    ["door.breach.kick.clear"] = { call = "breach", breach = 3, crashes = true },
    ["door.breach.kick.flashbang"] = { call = "breach", breach = 3, grenade = FLASHBANG, crashes = true },
    ["door.breach.kick.stinger"] = { call = "breach", breach = 3, grenade = STINGER, crashes = true },
    ["door.breach.kick.gas"] = { call = "breach", breach = 3, grenade = GAS, crashes = true },
    ["door.breach.kick.launcher"] = { call = "breach", breach = 3, launcher = true, crashes = true },
    ["door.breach.kick.leader"] = { call = "breach", breach = 3, leader = true, crashes = true },

    -- ---- porta: escopeta (Shotgun) ----
    ["door.breach.shotgun.clear"]     = { call = "breach", breach = 4 },
    ["door.breach.shotgun.flashbang"] = { call = "breach", breach = 4, grenade = FLASHBANG },
    ["door.breach.shotgun.stinger"]   = { call = "breach", breach = 4, grenade = STINGER },
    ["door.breach.shotgun.gas"]       = { call = "breach", breach = 4, grenade = GAS },
    ["door.breach.shotgun.launcher"]  = { call = "breach", breach = 4, launcher = true, verify = true },
    ["door.breach.shotgun.leader"]    = { call = "breach", breach = 4, leader = true, verify = true },

    -- ---- porta: C2 ----
    ["door.breach.c2.clear"]     = { call = "breach", breach = 6 },
    ["door.breach.c2.flashbang"] = { call = "breach", breach = 6, grenade = FLASHBANG },
    ["door.breach.c2.stinger"]   = { call = "breach", breach = 6, grenade = STINGER },
    ["door.breach.c2.gas"]       = { call = "breach", breach = 6, grenade = GAS },
    ["door.breach.c2.launcher"]  = { call = "breach", breach = 6, launcher = true, verify = true },
    ["door.breach.c2.leader"]    = { call = "breach", breach = 6, leader = true, verify = true },

    -- ---- porta: aríete (Ram) ----
    ["door.breach.ram.clear"] = { call = "breach", breach = 5, crashes = true },
    ["door.breach.ram.flashbang"] = { call = "breach", breach = 5, grenade = FLASHBANG, crashes = true },
    ["door.breach.ram.stinger"] = { call = "breach", breach = 5, grenade = STINGER, crashes = true },
    ["door.breach.ram.gas"] = { call = "breach", breach = 5, grenade = GAS, crashes = true },
    ["door.breach.ram.launcher"] = { call = "breach", breach = 5, launcher = true, crashes = true },
    ["door.breach.ram.leader"] = { call = "breach", breach = 5, leader = true, crashes = true },

    -- ---- porta: líder arromba (Leader) ----
    ["door.breach.leader.clear"] = { call = "breach", breach = 7, crashes = true },
    ["door.breach.leader.flashbang"] = { call = "breach", breach = 7, grenade = FLASHBANG, crashes = true },
    ["door.breach.leader.stinger"] = { call = "breach", breach = 7, grenade = STINGER, crashes = true },
    ["door.breach.leader.gas"] = { call = "breach", breach = 7, grenade = GAS, crashes = true },
    ["door.breach.leader.launcher"] = { call = "breach", breach = 7, launcher = true, crashes = true },
    ["door.breach.leader.leader"] = { call = "breach", breach = 7, leader = true, crashes = true },

    -- ---- porta: empilhar ----
    -- O RoNSpeech passa 0, 2, 3 e 1 em ramos diferentes de issueStackUp; qual e'
    -- qual depende de tecla modificadora que ele nao documenta. So' o 0, o ramo
    -- sem modificador, esta certo.
    ["door.stack.auto"]  = { call = "stack", position = 0 },
    ["door.stack.split"] = { call = "stack", position = 1, verify = true },
    ["door.stack.left"]  = { call = "stack", position = 2, verify = true },
    ["door.stack.right"] = { call = "stack", position = 3, verify = true },

    -- ---- porta: espiar ----
    -- EDoorScanMethod diz Slide=1 Slice=2 Snap=3, mas o que manda aqui e' o que
    -- essas frases JA' faziam: o RoNSpeech chama GiveScanDoorCommand com 2 no
    -- issuePeek e no issueCheckTrap modificado. Trocar por 3 "porque o enum diz
    -- Snap" quebrou o peek de quem ja' usava.
    ["door.scan.slide"] = { call = "scan", method = 1, verify = true },
    ["door.scan.pie"]   = { call = "scan", method = 2 },
    ["door.scan.peek"]  = { call = "scan", method = 2 },

    -- ---- porta: o resto ----
    -- "mirror" nunca foi soltar o espelho: e' com ele que se checa armadilha
    -- embaixo da porta, e nao existe funcao de soltar espelho. E' o que a tecla
    -- F23 do RoNSpeech sempre fez.
    ["door.mirror"]   = { call = "checktraps" },
    ["door.wedge"]    = { call = "wedge" },
    ["door.cover"]    = { call = "cover" },
    ["door.toggle"]   = { call = "opendoor" },
    ["door.picklock"] = { call = "picklock" },
    ["door.disarm"]   = { call = "disarmtrap" },

    -- ---- mover ----
    -- GiveFallInCommand(time, formacao). O 0 e' o ramo sem modificador; 1, 2 e 3
    -- saem dos ramos que batem com fila dupla, diamante e cunha no perfil.
    ["move.to"]                   = { call = "move" },
    ["move.fallin"]               = { call = "fallin", formation = 0 },
    ["move.formation.single"]     = { call = "fallin", formation = 0, verify = true },
    ["move.formation.double"]     = { call = "fallin", formation = 1 },
    ["move.formation.diamond"]    = { call = "fallin", formation = 2 },
    ["move.formation.wedge"]      = { call = "fallin", formation = 3 },

    -- ---- esquadrão ----
    ["hold"]   = { call = "hold" },
    ["cover"]  = { call = "cover" },
    ["search"] = { call = "search" },

    -- ---- lançar ----
    ["deploy.flashbang"] = { call = "grenade", grenade = FLASHBANG },
    ["deploy.stinger"]   = { call = "grenade", grenade = STINGER },
    ["deploy.gas"]       = { call = "grenade", grenade = GAS },
    ["deploy.chemlight"] = { call = "chemlight" },
    ["deploy.shield"]    = { call = "shield", verify = true },

    -- ---- pessoas ----
    ["person.restrain"] = { call = "restrain" },
    -- Nao e' o SWATManager: manda no civil/suspeito, pelo Pawn do jogador.
    ["person.moveto"]   = { call = "aimove", verify = true },
}

return M
