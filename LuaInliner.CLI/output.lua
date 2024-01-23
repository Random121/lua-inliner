local function inlineMe()
	--!!INLINE_FUNCTION

	return 1, 2, 3
end
local __inline_return__0, __inline_return__1, __inline_return__2 = nil, nil, nil
repeat
    __inline_return__0, __inline_return__1, __inline_return__2 = 1, 2, 3
    break
until true

local test1 = __inline_return__0
local __inline_return__3, __inline_return__4, __inline_return__5 = nil, nil, nil
repeat
    __inline_return__3, __inline_return__4, __inline_return__5 = 1, 2, 3
    break
until true

local test2 = 1, 2, __inline_return__3
local __inline_return__6, __inline_return__7, __inline_return__8 = nil, nil, nil
repeat
    __inline_return__6, __inline_return__7, __inline_return__8 = 1, 2, 3
    break
until true

local test3 = 1, __inline_return__6, 2
local __inline_return__9, __inline_return__10, __inline_return__11 = nil, nil, nil
repeat
    __inline_return__9, __inline_return__10, __inline_return__11 = 1, 2, 3
    break
until true

-- test4
print(1, __inline_return__9, 2)
local __inline_return__12, __inline_return__13, __inline_return__14 = nil, nil, nil
repeat
    __inline_return__12, __inline_return__13, __inline_return__14 = 1, 2, 3
    break
until true

local test5 = {1, __inline_return__12, 2}
local __inline_return__15, __inline_return__16, __inline_return__17 = nil, nil, nil
repeat
    __inline_return__15, __inline_return__16, __inline_return__17 = 1, 2, 3
    break
until true

local test5 = {[0] = 1, __inline_return__15, 2}

local function test7()
local __inline_return__18, __inline_return__19, __inline_return__20 = nil, nil, nil
repeat
    __inline_return__18, __inline_return__19, __inline_return__20 = 1, 2, 3
    break
until true
	return 1, __inline_return__18, 2
end