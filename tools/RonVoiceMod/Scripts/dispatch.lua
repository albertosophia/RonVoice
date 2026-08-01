-- dispatch.lua — o id vira uma chamada, mas ainda nao a faz.
--
-- Decide sem agir: devolve um PLANO — qual funcao do jogo, sobre quem, com
-- quais argumentos — e quem executa e' o main.lua. Assim a parte que erra em
-- silencio (escolher funcao e argumentos) pode ser conferida fora do jogo, por
-- tools/check_dispatch_contract.py. Dentro do jogo nao ha' como rodar teste, e
-- um argumento trocado nao da erro: o esquadrao obedece a ordem errada.
--
-- Recusar e' parte do trabalho. Sem porta mirada, arrombar nao tem alvo: melhor
-- devolver o motivo e deixar o app avisar do que mandar a ordem no escuro.

local orders = require("orders")

local M = {}

--- Empacota preservando nil no meio: o jogo distingue "sem granada" de
--- "argumento faltando", e table.pack e' o unico jeito de contar direito.
local function plano(alvo, fn, ...)
    local args = table.pack(...)
    return { on = alvo, fn = fn, args = args, argc = args.n }
end

local function noManager(fn, ...) return plano("manager", fn, ...) end
local function noPawn(fn, ...) return plano("pawn", fn, ...) end

local SEM_PORTA = "sem porta mirada"

local monta = {}

--- Arrombar. O tipo vem de EDoorBreachType; a granada e' uma CLASSE, achada
--- por caminho — caminho errado devolve nil e a porta abre sem o gas pedido.
---
--- Lancador e granada do lider viram os argumentos 7 e 8. E' hipotese: o
--- RoNSpeech liga os dois por tecla modificadora e nunca diz o que significam.
--- O 8 nunca aparece sozinho la', so' junto do 7 — dai lider ser os dois.
--- Os tipos de EDoorBreachType que o jogo comprovadamente aceita: abrir, mover,
--- escopeta e C2. Sao os quatro que o RoNSpeech passa, e ele funciona.
---
--- Chute (3), ariete (5) e lider (7) estao no enum, e por estarem no enum eu
--- os mandei. O jogo FECHOU — sem erro, sem recibo, crash — ao receber o 3.
--- Estar no enum nao e' o mesmo que a funcao aceitar, e a diferenca custou a
--- missao de quem estava jogando.
---
--- Recusar e' pior do que funcionar e MUITO melhor do que derrubar o jogo.
--- Enquanto nao houver um jeito provado de pedir chute, estas dizem que nao dao.
monta.breach = function(spec, ctx)
    if not ctx.target then return nil, SEM_PORTA end

    local granada = spec.grenade and ctx.findClass(spec.grenade) or nil
    local sete = (spec.launcher or spec.leader) and true or false
    local oito = spec.leader and true or false

    return noManager("GiveBreachAndClearCommand",
        ctx.target, spec.breach, ctx.team, ctx.location, granada, nil,
        sete, oito, false, false, 0, true)
end

monta.stack = function(spec, ctx)
    if not ctx.target then return nil, SEM_PORTA end
    return noManager("GiveStackUpCommand",
        ctx.target, ctx.team, ctx.location, ctx.up, true, spec.position)
end

monta.scan = function(spec, ctx)
    if not ctx.target then return nil, SEM_PORTA end
    return noManager("GiveScanDoorCommand", ctx.target, ctx.team, ctx.location, spec.method)
end

--- Checar armadilha embaixo da porta. E' com o espelho que se faz isso no jogo,
--- e por isso a frase e' "mirror" — nao ha' funcao de soltar espelho. Recebe o
--- vetor de cima, que as outras nao recebem.
monta.checktraps = function(_, ctx)
    if not ctx.target then return nil, SEM_PORTA end
    return noManager("GiveCheckForTrapsCommand", ctx.target, ctx.team, ctx.location, ctx.up)
end

--- As que so' precisam da porta, do time e de onde o jogador esta.
local naPorta = {
    opendoor   = "GiveOpenDoorCommand",
    picklock   = "GivePickLockCommand",
    wedge      = "GiveWedgeDoorCommand",
    disarmtrap = "GiveDisarmTrapOnDoorCommand",
    restrain   = "GiveRestrainCommand",
}

