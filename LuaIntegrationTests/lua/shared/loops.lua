﻿function break1()
    for i = 1, 10 do
        break
    end
end

function break2()
    for i = 1, 10 do
        if a() then
            b(i)
        end
        break
    end
end

function break3()
    for i = 1, 10 do
        if a() then
            b(i)
        else
            break
        end
    end
end

function break4()
    for i = 1, 10 do
        if a() then
            b(i)
        else
            break
        end
        break
    end
end

function break5()
    for i = 1, 10 do
        if a() then
            break
        end
    end
end

function break6()
    for i = 1, 10 do
        if a() then
            b(i)
            break
        elseif b() then
            c(i)
            break
        end
    end
end

function break7()
    for i = 1, 10 do
        if a() then
            b(i)
            break
        elseif b() then
            for j = 1, 10 do
                if d(i, j) then
                    break
                end
                e()
            end
            c(i)
        end
    end
end

function break8()
    local a = 1
    if a == 0 then
        for i = 1, 10 do
            if a > 5 then
                a = a + 2
                break
            elseif a == 3 then
                a = a + 1
            end
        end
    end
    return a
end

function break9()
    for i = 1, 10 do
        do
            break
        end
        a(i)
    end
end

function break10()
    for i = 1, 10 do
        if a() then
            break
        else
            break
        end
        b(i)
    end
end

function break11()
    for i = 1, 10 do
        a()
        break
    end
end

function break12()
    for k, v in pairs(tbl) do
        a()
        break
    end
end
