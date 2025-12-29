using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "MSTest requires public test classes.",
    Scope = "module")]
