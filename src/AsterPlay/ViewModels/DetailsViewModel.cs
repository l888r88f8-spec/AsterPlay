using AsterPlay.Models;
using AsterPlay.Services;

namespace AsterPlay.ViewModels;

public sealed class DetailsViewModel
{
    public DetailsViewModel(EmbyClient client, EmbyItem item)
    {
        Item = item;
        Title = item.Name;
        OriginalTitle = item.OriginalTitle;
        Overview = item.Overview;
        Tagline = item.Taglines.FirstOrDefault() ?? "";
        PosterUrl = client.BuildPrimaryUrl(item, 600);
        BackdropUrl = client.BuildBackdropUrl(item, 1920);
        Genres = item.Genres;
        Tags = item.Tags;
        Studios = item.Studios.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        Cast = item.People
            .Where(x => string.Equals(x.Type, "Actor", StringComparison.OrdinalIgnoreCase))
            .Select(person => new PersonViewModel(client, person))
            .ToArray();
        Directors = item.People
            .Where(x => string.Equals(x.Type, "Director", StringComparison.OrdinalIgnoreCase))
            .Select(person => new PersonViewModel(client, person))
            .ToArray();

        var streams = item.MediaSources
            .SelectMany(source => source.MediaStreams)
            .Concat(item.MediaStreams)
            .GroupBy(stream => (stream.Type, stream.Index, stream.Codec, stream.Language, stream.DisplayTitle))
            .Select(group => group.First())
            .ToArray();

        MediaStreams = streams
            .Select(stream => new MediaStreamViewModel(stream))
            .ToArray();

        MediaSources = item.MediaSources
            .Select(source => new MediaSourceViewModel(source))
            .ToArray();
    }

    public EmbyItem Item { get; }
    public string Title { get; }
    public string OriginalTitle { get; }
    public string Overview { get; }
    public string Tagline { get; }
    public string PosterUrl { get; }
    public string BackdropUrl { get; }
    public IReadOnlyList<string> Genres { get; }
    public IReadOnlyList<string> Tags { get; }
    public IReadOnlyList<string> Studios { get; }
    public IReadOnlyList<PersonViewModel> Cast { get; }
    public IReadOnlyList<PersonViewModel> Directors { get; }
    public IReadOnlyList<MediaSourceViewModel> MediaSources { get; }
    public IReadOnlyList<MediaStreamViewModel> MediaStreams { get; }

    public bool IsMovie => string.Equals(Item.Type, "Movie", StringComparison.OrdinalIgnoreCase);
    public bool IsSeries => string.Equals(Item.Type, "Series", StringComparison.OrdinalIgnoreCase);
    public bool IsEpisode => string.Equals(Item.Type, "Episode", StringComparison.OrdinalIgnoreCase);
    public bool IsFavorite => Item.UserData?.IsFavorite == true;
    public bool HasResumePosition => Item.UserData?.PlaybackPositionTicks > 0;

    public string MetaLine
    {
        get
        {
            var parts = new List<string>();

            if (Item.ProductionYear is > 0)
                parts.Add(Item.ProductionYear.Value.ToString());

            if (Item.CommunityRating is > 0)
                parts.Add($"★ {Item.CommunityRating:0.0}");

            if (Item.RunTimeTicks is > 0)
            {
                var minutes = Item.RunTimeTicks.Value / 600_000_000L;
                parts.Add(minutes >= 60
                    ? $"{minutes / 60}h {minutes % 60}m"
                    : $"{minutes}m");
            }

            if (!string.IsNullOrWhiteSpace(Item.OfficialRating))
                parts.Add(Item.OfficialRating);

            if (Genres.Count > 0)
                parts.Add(string.Join(" / ", Genres.Take(3)));

            return string.Join("   ·   ", parts);
        }
    }

    public string ResumeLabel =>
        HasResumePosition ? "继续播放" : "播放";

    public string FavoriteLabel =>
        IsFavorite ? "♥  已收藏" : "♡  收藏";

    public string DirectorsLine =>
        Directors.Count == 0
            ? ""
            : string.Join(" / ", Directors.Select(x => x.Name));

    public string StudiosLine =>
        Studios.Count == 0
            ? ""
            : string.Join(" / ", Studios);

    public IReadOnlyList<string> ProviderBadges =>
        Item.ProviderIds
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{pair.Key.ToUpperInvariant()}  {pair.Value}")
            .ToArray();

    public IReadOnlyList<string> MediaSummary =>
        BuildMediaSummary(MediaStreams);

