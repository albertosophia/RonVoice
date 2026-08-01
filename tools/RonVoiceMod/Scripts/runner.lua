-- runner.lua — o laco: le' o pedido, planeja, executa, responde.
--
-- Roda vinte vezes por segundo dentro do jogo, onde nao ha' como rodar teste e
-- onde erro nao aparece: no maximo o esquadrao nao obedece, e ninguem sabe por
-- que. Por isso o mailbox e a chamada ao jogo entram de fora, e
-- tools/check_runner_contract.py prende o comportamento aqui fora.
--
-- Tres coisas so' quebram neste arquivo:
--   o mesmo pedido executado duas vezes — o arquivo continua la' entre um
--     quadro e outro, entao sem memoria a ordem sairia vinte vezes por segundo;
--   executar e nao responder — o app fica dizendo que o mod nao respondeu;
--   a chamada estourar e levar o laco junto — o mod para calado, para sempre.

local dispatch = require("dispatch")

local M = {}

--- Times, como o RoNSpeech chama: vermelho 1, azul 2, ouro 5. Sem elemento
--- dito por voz, vale o que estiver ativo no menu do jogo.
local TIMES = { red = 1, blue = 2, gold = 5 }

function M.teamFor(element, activeTeam)
    if element == nil then return activeTeam end
    return TIMES[string.lower(element)] or activeTeam
end

--- A memoria do laco. Guarda so' a ultima sequencia respondida: e' o unico
--- estado que impede a ordem dobrada.
function M.newState() return { lastDone = nil } end

--- Um quadro. Devolve o que aconteceu, para o log do mod.
---
--- deps: {
---   mailbox = { read(), acknowledge(seq, status) },
---   call    = function(plano),  -- executa; pode estourar
---   world   = { target, location, up, activeTeam, findClass },
--- }
function M.tick(state, deps)
    local req = deps.mailbox.read()
    if not req then return "sem pedido" end

    -- Ja' respondido: o arquivo so' some quando chega outro pedido.
    if state.lastDone == req.sequence then return "ja respondido" end

    local mundo = deps.world
    local ctx = {
        target = mundo.target,
        team = M.teamFor(req.element, mundo.activeTeam),
        location = mundo.location,
        up = mundo.up,
        findClass = mundo.findClass,
    }

    local plano, motivo = dispatch.plan(req.order, ctx)

    -- Marcado como respondido ANTES de agir: se der errado, nao se insiste. Um
    -- pedido que falha e volta a ser tentado falha vinte vezes por segundo.
    state.lastDone = req.sequence

    if not plano then
        deps.mailbox.acknowledge(req.sequence, motivo)
        return "recusado"
    end

    local ok, erro = pcall(deps.call, plano)
    if not ok then
        deps.mailbox.acknowledge(req.sequence, "falhou: " .. tostring(erro))
        return "falhou"
    end

    deps.mailbox.acknowledge(req.sequence, "ok")
    return "ok"
end

return M
