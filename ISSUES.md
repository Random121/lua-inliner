# Known Issues

## Plan to Fix

1. Calls to inline functions from within inline functions don't work
2. Usage of upvalues within inline functions is undefined behaviour
3. No support for recursive calls

## No Plan to Fix

1. Shortcircuit logic will not work correctly as the inline function will always run
2. Function will always run if it is the condition in an `elseif` statement
3. Can't inline functions that are forward declared