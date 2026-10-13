using System.Text;
using Lms.Application;

namespace Lms.Application.Tests;

public sealed class Prs1SelectorTests
{
    private const string OrgText = "12345678-9abc-def0-1234-56789abcdef0";
    private const string IdText = "fedcba98-7654-3210-fedc-ba9876543210";
    private static readonly Guid Org = Guid.Parse(OrgText), Id = Guid.Parse(IdText);
    private static string Ref(string target) => $"prs1/{OrgText}/{target}";
    private static string Encode(string value) => Encode(Encoding.UTF8.GetBytes(value));
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Theory]
    [InlineData(Prs1ResourceKind.All, "all")]
    [InlineData(Prs1ResourceKind.Course, "course/Y291cnNlLUE")]
    [InlineData(Prs1ResourceKind.Group, "group/Y291cnNlLUE/Z3JvdXAtQg")]
    [InlineData(Prs1ResourceKind.Assignment, "assignment/" + IdText)]
    [InlineData(Prs1ResourceKind.Work, "work/" + IdText)]
    [InlineData(Prs1ResourceKind.Submission, "submission/" + IdText)]
    [InlineData(Prs1ResourceKind.Review, "review/" + IdText)]
    [InlineData(Prs1ResourceKind.Attachment, "attachment/" + IdText)]
    public void F01_Every_shape_builds_and_round_trips_exactly(Prs1ResourceKind kind, string target)
    {
        var course = kind is Prs1ResourceKind.Course or Prs1ResourceKind.Group ? "course-A" : null;
        var group = kind == Prs1ResourceKind.Group ? "group-B" : null;
        Guid? entity = kind is Prs1ResourceKind.All or Prs1ResourceKind.Course or Prs1ResourceKind.Group ? null : Id;
        Assert.True(Prs1Selector.TryBuild(Org, kind, out var built, out var error, course, group, entity));
        Assert.Equal(Prs1SelectorError.None, error);
        Assert.Equal(Ref(target), built.ToString());
        Assert.True(Prs1Selector.TryParse(Ref(target), out var parsed, out error));
        Assert.Equal(Prs1SelectorError.None, error);
        Assert.Equal(built, parsed);
        Assert.Equal(Org, parsed.OrganizationId);
        Assert.Equal(kind, parsed.Kind);
        Assert.Equal(course, parsed.CourseId);
        Assert.Equal(group, parsed.GroupId);
        Assert.Equal(entity, parsed.EntityId);
        Assert.Equal(Encoding.ASCII.GetBytes(Ref(target)), Encoding.ASCII.GetBytes(parsed.CanonicalReference));
    }

    public static IEnumerable<object?[]> InvalidReferences()
    {
        yield return new object?[] { null, Prs1SelectorError.EmptyReference };
        yield return new object?[] { "", Prs1SelectorError.EmptyReference };
        yield return new object?[] { " " + Ref("all"), Prs1SelectorError.UnsupportedProfile };
        yield return new object?[] { "prs1", Prs1SelectorError.InvalidStructure };
        yield return new object?[] { "prs1/" + OrgText, Prs1SelectorError.InvalidStructure };
        foreach (var org in new[] { OrgText.ToUpperInvariant(), OrgText.Replace("-", ""), "{" + OrgText + "}", " " + OrgText, "z" + OrgText[1..] })
            yield return new object?[] { $"prs1/{org}/all", Prs1SelectorError.InvalidUuid };
        yield return new object?[] { "prs1/00000000-0000-0000-0000-000000000000/all", Prs1SelectorError.ZeroUuid };
        foreach (var kind in new[] { "assignment", "work", "submission", "review", "attachment" })
        {
            foreach (var id in new[] { IdText.ToUpperInvariant(), IdText.Replace("-", ""), "", IdText + " ", "{" + IdText + "}" })
                yield return new object?[] { Ref(kind + "/" + id), Prs1SelectorError.InvalidUuid };
            yield return new object?[] { Ref(kind + "/00000000-0000-0000-0000-000000000000"), Prs1SelectorError.ZeroUuid };
        }
        foreach (var target in new[] { "all/", "all/x", "course", "course/YQ/", "group/YQ", "group/YQ/Yg/x", "work", "work/" + IdText + "/x" })
            yield return new object?[] { Ref(target), Prs1SelectorError.InvalidStructure };
        foreach (var kind in new[] { "All", "COURSE", "message", "identity", "session", "*", "..", "all ", "all?x", "all#x", "all\n" })
            yield return new object?[] { Ref(kind), Prs1SelectorError.UnknownKind };
        foreach (var profile in new[] { "prs2", "PRS1", "https:", "prs%31" })
            yield return new object?[] { profile + "/" + OrgText + "/all", Prs1SelectorError.UnsupportedProfile };
        yield return new object?[] { Ref("course/é"), Prs1SelectorError.NonAsciiReference };
        foreach (var sid in new[] { "YQ=", "YQ==", "+w", "/w", "%59Q", "Y Q", "YQ\t", "YQ\n", "A", "AAAAA", "*", "..", "YQ?x", "YQ#x" })
            yield return new object?[] { Ref("course/" + sid), sid == "/w" ? Prs1SelectorError.InvalidStructure : Prs1SelectorError.InvalidBase64Url };
        foreach (var sid in new[] { "YR", "YWJ", "__" })
            yield return new object?[] { Ref("course/" + sid), Prs1SelectorError.NonCanonicalBase64Url };
        foreach (var target in new[] { "course/", "group//YQ", "group/YQ/" })
            yield return new object?[] { Ref(target), Prs1SelectorError.EmptyStringId };
        foreach (var bytes in new byte[][] { [0xff], [0x80], [0xc0, 0xaf], [0xe2, 0x82], [0xed, 0xa0, 0x80], [0xf4, 0x90, 0x80, 0x80], [0xf5, 0x80, 0x80, 0x80] })
            yield return new object?[] { Ref("course/" + Encode(bytes)), Prs1SelectorError.InvalidUtf8 };
        yield return new object?[] { Ref("group/YQ/YR"), Prs1SelectorError.NonCanonicalBase64Url };
        yield return new object?[] { Ref("group/YQ/_w"), Prs1SelectorError.InvalidUtf8 };
    }

    [Theory]
    [MemberData(nameof(InvalidReferences))]
    public void F02_F03_F06_Reject_exact_reason(string? raw, Prs1SelectorError expected)
    {
        Assert.False(Prs1Selector.TryParse(raw, out var value, out var error));
        Assert.Null(value);
        Assert.Equal(expected, error);
    }

    [Theory]
    [InlineData("é", "é")]
    [InlineData("A", "a")]
    [InlineData("/", "%2F")]
    [InlineData("%2F", "%252F")]
    [InlineData("a", "YQ")]
    [InlineData("a", " a ")]
    public void F04_Identities_are_ordinal_and_never_decoded_twice(string first, string second)
    {
        Assert.True(Prs1Selector.TryBuild(Org, Prs1ResourceKind.Course, out var a, out _, first));
        Assert.True(Prs1Selector.TryBuild(Org, Prs1ResourceKind.Course, out var b, out _, second));
        Assert.NotEqual(a, b);
        Assert.NotEqual(a.CanonicalReference, b.CanonicalReference);
        Assert.Equal(first, a.CourseId);
        Assert.Equal(second, b.CourseId);
    }

    [Theory]
    [InlineData(" ")][InlineData("\0")][InlineData("../*")][InlineData("😀/é")][InlineData("﻿")]
    public void Stored_ids_have_no_extra_text_restrictions(string id)
    {
        Assert.True(Prs1Selector.TryParse(Ref("group/" + Encode(id) + "/" + Encode(id)), out var parsed, out _));
        Assert.Equal(id, parsed.CourseId);
        Assert.Equal(id, parsed.GroupId);
    }

    [Theory]
    [InlineData(false, false)][InlineData(false, true)][InlineData(true, false)][InlineData(true, true)]
    public void F05_String_byte_boundary_applies_to_each_sid_and_builder(bool unicode, bool groupSlot)
    {
        var atLimit = unicode ? string.Concat(Enumerable.Repeat("😀", 256)) : new string('a', 1024);
        foreach (var extra in new[] { "", "a" })
        {
            var id = atLimit + extra;
            Assert.Equal(1024 + extra.Length, Encoding.UTF8.GetByteCount(id));
            var target = groupSlot ? "group/YQ/" + Encode(id) : "course/" + Encode(id);
            var expected = extra.Length == 0;
            Assert.Equal(expected, Prs1Selector.TryParse(Ref(target), out var parsed, out var error));
            Assert.Equal(expected ? Prs1SelectorError.None : Prs1SelectorError.StringIdTooLong, error);
            Assert.Equal(expected, Prs1Selector.TryBuild(Org, groupSlot ? Prs1ResourceKind.Group : Prs1ResourceKind.Course,
                out var built, out error, groupSlot ? "a" : id, groupSlot ? id : null));
            Assert.Equal(expected ? Prs1SelectorError.None : Prs1SelectorError.StringIdTooLong, error);
            Assert.Equal(parsed, built);
        }
    }

    [Fact]
    public void F05_Maximum_valid_shape_is_2781_bytes_not_4096()
    {
        Assert.True(Prs1Selector.TryBuild(Org, Prs1ResourceKind.Group, out var value, out _, new string('a', 1024), new string('b', 1024)));
        Assert.Equal(2781, Encoding.ASCII.GetByteCount(value.CanonicalReference));
        Assert.True(Prs1Selector.TryParse(value.ToString(), out var roundTrip, out _));
        Assert.Equal(value, roundTrip);
    }

    [Theory]
    [InlineData(4096, Prs1SelectorError.StringIdTooLong)]
    [InlineData(4097, Prs1SelectorError.ReferenceTooLong)]
    public void F05_Reference_limit_is_checked_before_sid_limit(int length, Prs1SelectorError expected)
    {
        // At 4096 bytes the final sid is canonical base64url but exceeds the decoded limit.
        var raw = Ref("group/YWE/");
        raw += new string('A', length - raw.Length);
        Assert.Equal(length, Encoding.ASCII.GetByteCount(raw));
        Assert.False(Prs1Selector.TryParse(raw, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.Equal(expected, error);
    }

    [Fact]
    public void F03_All_nonzero_unused_base64_bits_are_rejected()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        for (var i = 1; i < 16; i++)
        {
            Assert.False(Prs1Selector.TryParse(Ref("course/Y" + alphabet[16 + i]), out var value, out var error));
            Assert.Null(value);
            Assert.Equal(Prs1SelectorError.NonCanonicalBase64Url, error);
        }
        for (var i = 1; i < 4; i++)
        {
            Assert.False(Prs1Selector.TryParse(Ref("course/YW" + alphabet[8 + i]), out var value, out var error));
            Assert.Null(value);
            Assert.Equal(Prs1SelectorError.NonCanonicalBase64Url, error);
        }
    }

    [Fact]
    public void Builder_rejects_unpaired_utf16_surrogates_without_replacement()
    {
        foreach (var id in new[] { "\ud800", "\udfff", "x\ud800y" })
        {
            Assert.False(Prs1Selector.TryBuild(Org, Prs1ResourceKind.Course, out var value, out var error, id));
            Assert.Null(value);
            Assert.Equal(Prs1SelectorError.InvalidUtf8, error);
        }
    }

    [Fact]
    public void Builder_rejects_invalid_typed_shapes()
    {
        var cases = new (Guid Org, Prs1ResourceKind Kind, string? Course, string? Group, Guid? Entity, Prs1SelectorError Error)[]
        {
            (Guid.Empty, Prs1ResourceKind.All, null, null, null, Prs1SelectorError.ZeroUuid),
            (Org, (Prs1ResourceKind)99, null, null, null, Prs1SelectorError.UnknownKind),
            (Org, Prs1ResourceKind.All, "c", null, null, Prs1SelectorError.InvalidStructure),
            (Org, Prs1ResourceKind.All, null, "g", null, Prs1SelectorError.InvalidStructure),
            (Org, Prs1ResourceKind.All, null, null, Id, Prs1SelectorError.InvalidStructure),
            (Org, Prs1ResourceKind.Course, null, null, null, Prs1SelectorError.EmptyStringId),
            (Org, Prs1ResourceKind.Course, "", null, null, Prs1SelectorError.EmptyStringId),
            (Org, Prs1ResourceKind.Course, "c", "g", null, Prs1SelectorError.InvalidStructure),
            (Org, Prs1ResourceKind.Group, "c", null, null, Prs1SelectorError.EmptyStringId),
            (Org, Prs1ResourceKind.Group, "c", "g", Id, Prs1SelectorError.InvalidStructure),
            (Org, Prs1ResourceKind.Work, null, null, null, Prs1SelectorError.InvalidStructure),
            (Org, Prs1ResourceKind.Work, "c", null, Id, Prs1SelectorError.InvalidStructure),
            (Org, Prs1ResourceKind.Work, null, "g", Id, Prs1SelectorError.InvalidStructure),
            (Org, Prs1ResourceKind.Work, null, null, Guid.Empty, Prs1SelectorError.ZeroUuid)
        };
        foreach (var c in cases)
        {
            Assert.False(Prs1Selector.TryBuild(c.Org, c.Kind, out var value, out var error, c.Course, c.Group, c.Entity));
            Assert.Null(value);
            Assert.Equal(c.Error, error);
        }
    }

    [Fact]
    public void Canonical_unicode_round_trip_vectors_cover_all_utf8_widths()
    {
        var random = new Random(1041);
        for (var i = 0; i < 256; i++)
        {
            var id = new StringBuilder();
            for (var n = 0; n <= i % 64; n++)
            {
                var scalar = random.Next(0x110000);
                if (scalar is >= 0xd800 and <= 0xdfff) scalar = 0x20;
                id.Append(char.ConvertFromUtf32(scalar));
            }
            var text = id.ToString();
            Assert.True(Prs1Selector.TryBuild(Org, Prs1ResourceKind.Group, out var built, out _, text, text));
            Assert.True(Prs1Selector.TryParse(built.CanonicalReference, out var parsed, out _));
            Assert.Equal(text, parsed.CourseId);
            Assert.Equal(text, parsed.GroupId);
            Assert.Equal(built, parsed);
        }
    }
}
