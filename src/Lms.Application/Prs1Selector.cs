using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Lms.Application;

public enum Prs1ResourceKind { All, Course, Group, Assignment, Work, Submission, Review, Attachment }

public enum Prs1SelectorError
{
    None, EmptyReference, ReferenceTooLong, NonAsciiReference, InvalidStructure,
    UnsupportedProfile, UnknownKind, InvalidUuid, ZeroUuid, EmptyStringId,
    InvalidBase64Url, NonCanonicalBase64Url, InvalidUtf8, StringIdTooLong
}

/// <summary>
/// Syntactic PRS1 value only. Success proves neither provenance, ownership nor authorization.
/// String identities are preserved ordinally, without normalization or URL decoding.
/// </summary>
public sealed record Prs1Selector
{
    public const int MaximumReferenceBytes = 4096;
    public const int MaximumStringIdBytes = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public Guid OrganizationId { get; }
    public Prs1ResourceKind Kind { get; }
    public string? CourseId { get; }
    public string? GroupId { get; }
    public Guid? EntityId { get; }
    public string CanonicalReference { get; }

    private Prs1Selector(Guid org, Prs1ResourceKind kind, string? course, string? group,
        Guid? entity, string canonical)
        => (OrganizationId, Kind, CourseId, GroupId, EntityId, CanonicalReference)
            = (org, kind, course, group, entity, canonical);

    public override string ToString() => CanonicalReference;

    public static bool TryParse(string? raw, [NotNullWhen(true)] out Prs1Selector? selector,
        out Prs1SelectorError error)
    {
        selector = null;
        error = Prs1SelectorError.None;
        if (string.IsNullOrEmpty(raw)) return Fail(Prs1SelectorError.EmptyReference, out error);
        // Length first bounds allocations; ASCII validation makes character and byte counts equal.
        if (raw.Length > MaximumReferenceBytes) return Fail(Prs1SelectorError.ReferenceTooLong, out error);
        if (raw.Any(c => c > 127)) return Fail(Prs1SelectorError.NonAsciiReference, out error);
        var parts = raw.Split('/');
        if (parts[0] != "prs1") return Fail(Prs1SelectorError.UnsupportedProfile, out error);
        if (parts.Length < 3) return Fail(Prs1SelectorError.InvalidStructure, out error);
        if (!TryUuid(parts[1], out var org, out error)) return false;
        Prs1ResourceKind kind;
        int count;
        switch (parts[2])
        {
            case "all": kind = Prs1ResourceKind.All; count = 3; break;
            case "course": kind = Prs1ResourceKind.Course; count = 4; break;
            case "group": kind = Prs1ResourceKind.Group; count = 5; break;
            case "assignment": kind = Prs1ResourceKind.Assignment; count = 4; break;
            case "work": kind = Prs1ResourceKind.Work; count = 4; break;
            case "submission": kind = Prs1ResourceKind.Submission; count = 4; break;
            case "review": kind = Prs1ResourceKind.Review; count = 4; break;
            case "attachment": kind = Prs1ResourceKind.Attachment; count = 4; break;
            default: return Fail(Prs1SelectorError.UnknownKind, out error);
        }
        if (parts.Length != count) return Fail(Prs1SelectorError.InvalidStructure, out error);
        string? course = null, group = null;
        Guid? entity = null;
        if (kind is Prs1ResourceKind.Course or Prs1ResourceKind.Group)
        {
            if (!TryStringId(parts[3], out course, out error)) return false;
            if (kind == Prs1ResourceKind.Group && !TryStringId(parts[4], out group, out error)) return false;
        }
        else if (kind != Prs1ResourceKind.All)
        {
            if (!TryUuid(parts[3], out var id, out error)) return false;
            entity = id;
        }
        // Every wire component has independently passed exact canonical round-trip validation.
        selector = new(org, kind, course, group, entity, raw);
        return true;
    }

