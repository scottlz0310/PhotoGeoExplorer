using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Design",
    "CA1515:Consider making public types internal",
    Justification = "xUnit requires public test classes.",
    Scope = "module")]

[assembly: SuppressMessage(
    "Naming",
    "CA1707:Identifiers should not contain underscores",
    Justification = "xUnitのテスト名は可読性のためにアンダースコアを使用する。",
    Scope = "module")]
