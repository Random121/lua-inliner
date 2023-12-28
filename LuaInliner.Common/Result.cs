using DotNext;

namespace LuaInliner.Common;

public readonly struct Result<T, E>
{
    public readonly bool IsOk { get; }
    public readonly bool IsErr => !IsOk;

    private readonly Optional<T> _okResult;
    private readonly Optional<E> _errResult;

    public readonly Optional<T> Ok => IsOk ? _okResult : Optional<T>.None;
    public readonly Optional<E> Err => !IsOk ? _errResult : Optional<E>.None;

    internal Result(bool isOk, Optional<T> okResult, Optional<E> errResult)
    {
        IsOk = isOk;
        _okResult = okResult;
        _errResult = errResult;
    }
}

public static class Result
{
    public static Result<T, E> Ok<T, E>(T okResult)
    {
        return new Result<T, E>(true, Optional.Some(okResult!), Optional<E>.None);
    }

    public static Result<T, E> Err<T, E>(E errResult)
    {
        return new Result<T, E>(true, Optional<T>.None, Optional.Some(errResult!));
    }
}
