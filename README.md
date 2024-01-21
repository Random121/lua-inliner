# Lua Inliner

Source-to-source transformer for Lua/Luau to inline functions.

## Known Issues

1. Invalid code is generated when an inline function calls another inline function
2. Having an inline function with upvalues results in code that has undefined behaviour