using SnabDrive.Web.Data.Entities;

namespace SnabDrive.Web.Services;

/// <summary>
/// Единая точка перевода закупки между стадиями. Фиксирует, кто и когда завершил этап
/// и кто начал следующий, — по этим данным видно, сколько и какой работы сделал каждый
/// сотрудник (вкладку «Задачи» и журнал передач ведут несколько человек одновременно).
/// </summary>
public static class StageTracker
{
    /// <summary>
    /// Переводит закупку на стадию <paramref name="toStage"/>, закрывая текущий этап:
    /// «завершил» — про этап, с которого ушли, «начал» — про новый.
    /// </summary>
    public static void MoveTo(Regedit item, int toStage, string userName, DateTime? atUtc = null)
    {
        var moment = atUtc ?? DateTime.UtcNow;
        var who = string.IsNullOrWhiteSpace(userName) ? "неизвестный" : userName.Trim();

        // Этап, который только что закрыли.
        item.StageCompletedAtUtc = moment;
        item.StageCompletedBy = who;

        item.Stage = toStage;
        item.IsLost = toStage == 0;   // «Проиграна»

        // Отсчёт времени на новом этапе.
        item.StageStartedAtUtc = moment;
        item.StageStartedBy = who;
    }
}
