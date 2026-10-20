using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sfp.App.ViewModels.Services;
using Sfp.Core.Dates;
using Sfp.Core.Manifests;
using Sfp.Core.Options;
using Sfp.OneNote.Generate;

namespace Sfp.App.ViewModels.Generate;

/// <summary>
/// The Generate tab: the fields of the PRD table with their inline messages, the size estimate, the preview strip and
/// the run. Every rule lives in <see cref="GenerateFormValidator"/>; this class turns field text into validator input,
/// keeps the notebook name following its default until the user edits it, and starts the PDF check and the previews.
/// </summary>
public sealed partial class GenerateViewModel : ObservableObject, IDisposable
{
    private static readonly HashSet<string> InputProperties = new(StringComparer.Ordinal)
    {
        nameof(PdfPath), nameof(Month), nameof(YearText), nameof(NotebookName), nameof(OutputFolder), nameof(DateFormat),
        nameof(WeeksRemaining), nameof(Dpi), nameof(GroupWeeks), nameof(Links), nameof(HideTitle), nameof(CustomPageSize),
        nameof(PageScaleText), nameof(SixthRow), nameof(OutOfMonthDays), nameof(ReflectionDateIsWeekStart),
        nameof(IncludeOutOfQuarterDays), nameof(TimeBlockStart), nameof(TimeBlockEnd),
    };

    /// <summary>Properties whose change re-renders the previews (the date, the formats, everything drawn on the sheet; not the name, folder or DPI).</summary>
    private static readonly HashSet<string> PreviewProperties = new(StringComparer.Ordinal)
    {
        nameof(Month), nameof(YearText), nameof(DateFormat), nameof(WeeksRemaining), nameof(Links), nameof(HideTitle),
        nameof(SixthRow), nameof(OutOfMonthDays), nameof(ReflectionDateIsWeekStart), nameof(IncludeOutOfQuarterDays),
        nameof(TimeBlockStart), nameof(TimeBlockEnd),
    };

    private readonly Manifest _manifest;
    private readonly IDateEngine _dates;
    private readonly GenerateFormValidator _validator;
    private readonly IDialogService _dialogs;
    private readonly IUiThread _ui;
    private readonly AppEnvironment _env;
    private GenerateFormValidation _validation = GenerateFormValidation.Empty;
    private ValidationResult? _pdfResult;
    private string? _pdfResultPath;
    private int _pdfCheckSequence;
    private bool _loading;
    private bool _nameFollowsDefault = true;
    private string _defaultName = "";
    private QuarterSpec _lastValidStart;

    [ObservableProperty]
    private string _pdfPath = "";

    [ObservableProperty]
    private int _month;

    [ObservableProperty]
    private string _yearText = "";

    [ObservableProperty]
    private string _notebookName = "";

    [ObservableProperty]
    private string _outputFolder = "";

    [ObservableProperty]
    private DateFormatChoice _dateFormat = DateFormatChoice.All[0];

    [ObservableProperty]
    private WeeksRemainingMode _weeksRemaining = WeeksRemainingMode.Corrected;

    [ObservableProperty]
    private int _dpi = 220;

    [ObservableProperty]
    private bool _groupWeeks;

    [ObservableProperty]
    private bool _links = true;

    [ObservableProperty]
    private bool _hideTitle;

    [ObservableProperty]
    private bool _customPageSize;

    [ObservableProperty]
    private string _pageScaleText = "1";

    [ObservableProperty]
    private SixthRowMode _sixthRow = SixthRowMode.Stack;

    [ObservableProperty]
    private OutOfMonthDayStyle _outOfMonthDays = OutOfMonthDayStyle.Greyed;

    [ObservableProperty]
    private bool _reflectionDateIsWeekStart;

    [ObservableProperty]
    private bool _includeOutOfQuarterDays;

    [ObservableProperty]
    private int _timeBlockStart = TimeBlockWindow.Default.StartHour;

    [ObservableProperty]
    private int _timeBlockEnd = TimeBlockWindow.Default.EndHour;

    [ObservableProperty]
    private string? _pdfError;

    [ObservableProperty]
    private string? _pdfPending;

    [ObservableProperty]
    private string? _pdfWarning;

    [ObservableProperty]
    private string? _startError;

    [ObservableProperty]
    private string? _nameError;

    [ObservableProperty]
    private string? _folderError;

    [ObservableProperty]
    private string? _folderWarning;

    [ObservableProperty]
    private string? _dpiError;

    [ObservableProperty]
    private string? _pageScaleError;

    [ObservableProperty]
    private string? _timeBlocksError;

    [ObservableProperty]
    private string _sizeEstimate = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private bool _canGenerate;

