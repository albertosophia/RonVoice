-- RonVoiceProbe — pergunta ao jogo quais sao os metodos de arrombamento.
--
-- O mod RoNSpeech chama GiveBreachAndClearCommand(Target, N, ...) e hoje so'
-- usa quatro valores de N. Descobrimos lendo o Lua que N NAO e' simplesmente
-- "o metodo": em issueBreach ele vale 1 para porta e 2 para vao sem porta.
-- Chutar os valores de chute, ariete e lider seria pior que a ordem nao
-- existir — mandar C2 numa porta que a pessoa queria chutar e' dano de
-- verdade, e o jogo nao reclamaria.
--
-- Entao esta sonda le a assinatura da funcao no proprio jogo e despeja o enum
-- do segundo parametro, com nome e valor de cada entrada.
--
-- COMO USAR
--   1. copie esta pasta inteira para  <jogo>\Binaries\Win64\Mods\
--   2. entre numa missao e aperte F11
--   3. mande o arquivo  ronvoice-enums.txt  que aparece ao lado do jogo
--
-- Alem do enum de arrombamento, ela despeja TODAS as funcoes do SWATManager.
-- O mod usa uma dezena; existem outras que ele nem chama, e cada uma pode ser
-- uma das ordens que faltam. Custa o mesmo F11.
--
-- Nao toca em nada: so' le e escreve um txt.

local OUT = "ronvoice-enums.txt"
local lines = {}

local function say(text)
    lines[#lines + 1] = text
    print("[RonVoiceProbe] " .. text .. "\n")
end

local function flush()
    local file = io.open(OUT, "w")
    if not file then
        print("[RonVoiceProbe] nao consegui escrever " .. OUT .. "\n")
        return
    end
    file:write(table.concat(lines, "\n"))
    file:write("\n")
    file:close()
    print("[RonVoiceProbe] gravado em " .. OUT .. "\n")
end

-- Toda leitura vai dentro de pcall: a API muda entre versoes do UE4SS, e uma
-- sonda que derruba o jogo do usuario e' pior que uma que nao descobre nada.
local function try(label, fn)
    local ok, err = pcall(fn)
    if not ok then say("  (falhou em " .. label .. ": " .. tostring(err) .. ")") end
end

local function dumpEnum(enum, why)
    say("")
    say("=== ENUM " .. enum:GetFullName() .. "   [" .. why .. "]")
    try("ForEachName", function()
        enum:ForEachName(function(name, value)
            say(string.format("   %-4s %s", tostring(value), name:ToString()))
        end)
    end)
end

-- 1) A assinatura da funcao, que e' quem diz o TIPO do segundo parametro.
local function dumpSignature()
    say("=== assinatura de GiveBreachAndClearCommand")

    local fn = StaticFindObject("/Script/ReadyOrNot.SWATManager:GiveBreachAndClearCommand")
    if not fn or not fn:IsValid() then
        say("  (nao achei a funcao pelo caminho direto; tentando pela classe)")
        local manager = FindFirstOf("SWATManager")
        if manager and manager:IsValid() then
            say("  SWATManager encontrado: " .. manager:GetFullName())
        else
            say("  SWATManager NAO encontrado — entre numa missao antes de apertar a tecla")
        end
        return
    end

    local index = 0
    try("ForEachProperty", function()
        fn:ForEachProperty(function(prop)
            index = index + 1
            say(string.format("   %2d  %-28s %s",
                index,
                prop:GetFName():ToString(),
                prop:GetClass():GetFName():ToString()))

            -- Se for enum ou byte com enum, despeja os valores.
            try("enum do parametro", function()
                local e = prop:GetEnum()
                if e and e:IsValid() then dumpEnum(e, "parametro " .. index) end
            end)
        end)
    end)

    if index == 0 then say("  (a funcao nao expos propriedades nesta versao do UE4SS)") end
end

-- 1b) TODAS as funcoes do SWATManager, com os parametros de cada uma.
--
-- O mod usa oito ou nove delas. Existem GiveOpenDoorCommand e
-- GiveCloseDoorCommand que ele nem chama, entao provavelmente ha mais — e cada
-- uma pode ser uma das 12 ordens que faltam e nao sao de arrombamento. Perguntar
-- isso custa o mesmo F11.
local function dumpSwatManagerApi()
    say("")
    say("=== todas as funcoes do SWATManager")

    local manager = FindFirstOf("SWATManager")
    if not manager or not manager:IsValid() then
        say("  SWATManager NAO encontrado — entre numa missao antes de apertar a tecla")
        return
    end

    local class = manager:GetClass()
    say("  classe: " .. class:GetFullName())

    local count = 0
    try("ForEachFunction", function()
        class:ForEachFunction(function(fn)
            count = count + 1
            say("")
            say("  " .. fn:GetFName():ToString())

            try("parametros", function()
                fn:ForEachProperty(function(prop)
                    local kind = prop:GetClass():GetFName():ToString()
                    say(string.format("      %-28s %s",
                        prop:GetFName():ToString(), kind))

                    -- Se o parametro for enum, despeja os valores: e' isto que
                    -- decide chute, ariete e lider.
                    try("enum do parametro", function()
                        local e = prop:GetEnum()
                        if e and e:IsValid() then dumpEnum(e, "parametro de " .. fn:GetFName():ToString()) end
                    end)
                end)
            end)
        end)
    end)

    if count == 0 then
        say("  (ForEachFunction nao existe nesta versao do UE4SS —")
        say("   a varredura de enums abaixo ainda pode achar o que precisamos)")
    else
        say("")
        say("  total de funcoes: " .. count)
    end
end

-- 2) Rede de seguranca: varre os enums do jogo atras dos que parecem ser disto.
local function dumpLikelyEnums()
    say("")
    say("=== enums do jogo com nome suspeito")

    local seen = {}
    try("ForEachUObject", function()
        ForEachUObject(function(object)
            if not object or not object:IsValid() then return end

            local ok, class = pcall(function() return object:GetClass():GetFName():ToString() end)
            if not ok or class ~= "Enum" then return end

            local name = object:GetFName():ToString()
            local lower = name:lower()
            if lower:find("breach") or lower:find("entry") or lower:find("swat")
               or lower:find("door") or lower:find("deploy") or lower:find("grenade") then
                if not seen[name] then
                    seen[name] = true
                    dumpEnum(object, "nome suspeito")
                end
            end
        end)
    end)
end

RegisterKeyBind(Key.F11, function()
    lines = {}
    say("RonVoiceProbe")
    say("Se as secoes vierem vazias, entre numa missao antes de apertar a tecla:")
    say("o SWATManager so' existe com uma partida carregada.")
    say("")

    dumpSignature()
    dumpSwatManagerApi()
    dumpLikelyEnums()
    flush()
end)

print("[RonVoiceProbe] carregado. Entre numa missao e aperte F11.\n")
