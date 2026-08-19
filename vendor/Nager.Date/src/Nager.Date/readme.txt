Daybreak.Nager.Date
===================

This is Daybreak's source-vendored build of Nager.Date. It calculates holidays
locally and does not require a license key or a remote API.

Upstream project: https://github.com/nager/Nager.Date
Daybreak changes: ../../DAYBREAK-VENDORING.md

Example
-------

    var holidays = HolidaySystem.GetHolidays(2024, "DE");
    foreach (var holiday in holidays)
    {
        Console.WriteLine($"{holiday.Date:yyyy-MM-dd} - {holiday.EnglishName}");
    }
