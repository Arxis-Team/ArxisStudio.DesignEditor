using System.Runtime.CompilerServices;

// Значительная часть interaction-логики редактора (политики перемещения и resize,
// групповые операции, резолв selection target) намеренно internal.
// Тестам она нужна напрямую, поэтому сборка тестов видит внутренности.
[assembly: InternalsVisibleTo("ArxisStudio.DesignEditor.Tests")]