    public GenerateViewModel(
        Manifest manifest,
        IDateEngine dates,
        GenerateFormValidator validator,
        PreviewStripViewModel previews,
        GenerateRunViewModel run,
        IDialogService dialogs,
        IUiThread ui,
        AppEnvironment env)
    {
        _manifest = manifest;
        _dates = dates;
        _validator = validator;
        _dialogs = dialogs;
        _ui = ui;
        _env = env;
        Previews = previews;
        Run = run;
        PendingPdfCheck = Task.CompletedTask;
        _lastValidStart = GenerateFormDefaults.NextQuarterStart(env.CurrentDate());
        Run.PropertyChanged += OnRunPropertyChanged;
        LoadFrom(new AppSettings());
    }

    /// <summary>Raised after any field changed by the user (not during <see cref="LoadFrom"/>), so settings can be saved.</summary>
    public event EventHandler? InputChanged;

    public PreviewStripViewModel Previews { get; }

    public GenerateRunViewModel Run { get; }

    public IReadOnlyList<Choice<int>> Months => GenerateChoices.Months;

    public IReadOnlyList<DateFormatChoice> DateFormats => DateFormatChoice.All;

    public IReadOnlyList<Choice<WeeksRemainingMode>> WeeksRemainingChoices => GenerateChoices.WeeksRemaining;

    public IReadOnlyList<string> CalendarMonthChoices => GenerateChoices.CalendarMonths;

    /// <summary>Fixed at three; the dropdown is read-only.</summary>
    public string CalendarMonths => GenerateChoices.CalendarMonths[0];

    public IReadOnlyList<int> DpiChoices => GenerateChoices.Dpis;

    public IReadOnlyList<Choice<SixthRowMode>> SixthRowChoices => GenerateChoices.SixthRow;

    public IReadOnlyList<Choice<OutOfMonthDayStyle>> OutOfMonthDayChoices => GenerateChoices.OutOfMonthDays;

    public IReadOnlyList<int> StartHours => GenerateChoices.StartHours;

    public IReadOnlyList<int> EndHours => GenerateChoices.EndHours;

    /// <summary>The fields unlock when no run is on.</summary>
    public bool FieldsEnabled => !Run.IsRunning;

    /// <summary>
    /// The PRD's "fallback font in use" banner: the font service's warnings, one per line, or null when the template
    /// typeface is installed. Shown before Generate so the user knows the dates will use the bundled face.
    /// </summary>
    public string? FontWarning => _env.FontWarnings.Count > 0 ? string.Join("\n", _env.FontWarnings) : null;

    /// <summary>
    /// The start month when the year parses and lies in 1900 to 2199 (the date engine's range) and the month is 1 to 12,
    /// else null. Every year the user can type passes through here ("2", "20", "202" while typing 2027), so nothing
    /// downstream ever hands the date engine a year it refuses.
    /// </summary>
    public QuarterSpec? Start => ParseYear() is int year && IsSupported(year, Month) ? new QuarterSpec(year, Month) : null;

    /// <summary>The default name for the current start ("SFP Q1 2027"), kept while the user has not edited the name.</summary>
    public string DefaultNotebookName => _defaultName;

    /// <summary>The last PDF check started, for tests and a clean shutdown.</summary>
    public Task PendingPdfCheck { get; private set; }

    /// <summary>The last validation pass, with every message.</summary>
    public GenerateFormValidation Validation => _validation;

