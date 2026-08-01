using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Importer.Core.Conversion;
using Importer.Core.Csv;
using Importer.Core.Models;
using Importer.Core.Processing;
using Importer.Core.Validation;
using Microsoft.Win32;

namespace Importer.UI;

public partial class MainWindow : Window
{
    private IReadOnlyList<ImportedSection> _sections = [];
    private ImportAnchor? _anchor;
    private AutomationWindow? _automationWindow;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void SelectFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a NoLimits 2 CSV export",
            Filter = "CSV and TSV files (*.csv;*.tsv)|*.csv;*.tsv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            FilePathTextBox.Text = dialog.FileName;
            _ = GeneratePreviewAsync();
        }
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e) =>
        await GeneratePreviewAsync();

    private async void FullPrecisionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(FilePathTextBox.Text) && File.Exists(FilePathTextBox.Text))
        {
            await GeneratePreviewAsync();
        }
    }

    private async void PositionReferenceComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(FilePathTextBox.Text) && File.Exists(FilePathTextBox.Text))
        {
            await GeneratePreviewAsync();
        }
    }

    private void OpenAutoModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sections.Count == 0 || _anchor is null)
        {
            return;
        }

        if (_automationWindow is not null)
        {
            _ = _automationWindow.Activate();
            return;
        }

        _automationWindow = new AutomationWindow(_anchor, _sections)
        {
            Owner = this,
        };
        _automationWindow.Closed += (_, _) => _automationWindow = null;
        _automationWindow.Show();
    }

    private async Task GeneratePreviewAsync()
    {
        var path = FilePathTextBox.Text;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show(
                this,
                "Select an existing NoLimits CSV export first.",
                "File required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var inputPrecision = FullPrecisionCheckBox.IsChecked == true
            ? InputPrecisionMode.Full
            : InputPrecisionMode.Fixed;
        var positionReference = PositionReferenceComboBox.SelectedIndex == 0
            ? PositionReferenceMode.PreviousNodeAxes
            : PositionReferenceMode.WorldAxes;
        var curveFitSettings = new Tpt2CurveFitSettings();
        if (!TryReadCurveFitSettings(out curveFitSettings))
        {
            MessageBox.Show(
                this,
                "Curve error, handle factor, average target length, and hard minimum must be positive numbers. " +
                "The hard minimum must be at least 4 and cannot exceed the average target; " +
                "the target cannot exceed 95 game units. " +
                "Recommended starting values are 0.50, 0.39, 15, and 4.",
                "Invalid curve-fit settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TryReadScale(out var scale))
        {
            MessageBox.Show(
                this,
                "Position scale must be a positive number, such as 0.5, 1, or 4.",
                "Invalid scale",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(true, "Reading and converting track samples…");
        try
        {
            var result = await Task.Run(async () =>
            {
                var parser = new NoLimitsCsvParser();
                var samples = await parser.ParseFileAsync(path);
                var curveFitResult = new Tpt2CurveFitter().Fit(
                    samples,
                    ImportConversionProfile.NoLimitsToRoblox,
                    scale,
                    curveFitSettings,
                    inputPrecision);
                var selectedSamples = curveFitResult.Samples;

                var generator = new SectionGenerator();
                var anchor = generator.CreateAnchor(
                    selectedSamples,
                    ImportConversionProfile.NoLimitsToRoblox,
                    inputPrecision: inputPrecision);
                var sections = generator.Generate(
                    selectedSamples,
                    ImportConversionProfile.NoLimitsToRoblox,
                    scale,
                    inputPrecision: inputPrecision,
                    positionReference: positionReference);
                var issues = new ImportPlanValidator().Validate(sections);
                return new PreviewResult(
                    samples.Count,
                    selectedSamples.Count,
                    curveFitResult.Metrics,
                    anchor,
                    sections,
                    issues);
            });

            PreviewDataGrid.ItemsSource = new[] { new PreviewRow(result.Anchor) }
                .Concat(result.Sections.Select(section => new PreviewRow(section)));
            _anchor = result.Anchor;
            _sections = result.Sections;
            OpenAutoModeButton.IsEnabled = true;
            var errors = result.Issues.Count(issue => issue.Severity == ValidationSeverity.Error);
            var warnings = result.Issues.Count(issue => issue.Severity == ValidationSeverity.Warning);
            var pointSummary =
                $"{result.SourceCount:N0} source samples → " +
                $"{result.SelectedPointCount:N0} curve-fit points; " +
                $"max/RMS error {result.CurveFitMetrics.MaximumDeviation:N3}/" +
                $"{result.CurveFitMetrics.RootMeanSquareDeviation:N3} game units; " +
                $"max roll error {result.CurveFitMetrics.MaximumRollDeviation:N2}°; " +
                $"min/average/max length {result.CurveFitMetrics.MinimumSectionLength:N2}/" +
                $"{result.CurveFitMetrics.AverageSectionLength:N2}/" +
                $"{result.CurveFitMetrics.MaximumSectionLength:N2} game units; " +
                $"{result.CurveFitMetrics.SimplifiedStraightSectionCount:N0} long-straight sections";
            var precisionSummary = inputPrecision == InputPrecisionMode.Full
                ? "full input precision"
                : "0.00 / 0.0 input precision";
            var referenceSummary = positionReference == PositionReferenceMode.PreviousNodeAxes
                ? "previous-node position axes; absolute rotation"
                : "world position axes; absolute rotation";
            StatusTextBlock.Text =
                $"{Path.GetFileName(path)}: {pointSummary} → " +
                $"Point 1 is the anchor; {result.Sections.Count:N0} sections begin with Point 1 → Point 2, " +
                $"{referenceSummary}, {precisionSummary} " +
                $"({warnings:N0} warnings, {errors:N0} errors).";

            if (result.Issues.Count > 0)
            {
                StatusTextBlock.ToolTip = string.Join(
                    Environment.NewLine,
                    result.Issues.Take(10).Select(FormatIssue));
            }
            else
            {
                StatusTextBlock.ToolTip = "No validation issues.";
            }
        }
        catch (Exception exception)
        {
            PreviewDataGrid.ItemsSource = null;
            _anchor = null;
            _sections = [];
            OpenAutoModeButton.IsEnabled = false;
            StatusTextBlock.Text = $"Could not generate preview: {exception.Message}";
            StatusTextBlock.ToolTip = exception.ToString();
            MessageBox.Show(
                this,
                exception.Message,
                "Import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool TryReadScale(out double scale)
    {
        return TryReadPositiveNumber(ScaleTextBox.Text, out scale);
    }

    private bool TryReadCurveFitSettings(out Tpt2CurveFitSettings settings)
    {
        settings = new Tpt2CurveFitSettings();
        if (!TryReadPositiveNumber(CurveFitToleranceTextBox.Text, out var maximumDeviation) ||
            !TryReadPositiveNumber(CurveFitHandleFactorTextBox.Text, out var handleFactor) ||
            !TryReadPositiveNumber(CurveFitTargetLengthTextBox.Text, out var targetLength) ||
            !TryReadPositiveNumber(CurveFitMinimumLengthTextBox.Text, out var minimumLength) ||
            minimumLength < settings.MinimumSectionLength ||
            minimumLength > targetLength ||
            targetLength > settings.MaximumSectionLength)
        {
            return false;
        }

        var preferredMinimumLength = Math.Clamp(
            settings.PreferredMinimumSectionLength,
            minimumLength,
            targetLength);
        settings = settings with
        {
            MaximumDeviation = maximumDeviation,
            HandleLengthFactor = handleFactor,
            TargetSectionLength = targetLength,
            MinimumSectionLength = minimumLength,
            PreferredMinimumSectionLength = preferredMinimumLength,
        };
        return true;
    }

    private static bool TryReadPositiveNumber(string text, out double value)
    {
        var parsed =
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return parsed && double.IsFinite(value) && value > 0.0;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        SelectFileButton.IsEnabled = !busy;
        GenerateButton.IsEnabled = !busy;
        FullPrecisionCheckBox.IsEnabled = !busy;
        PositionReferenceComboBox.IsEnabled = !busy;
        CurveFitToleranceTextBox.IsEnabled = !busy;
        CurveFitHandleFactorTextBox.IsEnabled = !busy;
        CurveFitTargetLengthTextBox.IsEnabled = !busy;
        CurveFitMinimumLengthTextBox.IsEnabled = !busy;
        ScaleTextBox.IsEnabled = !busy;
        BusyProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (message is not null)
        {
            StatusTextBlock.Text = message;
        }
    }

    private static string FormatIssue(ValidationIssue issue)
    {
        var location = issue.SectionIndex is null ? string.Empty : $"Section {issue.SectionIndex}: ";
        return $"{location}{issue.Message}";
    }

    private sealed record PreviewResult(
        int SourceCount,
        int SelectedPointCount,
        Tpt2CurveFitMetrics CurveFitMetrics,
        ImportAnchor Anchor,
        IReadOnlyList<Importer.Core.Models.ImportedSection> Sections,
        IReadOnlyList<ValidationIssue> Issues);
}
