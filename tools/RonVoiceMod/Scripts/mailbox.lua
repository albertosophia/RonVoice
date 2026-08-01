-- mailbox.lua — o lado do jogo da caixa de correio.
--
-- Le' o pedido que o RonVoice deixou e devolve o recibo. Vive separado do
-- main.lua para poder ser testado FORA do jogo: o formato e' um contrato entre
-- C# e Lua, e as duas pontas nao compilam juntas — nada avisa quando uma muda.
--
-- Deliberadamente burro. Isto roda dentro do laco do jogo, vinte vezes por
-- segundo: nada de JSON, nada de varrer diretorio, uma linha e pronto.

local M = {}

--- Onde os dois lados se encontram. O C# usa a mesma pasta; nenhum dos dois
--- depende de configuracao nem de saber onde o outro esta instalado.
function M.directory()
    local base = os.getenv("LOCALAPPDATA")
    if not base or base == "" then return nil end
    return base .. "\\RonVoice"
end

function M.orderPath() local d = M.directory(); return d and (d .. "\\order.txt") end
function M.receiptPath() local d = M.directory(); return d and (d .. "\\receipt.txt") end

--- Separa "17|door.breach.ram.clear|red|1" nos quatro campos.
--- Devolve nil quando a linha nao serve — meia linha nunca vira meia ordem.
function M.parse(line)
    if type(line) ~= "string" then return nil end

    local fields = {}
    for field in (line .. "|"):gmatch("([^|]*)|") do
        fields[#fields + 1] = (field:gsub("^%s+", ""):gsub("%s+$", ""))
    end
    if #fields < 4 then return nil end

    local sequence = tonumber(fields[1])
    if not sequence or sequence ~= math.floor(sequence) then return nil end

    -- Sem ordem E sem elemento nao e' pedido nenhum.
    local order = fields[2] ~= "-" and fields[2] or nil
    local element = fields[3] ~= "-" and fields[3] or nil
    if not order and not element then return nil end

    return {
        sequence = math.floor(sequence),
        order = order,
        element = element,
        queue = fields[4] == "1",
    }
end

--- Le' o pedido atual, ou nil se nao houver, estiver ilegivel ou pela metade.
function M.read()
    local path = M.orderPath()
    if not path then return nil end

    local file = io.open(path, "r")
    if not file then return nil end

    local line = file:read("*l")
    file:close()
    return M.parse(line)
end

--- Responde. "ok" quando executou; qualquer outra coisa e' o motivo — e' isso
--- que impede a ordem silenciosa do outro lado.
function M.acknowledge(sequence, status)
    local path = M.receiptPath()
    if not path then return false end

    local file = io.open(path, "w")
    if not file then return false end

    file:write(string.format("%d|%s", sequence, status or "ok"))
    file:close()
    return true
end

return M
