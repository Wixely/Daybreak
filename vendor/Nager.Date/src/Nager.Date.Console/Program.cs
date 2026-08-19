using Nager.Date;
using Spectre.Console;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

AnsiConsole.WriteLine("Nager.Date");
AnsiConsole.Write(new Rule { Style = Style.Parse("grey dim") });

var action = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("How would you like to [green]proceed[/]?")
        .PageSize(10)
        .AddChoices([
            "Show Holiday Table",
            "Export Markdown Table",
            "Exit"
        ]));

// 3. Branching based on selection
switch (action)
{
    case "Show Holiday Table":
        ShowHolidayTable();
        break;

    case "Export Markdown Table":
        ExportMarkdownTable();
        break;

    case "Exit":
        AnsiConsole.MarkupLine("[red]Program terminated.[/]");
        break;
}

static void ExportMarkdownTable()
{
    var countryCode = AnsiConsole.Ask<string>("Please enter the country code (example:AT)");

    var publicHolidays = HolidaySystem.GetHolidays(DateTime.Today.Year, countryCode);

    var selectedHolidayIds = AnsiConsole.Prompt(
        new MultiSelectionPrompt<string>()
            .Title("Please select the [green]years[/] to calculate:")
            .AddChoices(publicHolidays.Select(o => o.Id)));

    var minYear = DateTime.Today.AddYears(-20).Year;
    var maxYear = DateTime.Today.AddYears(20).Year;

    var markdownTable = new StringBuilder();

    markdownTable.Append('|');
    foreach (var id in selectedHolidayIds)
    {
        markdownTable.Append(id);
        markdownTable.Append('|');
    }

    markdownTable.AppendLine();

    markdownTable.Append('|');
    foreach (var id in selectedHolidayIds)
    {
        markdownTable.Append(new string('-', id.Length));
        markdownTable.Append('|');
    }

    markdownTable.AppendLine();

    for (var year = minYear; year <= maxYear; year++)
    {
        publicHolidays = HolidaySystem.GetHolidays(year, countryCode);

        markdownTable.Append('|');
        foreach (var id in selectedHolidayIds)
        {
            var holiday = publicHolidays.First(o => o.Id == id);
            var length = id.Length - 10;

            markdownTable.Append($"{holiday.Date:yyyy-MM-dd}{new string(' ', length)}");
            markdownTable.Append('|');
        }

        markdownTable.AppendLine();
    }

    AnsiConsole.Write(markdownTable.ToString());
}

static void ShowHolidayTable()
{
    var availableYears = new List<string>();

    for (var year1 = DateTime.Today.AddYears(5).Year; year1 >= DateTime.Today.AddYears(-16).Year; year1--)
    {
        availableYears.Add($"{year1}");
    }

    var selectedYears = AnsiConsole.Prompt(
        new MultiSelectionPrompt<string>()
            .Title("Please select the [green]years[/] to calculate:")
            .AddChoices(availableYears)
            .Select(DateTime.Today.Year.ToString())
            .Select(DateTime.Today.AddYears(-1).Year.ToString())
            .Select(DateTime.Today.AddYears(1).Year.ToString()));


    var countryCode = AnsiConsole.Ask<string>("Please enter the country code (example:AT)");

    var years = selectedYears.Select(int.Parse);

    foreach (var year in years)
    {
        var table = new Table();

        table.Title(new TableTitle($"Calculated holidays for [blue]{countryCode.ToUpper()}[/] [blue]{year}[/]"));

        table.AddColumn("Date");
        table.AddColumn("Observed");
        table.AddColumn("English Name");
        table.AddColumn("Local Name");
        table.AddColumn("Type");
        table.AddColumn("SubdivisionCodes");
        //table.AddColumn("ID");

        var publicHolidays = HolidaySystem.GetHolidays(year, countryCode);
        foreach (var publicHoliday in publicHolidays)
        {
            var subdivisionCodes = publicHoliday.SubdivisionCodes != null ? string.Join(',', publicHoliday.SubdivisionCodes) : "";

            var observedDate = "";
            if (!publicHoliday.Date.Equals(publicHoliday.ObservedDate.Date))
            {
                observedDate = $"{publicHoliday.ObservedDate:ddd} {publicHoliday.ObservedDate:d}";
            }

            table.AddRow(
                $"{publicHoliday.Date:ddd} {publicHoliday.Date:d}",
                $"{observedDate}",
                $"{publicHoliday.EnglishName}",
                $"{publicHoliday.LocalName}",
                $"{publicHoliday.HolidayTypes}",
                $"{subdivisionCodes}"
            //$"{publicHoliday.Id}"
            );
        }

        AnsiConsole.Write(table);
    }
}
