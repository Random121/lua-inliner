using Loretta.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LuaInliner.Core;

internal static class Diagnostics
{
    private const string DIAGNOSTIC_CATEGORY = "LuaInliner";

    public static readonly DiagnosticDescriptor InvalidInlineDirectiveUsage =
        new(
            "INLINER0001",
            "Invalid usage of inline directive",
            "Invalid usage of inline directive",
            DIAGNOSTIC_CATEGORY,
            DiagnosticSeverity.Error,
            true,
            customTags: [WellKnownDiagnosticTags.NotConfigurable]
        );

    public static readonly DiagnosticDescriptor CannotInlineVariadicFunction =
        new(
            "INLINER0002",
            "Cannot inline variadic function",
            "Cannot inline functions with variadic parameters",
            DIAGNOSTIC_CATEGORY,
            DiagnosticSeverity.Error,
            true,
            customTags: [WellKnownDiagnosticTags.NotConfigurable]
        );

    public static readonly DiagnosticDescriptor CannotInlineRecursiveFunction =
        new(
            "INLINER0003",
            "Cannot inline recursive functions",
            "Cannot inline recursive functions",
            DIAGNOSTIC_CATEGORY,
            DiagnosticSeverity.Error,
            true,
            customTags: [WellKnownDiagnosticTags.NotConfigurable]
        );
}
