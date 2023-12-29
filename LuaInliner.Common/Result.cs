using DotNext;

namespace LuaInliner.Common;

/// <summary>
/// A type that represents either a success value (<see cref="T"/>)
/// or a failure/error value (<see cref="E"/>)
/// </summary>
/// <typeparam name="T">Type of the success value</typeparam>
/// <typeparam name="E">Type of the failure/error value</typeparam>
public readonly struct Result<T, E>
{
    public readonly bool IsOk { get; }
    public readonly bool IsErr => !IsOk;

    public readonly Optional<T> Ok { get; }
    public readonly Optional<E> Err { get; }

    internal Result(T okResult)
    {
        IsOk = true;
        Ok = Optional.Some(okResult!);
        Err = Optional<E>.None;
    }

    internal Result(E errResult)
    {
        IsOk = false;
        Ok = Optional<T>.None;
        Err = Optional.Some(errResult!);
    }
}

/// <summary>
/// Class containing factory methods for <see cref="Result{T, E}"/>
/// </summary>
public static class Result
{
    /// <summary>
    /// Creates a <see cref="Result{T, E}"/> type that represents a success value
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="E"></typeparam>
    /// <param name="okResult"></param>
    /// <returns></returns>
    public static Result<T, E> Ok<T, E>(T okResult)
    {
        return new Result<T, E>(okResult);
    }

    /// <summary>
    /// Creates a <see cref="Result{T, E}"/> type that represents a failure/error value
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="E"></typeparam>
    /// <param name="errResult"></param>
    /// <returns></returns>
    public static Result<T, E> Err<T, E>(E errResult)
    {
        return new Result<T, E>(errResult);
    }
}
