namespace YingBan.App;

// The overlay consumes this data contract; browser transport is tested separately.
public record MediaState(string Url, string Title, double CurrentTime, double Duration,
    bool Paused, double PlaybackRate, bool HasVideo);