    /// <summary>
    /// Fills every field from saved settings, with the PRD defaults where a value is missing, then starts the PDF check.
    /// The saved start month is restored only when it is not before the next calendar quarter (as of the clock now):
    /// last quarter's start would otherwise greet the user every time. A saved name that was the default of the saved
    /// start follows the default of the start shown; a name the user typed is kept.
    /// </summary>
    public void LoadFrom(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loading = true;
        try
        {
            QuarterSpec next = GenerateFormDefaults.NextQuarterStart(_env.CurrentDate());
            QuarterSpec? saved = settings.Start;
            QuarterSpec start = IsSupported(saved) && !IsBefore(saved, next) ? saved : next;
            _lastValidStart = start;
            Month = start.Month;
            YearText = start.Year.ToString(CultureInfo.InvariantCulture);
            _defaultName = DefaultNameFor(start);
            string? savedDefault = IsSupported(saved) ? DefaultNameFor(saved) : null;
            NotebookName = string.IsNullOrWhiteSpace(settings.NotebookName) || settings.NotebookName == savedDefault ? _defaultName : settings.NotebookName;
            _nameFollowsDefault = NotebookName == _defaultName;
            OutputFolder = string.IsNullOrWhiteSpace(settings.OutputFolder) ? _env.DefaultOutputFolder : settings.OutputFolder;
            DateFormat = DateFormatChoice.For(settings.DateOrder, settings.PadDateFields);
            WeeksRemaining = settings.WeeksRemaining;
            Dpi = GenerateOptions.AllowedDpis.Contains(settings.Dpi) ? settings.Dpi : 220;
            GroupWeeks = settings.GroupWeeks;
            Links = settings.Links;
            HideTitle = settings.HideTitle;
            CustomPageSize = settings.PageSize == PageSizeMode.Custom;
            PageScaleText = settings.PageScale.ToString("0.##", CultureInfo.InvariantCulture);
            SixthRow = settings.SixthRow;
            OutOfMonthDays = settings.OutOfMonthDays;
            ReflectionDateIsWeekStart = settings.ReflectionDateIsWeekStart;
            IncludeOutOfQuarterDays = settings.IncludeOutOfQuarterDays;
            TimeBlockStart = settings.TimeBlocks.StartHour;
            TimeBlockEnd = settings.TimeBlocks.EndHour;
            PdfPath = settings.PdfPath ?? "";
        }
        finally
        {
            _loading = false;
        }

        StartPdfCheck();
        Revalidate();
        RequestPreviews();
    }

    /// <summary>The options the fields describe, using the last valid start when the year box does not parse.</summary>
    public GenerateOptions ToOptions() => new()
    {
        Start = Start ?? _lastValidStart,
        PdfPath = PdfPath.Trim(),
        NotebookName = NotebookName.Trim(),
        OutputFolder = OutputFolder.Trim(),
        DateOrder = DateFormat.Order,
        PadDateFields = DateFormat.Padded,
        WeeksRemaining = WeeksRemaining,
        Dpi = Dpi,
        GroupWeeks = GroupWeeks,
        Links = Links,
        HideTitle = HideTitle,
        PageSize = CustomPageSize ? PageSizeMode.Custom : PageSizeMode.Automatic,
        PageScale = ParsePageScale() ?? 1.0,
        SixthRow = SixthRow,
        OutOfMonthDays = OutOfMonthDays,
        ReflectionDateIsWeekStart = ReflectionDateIsWeekStart,
        IncludeOutOfQuarterDays = IncludeOutOfQuarterDays,
        TimeBlocks = new TimeBlockWindow(TimeBlockStart, TimeBlockEnd),
    };

    /// <summary>The settings to save for the current fields.</summary>
    public AppSettings ToSettings(IReadOnlyList<string> knownOutputFolders) => AppSettings.From(ToOptions(), knownOutputFolders);

    /// <summary>The request the Generate button sends.</summary>
    public GenerateRequest ToRequest() => new(ToOptions(), _manifest, _env.TemplatesDir, RenderOnly: false, RenderOutputDir: null);

    /// <summary>The validator input for the current field text.</summary>
    public GenerateFormInput ToInput() => new(
        PdfPath,
        ParseYear(),
        Month,
        NotebookName,
        OutputFolder,
        Dpi,
        ParsePageScale(),
        new TimeBlockWindow(TimeBlockStart, TimeBlockEnd));

