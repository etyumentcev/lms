using System.Text.Json;
using Lms.Domain;
namespace Lms.Infrastructure;
internal static class AnnotationJson
{
    public static string Serialize(IReadOnlyList<AnnotatedAttachment> value) => JsonSerializer.Serialize(value);
    public static IReadOnlyList<AnnotatedAttachment> Deserialize(string value)
    {
        var pages = JsonSerializer.Deserialize<AnnotatedAttachment[]>(value) ?? [];
        return AnnotationRules.Copy(pages, pages.Select(p => p.AttachmentId).ToArray());
    }
}