for chave, fn in pairs(naPorta) do
    monta[chave] = function(_, ctx)
        if not ctx.target then return nil, SEM_PORTA end
        return noManager(fn, ctx.target, ctx.team, ctx.location)
    end
end

monta.hold   = function(_, ctx) return noManager("GiveHoldCommand", ctx.team) end
monta.cover  = function(_, ctx) return noManager("GiveCoverAreaCommand", ctx.team, ctx.location) end
monta.move   = function(_, ctx) return noManager("GiveMoveCommand", ctx.team, ctx.location) end
monta.search = function(_, ctx) return noManager("GiveSearchAndSecureCommand", ctx.team, ctx.location, true) end
monta.fallin = function(spec, ctx) return noManager("GiveFallInCommand", ctx.team, spec.formation) end

monta.grenade = function(spec, ctx)
    return noManager("GiveDeployGrenadeAtLocation", ctx.team, ctx.location, ctx.findClass(spec.grenade))
end

monta.chemlight = function(_, ctx)
    return noManager("GiveDropChemlightAtLocation", ctx.team, ctx.location)
end

monta.shield = function(_, ctx) return noManager("GiveDeployShield", ctx.team) end

--- Mandar num civil ou suspeito nao passa pelo SWATManager: e' no Pawn do
--- jogador. Outra API, outro objeto.
monta.aimove = function(_, ctx)
    if not ctx.target then return nil, "sem pessoa mirada" end
    return noPawn("Server_GiveAIMoveTo", ctx.target, ctx.location)
end

--- A fala de confirmação de cada time, e a resposta positiva que o jogo usa.
--- Vem do RoNSpeech: era ele quem registrava F5/F6/F7 e tocava isto. A tecla
--- sozinha só troca o time, calada — e escolher um elemento em silêncio não deixa
--- ninguém saber se o programa ouviu.
local RESPOSTA = "[RESPONSE]CommandS_2"
local FALA = { [1] = "SWATB_01", [2] = "SWATA_01", [5] = "SWATB_03" }

--- Plano para confirmar em voz alta que o elemento foi escolhido.
---
--- ctx precisa de isTeamDead: um esquadrão caído não responde, e sem a checagem
--- ele responderia do túmulo. O RoNSpeech conferia isso antes de falar.
function M.planAcknowledge(team, ctx)
    local fala = FALA[team]
    if not fala then return nil, "time que o mod nao conhece: " .. tostring(team) end

    if ctx.isTeamDead and ctx.isTeamDead(team) then
        return nil, "esse elemento esta fora de combate"
    end

    return noManager("PlaySwatCommandVoiceLine", RESPOSTA, fala, false)
end

--- Plano para este id, ou nil e o motivo.
---
--- ctx: { target, team, location, up, findClass }
--- findClass entra de fora para o plano poder ser conferido sem o jogo.
function M.plan(id, ctx)
    local spec = orders.orders[id]
    if not spec then return nil, "ordem que o mod nao conhece: " .. tostring(id) end

    -- Marcada como derrubadora: nem tenta. O jogo FECHOU ao receber o tipo de
    -- arrombamento 3 — sem erro, sem recibo, crash — e o 5 e o 7 vem da mesma
    -- suposicao: estar no enum nao e' o mesmo que a funcao aceitar.
    --
    -- Recusar e' pior do que funcionar e MUITO melhor do que fechar o jogo de
    -- quem esta no meio de uma missao.
    if spec.crashes then
        return nil, "esta ordem fecha o jogo; ainda nao ha' um jeito seguro de pedir"
    end

    local como = monta[spec.call]
    if not como then return nil, "o mod nao sabe fazer: " .. tostring(spec.call) end

    return como(spec, ctx)
end

--- Se ainda e' hipotese. O main.lua marca isso no recibo, para o app poder
--- dizer que obedeceu mas ninguem confirmou que obedeceu o certo.
function M.isGuess(id)
    local spec = orders.orders[id]
    return spec ~= nil and spec.verify == true
end

return M
