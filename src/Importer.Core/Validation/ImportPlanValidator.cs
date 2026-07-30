using Importer.Core.Conversion;
using Importer.Core.Models;

namespace Importer.Core.Validation;

public sealed class ImportPlanValidator
{
    public IReadOnlyList<ValidationIssue> Validate(
        IReadOnlyList<ImportedSection> sections,
        ImportValidationSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(sections);
        settings ??= new ImportValidationSettings();

        var issues = new List<ValidationIssue>();
        if (sections.Count == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "NO_SECTIONS",
                "The import plan contains no sections."));
            return issues;
        }

        if (sections.Count > settings.MaximumSectionCount)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "SECTION_COUNT",
                $"The plan contains {sections.Count:N0} sections, above the configured maximum of {settings.MaximumSectionCount:N0}."));
        }

        ImportedSection? previous = null;
        foreach (var section in sections)
        {
            ValidateFinite(section, issues);
            ValidateLength(section, settings, issues);
            ValidateAngles(section, issues);

            if (previous is not null)
            {
                ValidateOrientationChange(previous, section, settings, issues);
            }

            previous = section;
        }

        return issues;
    }

    private static void ValidateFinite(
        ImportedSection section,
        ICollection<ValidationIssue> issues)
    {
        if (!section.DesiredPosition.IsFinite ||
            !section.EnteredDelta.IsFinite ||
            !section.ExpectedBuiltPosition.IsFinite ||
            !section.Rotation.IsFinite)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "NON_FINITE",
                "A generated value is NaN or infinity.",
                section.SectionIndex));
        }
    }

    private static void ValidateLength(
        ImportedSection section,
        ImportValidationSettings settings,
        ICollection<ValidationIssue> issues)
    {
        var length = section.EnteredDelta.Length;
        if (length < settings.MinimumSectionLength)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "SECTION_TOO_SHORT",
                $"Entered section length {length:0.###} is below {settings.MinimumSectionLength:0.###}.",
                section.SectionIndex));
        }
        else if (length > settings.MaximumSectionLength)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "SECTION_TOO_LONG",
                $"Entered section length {length:0.###} exceeds {settings.MaximumSectionLength:0.###}.",
                section.SectionIndex));
        }
    }

    private static void ValidateAngles(
        ImportedSection section,
        ICollection<ValidationIssue> issues)
    {
        var angles = new[]
        {
            section.Rotation.Yaw,
            section.Rotation.Pitch,
            section.Rotation.Roll,
        };

        if (angles.Any(angle => angle < -180.0 || angle >= 180.0))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "ANGLE_RANGE",
                "An angle is outside the normalized [-180, 180) range.",
                section.SectionIndex));
        }
    }

    private static void ValidateOrientationChange(
        ImportedSection previous,
        ImportedSection current,
        ImportValidationSettings settings,
        ICollection<ValidationIssue> issues)
    {
        var largestChange = new[]
        {
            Math.Abs(AngleMath.ShortestDifference(previous.Rotation.Yaw, current.Rotation.Yaw)),
            Math.Abs(AngleMath.ShortestDifference(previous.Rotation.Pitch, current.Rotation.Pitch)),
            Math.Abs(AngleMath.ShortestDifference(previous.Rotation.Roll, current.Rotation.Roll)),
        }.Max();

        if (largestChange > settings.MaximumOrientationChange)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "ORIENTATION_JUMP",
                $"Orientation changes by {largestChange:0.0}°, above the configured {settings.MaximumOrientationChange:0.0}°.",
                current.SectionIndex));
        }
    }
}