    public void Dispose()
    {
        Run.PropertyChanged -= OnRunPropertyChanged;
        Previews.Dispose();
        Run.Dispose();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is null || !InputProperties.Contains(e.PropertyName))
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(PdfPath):
                StartPdfCheck();
                break;
            case nameof(Month):
            case nameof(YearText):
                OnStartChanged();
                break;
            case nameof(NotebookName):
                _nameFollowsDefault = NotebookName == _defaultName;
                break;
            default:
                break;
        }

        Revalidate();
        if (PreviewProperties.Contains(e.PropertyName))
        {
            RequestPreviews();
        }

        InputChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void BrowsePdf()
    {
        string? chosen = _dialogs.PickFile("Choose the SFP template PDF", "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*", PdfPath);
        if (chosen is not null)
        {
            PdfPath = chosen;
        }
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        string? chosen = _dialogs.PickFolder("Choose the output folder", OutputFolder);
        if (chosen is not null)
        {
            OutputFolder = chosen;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (!CanGenerate)
        {
            return;
        }

        await Run.StartAsync(ToRequest());
        Revalidate();
    }

    private void OnRunPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GenerateRunViewModel.IsRunning))
        {
            OnPropertyChanged(nameof(FieldsEnabled));
            Revalidate();
        }
    }

    private void OnStartChanged()
    {
        if (Start is not { } start)
        {
            return;
        }

        _lastValidStart = start;
        _defaultName = DefaultNameFor(start);
        if (_nameFollowsDefault && NotebookName != _defaultName)
        {
            NotebookName = _defaultName;
        }
    }

    /// <summary>True for a year and month the date engine accepts (1900 to 2199, 1 to 12).</summary>
    private static bool IsSupported(int year, int month) => year is >= QuarterSpec.MinYear and <= QuarterSpec.MaxYear && month is >= 1 and <= 12;

    private static bool IsSupported([NotNullWhen(true)] QuarterSpec? spec) => spec is not null && IsSupported(spec.Year, spec.Month);

    /// <summary>True when <paramref name="a"/> is an earlier month than <paramref name="b"/>.</summary>
    private static bool IsBefore(QuarterSpec a, QuarterSpec b) => a.Year < b.Year || (a.Year == b.Year && a.Month < b.Month);

    /// <summary>The quarter of a supported start, else null. Every date engine call goes through here because <see cref="IDateEngine.Build"/> throws outside 1900 to 2199.</summary>
    private Quarter? QuarterOf(QuarterSpec? spec) => IsSupported(spec) ? _dates.Build(spec) : null;

    /// <summary>"SFP Q1 2027" for a supported start; empty when the start is outside the supported years.</summary>
    private string DefaultNameFor(QuarterSpec start) =>
        QuarterOf(start) is { } quarter ? GenerateFormDefaults.NotebookName(_manifest.Notebook.NamePattern, quarter) : "";

    private int? ParseYear() =>
        int.TryParse(YearText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int year) ? year : null;

    private double? ParsePageScale() =>
        double.TryParse(PageScaleText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) ? scale : null;

    private void StartPdfCheck()
    {
        int sequence = ++_pdfCheckSequence;
        string path = PdfPath;
        _pdfResult = null;
        _pdfResultPath = null;
        PendingPdfCheck = CheckPdfAsync(path, sequence);
    }

    private async Task CheckPdfAsync(string path, int sequence)
    {
        ValidationResult result;
        try
        {
            result = await Task.Run(() => _validator.CheckPdf(_manifest, path));
        }
        catch (Exception ex)
        {
            // The validator reports unreadable files as results; anything else still becomes a message, never a crash.
            result = ValidationResult.Fail("Could not check the PDF: " + ex.Message);
        }

        _ui.Post(() =>
        {
            if (sequence != _pdfCheckSequence)
            {
                return;
            }

            _pdfResult = result;
            _pdfResultPath = path;
            Revalidate();
            RequestPreviews();
        });
    }

    private void Revalidate()
    {
        ValidationResult? pdf = _pdfResultPath == PdfPath ? _pdfResult : null;
        _validation = _validator.Validate(ToInput(), pdf);
        PdfError = _validation.ErrorFor(GenerateFormFields.PdfPath);
        PdfPending = _validation.PendingFor(GenerateFormFields.PdfPath);
        PdfWarning = _validation.WarningFor(GenerateFormFields.PdfPath);
        StartError = _validation.ErrorFor(GenerateFormFields.Start);
        NameError = _validation.ErrorFor(GenerateFormFields.NotebookName);
        FolderError = _validation.ErrorFor(GenerateFormFields.OutputFolder);
        FolderWarning = _validation.WarningFor(GenerateFormFields.OutputFolder);
        DpiError = _validation.ErrorFor(GenerateFormFields.Dpi);
        PageScaleError = _validation.ErrorFor(GenerateFormFields.PageScale);
        TimeBlocksError = _validation.ErrorFor(GenerateFormFields.TimeBlocks);
        SizeEstimate = GenerateOptions.AllowedDpis.Contains(Dpi) && QuarterOf(Start ?? _lastValidStart) is { } quarter
            ? NotebookSizeEstimate.Describe(Dpi, GenerateFormDefaults.PageCount(quarter))
            : "";
        CanGenerate = _validation.CanGenerate && !Run.IsRunning;
        OnPropertyChanged(nameof(Start));
        OnPropertyChanged(nameof(DefaultNotebookName));
        OnPropertyChanged(nameof(Validation));
    }

    private void RequestPreviews()
    {
        if (Start is null)
        {
            Previews.Clear("Fix the start month to see previews.");
            return;
        }

        bool pdfOk = _pdfResult is { IsValid: true } && _pdfResultPath == PdfPath;
        if (!pdfOk)
        {
            Previews.Clear(_pdfResult is null && !string.IsNullOrWhiteSpace(PdfPath)
                ? "Checking the PDF..."
                : "Choose a template PDF that matches the manifest to see previews.");
            return;
        }

        Previews.Request(ToOptions());
    }
}
