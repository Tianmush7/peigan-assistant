namespace YingBan.Core;

internal static class CommentLimits
{
    internal const int MaximumComments = 100_000;
    internal const int MaximumIdLength = 128;
    private const int MaximumTextLength = 512;

    internal static DanmakuComment? Normalize(DanmakuComment? comment)
    {
        if (comment is null || !double.IsFinite(comment.Time) || comment.Time < 0 || comment.Time > 864_000 ||
            comment.Mode is not (1 or 4 or 5 or 6) || comment.Color < 0 || comment.Color > 0xFFFFFF ||
            comment.FontSize is < 1 or > 128 || string.IsNullOrWhiteSpace(comment.Text) ||
            comment.Id is null || comment.Id.Length > MaximumIdLength) return null;
        if (comment.Text.Length <= MaximumTextLength) return comment;
        var length = char.IsHighSurrogate(comment.Text[MaximumTextLength - 1]) ? MaximumTextLength - 1 : MaximumTextLength;
        return comment with { Text = comment.Text[..length] };
    }
}
