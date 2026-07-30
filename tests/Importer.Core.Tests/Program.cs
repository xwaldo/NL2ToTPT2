using Importer.Core.Conversion;
using Importer.Core.Csv;
using Importer.Core.Geometry;
using Importer.Core.Models;
using Importer.Core.Processing;
using Importer.Core.Validation;
using Importer.Windows.Automation;
using Importer.Windows.Calibration;
using Importer.Windows.Input;
using Importer.Windows.Windowing;

var tests = new (string Name, Func<Task> Run)[]
{
    ("CSV parser reads the NoLimits vector schema", CsvParserReadsSchema),
    ("Resampler uses target arc distance and preserves endpoint", ResamplerUsesTargetDistance),
    ("Section generation compensates rounded built position", SectionGenerationCompensatesRounding),
    ("Full precision section generation preserves small deltas", FullPrecisionPreservesSmallDeltas),
    ("Section generation applies position scale around the source origin", SectionGenerationAppliesScale),
    ("Point 1 is the anchor and section 1 connects point 1 to point 2", AnchorPrecedesFirstSection),
    ("Previous-node mode expresses position in the preceding rotated axes", PreviousNodeModeUsesRotatedAxes),
    ("Rotation mapping preserves angles until final formatting", RotationMappingPreservesPrecision),
    ("Calibrated profile inverts horizontal position and roll", CalibratedProfileAppliesObservedConventions),
    ("Position and rotation formatting clean negative zero", FormattingCleansNegativeZero),
    ("Orientation matrix extracts straight and yawed headings", OrientationExtractionHandlesHeadings),
    ("Validator rejects an empty plan", ValidatorRejectsEmptyPlan),
    ("Included NoLimits export completes the preview pipeline", IncludedExportCompletesPipeline),
    ("Previous-node mode follows the included track tangent", PreviousNodeModeFollowsIncludedTrack),
    ("Dry-run automation emits the complete section sequence", DryRunAutomationEmitsSequence),
    ("Full precision automation sends all six unrounded values", FullPrecisionAutomationSendsAllValues),
    ("Live routing keeps tabs, build, Y, and Z on distinct targets", LiveRoutingUsesDistinctTargets),
    ("Cancellation stops automation during build wait", CancellationStopsAutomation),
    ("Calibration profile round-trips partial saved points", CalibrationProfileRoundTrips),
    ("Automation defaults match the release settings", AutomationDefaultsMatchReleaseSettings),
    ("Mouse trajectory is eased, clocked, and ends exactly on target", MouseTrajectoryIsDeterministic),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL  {test.Name}");
        Console.Error.WriteLine($"      {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static async Task CsvParserReadsSchema()
{
    const string csv =
        "\"No.\"\t\"PosX\"\t\"PosY\"\t\"PosZ\"\t\"FrontX\"\t\"FrontY\"\t\"FrontZ\"\t\"LeftX\"\t\"LeftY\"\t\"LeftZ\"\t\"UpX\"\t\"UpY\"\t\"UpZ\"\n" +
        "1\t1.25\t2.5\t-3\t0\t0\t1\t-1\t0\t0\t0\t1\t0\n" +
        "2\t1.25\t2.5\t-2\t0\t0\t1\t-1\t0\t0\t0\t1\t0\n";

    var samples = await new NoLimitsCsvParser().ParseAsync(new StringReader(csv));

    AssertEqual(2, samples.Count);
    AssertNear(1.25, samples[0].Position.X);
    AssertNear(-3.0, samples[0].Position.Z);
    AssertNear(1.0, samples[1].Front.Z);
}

static Task ResamplerUsesTargetDistance()
{
    var samples = new[]
    {
        Sample(1, new Vec3(0, 0, 0)),
        Sample(2, new Vec3(0.4, 0, 0)),
        Sample(3, new Vec3(1.0, 0, 0)),
    };

    var result = new TrackResampler().Resample(samples, 0.5);

    AssertEqual(3, result.Count);
    AssertNear(0.0, result[0].Position.X);
    AssertNear(0.5, result[1].Position.X);
    AssertNear(1.0, result[2].Position.X);
    return Task.CompletedTask;
}

static Task SectionGenerationCompensatesRounding()
{
    var samples = new[]
    {
        Sample(1, new Vec3(0, 0, 0)),
        Sample(2, new Vec3(0.334, 0, 0)),
        Sample(3, new Vec3(0.668, 0, 0)),
        Sample(4, new Vec3(1.002, 0, 0)),
    };

    var sections = new SectionGenerator().Generate(
        samples,
        ImportConversionProfile.Identity,
        positionReference: PositionReferenceMode.WorldAxes);

    AssertEqual(3, sections.Count);
    AssertNear(0.33, sections[0].EnteredDelta.X);
    AssertNear(0.34, sections[1].EnteredDelta.X);
    AssertNear(0.33, sections[2].EnteredDelta.X);
    AssertNear(1.00, sections[2].ExpectedBuiltPosition.X);
    AssertNear(0.002, sections[2].DesiredPosition.X - sections[2].ExpectedBuiltPosition.X);
    return Task.CompletedTask;
}

static Task FullPrecisionPreservesSmallDeltas()
{
    var samples = new[]
    {
        Sample(1, new Vec3(0, 0, 0)),
        Sample(2, new Vec3(0.0034, 0, 0.0076)),
        Sample(3, new Vec3(0.0068, 0, 0.0152)),
    };

    var sections = new SectionGenerator().Generate(
        samples,
        ImportConversionProfile.Identity,
        inputPrecision: InputPrecisionMode.Full,
        positionReference: PositionReferenceMode.WorldAxes);

    AssertEqual(2, sections.Count);
    AssertNear(0.0034, sections[0].EnteredDelta.X);
    AssertNear(0.0076, sections[0].EnteredDelta.Z);
    AssertNear(0.0034, sections[1].EnteredDelta.X);
    AssertNear(0.0076, sections[1].EnteredDelta.Z);
    AssertEqual(InputPrecisionMode.Full, sections[0].InputPrecision);
    AssertEqual(
        "0.0034",
        PositionMath.FormatPosition(
            sections[0].EnteredDelta.X,
            sections[0].InputPrecision));
    return Task.CompletedTask;
}

static Task SectionGenerationAppliesScale()
{
    var samples = new[]
    {
        Sample(1, new Vec3(10, 2, -4)),
        Sample(2, new Vec3(11.25, 2.5, -3)),
    };

    var sections = new SectionGenerator().Generate(
        samples,
        ImportConversionProfile.Identity,
        positionScale: 2.0,
        positionReference: PositionReferenceMode.WorldAxes);

    AssertEqual(1, sections.Count);
    AssertNear(2.50, sections[0].EnteredDelta.X);
    AssertNear(1.00, sections[0].EnteredDelta.Y);
    AssertNear(2.00, sections[0].EnteredDelta.Z);
    return Task.CompletedTask;
}

static Task AnchorPrecedesFirstSection()
{
    var samples = new[]
    {
        Sample(
            10,
            new Vec3(5, 2, 1),
            front: new Vec3(1, 0, 0),
            left: new Vec3(0, 0, -1),
            up: new Vec3(0, 1, 0)),
        Sample(
            20,
            new Vec3(6, 2, 1),
            front: new Vec3(1, 0, 0),
            left: new Vec3(0, 0, -1),
            up: new Vec3(0, 1, 0)),
    };
    var generator = new SectionGenerator();

    var anchor = generator.CreateAnchor(
        samples,
        ImportConversionProfile.NoLimitsToRoblox,
        inputPrecision: InputPrecisionMode.Full);
    var section = generator.Generate(
        samples,
        ImportConversionProfile.NoLimitsToRoblox,
        inputPrecision: InputPrecisionMode.Full)
        .Single();

    AssertEqual(10, anchor.SourceIndex);
    AssertNear(90.0, anchor.Rotation.Yaw);
    AssertEqual(1, section.SectionIndex);
    AssertEqual(20, section.SourceIndex);
    AssertEqual(PositionReferenceMode.PreviousNodeAxes, section.PositionReference);
    AssertNear(0.0, section.EnteredDelta.X);
    AssertNear(-1.0, section.EnteredDelta.Z);
    return Task.CompletedTask;
}

static Task PreviousNodeModeUsesRotatedAxes()
{
    var yawedSamples = new[]
    {
        Sample(
            1,
            Vec3.Zero,
            front: new Vec3(1, 0, 0),
            left: new Vec3(0, 0, -1),
            up: new Vec3(0, 1, 0)),
        Sample(
            2,
            new Vec3(1, 0, 0),
            front: new Vec3(1, 0, 0),
            left: new Vec3(0, 0, -1),
            up: new Vec3(0, 1, 0)),
        Sample(
            3,
            new Vec3(1, 0, -1),
            front: new Vec3(1, 0, 0),
            left: new Vec3(0, 0, -1),
            up: new Vec3(0, 1, 0)),
    };

    var worldSection = new SectionGenerator()
        .Generate(
            yawedSamples,
            ImportConversionProfile.NoLimitsToRoblox,
            positionReference: PositionReferenceMode.WorldAxes)
        .First();
    var previousNodeSections = new SectionGenerator()
        .Generate(
            yawedSamples,
            ImportConversionProfile.NoLimitsToRoblox,
            positionReference: PositionReferenceMode.PreviousNodeAxes);
    var forwardSection = previousNodeSections[0];
    var leftSection = previousNodeSections[1];

    AssertNear(-1.0, worldSection.EnteredDelta.X);
    AssertNear(0.0, worldSection.EnteredDelta.Z);
    AssertNear(0.0, forwardSection.EnteredDelta.X);
    AssertNear(-1.0, forwardSection.EnteredDelta.Z);
    AssertNear(-1.0, forwardSection.ExpectedBuiltPosition.X);
    AssertNear(90.0, forwardSection.Rotation.Yaw);
    AssertNear(-1.0, leftSection.EnteredDelta.X);
    AssertNear(0.0, leftSection.EnteredDelta.Z);
    AssertNear(1.0, leftSection.ExpectedBuiltPosition.Z);
    AssertEqual(
        PositionReferenceMode.PreviousNodeAxes,
        forwardSection.PositionReference);

    var calibratedSamples = new[]
    {
        Sample(1, Vec3.Zero),
        Sample(2, new Vec3(0, 0, 1)),
    };
    var calibratedSection = new SectionGenerator()
        .Generate(
            calibratedSamples,
            ImportConversionProfile.NoLimitsToRoblox,
            positionReference: PositionReferenceMode.PreviousNodeAxes)
        .Single();
    AssertNear(0.0, calibratedSection.EnteredDelta.X);
    AssertNear(0.0, calibratedSection.EnteredDelta.Y);
    AssertNear(-1.0, calibratedSection.EnteredDelta.Z);
    AssertNear(-1.0, calibratedSection.ExpectedBuiltPosition.Z);
    AssertNear(0.0, calibratedSection.Rotation.Yaw);
    return Task.CompletedTask;
}

static Task RotationMappingPreservesPrecision()
{
    var source = new EulerAngles(
        12.3456789012345,
        -6.7890123456789,
        0.000123456789);
    var mapped = new RotationMapping(1, 1, -1).Map(source);

    AssertEqual(12.3456789012345, mapped.Yaw);
    AssertEqual(-6.7890123456789, mapped.Pitch);
    AssertEqual(-0.000123456789, mapped.Roll);
    AssertEqual(
        "12.3456789012345",
        AngleMath.FormatRotation(mapped.Yaw, InputPrecisionMode.Full));
    AssertEqual("12.3", AngleMath.FormatRotation(mapped.Yaw, InputPrecisionMode.Fixed));
    return Task.CompletedTask;
}

static Task CalibratedProfileAppliesObservedConventions()
{
    var samples = new[]
    {
        Sample(1, new Vec3(10, 2, -4)),
        Sample(
            2,
            new Vec3(11, 4, -1),
            front: new Vec3(0, 0, 1),
            left: new Vec3(Math.Sqrt(0.5), -Math.Sqrt(0.5), 0),
            up: new Vec3(Math.Sqrt(0.5), Math.Sqrt(0.5), 0)),
    };

    var section = new SectionGenerator()
        .Generate(
            samples,
            ImportConversionProfile.NoLimitsToRoblox,
            positionReference: PositionReferenceMode.WorldAxes)
        .Single();

    AssertNear(-1.0, section.EnteredDelta.X);
    AssertNear(2.0, section.EnteredDelta.Y);
    AssertNear(-3.0, section.EnteredDelta.Z);
    AssertNear(0.0, section.Rotation.Yaw);
    AssertNear(0.0, section.Rotation.Pitch);
    AssertNear(45.0, section.Rotation.Roll);
    return Task.CompletedTask;
}

static Task FormattingCleansNegativeZero()
{
    AssertEqual("0.00", PositionMath.FormatPosition(-0.0049));
    AssertEqual("1.20", PositionMath.FormatPosition(1.2));
    AssertEqual("0.0", AngleMath.FormatRotation(-0.049));
    AssertEqual("1.0", AngleMath.FormatRotation(1.0));
    AssertEqual("-0.01", PositionMath.FormatPosition(-0.005));
    AssertEqual(
        "0.123456789012345",
        PositionMath.FormatPosition(
            0.123456789012345,
            InputPrecisionMode.Full));
    AssertEqual(
        "12.3456789012345",
        AngleMath.FormatRotation(
            12.3456789012345,
            InputPrecisionMode.Full));
    AssertEqual("-0.1", AngleMath.FormatRotation(-0.05));
    return Task.CompletedTask;
}

static Task OrientationExtractionHandlesHeadings()
{
    var straight = OrientationMatrix.FromNoLimitsBasis(
        new Vec3(0, 0, 1),
        new Vec3(1, 0, 0),
        new Vec3(0, 1, 0)).ToYawPitchRoll();
    var right = OrientationMatrix.FromNoLimitsBasis(
        new Vec3(1, 0, 0),
        new Vec3(0, 0, -1),
        new Vec3(0, 1, 0)).ToYawPitchRoll();

    var robloxRight = OrientationMatrix.FromRobloxYawPitchRoll(
        new EulerAngles(90, 0, 0));

    AssertNear(0.0, straight.Yaw);
    AssertNear(0.0, straight.Pitch);
    AssertNear(0.0, straight.Roll);
    AssertNear(90.0, right.Yaw);
    AssertNear(-1.0, robloxRight.Forward.X);
    AssertNear(-1.0, robloxRight.Right.Z);
    AssertNear(1.0, robloxRight.Up.Y);
    return Task.CompletedTask;
}

static Task ValidatorRejectsEmptyPlan()
{
    var issues = new ImportPlanValidator().Validate([]);

    AssertEqual(1, issues.Count);
    AssertEqual(ValidationSeverity.Error, issues[0].Severity);
    AssertEqual("NO_SECTIONS", issues[0].Code);
    return Task.CompletedTask;
}

static async Task IncludedExportCompletesPipeline()
{
    var samplePath = FindRepositoryFile(Path.Combine("TestCSVs", "gcigcigci.csv"));
    var samples = await new NoLimitsCsvParser().ParseFileAsync(samplePath);
    var resampled = new TrackResampler().Resample(samples, 1.0);
    var sections = new SectionGenerator().Generate(
        resampled,
        ImportConversionProfile.NoLimitsToRoblox);
    var issues = new ImportPlanValidator().Validate(sections);

    AssertTrue(samples.Count > 1_000, "Expected the included export to contain more than 1,000 samples.");
    AssertTrue(sections.Count > 100, "Expected the included export to generate more than 100 sections.");
    AssertEqual(0, issues.Count(issue => issue.Severity == ValidationSeverity.Error));
}

static async Task PreviousNodeModeFollowsIncludedTrack()
{
    var samplePath = FindRepositoryFile(Path.Combine("TestCSVs", "vekoma.csv"));
    var samples = await new NoLimitsCsvParser().ParseFileAsync(samplePath);
    var resampled = new TrackResampler().Resample(samples, 1.0);
    var sections = new SectionGenerator().Generate(
        resampled,
        ImportConversionProfile.NoLimitsToRoblox,
        inputPrecision: InputPrecisionMode.Full,
        positionReference: PositionReferenceMode.PreviousNodeAxes);

    var forwardAligned = sections.Count(section =>
        section.EnteredDelta.Z < 0.0 &&
        -section.EnteredDelta.Z / section.EnteredDelta.Length > 0.9);
    var alignedFraction = forwardAligned / (double)sections.Count;

    AssertTrue(
        alignedFraction > 0.9,
        $"Expected more than 90% of sections to align with negative local Z, " +
        $"but observed {alignedFraction:P1}.");
}

static async Task DryRunAutomationEmitsSequence()
{
    var section = new ImportedSection(
        1,
        10,
        new Vec3(-1.2, 2.34, 0),
        new Vec3(-1.2, 2.34, 0),
        new Vec3(-1.2, 2.34, 0),
        new EulerAngles(10, -5, 3.2));
    var input = new RecordingAutomationInput();
    var runner = new AutomationRunner(
        input,
        new AutomationSettings
        {
            InputDelay = TimeSpan.Zero,
            BuildDelay = TimeSpan.Zero,
            ModeSwitchDelay = TimeSpan.Zero,
            DryRun = true,
        });
    var states = new List<AutomationState>();
    runner.ProgressChanged += progress => states.Add(progress.State);

    await runner.RunAsync([section], calibration: null, startIndex: 0, CancellationToken.None);

    var expected = new[]
    {
        "Locate",
        "Focus",
        "Click",
        "Click",
        "Value:-1.20",
        "Value:2.34",
        "Value:0.00",
        "Click",
        "Click",
        "Value:10.0",
        "Value:-5.0",
        "Value:3.2",
        "Click",
        "Click",
    };
    AssertEqual(string.Join("|", expected), string.Join("|", input.Operations));
    AssertEqual(AutomationState.Completed, states[^1]);
}

static async Task FullPrecisionAutomationSendsAllValues()
{
    var section = new ImportedSection(
        1,
        10,
        new Vec3(0.0034, -0.0076, 0.123456789012345),
        new Vec3(0.0034, -0.0076, 0.123456789012345),
        new Vec3(0.0034, -0.0076, 0.123456789012345),
        new EulerAngles(12.3456789012345, -6.7890123456789, 0.000123456789),
        InputPrecisionMode.Full);
    var input = new RecordingAutomationInput();
    var runner = new AutomationRunner(
        input,
        new AutomationSettings
        {
            InputDelay = TimeSpan.Zero,
            BuildDelay = TimeSpan.Zero,
            ModeSwitchDelay = TimeSpan.Zero,
            DryRun = true,
        });

    await runner.RunAsync([section], calibration: null, startIndex: 0, CancellationToken.None);

    var expected = new[]
    {
        "0.0034",
        "-0.0076",
        "0.123456789012345",
        "12.3456789012345",
        "-6.7890123456789",
        "0.000123456789",
    };
    AssertEqual(
        string.Join("|", expected),
        string.Join("|", input.ValueOperations.Select(operation => operation.Value)));
}

static async Task LiveRoutingUsesDistinctTargets()
{
    var section = new ImportedSection(
        1,
        1,
        new Vec3(1, 0, -16),
        new Vec3(1, 0, -16),
        new Vec3(1, 0, -16),
        new EulerAngles(0, 0, 0));
    var calibration = CreateDistinctCalibration();
    var input = new RecordingAutomationInput();
    var runner = new AutomationRunner(
        input,
        new AutomationSettings
        {
            InputDelay = TimeSpan.Zero,
            BuildDelay = TimeSpan.Zero,
            ModeSwitchDelay = TimeSpan.Zero,
            DryRun = false,
        });

    await runner.RunAsync([section], calibration, startIndex: 0, CancellationToken.None);

    var expectedClicks = new[]
    {
        calibration.FocusRelease,
        calibration.MoveSplineTab,
        calibration.FocusRelease,
        calibration.RotateSplineTab,
        calibration.FocusRelease,
        calibration.BuildButton,
    };
    AssertEqual(
        string.Join("|", expectedClicks),
        string.Join("|", input.ClickOperations));
    AssertEqual(calibration.PositionY, input.ValueOperations[1].Point);
    AssertEqual("0.00", input.ValueOperations[1].Value);
    AssertEqual(calibration.PositionZ, input.ValueOperations[2].Point);
    AssertEqual("-16.00", input.ValueOperations[2].Value);
}

static async Task CancellationStopsAutomation()
{
    var sections = new[]
    {
        new ImportedSection(
            1,
            1,
            Vec3.Zero,
            new Vec3(1, 0, 0),
            new Vec3(1, 0, 0),
            new EulerAngles(0, 0, 0)),
        new ImportedSection(
            2,
            2,
            Vec3.Zero,
            new Vec3(1, 0, 0),
            new Vec3(2, 0, 0),
            new EulerAngles(0, 0, 0)),
    };
    var input = new RecordingAutomationInput();
    var runner = new AutomationRunner(
        input,
        new AutomationSettings
        {
            InputDelay = TimeSpan.Zero,
            BuildDelay = TimeSpan.FromSeconds(30),
            ModeSwitchDelay = TimeSpan.Zero,
            DryRun = true,
        });
    var states = new List<AutomationState>();
    runner.ProgressChanged += progress => states.Add(progress.State);
    using var cancellation = new CancellationTokenSource();

    var runTask = runner.RunAsync(sections, calibration: null, startIndex: 0, cancellation.Token);
    await input.BuildButtonClicked.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();

    try
    {
        await runTask;
        throw new InvalidOperationException("Expected automation cancellation.");
    }
    catch (OperationCanceledException)
    {
        // Expected.
    }

    AssertEqual(AutomationState.Stopped, states[^1]);
    AssertEqual(6, input.Operations.Count(operation => operation == "Click"));
}

static ControlCalibration CreateDistinctCalibration() =>
    new()
    {
        PositionX = new NormalizedPoint(0.10, 0.10),
        PositionY = new NormalizedPoint(0.20, 0.20),
        PositionZ = new NormalizedPoint(0.30, 0.30),
        Yaw = new NormalizedPoint(0.40, 0.40),
        Pitch = new NormalizedPoint(0.50, 0.50),
        Roll = new NormalizedPoint(0.60, 0.60),
        FocusRelease = new NormalizedPoint(0.70, 0.70),
        MoveSplineTab = new NormalizedPoint(0.80, 0.10),
        RotateSplineTab = new NormalizedPoint(0.80, 0.20),
        BuildButton = new NormalizedPoint(0.80, 0.30),
    };

static Task CalibrationProfileRoundTrips()
{
    var temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "NL2ToTPT2.Tests",
        Guid.NewGuid().ToString("N"));
    var filePath = Path.Combine(temporaryDirectory, "calibration.json");
    var temporaryPath = $"{filePath}.tmp";

    try
    {
        var expected = new Dictionary<CalibrationTarget, NormalizedPoint>
        {
            [CalibrationTarget.PositionY] = new NormalizedPoint(0.25, 0.50),
            [CalibrationTarget.BuildButton] = new NormalizedPoint(0.75, 0.80),
        };
        var store = new CalibrationProfileStore(filePath);

        store.Save(expected);
        var loaded = store.Load();

        AssertEqual(2, loaded.Count);
        AssertEqual(expected[CalibrationTarget.PositionY], loaded[CalibrationTarget.PositionY]);
        AssertEqual(expected[CalibrationTarget.BuildButton], loaded[CalibrationTarget.BuildButton]);
        store.Clear();
        AssertTrue(!File.Exists(filePath), "Expected Clear to remove the saved profile.");
        return Task.CompletedTask;
    }
    finally
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory);
        }
    }
}

