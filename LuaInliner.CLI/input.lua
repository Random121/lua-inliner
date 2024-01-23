local function inlineMe()
	--!!INLINE_FUNCTION

	return 1, 2, 3
end

local function inlineNil()
	--!!INLINE_FUNCTION
end

local test1 = inlineMe()

local test2 = 1, 2, inlineMe()

local test3 = 1, inlineMe(), 2

-- test4
print(1, inlineMe(), 2)

local test5 = {1, inlineMe(), 2}

local test5 = {[0] = 1, inlineMe(), 2}

local function test7()
	return 1, inlineMe(), 2
end