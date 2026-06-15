namespace PPDO.Application.DTOs.BudgetPlanning;

public record LdipRecordDto(
    int    Id,
    string RefCode,
    string Title,
    int    FiscalYearStart,
    int    FiscalYearEnd,
    string EntryMode,
    string Status,
    int?   SourceId,
    Guid   CreatedById,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateLdipDto(
    string Title,
    int    FiscalYearStart,
    int    FiscalYearEnd,
    string EntryMode);

public record UpdateLdipDto(
    string Title,
    int    FiscalYearStart,
    int    FiscalYearEnd,
    string EntryMode);