static Task AutomationDefaultsMatchReleaseSettings()
{
    var settings = new AutomationSettings();

    AssertEqual(TimeSpan.FromMilliseconds(20), settings.InputDelay);
    AssertEqual(TimeSpan.FromMilliseconds(20), settings.ModeSwitchDelay);
    AssertEqual(TimeSpan.FromMilliseconds(20), settings.BuildDelay);
    AssertTrue(!settings.DryRun, "Dry-run should be disabled by default.");
    return Task.CompletedTask;
}

static Task MouseTrajectoryIsDeterministic()
{
    var start = new PixelPoint(10, 20);
    var end = new PixelPoint(110, 220);
    var samples = MouseTrajectory.Create(
        start,
        end,
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(20));

    AssertEqual(25, samples.Count);
    AssertEqual(TimeSpan.FromMilliseconds(500), samples[^1].Offset);
    AssertEqual(end, samples[^1].Point);
    for (var index = 0; index < samples.Count; index++)
    {
        AssertEqual(TimeSpan.FromMilliseconds((index + 1) * 20), samples[index].Offset);
        AssertTrue(
            samples[index].Point.X >= start.X && samples[index].Point.X <= end.X,
            "Mouse trajectory X must not overshoot its endpoints.");
        AssertTrue(
            samples[index].Point.Y >= start.Y && samples[index].Point.Y <= end.Y,
            "Mouse trajectory Y must not overshoot its endpoints.");
        if (index > 0)
        {
            AssertTrue(
                samples[index].Point.X >= samples[index - 1].Point.X,
                "Mouse trajectory X must move monotonically.");
            AssertTrue(
                samples[index].Point.Y >= samples[index - 1].Point.Y,
                "Mouse trajectory Y must move monotonically.");
            AssertTrue(
                samples[index].Offset > samples[index - 1].Offset,
                "Mouse trajectory timestamps must increase.");
        }
    }

    var zeroDuration = MouseTrajectory.Create(
        start,
        end,
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(20));
    AssertEqual(1, zeroDuration.Count);
    AssertEqual(TimeSpan.Zero, zeroDuration[0].Offset);
    AssertEqual(end, zeroDuration[0].Point);

    var sixtyHertzSamples = MouseTrajectory.Create(
        start,
        end,
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromTicks(
            (long)Math.Ceiling(TimeSpan.TicksPerSecond / 60.0)));
    AssertEqual(30, sixtyHertzSamples.Count);

    var shortMoveDuration = MouseTrajectory.CalculateDuration(
        new PixelPoint(0, 0),
        new PixelPoint(60, 0),
        TimeSpan.FromMilliseconds(100),
        maximumPixelsPerSecond: 4_000);
    AssertEqual(TimeSpan.FromMilliseconds(100), shortMoveDuration);

    var longMoveDuration = MouseTrajectory.CalculateDuration(
        new PixelPoint(0, 0),
        new PixelPoint(1_200, 0),
        TimeSpan.FromMilliseconds(100),
        maximumPixelsPerSecond: 4_000);
    AssertEqual(TimeSpan.FromMilliseconds(300), longMoveDuration);

    var precisionMoveDuration = MouseTrajectory.CalculateResponsiveDuration(
        new PixelPoint(0, 0),
        new PixelPoint(240, 0),
        TimeSpan.FromMilliseconds(100),
        preferredPixelsPerSecond: 1_600,
        maximumPixelsPerSecond: 4_000,
        maximumPreferredDuration: TimeSpan.FromMilliseconds(200));
    AssertEqual(TimeSpan.FromMilliseconds(150), precisionMoveDuration);

    var responsiveLongMoveDuration = MouseTrajectory.CalculateResponsiveDuration(
        new PixelPoint(0, 0),
        new PixelPoint(1_200, 0),
        TimeSpan.FromMilliseconds(100),
        preferredPixelsPerSecond: 1_600,
        maximumPixelsPerSecond: 4_000,
        maximumPreferredDuration: TimeSpan.FromMilliseconds(200));
    AssertEqual(TimeSpan.FromMilliseconds(300), responsiveLongMoveDuration);
    return Task.CompletedTask;
}

