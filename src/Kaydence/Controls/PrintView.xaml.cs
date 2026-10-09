using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Kaydence.Services;

namespace Kaydence.Controls;

// I pick the days and what to include, see the pages, then print them or save a PDF
public partial class PrintView : UserControl
{
    private static readonly string[] DateFormats = { "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "d MMM yyyy", "d MMMM yyyy", "yyyy-MM-dd" };

    private readonly DispatcherTimer _debounce;
    private DiaryStore? _store;
    private AppSettings? _settings;
    private bool _settingUp;

    public PrintView()
    {
        InitializeComponent();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += (_, _) => RefreshPreview();

        RangeChips.Children.Add(UiKit.ChoiceChips(new[]
        {
            ("week", "Last 7 days"), ("this", "This month"), ("last", "Last month"), ("three", "Last 3 months"), ("all", "Everything")
        }, "last", SetRange));

        AudienceChips.Children.Add(UiKit.ChoiceChips(new[]
        {
            ("me", "Just for you"), ("doctor", "Your doctor")
        }, "me", SetAudience));
    }

    public void Initialise(DiaryStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        SetRange("last");
    }

    public void Refresh()
    {
        ApplyOptionalSections();
        RefreshPreview();
    }

    // I only offer health, depression and anxiety, transition and cycle options when those sections are switched on
    private void ApplyOptionalSections()
    {
        if (_settings == null) return;
        void Show(CheckBox box, bool show)
        {
            box.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) box.IsChecked = false;
        }
        Show(IncHealth, _settings.ShowsAny(AppSettings.HealthSections) || _settings.ShowsAny("Cycle"));
        Show(IncHealthCharts, _settings.ShowsAny("Sleep", "Weight", "BloodPressure", "Symptoms"));
        Show(IncTransition, _settings.ShowsAny("Transition"));
        Show(IncMind, _settings.ShowsAny(AppSettings.MindSections));
    }

    // I pick every day for a full PDF of the diary
    public void ShowEverything()
    {
        foreach (var chip in RangeChips.Children.OfType<WrapPanel>().SelectMany(p => p.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>()))
            chip.IsChecked = (string)chip.Tag == "all";
        SetRange("all");
        Summary.Text += ". Press Save as PDF to keep them all in one file.";
    }

    private void SetRange(string range)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var (from, to) = range switch
        {
            "week" => (today.AddDays(-6), today),
            "this" => (thisMonth, today),
            "three" => (today.AddMonths(-3).AddDays(1), today),
            "all" => (_store?.Days.DefaultIfEmpty(today).Min() ?? today, today),
            _ => (thisMonth.AddMonths(-1), thisMonth.AddDays(-1))
        };
        _settingUp = true;
        FromBox.Text = from.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        ToBox.Text = to.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        _settingUp = false;
        RefreshPreview();
    }

    // I switch the tick boxes to sensible choices for who the pages are for
    private void SetAudience(string audience)
    {
        var doctor = audience == "doctor";
        IncWriting.IsChecked = !doctor;
        IncPictures.IsChecked = !doctor;
        IncMood.IsChecked = true;
        IncChart.IsChecked = true;
        IncCheckIn.IsChecked = true;
        IncHealth.IsChecked = doctor;
        IncHealthCharts.IsChecked = doctor;
        IncMind.IsChecked = doctor;
        IncTransition.IsChecked = doctor;
        ApplyOptionalSections();
        RefreshPreview();
    }

    private void Dates_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingUp) return;
        _debounce.Stop();
        _debounce.Start();
    }

    private void Option_Click(object sender, RoutedEventArgs e) => RefreshPreview();

    private ReportOptions Options() => new()
    {
        Writing = IncWriting.IsChecked == true,
        Pictures = IncPictures.IsChecked == true,
        Mood = IncMood.IsChecked == true,
        MoodChart = IncChart.IsChecked == true,
        CheckIn = IncCheckIn.IsChecked == true,
        Health = IncHealth.IsChecked == true,
        HealthCharts = IncHealthCharts.IsChecked == true,
        Mind = IncMind.IsChecked == true,
        Transition = IncTransition.IsChecked == true,
        DayPerPage = DayPerPage.IsChecked == true
    };

    private bool TryGetRange(out DateOnly from, out DateOnly to)
    {
        var fromOk = DateOnly.TryParseExact(FromBox.Text.Trim(), DateFormats, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out from);
        var toOk = DateOnly.TryParseExact(ToBox.Text.Trim(), DateFormats, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out to);
        if (fromOk && toOk && from > to) (from, to) = (to, from);
        return fromOk && toOk;
    }

    private FlowDocument? BuildDocument(out int days)
    {
        days = 0;
        if (_store == null || !TryGetRange(out var from, out var to)) return null;
        var options = Options();
        using (Log.Time("Print", $"Building pages for {from:yyyy-MM-dd} to {to:yyyy-MM-dd}"))
        {
            var document = ReportBuilder.Build(_store, from, to, options, _settings?.WeightUnit ?? "kg", out days);
            Log.Debug("Print", $"{days} days, writing {options.Writing}, pictures {options.Pictures}, mood {options.Mood}, chart {options.MoodChart}, " +
                               $"check-in {options.CheckIn}, health {options.Health}, health charts {options.HealthCharts}, transition {options.Transition}, day per page {options.DayPerPage}");
            return document;
        }
    }

    private void RefreshPreview()
    {
        _debounce.Stop();
        if (_store == null) return;
        var document = BuildDocument(out var days);
        if (document == null)
        {
            Summary.Text = "Type the dates like 01/09/2026";
            Preview.Document = null;
            return;
        }
        Preview.Document = document;
        Summary.Text = days == 1 ? "1 day with an entry" : $"{days} days with entries";
    }

    private void Print_Click(object sender, RoutedEventArgs e) => PrintPages(false);

    private void Pdf_Click(object sender, RoutedEventArgs e) => PrintPages(true);

    private void PrintPages(bool pdf)
    {
        var dialog = new PrintDialog();
        if (pdf)
        {
            try
            {
                dialog.PrintQueue = new LocalPrintServer().GetPrintQueue("Microsoft Print to PDF");
            }
            catch (Exception ex)
            {
                // I let Windows pick the printer if Microsoft Print to PDF isn't installed
                Log.Warn("Print", "Microsoft Print to PDF isn't available", ex);
            }
        }
        if (dialog.ShowDialog() != true) return;
        Log.Info("Print", $"Printing to {dialog.PrintQueue?.Name ?? "the default printer"}, as PDF: {pdf}");

        // I build a fresh copy for printing because the preview is already using the other one
        var document = BuildDocument(out _);
        if (document == null) return;
        document.PageWidth = dialog.PrintableAreaWidth;
        document.PageHeight = dialog.PrintableAreaHeight;
        document.PagePadding = new Thickness(56);
        document.ColumnWidth = double.PositiveInfinity;

        try
        {
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Kaydence diary");
            Log.Info("Print", "Sent to the printer");
        }
        catch (Exception ex)
        {
            Log.Error("Print", "Printing failed", ex);
            MessageBox.Show(Window.GetWindow(this)!, $"Kaydence couldn't print.\n\n{ex.Message}", "Kaydence",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
