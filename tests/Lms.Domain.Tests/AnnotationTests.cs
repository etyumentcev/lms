using Lms.Domain;
namespace Lms.Domain.Tests;
public sealed class AnnotationTests
{
    private static readonly Guid Attachment = Guid.NewGuid();
    private static AnnotatedAttachment Page(PenStroke? stroke = null) => new(Attachment, [stroke ?? Stroke()]);
    private static PenStroke Stroke() => new(Guid.NewGuid(), "pen", "#dc2626", .003, [new(.2, .3, .7), new(.8, .9)]);
    private static (ProblemWork, Submission) Work()
    {
        var a = new ProblemAssignment(Guid.NewGuid(), Guid.NewGuid(), "course", new(Guid.NewGuid(), "lesson", "problem"));
        var work = new ProblemWork(Guid.NewGuid(), a);
        return (work, work.SubmitSolution(a, a.StudentId, null, [Attachment], DateTimeOffset.UtcNow));
    }
    [Fact] public void Copies_mutable_input_and_preserves_original_ids()
    {
        var (work, submission) = Work(); var points = new List<StrokePoint> { new(.2, .4) };
        var strokes = new List<PenStroke> { Stroke() with { Points = points } };
        var review = work.Accept(Guid.NewGuid(), submission.Id, null, DateTimeOffset.UtcNow, [new(Attachment, strokes)]);
        points.Clear(); strokes.Clear();
        Assert.Single(review.Annotations[0].Strokes[0].Points); Assert.Equal(Attachment, submission.AttachmentIds[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<StrokePoint>)review.Annotations[0].Strokes[0].Points).Clear());
    }
    [Theory] [InlineData(-.01)] [InlineData(1.01)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void Rejects_coordinates_atomically(double x)
    {
        var (work, submission) = Work();
        var ex = Assert.Throws<DomainException>(() => work.RequestRevision(Guid.NewGuid(), submission.Id, "feedback", DateTimeOffset.UtcNow,
            [Page(Stroke() with { Points = [new(x, .5)] })]));
        Assert.Equal("InvalidAnnotations", ex.Code); Assert.Empty(work.Messages); Assert.Empty(work.Reviews);
        Assert.Equal(1, work.Version); Assert.Equal(ProblemWorkStatus.Submitted, work.Status);
    }
    [Fact] public void Rejects_foreign_attachment() => Assert.Throws<DomainException>(() => AnnotationRules.Copy([Page()], [Guid.NewGuid()]));
    [Theory] [InlineData("eraser", "#dc2626", .003)] [InlineData("pen", "red", .003)] [InlineData("pen", "#dc2626", 0)] [InlineData("pen", "#dc2626", .051)]
    public void Rejects_style(string tool, string color, double width) => Assert.Throws<DomainException>(() => AnnotationRules.Copy([Page(Stroke() with { Tool = tool, Color = color, Width = width })], [Attachment]));
    [Fact] public void Rejects_pressure() => Assert.Throws<DomainException>(() => AnnotationRules.Copy([Page(Stroke() with { Points = [new(.2, .3, 2)] })], [Attachment]));
    [Fact] public void Rejects_duplicate_pages() => Assert.Throws<DomainException>(() => AnnotationRules.Copy([Page(), Page()], [Attachment]));
    [Fact] public void Rejects_duplicate_strokes() { var s = Stroke(); Assert.Throws<DomainException>(() => AnnotationRules.Copy([new(Attachment, [s, s])], [Attachment])); }
    [Fact] public void Rejects_point_limit() => Assert.Throws<DomainException>(() => AnnotationRules.Copy([Page(Stroke() with { Points = Enumerable.Repeat(new StrokePoint(.2, .3), 4097).ToArray() })], [Attachment]));
    [Fact] public void Rejects_stroke_limit() => Assert.Throws<DomainException>(() => AnnotationRules.Copy([new(Attachment, Enumerable.Range(0, 2001).Select(_ => Stroke()).ToArray())], [Attachment]));
    [Fact] public void Rejects_total_point_limit() => Assert.Throws<DomainException>(() => AnnotationRules.Copy([new(Attachment, Enumerable.Range(0, 25).Select(_ => Stroke() with { Points = Enumerable.Repeat(new StrokePoint(.2, .3), 4096).ToArray() }).ToArray())], [Attachment]));
    [Fact] public void Rejects_page_limit() { var ids = Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray(); Assert.Throws<DomainException>(() => AnnotationRules.Copy(ids.Select(id => new AnnotatedAttachment(id, [])), ids)); }
    [Fact] public void Feedback_is_required_even_with_annotations()
    {
        var (work, sub) = Work(); Assert.Equal("FeedbackRequired", Assert.Throws<DomainException>(() => work.RequestRevision(Guid.NewGuid(), sub.Id, " ", DateTimeOffset.UtcNow, [Page()])).Code);
    }
    [Fact] public void Old_clients_have_empty_annotations() { var (work, sub) = Work(); Assert.Empty(work.Accept(Guid.NewGuid(), sub.Id, null, DateTimeOffset.UtcNow).Annotations); }
}
