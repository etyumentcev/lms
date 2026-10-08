namespace Lms.Domain;

public sealed record StrokePoint(double X, double Y, double? Pressure = null);
public sealed record PenStroke(Guid Id, string Tool, string Color, double Width, IReadOnlyList<StrokePoint> Points);
public sealed record AnnotatedAttachment(Guid AttachmentId, IReadOnlyList<PenStroke> Strokes);

public static class AnnotationRules
{
    public const int MaxPages = 20, MaxStrokes = 2000, MaxStrokePoints = 4096, MaxPoints = 100000;
    public const int MaxRequestBytes = 4 * 1024 * 1024;
    public static IReadOnlyList<AnnotatedAttachment> Copy(IEnumerable<AnnotatedAttachment>? annotations, IReadOnlyList<Guid> attachments)
    {
        var pages = annotations?.ToArray() ?? [];
        if (pages.Length > MaxPages) throw new DomainException("InvalidAnnotations");
        var pageIds = new HashSet<Guid>(); var strokeIds = new HashSet<Guid>(); var points = 0;
        var result = new List<AnnotatedAttachment>();
        foreach (var page in pages)
        {
            if (page is null || !attachments.Contains(page.AttachmentId) || !pageIds.Add(page.AttachmentId) || page.Strokes is null)
                throw new DomainException("InvalidAnnotations");
            var strokes = new List<PenStroke>();
            foreach (var stroke in page.Strokes)
            {
                if (stroke is null || stroke.Id == Guid.Empty || !strokeIds.Add(stroke.Id) || strokeIds.Count > MaxStrokes ||
                    stroke.Tool != "pen" || stroke.Color is null || !System.Text.RegularExpressions.Regex.IsMatch(stroke.Color, "^#[0-9a-fA-F]{6}$") ||
                    !double.IsFinite(stroke.Width) || stroke.Width < .0005 || stroke.Width > .05 || stroke.Points is null ||
                    stroke.Points.Count < 1 || stroke.Points.Count > MaxStrokePoints)
                    throw new DomainException("InvalidAnnotations");
                points += stroke.Points.Count;
                if (points > MaxPoints || stroke.Points.Any(p => p is null || !Unit(p.X) || !Unit(p.Y) || (p.Pressure.HasValue && !Unit(p.Pressure.Value))))
                    throw new DomainException("InvalidAnnotations");
                strokes.Add(stroke with { Points = Array.AsReadOnly(stroke.Points.ToArray()) });
            }
            result.Add(page with { Strokes = strokes.AsReadOnly() });
        }
        return result.AsReadOnly();
    }
    private static bool Unit(double value) => double.IsFinite(value) && value >= 0 && value <= 1;
}