    private static IReadOnlyList<string> BuildMediaSummary(
        IReadOnlyList<MediaStreamViewModel> streams)
    {
        var summary = new List<string>();

        foreach (var video in streams.Where(x =>
                     string.Equals(x.Type, "Video", StringComparison.OrdinalIgnoreCase)))
        {
            var parts = new List<string> { "视频" };

            if (!string.IsNullOrWhiteSpace(video.Codec))
                parts.Add(video.Codec.ToUpperInvariant());

            if (video.Width is > 0 && video.Height is > 0)
                parts.Add($"{video.Width}×{video.Height}");

            if (!string.IsNullOrWhiteSpace(video.Stream.VideoRange))
                parts.Add(video.Stream.VideoRange);

            if (!string.IsNullOrWhiteSpace(video.Stream.ColorTransfer))
                parts.Add(video.Stream.ColorTransfer);

            if (video.BitRate is > 0)
                parts.Add($"{video.BitRate.Value / 1_000_000d:0.#} Mbps");

            summary.Add(string.Join(" · ", parts));
        }

        foreach (var audio in streams.Where(x =>
                     string.Equals(x.Type, "Audio", StringComparison.OrdinalIgnoreCase)))
        {
            var parts = new List<string> { "音频" };

            if (!string.IsNullOrWhiteSpace(audio.Language))
                parts.Add(audio.Language.ToUpperInvariant());

            if (!string.IsNullOrWhiteSpace(audio.Codec))
                parts.Add(audio.Codec.ToUpperInvariant());

            if (audio.Channels is > 0)
                parts.Add($"{audio.Channels}ch");

            if (!string.IsNullOrWhiteSpace(audio.Title))
                parts.Add(audio.Title);

            if (audio.IsDefault)
                parts.Add("默认");

            summary.Add(string.Join(" · ", parts));
        }

        foreach (var subtitle in streams.Where(x =>
                     string.Equals(x.Type, "Subtitle", StringComparison.OrdinalIgnoreCase)))
        {
            var parts = new List<string> { "字幕" };

            if (!string.IsNullOrWhiteSpace(subtitle.Language))
                parts.Add(subtitle.Language.ToUpperInvariant());

            if (!string.IsNullOrWhiteSpace(subtitle.Codec))
                parts.Add(subtitle.Codec.ToUpperInvariant());

            if (!string.IsNullOrWhiteSpace(subtitle.Title))
                parts.Add(subtitle.Title);

            if (subtitle.IsExternal)
                parts.Add("外挂");

            if (subtitle.IsForced)
                parts.Add("强制");

            if (subtitle.IsDefault)
                parts.Add("默认");

            summary.Add(string.Join(" · ", parts));
        }

        return summary;
    }
}

public sealed class PersonViewModel
{
    public PersonViewModel(EmbyClient client, EmbyPerson person)
    {
        Person = person;
        ImageUrl = client.BuildPersonPrimaryUrl(person, 320);
    }

    public EmbyPerson Person { get; }
    public string Name => Person.Name;
    public string Role => Person.Role;
    public string Type => Person.Type;
    public string ImageUrl { get; }
}

public sealed class SeasonViewModel
{
    public SeasonViewModel(EmbyClient client, EmbyItem item)
    {
        Item = item;
        Title = string.IsNullOrWhiteSpace(item.Name)
            ? item.IndexNumber is >= 0 ? $"Season {item.IndexNumber}" : "Season"
            : item.Name;
        PosterUrl = client.BuildPrimaryUrl(item, 400);
    }

    public EmbyItem Item { get; }
    public string Title { get; }
    public string PosterUrl { get; }
    public int? Number => Item.IndexNumber;
    public bool IsSpecials => Number == 0;
}

public sealed class EpisodeViewModel
{
    public EpisodeViewModel(EmbyClient client, EmbyItem item)
    {
        Item = item;
        Title = item.Name;
        ImageUrl = client.BuildBackdropUrl(item, 700);
    }

    public EmbyItem Item { get; }
    public string Title { get; }
    public string ImageUrl { get; }
    public string Overview => Item.Overview;
    public int? SeasonNumber => Item.ParentIndexNumber;
    public int? EpisodeNumber => Item.IndexNumber;
    public long ResumePositionTicks => Item.UserData?.PlaybackPositionTicks ?? 0;
    public bool IsPlayed => Item.UserData?.Played == true;
    public double PlayedPercentage => IsPlayed
        ? 100
        : Math.Clamp(Item.UserData?.PlayedPercentage ?? 0, 0, 100);
    public bool HasResumePosition => ResumePositionTicks > 0 && !IsPlayed;
    public string PlayLabel => HasResumePosition ? "继续播放" : "播放";

    public string EpisodeLabel =>
        SeasonNumber is >= 0 && EpisodeNumber is >= 0
            ? $"S{SeasonNumber:00}E{EpisodeNumber:00}"
            : EpisodeNumber is >= 0
                ? $"E{EpisodeNumber:00}"
                : "";

    public string RuntimeLabel
    {
        get
        {
            if (Item.RunTimeTicks is not > 0)
                return "";

            var minutes = Item.RunTimeTicks.Value / 600_000_000L;
            return minutes >= 60
                ? $"{minutes / 60}h {minutes % 60}m"
                : $"{minutes}m";
        }
    }

    public string WatchStateLabel =>
        IsPlayed
            ? "已播放"
            : PlayedPercentage > 0
                ? $"已观看 {PlayedPercentage:0}%"
                : "";
}

public sealed class MediaSourceViewModel
{
    public MediaSourceViewModel(MediaSource source)
    {
        Source = source;
        Streams = source.MediaStreams
            .Select(stream => new MediaStreamViewModel(stream))
            .ToArray();
    }

    public MediaSource Source { get; }
    public IReadOnlyList<MediaStreamViewModel> Streams { get; }
    public string Name => string.IsNullOrWhiteSpace(Source.Name) ? Source.Container : Source.Name;
    public string Container => Source.Container;
    public long? Size => Source.Size;
    public int? Bitrate => Source.Bitrate;
}

public sealed class MediaStreamViewModel
{
    public MediaStreamViewModel(EmbyMediaStream stream)
    {
        Stream = stream;
    }

    public EmbyMediaStream Stream { get; }
    public string Type => Stream.Type;
    public string Codec => Stream.Codec;
    public string Language => Stream.Language;
    public string Title => Stream.DisplayTitle.Length > 0 ? Stream.DisplayTitle : Stream.Title;
    public int Index => Stream.Index;
    public int? Width => Stream.Width;
    public int? Height => Stream.Height;
    public int? Channels => Stream.Channels;
    public int? BitRate => Stream.BitRate;
    public bool IsDefault => Stream.IsDefault;
    public bool IsForced => Stream.IsForced;
    public bool IsExternal => Stream.IsExternal;
}
