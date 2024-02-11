# Known Issues

1. Invalid code is generated when an inline function calls another inline function
2. Having an inline function with upvalues results in code that has undefined behaviour
3. Excessive usage of inline functions (or functions with many return values) can exceed the Lua local variable limit
4. Shortcircuit logic will not work correctly as the inline function will always run
5. Function will always run if it is the condition in an `elseif` statement