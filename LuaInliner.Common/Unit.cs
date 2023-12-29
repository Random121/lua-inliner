using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LuaInliner.Common;

/// <summary>
/// A type that represents the absence of a specific value.<br/>
/// Comparable to a <c>void</c> type.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    public static Unit Default => default;

    public bool Equals(Unit other)
    {
        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is Unit;
    }

    public override int GetHashCode()
    {
        return 0;
    }

    public override string ToString()
    {
        return "()";
    }

    [SuppressMessage(
        "Roslynator",
        "RCS1163",
        Justification = "Comparison is always true regardless of Unit passed in."
    )]
    [SuppressMessage(
        "Style",
        "IDE0060",
        Justification = "Comparison is always true regardless of Unit passed in."
    )]
    public static bool operator ==(Unit left, Unit right)
    {
        return true;
    }

    [SuppressMessage(
        "Roslynator",
        "RCS1163",
        Justification = "Comparison is always false regardless of Unit passed in."
    )]
    [SuppressMessage(
        "Style",
        "IDE0060",
        Justification = "Comparison is always false regardless of Unit passed in."
    )]
    public static bool operator !=(Unit left, Unit right)
    {
        return false;
    }
}
