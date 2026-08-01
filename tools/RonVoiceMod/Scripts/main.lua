-- RonVoice — o lado do jogo.
--
-- O app ouve a voz, resolve a frase e deixa o id da ordem num arquivo. Este mod
-- le', chama a funcao do jogo e responde. Nao ha' tecla nenhuma no meio: e' por
-- isso que ele existe. O Windows para no F24, e o jogo tem 70 ordens.
--
-- Este arquivo e' de proposito o mais fino possivel, porque e' o unico que
-- nenhum teste alcanca. Tudo que decide alguma coisa mora ao lado, onde
-- tools/check_*.py consegue chegar sem abrir o jogo:
--
--   mailbox.lua    o formato do pedido e do recibo   check_mailbox_contract.py
--   orders.lua     id -> como pedir                  ModOrdersTests (C#)
--   dispatch.lua   id -> qual funcao, quais args     check_dispatch_contract.py
--   runner.lua     o laco: ler, agir, responder      check_runner_contract.py
--
-- Aqui so' fica o que depende do jogo estar rodando: achar o SWATManager, achar
-- a porta mirada, e um gancho que chame o laco.

local UEHelpers = require("UEHelpers")
local mailbox = require("mailbox")
local runner = require("runner")

local PREFIXO = "[RonVoice] "
local function log(msg) print(PREFIXO .. msg .. "\n") end

local estado = runner.newState()

--- O mod nao pode ler o arquivo em todo quadro: o gancho dispara junto com a
--- camera, e seriam dezenas de aberturas de arquivo por segundo, para nada. A
--- voz nao chega mais rapido que isso.
local INTERVALO = 0.05
local ultimaLeitura = 0

--- O SWATManager pode nao existir ainda no menu, e some ao trocar de mapa.
--- Reachar quando estiver invalido e' mais barato que travar tudo.
local manager = nil
local function pegaManager()
    if manager and manager:IsValid() then return manager end
    manager = FindFirstOf("SWATManager")
    return manager
end

--- A porta (ou pessoa) que o jogador esta mirando. O jogo guarda isso no widget
--- do menu de comandos; uma porta dupla responde pela folha principal, entao a
--- sub-porta precisa ser resolvida ou a ordem vai para a metade errada.
---
--- O nome e' conferido ANTES de ler bMainSubDoor, e nao depois: essas
--- propriedades so' existem em porta. Le-las num civil ou num movel derruba o
--- jogo por dentro, sem erro de Lua e sem uma linha no log. E' o que o RoNSpeech
--- faz, e foi por nao fazer que este mod caiu na primeira vez.
local function alvoMirado(pawn)
    local widget = pawn.SwatCommandWidget
    if not widget then return nil end

    local ator = widget.LastContextActor
    if not ator or not ator:IsValid() then return nil end

    if not string.find(ator:GetFullName(), "Door") then return ator end

    if ator.bMainSubDoor == true then return ator end
    if ator.DriveSubDoor and ator.DriveSubDoor:IsValid() then return ator.DriveSubDoor end
    return ator
end

--- O time ativo como NUMERO simples. ActiveTeamType vem do jogo como
--- propriedade de enum, e entregar isso a uma chamada nativa que espera um byte
--- e' pedir para cair. O RoNSpeech guarda um numero Lua e comeca em 5 — ouro,
--- o esquadrao inteiro — que e' o padrao menos surpreendente quando nao se sabe.
local OURO = 5
local function timeAtivo(pawn)
    local widget = pawn.SwatCommandWidget
    if not widget then return OURO end
    return tonumber(widget.ActiveTeamType) or OURO
end

--- Tudo que o dispatch precisa saber do mundo, junto num lugar so'. Chamado
--- SO' quando ha' ordem para executar: cada leitura aqui mexe com objeto do
--- jogo, e fazer isso a esmo e' arriscar sem ter o que ganhar.
local function mundo()
    local controller = UEHelpers:GetPlayerController()
    if not controller or not controller.Pawn or not controller.Pawn:IsValid() then return nil end

    local pawn = controller.Pawn
    return {
        target = alvoMirado(pawn),
        location = pawn:K2_GetActorLocation(),
        up = pawn:GetActorUpVector(),
        activeTeam = timeAtivo(pawn),
        findClass = StaticFindObject,
        pawn = pawn,

        -- Um esquadrão caído não confirma nada. Sem esta checagem ele
        -- responderia do túmulo — é o que o RoNSpeech conferia antes de falar.
        -- Na dúvida, considera vivo: calar por engano é pior que falar demais.
        isTeamDead = function(time)
            local m = pegaManager()
            if not m then return false end
            local ok, morto = pcall(function() return m:IsSWATTeamDead(time) end)
            return ok and morto == true
        end,
    }
end

--- Executa o plano. O dispatch ja' decidiu tudo; aqui so' se aponta o objeto.
local function executa(plano, m)
    local alvo = plano.on == "pawn" and m.pawn or pegaManager()
    if not alvo then error("SWATManager nao encontrado") end

    alvo[plano.fn](alvo, table.unpack(plano.args, 1, plano.argc))
end

local function quadro()
    local agora = os.clock()
    if agora - ultimaLeitura < INTERVALO then return end
    ultimaLeitura = agora

    -- O mundo entra como FUNCAO: o runner so' a chama depois de achar um pedido
    -- novo. Fora isso, este gancho — que dispara junto com a camera — nao lê
    -- nada do jogo. Nao e' economia de tempo, e' de risco: uma leitura errada
    -- num objeto do jogo fecha tudo sem erro de Lua e sem uma linha no log.
    local atual = nil

    runner.tick(estado, {
        mailbox = mailbox,
        world = function()
            atual = mundo()
            return atual
        end,
        call = function(plano) executa(plano, atual) end,
    })
end

-- Um erro solto aqui derruba o gancho e o mod para calado, para sempre. O
-- runner ja' protege a chamada ao jogo; este pcall e' para o resto — pegar o
-- controller, ler o widget — que tambem mexe com objeto que pode sumir.
RegisterHook(
    "/Script/ReadyOrNot.PlayerCharacter:Server_UpdateCameraRotationRate",
    function() end,
    function()
        local ok, erro = pcall(quadro)
        if not ok then log("quadro falhou: " .. tostring(erro)) end
    end
)

log("pronto. caixa de correio em " .. tostring(mailbox.directory()))