    /// <summary>Build from trusted typed identities; this does not verify their existence or trust.</summary>
    public static bool TryBuild(Guid organizationId, Prs1ResourceKind kind,
        [NotNullWhen(true)] out Prs1Selector? selector, out Prs1SelectorError error,
        string? courseId = null, string? groupId = null, Guid? entityId = null)
    {
        selector = null;
        error = Prs1SelectorError.None;
        if (organizationId == Guid.Empty) return Fail(Prs1SelectorError.ZeroUuid, out error);
        string target;
        switch (kind)
        {
            case Prs1ResourceKind.All:
                if (courseId != null || groupId != null || entityId != null)
                    return Fail(Prs1SelectorError.InvalidStructure, out error);
                target = "all";
                break;
            case Prs1ResourceKind.Course:
            case Prs1ResourceKind.Group:
                if (entityId != null || (kind == Prs1ResourceKind.Course && groupId != null))
                    return Fail(Prs1SelectorError.InvalidStructure, out error);
                if (!TryEncodeId(courseId, out var course, out error)) return false;
                target = "course/" + course;
                if (kind == Prs1ResourceKind.Group)
                {
                    if (!TryEncodeId(groupId, out var group, out error)) return false;
                    target = "group/" + course + "/" + group;
                }
                break;
            case Prs1ResourceKind.Assignment:
            case Prs1ResourceKind.Work:
            case Prs1ResourceKind.Submission:
            case Prs1ResourceKind.Review:
            case Prs1ResourceKind.Attachment:
                if (courseId != null || groupId != null || entityId == null)
                    return Fail(Prs1SelectorError.InvalidStructure, out error);
                if (entityId == Guid.Empty) return Fail(Prs1SelectorError.ZeroUuid, out error);
                var name = kind switch
                {
                    Prs1ResourceKind.Assignment => "assignment", Prs1ResourceKind.Work => "work",
                    Prs1ResourceKind.Submission => "submission", Prs1ResourceKind.Review => "review",
                    _ => "attachment"
                };
                target = name + "/" + entityId.Value.ToString("D");
                break;
            default: return Fail(Prs1SelectorError.UnknownKind, out error);
        }
        return TryParse($"prs1/{organizationId:D}/{target}", out selector, out error);
    }

    private static bool TryUuid(string raw, out Guid id, out Prs1SelectorError error)
    {
        if (!Guid.TryParseExact(raw, "D", out id) || raw != id.ToString("D"))
            return Fail(Prs1SelectorError.InvalidUuid, out error);
        if (id == Guid.Empty) return Fail(Prs1SelectorError.ZeroUuid, out error);
        error = Prs1SelectorError.None;
        return true;
    }

    private static bool TryStringId(string raw, out string? value, out Prs1SelectorError error)
    {
        value = null;
        if (raw.Length == 0) return Fail(Prs1SelectorError.EmptyStringId, out error);
        if (raw.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))
            || raw.Length % 4 == 1)
            return Fail(Prs1SelectorError.InvalidBase64Url, out error);
        var bytes = Convert.FromBase64String(raw.Replace('-', '+').Replace('_', '/')
            + new string('=', (4 - raw.Length % 4) % 4));
        if (Encode(bytes) != raw) return Fail(Prs1SelectorError.NonCanonicalBase64Url, out error);
        if (bytes.Length > MaximumStringIdBytes) return Fail(Prs1SelectorError.StringIdTooLong, out error);
        try { value = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Fail(Prs1SelectorError.InvalidUtf8, out error); }
        error = Prs1SelectorError.None;
        return true;
    }

    private static bool TryEncodeId(string? value, out string? encoded, out Prs1SelectorError error)
    {
        encoded = null;
        if (string.IsNullOrEmpty(value)) return Fail(Prs1SelectorError.EmptyStringId, out error);
        try
        {
            if (StrictUtf8.GetByteCount(value) > MaximumStringIdBytes)
                return Fail(Prs1SelectorError.StringIdTooLong, out error);
            encoded = Encode(StrictUtf8.GetBytes(value));
        }
        catch (EncoderFallbackException) { return Fail(Prs1SelectorError.InvalidUtf8, out error); }
        error = Prs1SelectorError.None;
        return true;
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool Fail(Prs1SelectorError reason, out Prs1SelectorError error) { error = reason; return false; }
}