static NoLimitsSample Sample(
    int index,
    Vec3 position,
    Vec3? front = null,
    Vec3? left = null,
    Vec3? up = null) =>
    new(
        index,
        position,
        front ?? new Vec3(0, 0, 1),
        left ?? new Vec3(1, 0, 0),
        up ?? new Vec3(0, 1, 0));

static string FindRepositoryFile(string relativePath)
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        directory = directory.Parent;
    }

    throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
}

static void AssertNear(double expected, double actual, double tolerance = 1e-9)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"Expected {expected}, received {actual}.");
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, received {actual}.");
    }
}

sealed class RecordingAutomationInput : IAutomationInput
{
    public List<string> Operations { get; } = [];

    public List<(NormalizedPoint Point, string Value)> ValueOperations { get; } = [];

    public List<NormalizedPoint> ClickOperations { get; } = [];

    public TaskCompletionSource<bool> BuildButtonClicked { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _clickCount;

    public Task LocateWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Operations.Add("Locate");
        return Task.CompletedTask;
    }

    public Task FocusWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Operations.Add("Focus");
        return Task.CompletedTask;
    }

    public Task ClickAsync(
        NormalizedPoint point,
        TimeSpan inputDelay,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Operations.Add("Click");
        ClickOperations.Add(point);
        _clickCount++;
        if (_clickCount == 6)
        {
            _ = BuildButtonClicked.TrySetResult(true);
        }

        return Task.CompletedTask;
    }

    public Task EnterValueAsync(
        NormalizedPoint point,
        string value,
        TimeSpan inputDelay,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Operations.Add($"Value:{value}");
        ValueOperations.Add((point, value));
        return Task.CompletedTask;
    }
}
